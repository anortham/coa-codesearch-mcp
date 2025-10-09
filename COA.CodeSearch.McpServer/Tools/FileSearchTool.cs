using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Search;
using Lucene.Net.Index;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for searching files by name pattern in indexed workspaces with token optimization
/// </summary>
[Obsolete("Use SearchFilesTool with resourceType='file' instead. This tool will be removed in a future version.", error: false)]
public class FileSearchTool : CodeSearchToolBase<FileSearchParameters, AIOptimizedResponse<FileSearchResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly FileSearchResponseBuilder _responseBuilder;
    private readonly ILogger<FileSearchTool> _logger;
    private readonly CodeAnalyzer _codeAnalyzer;

    /// <summary>
    /// Initializes a new instance of the FileSearchTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="sqliteService">SQLite symbol service for fast file pattern queries</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    public FileSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        ISQLiteSymbolService sqliteService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<FileSearchTool> logger,
        CodeAnalyzer codeAnalyzer) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _sqliteService = sqliteService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new FileSearchResponseBuilder(logger as ILogger<FileSearchResponseBuilder>, storageService);
        _logger = logger;
        _codeAnalyzer = codeAnalyzer;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.FileSearch;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "USE BEFORE Read - Locate files by name/pattern instead of guessing paths. FASTER than manual navigation. Supports recursive patterns (**/*.ext) and directory-specific searches (src/**/*.cs). Essential for finding: UserService.cs, **/*.test.js, configuration files.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;


    /// <summary>
    /// Executes the file search operation to find files matching the specified pattern.
    /// </summary>
    /// <param name="parameters">File search parameters including pattern and workspace path</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>File search results with matching file paths and metadata</returns>
    protected override async Task<AIOptimizedResponse<FileSearchResult>> ExecuteInternalAsync(
        FileSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var pattern = ValidateRequired(parameters.Pattern, nameof(parameters.Pattern));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Validate max results
        var maxResults = ValidateRange(parameters.MaxResults, 1, 500, nameof(parameters.MaxResults));
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<FileSearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached file search results for pattern: {Pattern}", pattern);
                cached.Meta ??= new AIResponseMeta();
                if (cached.Meta.ExtensionData == null)
                    cached.Meta.ExtensionData = new Dictionary<string, object>();
                cached.Meta.ExtensionData["cacheHit"] = true;
                return cached;
            }
        }
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Determine if this pattern requires path-based search (contains ** or /)
            // For regex patterns, only check for actual path separators, not escape sequences
            var requiresPathSearch = parameters.UseRegex
                ? pattern.Contains("/")  // For regex, only forward slash indicates path search
                : pattern.Contains("**") || pattern.Contains("/") || pattern.Contains("\\");

            // TIER 1: Try SQLite fast path for simple file pattern searches (0-5ms)
            if (_sqliteService.DatabaseExists(workspacePath))
            {
                try
                {
                    var sqliteFiles = await _sqliteService.SearchFilesByPatternAsync(
                        workspacePath,
                        pattern,
                        searchFullPath: requiresPathSearch,
                        extensionFilter: parameters.ExtensionFilter,
                        maxResults: maxResults,
                        cancellationToken);

                    if (sqliteFiles.Any())
                    {
                        stopwatch.Stop();
                        _logger.LogInformation("✅ Tier 1 HIT: Found {Count} files via SQLite in {Ms}ms",
                            sqliteFiles.Count, stopwatch.ElapsedMilliseconds);

                        // Create FileSearchResult for response builder (matching Lucene pattern)
                        var tier1Result = new ResponseBuilders.FileSearchResult
                        {
                            Files = sqliteFiles.Select(f => new ResponseBuilders.FileInfo
                            {
                                Path = f.Path,
                                Size = f.Size,
                                LastModified = DateTimeOffset.FromUnixTimeSeconds(f.LastModified).UtcDateTime,
                                IsDirectory = false
                            }).ToList(),
                            TotalFiles = sqliteFiles.Count,
                            Pattern = pattern,
                            SearchPath = workspacePath
                        };

                        // Build response context
                        var tier1Context = new ResponseContext
                        {
                            ResponseMode = parameters.ResponseMode,
                            TokenLimit = parameters.MaxTokens,
                            StoreFullResults = true,
                            ToolName = Name,
                            CacheKey = cacheKey
                        };

                        // Use response builder to create optimized response
                        var tier1Response = await _responseBuilder.BuildResponseAsync(tier1Result, tier1Context);

                        if (!parameters.NoCache)
                        {
                            await _cacheService.SetAsync(cacheKey, tier1Response);
                        }

                        return tier1Response;
                    }

                    _logger.LogDebug("⏭️ Tier 1 MISS: SQLite found 0 files, falling back to Lucene");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 1 SQLite search failed for pattern '{Pattern}', falling back to Lucene", pattern);
                }
            }


            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateNoIndexError(workspacePath);
            }

            var searchField = requiresPathSearch ? "path" : "filename_lower";
            var searchPattern = requiresPathSearch ? pattern : pattern.ToLowerInvariant();
            
            // Create appropriate query based on pattern type
            Query query;
            if (parameters.UseRegex)
            {
                query = new MatchAllDocsQuery();
                _logger.LogDebug("Using MatchAllDocsQuery for regex pattern: {Pattern}", pattern);
            }
            else if (pattern == "*")
            {
                query = new MatchAllDocsQuery();
                _logger.LogDebug("Using MatchAllDocsQuery for match-all pattern");
            }
            else if (pattern.Contains("**"))
            {
                // Lucene WildcardQuery doesn't support ** syntax, use MatchAllDocsQuery and filter later
                query = new MatchAllDocsQuery();
                _logger.LogDebug("Using MatchAllDocsQuery for ** pattern: {Pattern} (Lucene doesn't support ** wildcards)", pattern);
            }
            else if (pattern.Contains("*") || pattern.Contains("?"))
            {
                // Use wildcard query on appropriate field for simple wildcards
                query = new WildcardQuery(new Term(searchField, searchPattern));
                _logger.LogDebug("Using WildcardQuery for pattern: {Pattern} on {Field} field", pattern, searchField);
            }
            else
            {
                // Exact match on appropriate field
                query = new TermQuery(new Term(searchField, searchPattern));
                _logger.LogDebug("Using TermQuery for exact match: {Pattern} on {Field} field", pattern, searchField);
            }
            
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                query,
                maxResults * 10,  // Get more results since we might filter
                true, // Enable snippets to ensure all fields (including type_info) are populated
                cancellationToken
            );
            
            // Process results and filter by pattern
            var files = new List<FileSearchMatch>();
            var useRegex = parameters.UseRegex;
            var regex = useRegex ? new Regex(pattern, RegexOptions.IgnoreCase) : null;
            
            // For ** patterns, we need to normalize the pattern and create the regex once
            var normalizedPattern = pattern.Replace('\\', '/');
            var globPattern = useRegex ? null : ConvertGlobToRegex(normalizedPattern);
            
            _logger.LogDebug("Filtering {Count} search hits with pattern: {Pattern} (regex: {UseRegex})", 
                searchResult.Hits.Count, pattern, useRegex);
            
            foreach (var hit in searchResult.Hits)
            {
                var filePath = hit.FilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    var fileName = Path.GetFileName(filePath);
                    
                    // Choose what to match against based on pattern type
                    var matchTarget = requiresPathSearch ? filePath : fileName;
                    
                    // Check if target matches pattern
                    bool matches;
                    if (useRegex)
                    {
                        // For regex patterns, use the original target without normalization
                        matches = regex!.IsMatch(matchTarget);
                    }
                    else
                    {
                        // For glob patterns, normalize path separators for consistent matching
                        var normalizedTarget = matchTarget.Replace('\\', '/');
                        matches = globPattern!.IsMatch(normalizedTarget);
                    }
                    
                    _logger.LogDebug("Target {MatchTarget} matches pattern {Pattern}: {Matches}", 
                        requiresPathSearch ? $"path:{filePath}" : $"filename:{fileName}", pattern, matches);
                    
                    if (matches)
                    {
                        files.Add(new FileSearchMatch
                        {
                            FilePath = filePath,
                            FileName = fileName,
                            Directory = Path.GetDirectoryName(filePath) ?? "",
                            Extension = Path.GetExtension(filePath),
                            Score = CalculateFileMatchScore(fileName, pattern, useRegex),
                            TypeSummary = ExtractTypeSummaryFromHit(hit)
                        });
                    }
                }
            }
            
            _logger.LogDebug("Filtered to {Count} files matching pattern", files.Count);
            
            // Apply extension filter if provided
            if (!string.IsNullOrEmpty(parameters.ExtensionFilter))
            {
                var extensions = parameters.ExtensionFilter
                    .Split(',')
                    .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                files = files.Where(f => extensions.Contains(f.Extension)).ToList();
            }
            
            // Sort by file name
            files = files.OrderBy(f => f.FileName).ThenBy(f => f.Directory).ToList();
            
            // Include directories if requested
            List<string>? directories = null;
            if (parameters.IncludeDirectories)
            {
                directories = files
                    .Select(f => f.Directory)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();
            }
            
            // Create FileSearchResult for response builder
            var fileSearchResult = new ResponseBuilders.FileSearchResult
            {
                Files = files.Select(f => new ResponseBuilders.FileInfo
                {
                    Path = f.FilePath,
                    Size = 0, // We don't have size from index currently
                    LastModified = null, // We don't have this from index currently
                    IsDirectory = false
                }).ToList(),
                TotalFiles = files.Count,
                Pattern = pattern,
                SearchPath = workspacePath
            };
            
            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = parameters.ResponseMode,
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };
            
            // Use response builder to create optimized response
            var result = await _responseBuilder.BuildResponseAsync(fileSearchResult, context);

            // Add directories to extension data if requested
            if (directories != null)
            {
                if (result.Data.ExtensionData == null)
                    result.Data.ExtensionData = new Dictionary<string, object>();
                result.Data.ExtensionData["directories"] = directories;
            }
            
            // Cache the successful response
            if (!parameters.NoCache && result.Success)
            {
                await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(15),
                    Priority = files.Count > 100 ? CachePriority.High : CachePriority.Normal
                });
                _logger.LogDebug("Cached file search results for pattern: {Pattern}", pattern);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for files with pattern: {Pattern}", pattern);
            var errorResult = new AIOptimizedResponse<FileSearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "FILE_SEARCH_ERROR",
                    Message = $"Error searching for files: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the pattern syntax is valid",
                            "Check if the workspace is properly indexed",
                            "Try a simpler pattern",
                            "Check logs for detailed error information"
                        }
                    }
                }
            };
            return errorResult;
        }
    }
    
    /// <summary>
    /// Calculate a relevance score for file name matches
    /// </summary>
    private float CalculateFileMatchScore(string fileName, string pattern, bool isRegex)
    {
        // Remove wildcards and extension for comparison
        var cleanPattern = pattern.Replace("*", "").Replace("?", "").Replace(".cs", "").Replace(".ts", "").Replace(".js", "");
        
        // If pattern is empty after cleaning (like *.cs), give medium score
        if (string.IsNullOrEmpty(cleanPattern))
            return 0.5f;
            
        var fileNameLower = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var patternLower = cleanPattern.ToLowerInvariant();
        
        // Exact match (ignoring extension) = highest score
        if (fileNameLower == patternLower)
            return 1.0f;
            
        // Exact match with case difference = very high score
        if (fileNameLower.Equals(patternLower, StringComparison.OrdinalIgnoreCase))
            return 0.95f;
            
        // Starts with pattern = high score
        if (fileNameLower.StartsWith(patternLower))
            return 0.85f;
            
        // Ends with pattern = good score
        if (fileNameLower.EndsWith(patternLower))
            return 0.75f;
            
        // Contains as word boundary = decent score
        if (System.Text.RegularExpressions.Regex.IsMatch(fileNameLower, $@"\b{System.Text.RegularExpressions.Regex.Escape(patternLower)}\b"))
            return 0.65f;
            
        // Contains pattern anywhere = lower score
        if (fileNameLower.Contains(patternLower))
            return 0.5f;
            
        // Matched by wildcard/regex but pattern not directly found = lowest score
        return 0.3f;
    }
    
    private Regex ConvertGlobToRegex(string pattern)
    {
        // Handle recursive ** patterns properly (assumes normalized forward slash paths)
        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*\\*/", ".*/")      // **/ becomes .*/ (match any directory path)
            .Replace("/\\*\\*", "/.*")      // /** becomes /.* (match any subdirectory)
            .Replace("\\*\\*", ".*")        // ** becomes .* (match anything)
            .Replace("\\*", "[^/]*")        // * becomes [^/]* (match filename without path separators)
            .Replace("\\?", "[^/]");        // ? becomes [^/] (single char without path separators)
        
        // For patterns starting with a directory (like "src/**/*.cs"), allow matching anywhere in path
        // For patterns starting with ** (like "**/*.cs"), ensure they match from path boundaries
        if (pattern.StartsWith("**/"))
        {
            regexPattern = "(^|/)" + regexPattern + "$";  // Match at start or after path separator
        }
        else if (pattern.Contains("/"))
        {
            regexPattern = "/" + regexPattern + "$";      // Match after path separator for directory patterns
        }
        else
        {
            regexPattern = "^" + regexPattern + "$";      // Exact match for filename-only patterns
        }
        
        return new Regex(regexPattern, RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Extract type summary from search hit to help Claude understand what's in the file
    /// </summary>
    private string? ExtractTypeSummaryFromHit(SearchHit hit)
    {
        try
        {
            var typeInfoJson = hit.Fields?.GetValueOrDefault("type_info");
            
            if (!string.IsNullOrEmpty(typeInfoJson))
            {
                try
                {
                    // Deserialize using case-insensitive options for external JSON data
                    var typeData = System.Text.Json.JsonSerializer.Deserialize<COA.CodeSearch.McpServer.Models.StoredTypeInfo>(
                        typeInfoJson, 
                        COA.Mcp.Framework.Serialization.JsonOptionsFactory.CreateForDeserialization());
                    
                    if (typeData != null)
                    {
                        var summary = new List<string>();
                        
                        // Add primary types (classes, interfaces, etc.)
                        if (typeData.Types?.Any() == true)
                        {
                            var primaryTypes = typeData.Types.Take(3).Select(t => $"{t.Kind} {t.Name}");
                            summary.Add(string.Join(", ", primaryTypes));
                        }
                        
                        // Add method count if significant
                        if (typeData.Methods?.Count > 0)
                        {
                            summary.Add($"{typeData.Methods.Count} methods");
                        }
                        
                        if (summary.Any())
                        {
                            return string.Join(" - ", summary);
                        }
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to parse type info for {FilePath}", hit.FilePath);
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract type summary for {FilePath}", hit.FilePath);
            return null;
        }
    }
    
    private AIOptimizedResponse<FileSearchResult> CreateNoIndexError(string workspacePath)
    {
        var result = new AIOptimizedResponse<FileSearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "NO_INDEX",
                Message = $"No index found for workspace: {workspacePath}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        $"Run {ToolNames.IndexWorkspace} tool to create the index",
                        "Verify the workspace path is correct",
                        "Check if you have read permissions for the workspace"
                    }
                }
            },
            Insights = new List<string>
            {
                "The workspace needs to be indexed before searching",
                "Indexing creates a searchable database of file contents"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = ToolNames.IndexWorkspace,
                    Description = "Create search index for this workspace",
                    Priority = 100
                }
            }
        };
        return result;
    }
}

