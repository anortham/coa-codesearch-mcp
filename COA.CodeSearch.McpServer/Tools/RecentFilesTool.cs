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
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for finding recently modified files with token optimization
/// </summary>
public class RecentFilesTool : CodeSearchToolBase<RecentFilesParameters, AIOptimizedResponse<RecentFilesResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly RecentFilesResponseBuilder _responseBuilder;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<RecentFilesTool> _logger;
    private readonly CodeAnalyzer _codeAnalyzer;

    /// <summary>
    /// Initializes a new instance of the RecentFilesTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="vscode">VS Code bridge for IDE integration</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    public RecentFilesTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<RecentFilesTool> logger,
        CodeAnalyzer codeAnalyzer) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new RecentFilesResponseBuilder(null, storageService);
        _vscode = vscode;
        _logger = logger;
        _codeAnalyzer = codeAnalyzer;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.RecentFiles;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "CHECK FIRST when resuming - See what changed since last session. IMMEDIATELY use after breaks, new sessions, or asking 'what was I working on?' Shows temporal context.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Executes the recent files operation to find recently modified files.
    /// </summary>
    /// <param name="parameters">Recent files parameters including workspace path and time constraints</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Recent files results with modification details and temporal context</returns>
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
            var indexExists = await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken);
            _logger.LogInformation("Index exists for {Workspace}: {Exists}", workspacePath, indexExists);
            
            if (!indexExists)
            {
                return CreateNoIndexError(workspacePath);
            }
            
            // Use NumericRangeQuery for efficient server-side filtering by modification time
            var cutoffTicks = cutoffTime.Ticks;
            var query = NumericRangeQuery.NewInt64Range("modified", cutoffTicks, long.MaxValue, true, true);
            
            COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult;
            try
            {
                searchResult = await _luceneIndexService.SearchAsync(
                    workspacePath,
                    query,
                    maxResults, // Use requested max results since we're filtering server-side
                    false, // No snippets needed for recent files
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute search query");
                throw;
            }
            
            // Process results and create recent file entries
            var recentFiles = new List<RecentFileInfo>();
            
            _logger.LogInformation("Search returned {TotalHits} total hits, processing {Count} hits. Query: {Query}, Cutoff: {Cutoff} ({CutoffDate})", 
                searchResult.TotalHits, searchResult.Hits.Count, query.ToString(), cutoffTime.Ticks, cutoffTime);
            
            if (searchResult.Hits.Count == 0 && searchResult.TotalHits > 0)
            {
                _logger.LogWarning("Search returned {TotalHits} total hits but Hits collection is empty!", searchResult.TotalHits);
            }
            
            foreach (var hit in searchResult.Hits)
            {
                _logger.LogDebug("Hit: FilePath={FilePath}, LastModified={LastMod}, HasLastMod={HasLastMod}", 
                    hit.FilePath, hit.LastModified, hit.LastModified.HasValue);
                    
                if (!string.IsNullOrEmpty(hit.FilePath) && hit.LastModified.HasValue)
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
                        SizeBytes = hit.Fields.TryGetValue("size", out var sizeStr) && long.TryParse(sizeStr, out var size) ? size : 0,
                        ModifiedAgo = DateTime.UtcNow - hit.LastModified.Value
                    });
                }
            }
            
            // Sort by modification time (most recent first) - Lucene already limited results
            recentFiles = recentFiles
                .OrderByDescending(f => f.LastModified)
                .ToList();
            
            _logger.LogDebug("Found {Count} recent files in the last {TimeFrame}", 
                recentFiles.Count, timeFrame);
            
            // Create RecentFilesResult for response builder
            var recentFilesResult = new ResponseBuilders.RecentFilesResult
            {
                Files = recentFiles.Select(f => new ResponseBuilders.RecentFileInfo
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
            
            // NEW: Send timeline visualizations to VS Code (if connected)
            if (_vscode.IsConnected && result.Success && recentFiles.Count > 0)
            {
                // Fire and forget - don't block the main response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Show timeline chart of recent activity
                        await SendTimelineVisualizationAsync(recentFiles, timeFrame);
                        
                        _logger.LogDebug("Successfully sent recent files timeline visualization to VS Code");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send recent files visualizations to VS Code (non-blocking)");
                    }
                }, cancellationToken);
            }
            
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
            return errorResult;
        }
    }
    
    private Query CreateRecentFilesQuery(DateTime cutoffTime)
    {
        // Convert to ticks (same format as stored in index)
        var cutoffTicks = cutoffTime.Ticks;
        
        // Create numeric range query for modification date (stored as Int64 ticks)
        var rangeQuery = NumericRangeQuery.NewInt64Range(
            "modified",  // Field name matches what's stored in index
            cutoffTicks, 
            long.MaxValue, // Up to now
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
        string numberPart;
        string unit;
        
        if (timeFrame.EndsWith("min"))
        {
            numberPart = timeFrame.Substring(0, timeFrame.Length - 3);
            unit = "min";
        }
        else
        {
            numberPart = timeFrame.Substring(0, timeFrame.Length - 1);
            unit = timeFrame.Substring(timeFrame.Length - 1);
        }
        
        if (!int.TryParse(numberPart, out var number) || number <= 0)
        {
            throw new ArgumentException($"Invalid time frame format: '{timeFrame}'. Expected format like '1h', '2d', '1w', '30min'");
        }
        
        return unit switch
        {
            "h" => TimeSpan.FromHours(number),
            "d" => TimeSpan.FromDays(number),
            "w" => TimeSpan.FromDays(number * 7),
            "min" => TimeSpan.FromMinutes(number),
            _ => throw new ArgumentException($"Unsupported time unit: '{unit}'. Supported: h (hours), d (days), w (weeks), min (minutes)")
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
        return result;
    }
    
    private async Task SendTimelineVisualizationAsync(List<RecentFileInfo> recentFiles, TimeSpan timeFrame)
    {
        try
        {
            // Group files by hour/day depending on timeframe
            var groupingUnit = timeFrame.TotalDays <= 1 ? "hour" : "day";
            
            // Convert files to timeline events
            var events = recentFiles.Select((file, index) => new
            {
                id = $"file-{index}",
                timestamp = file.LastModified.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                title = file.FileName,
                description = $"Modified in {file.Directory}",
                type = "file_modification",
                category = file.Extension.TrimStart('.'),
                metadata = new
                {
                    filePath = file.FilePath,
                    sizeBytes = file.SizeBytes,
                    extension = file.Extension
                }
            }).ToArray();
            
            await _vscode.SendVisualizationAsync(
                "timeline",
                new
                {
                    title = $"Recent File Activity ({recentFiles.Count} files)",
                    events = events,
                    groupBy = groupingUnit,
                    dateRange = new
                    {
                        start = recentFiles.Min(f => f.LastModified).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        end = recentFiles.Max(f => f.LastModified).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    }
                },
                new VisualizationHint { Interactive = true }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send timeline visualization");
        }
    }
    
    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} minutes";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hours";
        if (timeSpan.TotalDays < 7)
            return $"{(int)timeSpan.TotalDays} days";
        return $"{(int)timeSpan.TotalDays / 7} weeks";
    }
}

/// <summary>
/// Parameters for the RecentFiles tool - discover recently modified files to understand project activity and changes
/// </summary>
public class RecentFilesParameters
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
    /// Time frame for recent files - specifies how far back to look for modifications.
    /// </summary>
    /// <example>1h</example>
    /// <example>2d</example>
    /// <example>1w</example>
    [Description("Time frame for recent files. Examples: '1h' (1 hour), '2d' (2 days), '1w' (1 week). Default: '7d'")]
    public string? TimeFrame { get; set; }

    /// <summary>
    /// Maximum number of results to return (default: 50, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 50, max: 500)")]
    public int? MaxResults { get; set; }

    /// <summary>
    /// Comma-separated list of file extensions to filter results for focused analysis.
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