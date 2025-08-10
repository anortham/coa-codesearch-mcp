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
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

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
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<FileSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new FileSearchResponseBuilder(null, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.FileSearch;
    public override string Description => "Search for files by name pattern with token-optimized responses";
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
            
            // Create a MatchAllDocsQuery to get all indexed files
            // We'll filter by pattern in memory since filename patterns are complex
            var query = new MatchAllDocsQuery();
            
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                query,
                maxResults * 2,  // Get extra to account for filtering
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
                            Extension = Path.GetExtension(filePath)
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
            errorResult.SetOperation(Name);
            return errorResult;
        }
    }
    
    private Regex ConvertGlobToRegex(string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase);
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
        result.SetOperation(Name);
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
}