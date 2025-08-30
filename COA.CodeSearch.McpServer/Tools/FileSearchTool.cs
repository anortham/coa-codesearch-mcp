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
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Search;
using Lucene.Net.Index;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.Util;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for searching files by name pattern in indexed workspaces with token optimization
/// </summary>
public class FileSearchTool : McpToolBase<FileSearchParameters, AIOptimizedResponse<FileSearchResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly FileSearchResponseBuilder _responseBuilder;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<FileSearchTool> logger) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new FileSearchResponseBuilder(null, storageService);
        _vscode = vscode;
        _logger = logger;
    }

    public override string Name => ToolNames.FileSearch;
    public override string Description => "Find files by name or pattern. Great for locating specific files like 'UserService.cs' or all test files '*.test.cs'.";
    public override ToolCategory Category => ToolCategory.Query;

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
        var maxResults = parameters.MaxResults ?? 100;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
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
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateNoIndexError(workspacePath);
            }
            
            // Create a query for the filename_lower field (case-insensitive matching)
            // For simple patterns, we can use a wildcard query
            Query query;
            if (pattern.Contains("*") || pattern.Contains("?"))
            {
                // Use filename_lower field for case-insensitive wildcard search
                query = new WildcardQuery(new Term("filename_lower", pattern.ToLowerInvariant()));
                _logger.LogDebug("Using WildcardQuery for pattern: {Pattern} on filename_lower field", pattern);
            }
            else
            {
                // Exact filename match using filename_lower for case-insensitive
                query = new TermQuery(new Term("filename_lower", pattern.ToLowerInvariant()));
                _logger.LogDebug("Using TermQuery for exact match: {Pattern} on filename_lower field", pattern);
            }
            
            // For regex patterns or match-all patterns, use MatchAllDocsQuery and filter later
            if (parameters.UseRegex == true)
            {
                query = new MatchAllDocsQuery();
                _logger.LogDebug("Using MatchAllDocsQuery for regex pattern: {Pattern}", pattern);
            }
            else if (pattern == "*")
            {
                query = new MatchAllDocsQuery();
                _logger.LogDebug("Using MatchAllDocsQuery for match-all pattern");
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
            var useRegex = parameters.UseRegex ?? false;
            var regex = useRegex ? new Regex(pattern, RegexOptions.IgnoreCase) : null;
            var globPattern = useRegex ? null : ConvertGlobToRegex(pattern);
            
            _logger.LogDebug("Filtering {Count} search hits with pattern: {Pattern} (regex: {UseRegex})", 
                searchResult.Hits.Count, pattern, useRegex);
            
            foreach (var hit in searchResult.Hits)
            {
                var filePath = hit.FilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    var fileName = Path.GetFileName(filePath);
                    
                    // Check if filename matches pattern
                    bool matches = useRegex 
                        ? regex!.IsMatch(fileName)
                        : globPattern!.IsMatch(fileName);
                    
                    _logger.LogDebug("File {FileName} matches pattern {Pattern}: {Matches}", 
                        fileName, pattern, matches);
                    
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
            if (parameters.IncludeDirectories ?? false)
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
                ResponseMode = parameters.ResponseMode ?? "adaptive",
                TokenLimit = parameters.MaxTokens ?? 8000,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };
            
            // Use response builder to create optimized response
            var result = await _responseBuilder.BuildResponseAsync(fileSearchResult, context);
            
            // Open first result in VS Code if requested
            if ((parameters.OpenFirstResult ?? false) && _vscode.IsConnected && result.Success && files.Count > 0)
            {
                // Fire and forget - don't block the main response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var firstFile = files.First();
                        var success = await _vscode.OpenFileAsync(firstFile.FilePath);
                        
                        if (success)
                        {
                            _logger.LogDebug("Opened file in VS Code: {FilePath}", firstFile.FilePath);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to open file in VS Code: {FilePath}", firstFile.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to open first file result in VS Code");
                    }
                }, cancellationToken);
            }
            
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
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
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
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    var typeData = System.Text.Json.JsonSerializer.Deserialize<COA.CodeSearch.McpServer.Models.StoredTypeInfo>(typeInfoJson, options);
                    
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
/// Parameters for the FileSearch tool
/// </summary>
public class FileSearchParameters
{
    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// The search pattern (glob or regex)
    /// </summary>
    [Required]
    [Description("The search pattern (glob or regex)")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return (default: 100, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 100, max: 500)")]
    public int? MaxResults { get; set; }

    /// <summary>
    /// Use regular expression instead of glob pattern
    /// </summary>
    [Description("Use regular expression instead of glob pattern")]
    public bool? UseRegex { get; set; }

    /// <summary>
    /// Include list of matching directories
    /// </summary>
    [Description("Include list of matching directories")]
    public bool? IncludeDirectories { get; set; }

    /// <summary>
    /// Comma-separated list of file extensions to filter (e.g., ".cs,.js")
    /// </summary>
    [Description("Comma-separated list of file extensions to filter (e.g., '.cs,.js')")]
    public string? ExtensionFilter { get; set; }
    
    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string? ResponseMode { get; set; }
    
    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;
    
    /// <summary>
    /// Automatically open the first matching file in VS Code
    /// </summary>
    [Description("Automatically open the first matching file in VS Code")]
    public bool? OpenFirstResult { get; set; }
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