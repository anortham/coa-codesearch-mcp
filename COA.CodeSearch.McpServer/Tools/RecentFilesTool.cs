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
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for finding recently modified files with token optimization
/// </summary>
public class RecentFilesTool : CodeSearchToolBase<RecentFilesParameters, AIOptimizedResponse<RecentFilesResult>>
{
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly RecentFilesResponseBuilder _responseBuilder;
    private readonly ILogger<RecentFilesTool> _logger;

    /// <summary>
    /// Initializes a new instance of the RecentFilesTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="sqliteService">SQLite symbol service for efficient file queries</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="logger">Logger instance</param>
    public RecentFilesTool(
        IServiceProvider serviceProvider,
        ISQLiteSymbolService sqliteService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<RecentFilesTool> logger) : base(serviceProvider, logger)
    {
        _sqliteService = sqliteService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new RecentFilesResponseBuilder(null, storageService);
        _logger = logger;
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
        var maxResults = ValidateRange(parameters.MaxResults, 1, 500, nameof(parameters.MaxResults));
        
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
            // Check if SQLite database exists
            var dbExists = _sqliteService.DatabaseExists(workspacePath);
            _logger.LogInformation("SQLite database exists for {Workspace}: {Exists}", workspacePath, dbExists);

            if (!dbExists)
            {
                return CreateNoIndexError(workspacePath);
            }

            // Query SQLite for recent files (much faster than Lucene!)
            // Convert to Unix seconds (database stores timestamps as Unix epochs, not .NET Ticks)
            var cutoffUnixSeconds = new DateTimeOffset(cutoffTime).ToUnixTimeSeconds();
            var fileRecords = await _sqliteService.GetRecentFilesAsync(
                workspacePath,
                cutoffUnixSeconds,
                maxResults,
                parameters.ExtensionFilter,
                cancellationToken);

            _logger.LogInformation("SQLite query returned {Count} files modified after {CutoffDate}",
                fileRecords.Count, cutoffTime);

            // Convert FileRecord to RecentFileInfo
            // Note: record.LastModified is stored as Unix seconds, not .NET Ticks
            var recentFiles = fileRecords.Select(record => new RecentFileInfo
            {
                FilePath = record.Path,
                FileName = Path.GetFileName(record.Path),
                Directory = Path.GetDirectoryName(record.Path) ?? "",
                Extension = Path.GetExtension(record.Path),
                LastModified = DateTimeOffset.FromUnixTimeSeconds(record.LastModified).UtcDateTime,
                SizeBytes = record.Size,
                ModifiedAgo = DateTime.UtcNow - DateTimeOffset.FromUnixTimeSeconds(record.LastModified).UtcDateTime
            }).ToList();
            
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
                ResponseMode = parameters.ResponseMode,
                TokenLimit = parameters.MaxTokens,
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
            return errorResult;
        }
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
    public int MaxResults { get; set; } = 50;

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