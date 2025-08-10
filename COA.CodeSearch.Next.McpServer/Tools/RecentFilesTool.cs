using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for finding recently modified files with token optimization
/// </summary>
public class RecentFilesTool : McpToolBase<RecentFilesParameters, AIOptimizedResponse<RecentFilesResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly RecentFilesResponseBuilder _responseBuilder;
    private readonly ILogger<RecentFilesTool> _logger;

    public RecentFilesTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<RecentFilesTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new RecentFilesResponseBuilder(null, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.RecentFiles;
    public override string Description => "Find recently modified files with time filtering and token-optimized responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<RecentFilesResult>> ExecuteInternalAsync(
        RecentFilesParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Validate max results
        var maxResults = parameters.MaxResults ?? 50;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
        // Parse time frame
        var timeFrame = TimeSpan.FromDays(7); // Default: last week
        if (!string.IsNullOrEmpty(parameters.TimeFrame))
        {
            timeFrame = ParseTimeFrame(parameters.TimeFrame);
        }
        
        var cutoffTime = DateTime.UtcNow.Subtract(timeFrame);
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<RecentFilesResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached recent files results for timeframe: {TimeFrame}", parameters.TimeFrame);
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
            
            // Create query for recent files using modification date range
            var query = CreateRecentFilesQuery(cutoffTime);
            
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                query,
                maxResults * 2,  // Get extra to account for filtering
                cancellationToken
            );
            
            // Process results and create recent file entries
            var recentFiles = new List<RecentFileInfo>();
            
            _logger.LogDebug("Processing {Count} search hits for recent files", searchResult.Hits.Count);
            
            foreach (var hit in searchResult.Hits)
            {
                if (!string.IsNullOrEmpty(hit.FilePath) && hit.LastModified.HasValue)
                {
                    // Double-check the time filter (Lucene queries can be approximate)
                    if (hit.LastModified.Value >= cutoffTime)
                    {
                        // Apply extension filter if provided
                        if (!string.IsNullOrEmpty(parameters.ExtensionFilter))
                        {
                            var extensions = parameters.ExtensionFilter
                                .Split(',')
                                .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                            
                            var fileExtension = Path.GetExtension(hit.FilePath);
                            if (!extensions.Contains(fileExtension))
                                continue;
                        }
                        
                        recentFiles.Add(new RecentFileInfo
                        {
                            FilePath = hit.FilePath,
                            FileName = Path.GetFileName(hit.FilePath),
                            Directory = Path.GetDirectoryName(hit.FilePath) ?? "",
                            Extension = Path.GetExtension(hit.FilePath),
                            LastModified = hit.LastModified.Value,
                            SizeBytes = hit.FileSize,
                            ModifiedAgo = DateTime.UtcNow - hit.LastModified.Value
                        });
                    }
                }
            }
            
            // Sort by modification time (most recent first)
            recentFiles = recentFiles
                .OrderByDescending(f => f.LastModified)
                .Take(maxResults)
                .ToList();
            
            _logger.LogDebug("Found {Count} recent files in the last {TimeFrame}", 
                recentFiles.Count, timeFrame);
            
            // Create RecentFilesResult for response builder
            var recentFilesResult = new ResponseBuilders.RecentFilesResult
            {
                Files = recentFiles.Select(f => new ResponseBuilders.FileInfo
                {
                    Path = f.FilePath,
                    Size = f.SizeBytes,
                    LastModified = f.LastModified,
                    IsDirectory = false
                }).ToList(),
                TimeFrameRequested = parameters.TimeFrame ?? "7d",
                CutoffTime = cutoffTime,
                TotalFiles = recentFiles.Count,
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
            var result = await _responseBuilder.BuildResponseAsync(recentFilesResult, context);
            
            // Cache the successful response
            if (!parameters.NoCache && result.Success)
            {
                await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(5), // Short cache for recent files
                    Priority = recentFiles.Count > 20 ? CachePriority.High : CachePriority.Normal
                });
                _logger.LogDebug("Cached recent files results for timeframe: {TimeFrame}", parameters.TimeFrame);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding recent files for timeframe: {TimeFrame}", parameters.TimeFrame);
            var errorResult = new AIOptimizedResponse<RecentFilesResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "RECENT_FILES_ERROR",
                    Message = $"Error finding recent files: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the time frame format (e.g., '1h', '2d', '1w')",
                            "Check if the workspace is properly indexed",
                            "Try a different time frame",
                            "Check logs for detailed error information"
                        }
                    }
                }
            };
            errorResult.SetOperation(Name);
            return errorResult;
        }
    }
    
    private Query CreateRecentFilesQuery(DateTime cutoffTime)
    {
        // Convert to Lucene date format (yyyyMMddHHmmss)
        var cutoffString = cutoffTime.ToString("yyyyMMddHHmmss");
        
        // Create range query for modification date
        var rangeQuery = TermRangeQuery.NewStringRange(
            "LastModified", 
            cutoffString, 
            null, // No upper bound (up to now)
            true, 
            true);
        
        return rangeQuery;
    }
    
    private TimeSpan ParseTimeFrame(string timeFrame)
    {
        if (string.IsNullOrWhiteSpace(timeFrame))
            return TimeSpan.FromDays(7);
        
        timeFrame = timeFrame.Trim().ToLowerInvariant();
        
        // Extract number and unit
        var unitChar = timeFrame.LastOrDefault();
        var numberPart = timeFrame.Substring(0, timeFrame.Length - 1);
        
        if (!int.TryParse(numberPart, out var number) || number <= 0)
        {
            throw new ArgumentException($"Invalid time frame format: '{timeFrame}'. Expected format like '1h', '2d', '1w'");
        }
        
        return unitChar switch
        {
            'h' => TimeSpan.FromHours(number),
            'd' => TimeSpan.FromDays(number),
            'w' => TimeSpan.FromDays(number * 7),
            'm' when timeFrame.EndsWith("min") => TimeSpan.FromMinutes(number),
            _ => throw new ArgumentException($"Unsupported time unit: '{unitChar}'. Supported: h (hours), d (days), w (weeks), min (minutes)")
        };
    }
    
    private AIOptimizedResponse<RecentFilesResult> CreateNoIndexError(string workspacePath)
    {
        var result = new AIOptimizedResponse<RecentFilesResult>
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
                "The workspace needs to be indexed before searching for recent files",
                "Indexing creates a searchable database with file modification timestamps"
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
/// Parameters for the RecentFiles tool
/// </summary>
public class RecentFilesParameters
{
    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Time frame for recent files (e.g., '1h', '2d', '1w'). Default: '7d'
    /// </summary>
    [Description("Time frame for recent files (e.g., '1h', '2d', '1w'). Default: '7d'")]
    public string? TimeFrame { get; set; }

    /// <summary>
    /// Maximum number of results to return (default: 50, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 50, max: 500)")]
    public int? MaxResults { get; set; }

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
/// Result from the RecentFiles tool
/// </summary>
public class RecentFilesResult : ToolResultBase
{
    public override string Operation => ToolNames.RecentFiles;
    
    /// <summary>
    /// List of recent files
    /// </summary>
    public List<RecentFileInfo> Files { get; set; } = new();
    
    /// <summary>
    /// The time frame that was searched
    /// </summary>
    public string TimeFrameRequested { get; set; } = string.Empty;
    
    /// <summary>
    /// The actual cutoff time used
    /// </summary>
    public DateTime CutoffTime { get; set; }
    
    /// <summary>
    /// Total number of recent files found
    /// </summary>
    public int TotalFiles { get; set; }
}

/// <summary>
/// Information about a recent file
/// </summary>
public class RecentFileInfo
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
    /// Last modification time
    /// </summary>
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }
    
    /// <summary>
    /// How long ago the file was modified
    /// </summary>
    public TimeSpan ModifiedAgo { get; set; }
}