/// <summary>
/// Parameters for the FileSearch tool - locate files by name/pattern with intelligent matching
/// </summary>
public class FileSearchParameters
{
    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Required]
    [Description("Path to the workspace directory to search. Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// The search pattern supporting glob patterns, wildcards, and recursive directory traversal.
    /// </summary>
    /// <example>*.cs</example>
    /// <example>**/*.test.js</example>
    /// <example>src/**/*Controller.cs</example>
    /// <example>UserService*</example>
    [Required]
    [Description("The search pattern supporting glob and wildcards. Examples: '*.cs', '**/*.test.js', 'src/**/*Controller.cs', 'UserService*'")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return (default: 100, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 100, max: 500)")]
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Use regular expression instead of glob pattern for advanced pattern matching.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Use regular expression instead of glob pattern for advanced matching (default: false).")]
    public bool UseRegex { get; set; } = false;

    /// <summary>
    /// Include list of matching directories in results for better context and navigation.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include list of matching directories in results for better navigation context (default: false).")]
    public bool IncludeDirectories { get; set; } = false;

    /// <summary>
    /// Comma-separated list of file extensions to filter results, improving precision.
    /// </summary>
    /// <example>.cs,.js</example>
    /// <example>.tsx,.ts</example>
    /// <example>.json,.xml</example>
    [Description("Comma-separated list of file extensions to filter. Examples: '.cs,.js', '.tsx,.ts', '.json,.xml'")]
    public string? ExtensionFilter { get; set; }
    
    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string ResponseMode { get; set; } = "adaptive";
    
    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int MaxTokens { get; set; } = 8000;
    
    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;
    
    /// <summary>
    /// Automatically open the first matching file in VS Code
    /// </summary>
    [Description("Automatically open the first matching file in VS Code (default: false)")]
    public bool OpenFirstResult { get; set; } = false;
}

/// <summary>
/// Result from the FileSearch tool
/// </summary>
public class FileSearchResult : ToolResultBase
{
    public override string Operation => ToolNames.FileSearch;
    
    /// <summary>
    /// List of matching files
    /// </summary>
    public List<FileSearchMatch> Files { get; set; } = new();
    
    /// <summary>
    /// List of unique directories containing matches (if requested)
    /// </summary>
    public List<string>? Directories { get; set; }
    
    /// <summary>
    /// Total number of matches found
    /// </summary>
    public int TotalMatches { get; set; }
}

/// <summary>
/// Information about a matching file
/// </summary>
public class FileSearchMatch
{
    /// <summary>
    /// Full path to the file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File name without path
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory containing the file
    /// </summary>
    public string Directory { get; set; } = string.Empty;
    
    /// <summary>
    /// File extension
    /// </summary>
    public string Extension { get; set; } = string.Empty;
    
    /// <summary>
    /// Lucene relevance score
    /// </summary>
    public float Score { get; set; }
    
    /// <summary>
    /// Summary of primary types defined in this file (helps Claude understand what's in the file)
    /// </summary>
    public string? TypeSummary { get; set; }
}