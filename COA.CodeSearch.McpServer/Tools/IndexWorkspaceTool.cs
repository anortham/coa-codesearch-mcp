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
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for indexing a workspace directory with token-optimized responses
/// </summary>
public class IndexWorkspaceTool : McpToolBase<IndexWorkspaceParameters, AIOptimizedResponse<IndexWorkspaceResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly IndexResponseBuilder _responseBuilder;
    private readonly FileWatcherService? _fileWatcherService;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<IndexWorkspaceTool> _logger;

    public IndexWorkspaceTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IFileIndexingService fileIndexingService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        IServiceProvider serviceProvider,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<IndexWorkspaceTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _fileIndexingService = fileIndexingService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new IndexResponseBuilder(null, storageService);
        _fileWatcherService = serviceProvider.GetService<FileWatcherService>();
        _vscode = vscode;
        _logger = logger;
    }

    public override string Name => ToolNames.IndexWorkspace;
    public override string Description => "Index a workspace directory with token-optimized progress reporting";
    public override ToolCategory Category => ToolCategory.Resources;

    protected override async Task<AIOptimizedResponse<IndexWorkspaceResult>> ExecuteInternalAsync(
        IndexWorkspaceParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        if (!Directory.Exists(workspacePath))
        {
            return CreateDirectoryNotFoundError(workspacePath);
        }
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // For index operations, we typically don't cache the result
        // since it's a state-changing operation

        var startTime = DateTime.UtcNow;
        
        try
        {
            // Initialize the index for this workspace
            var initResult = await _luceneIndexService.InitializeIndexAsync(workspacePath, cancellationToken);
            
            if (!initResult.Success)
            {
                var errorResult = new AIOptimizedResponse<IndexWorkspaceResult>
                {
                    Success = false,
                    Error = new COA.Mcp.Framework.Models.ErrorInfo
                    {
                        Code = "INIT_FAILED",
                        Message = initResult.ErrorMessage ?? "Failed to initialize index",
                        Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Check if another process is using the index",
                                "Verify write permissions for the index directory",
                                "Try with ForceRebuild option",
                                "Delete any existing write.lock files"
                            }
                        }
                    }
                };
                return errorResult;
            }

            // Check if force rebuild is requested or if it's a new index
            if (parameters.ForceRebuild == true || initResult.IsNewIndex)
            {
                _logger.LogInformation("Starting full index for workspace: {WorkspacePath}", workspacePath);
                
                // Force rebuild with new schema if explicitly requested
                if (parameters.ForceRebuild == true && !initResult.IsNewIndex)
                {
                    await _luceneIndexService.ForceRebuildIndexAsync(workspacePath, cancellationToken);
                    _logger.LogInformation("Force rebuild completed - new schema active for workspace: {WorkspacePath}", workspacePath);
                }
                
                // Index all files in the workspace
                var indexResult = await _fileIndexingService.IndexWorkspaceAsync(workspacePath, cancellationToken);
                
                if (!indexResult.Success)
                {
                    var indexErrorResult = new AIOptimizedResponse<IndexWorkspaceResult>
                    {
                        Success = false,
                        Error = new COA.Mcp.Framework.Models.ErrorInfo
                        {
                            Code = "INDEXING_FAILED",
                            Message = indexResult.ErrorMessage ?? "Failed to index files",
                            Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                            {
                                Steps = new[]
                                {
                                    "Check if files are accessible",
                                    "Verify file permissions",
                                    "Check available disk space",
                                    "Try indexing with different file extensions"
                                }
                            }
                        }
                    };
                    return indexErrorResult;
                }
                
                // Start watching this workspace for changes
                bool watcherEnabled = false;
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watcher for workspace: {WorkspacePath}", workspacePath);
                    watcherEnabled = true;
                }
                
                // Get statistics if available
                var stats = await _luceneIndexService.GetStatisticsAsync(workspacePath, cancellationToken);
                
                // Create IndexResult for response builder
                var indexResultData = new IndexResult
                {
                    Success = true,
                    WorkspacePath = workspacePath,
                    WorkspaceHash = initResult.WorkspaceHash,
                    IndexPath = initResult.IndexPath,
                    IsNewIndex = initResult.IsNewIndex,
                    FilesIndexed = indexResult.IndexedFileCount,
                    FilesSkipped = indexResult.SkippedFileCount,
                    TotalSizeBytes = stats.IndexSizeBytes, // Use stats for size
                    IndexTimeMs = (long)indexResult.Duration.TotalMilliseconds,
                    WatcherEnabled = watcherEnabled,
                    IndexedFiles = null, // We don't have detailed file list from IndexingResult
                    Statistics = new ResponseBuilders.IndexStatistics
                    {
                        DocumentCount = stats.DocumentCount,
                        DeletedDocumentCount = stats.DeletedDocumentCount,
                        SegmentCount = stats.SegmentCount,
                        IndexSizeBytes = stats.IndexSizeBytes,
                        FileTypeDistribution = stats.FileTypeDistribution
                    }
                };
                
                // Build response context
                var context = new ResponseContext
                {
                    ResponseMode = parameters.ResponseMode ?? "summary",
                    TokenLimit = parameters.MaxTokens ?? 8000,
                    StoreFullResults = true,
                    ToolName = Name,
                    CacheKey = cacheKey
                };
                
                // Use response builder to create optimized response
                var result = await _responseBuilder.BuildResponseAsync(indexResultData, context);
                
                // NEW: Send rich visualizations to VS Code (if connected)
                if ((parameters.ShowInVSCode ?? false) && _vscode.IsConnected && result.Success)
                {
                    // Fire and forget - don't block the main response
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendIndexVisualizationsAsync(indexResultData, parameters.ForceRebuild == true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send index visualization to VS Code");
                        }
                    }, cancellationToken);
                }
                
                return result;
            }
            else
            {
                // Index already exists and no force rebuild requested
                var documentCount = await _luceneIndexService.GetDocumentCountAsync(workspacePath, cancellationToken);
                var stats = await _luceneIndexService.GetStatisticsAsync(workspacePath, cancellationToken);
                
                // Start watching this workspace for changes (if not already watching)
                bool watcherEnabled = false;
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watcher for workspace: {WorkspacePath}", workspacePath);
                    watcherEnabled = true;
                }
                
                // Create IndexResult for existing index
                var indexResultData = new IndexResult
                {
                    Success = true,
                    WorkspacePath = workspacePath,
                    WorkspaceHash = initResult.WorkspaceHash,
                    IndexPath = initResult.IndexPath,
                    IsNewIndex = false,
                    FilesIndexed = documentCount,
                    FilesSkipped = 0,
                    TotalSizeBytes = stats.IndexSizeBytes,
                    IndexTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    WatcherEnabled = watcherEnabled,
                    IndexedFiles = null, // Don't list all files for existing index
                    Statistics = new ResponseBuilders.IndexStatistics
                    {
                        DocumentCount = stats.DocumentCount,
                        DeletedDocumentCount = stats.DeletedDocumentCount,
                        SegmentCount = stats.SegmentCount,
                        IndexSizeBytes = stats.IndexSizeBytes,
                        FileTypeDistribution = stats.FileTypeDistribution
                    }
                };
                
                // Build response context
                var context = new ResponseContext
                {
                    ResponseMode = parameters.ResponseMode ?? "summary",
                    TokenLimit = parameters.MaxTokens ?? 8000,
                    StoreFullResults = false, // No need to store for existing index
                    ToolName = Name,
                    CacheKey = cacheKey
                };
                
                // Use response builder to create optimized response
                var result = await _responseBuilder.BuildResponseAsync(indexResultData, context);
                
                // NEW: Send rich visualizations to VS Code (if connected)
                if ((parameters.ShowInVSCode ?? false) && _vscode.IsConnected && result.Success)
                {
                    // Fire and forget - don't block the main response
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendIndexVisualizationsAsync(indexResultData, false); // Not a rebuild
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send index visualization to VS Code");
                        }
                    }, cancellationToken);
                }
                
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index workspace: {WorkspacePath}", workspacePath);
            
            var exceptionErrorResult = new AIOptimizedResponse<IndexWorkspaceResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "INDEX_ERROR",
                    Message = $"Failed to index workspace: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Check logs for detailed error information",
                            "Ensure the workspace path is accessible",
                            "Verify you have write permissions for the index location",
                            "Try with a smaller workspace first"
                        }
                    }
                }
            };
            return exceptionErrorResult;
        }
    }
    
    /// <summary>
    /// Send index summary to VS Code as markdown visualization
    /// </summary>
    private async Task SendIndexVisualizationsAsync(IndexResult indexResult, bool isRebuild)
    {
        try
        {
            // Only show the markdown summary view
            var summary = GenerateIndexSummary(indexResult, isRebuild);
            await _vscode.SendVisualizationAsync(
                "markdown",
                new { content = summary },
                new VisualizationHint 
                { 
                    Interactive = false,
                    ConsolidateTabs = true
                }
            );

            _logger.LogDebug("Successfully sent index visualizations to VS Code for workspace: {WorkspacePath}", indexResult.WorkspacePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send index visualizations for workspace: {WorkspacePath}", indexResult.WorkspacePath);
            // Don't throw - visualization failure shouldn't break the main indexing functionality
        }
    }

    /// <summary>
    /// Generate index summary markdown content
    /// </summary>
    private string GenerateIndexSummary(IndexResult indexResult, bool isRebuild)
    {
        var summary = new System.Text.StringBuilder();
        var workspaceName = Path.GetFileName(indexResult.WorkspacePath);
        
        summary.AppendLine($"# Index {(isRebuild ? "Rebuild" : "Update")} Complete");
        summary.AppendLine();
        summary.AppendLine($"**Workspace:** `{workspaceName}`");
        summary.AppendLine($"**Path:** `{indexResult.WorkspacePath}`");
        summary.AppendLine($"**Operation:** {(indexResult.IsNewIndex ? "New Index Created" : "Existing Index Updated")}");
        summary.AppendLine();
        
        summary.AppendLine("## Results");
        summary.AppendLine();
        summary.AppendLine($"- **Files Indexed:** {indexResult.FilesIndexed:N0}");
        summary.AppendLine($"- **Files Skipped:** {indexResult.FilesSkipped:N0}");
        summary.AppendLine($"- **Total Size:** {Math.Round(indexResult.TotalSizeBytes / (1024.0 * 1024.0), 2):N2} MB");
        summary.AppendLine($"- **Index Time:** {Math.Round(indexResult.IndexTimeMs / 1000.0, 2):N2} seconds");
        summary.AppendLine($"- **File Watcher:** {(indexResult.WatcherEnabled ? "✅ Enabled" : "❌ Disabled")}");
        summary.AppendLine();
        
        if (indexResult.Statistics?.FileTypeDistribution?.Any() == true)
        {
            summary.AppendLine("## File Types Indexed");
            summary.AppendLine();
            var sortedTypes = indexResult.Statistics.FileTypeDistribution
                .OrderByDescending(kvp => kvp.Value)
                .Take(10);
            
            foreach (var fileType in sortedTypes)
            {
                summary.AppendLine($"- **{fileType.Key}**: {fileType.Value:N0} files");
            }
            summary.AppendLine();
        }
        
        summary.AppendLine("## Next Steps");
        summary.AppendLine();
        summary.AppendLine("You can now search this workspace using:");
        summary.AppendLine("- `text_search` - Search for content within files");
        summary.AppendLine("- `file_search` - Find files by name pattern");
        summary.AppendLine("- `recent_files` - View recently modified files");
        summary.AppendLine("- `similar_files` - Find files similar to a given file");
        
        return summary.ToString();
    }
    
    private AIOptimizedResponse<IndexWorkspaceResult> CreateDirectoryNotFoundError(string workspacePath)
    {
        var result = new AIOptimizedResponse<IndexWorkspaceResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "DIRECTORY_NOT_FOUND",
                Message = $"Directory does not exist: {workspacePath}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Verify the workspace path is correct",
                        "Check if the directory was moved or deleted",
                        "Create the directory if it should exist",
                        "Use an absolute path instead of relative"
                    }
                }
            },
            Insights = new List<string>
            {
                "The specified directory must exist before indexing",
                "Use an absolute path for best results"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = "verify_path",
                    Description = "Check if the path exists and is accessible",
                    Priority = 100
                }
            }
        };
        return result;
    }
}

/// <summary>
/// Parameters for the IndexWorkspace tool
/// </summary>
public class IndexWorkspaceParameters
{
    /// <summary>
    /// Path to the workspace directory to index
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to index")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether to force a full rebuild of the index
    /// </summary>
    [Description("Force a full rebuild of the index even if it exists (default: false)")]
    public bool? ForceRebuild { get; set; }

    /// <summary>
    /// File extensions to include in indexing
    /// </summary>
    [Description("File extensions to include (e.g., [\".cs\", \".js\"]). If not specified, uses default set.")]
    public string[]? IncludeExtensions { get; set; }

    /// <summary>
    /// File extensions to exclude from indexing
    /// </summary>
    [Description("File extensions to exclude from indexing")]
    public string[]? ExcludeExtensions { get; set; }
    
    /// <summary>
    /// Response mode: 'summary' or 'full' (default: summary)
    /// </summary>
    [Description("Response mode: 'summary' or 'full' (default: summary)")]
    public string? ResponseMode { get; set; }
    
    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Whether to show visualization in VS Code
    /// </summary>
    [Description("Whether to show visualization in VS Code (default: false)")]
    public bool? ShowInVSCode { get; set; }
}

/// <summary>
/// Result from the IndexWorkspace tool
/// </summary>
public class IndexWorkspaceResult : ToolResultBase
{
    public override string Operation => ToolNames.IndexWorkspace;

    /// <summary>
    /// The workspace path that was indexed
    /// </summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// The computed hash of the workspace path
    /// </summary>
    public string WorkspaceHash { get; set; } = string.Empty;

    /// <summary>
    /// Path to the index directory
    /// </summary>
    public string? IndexPath { get; set; }

    /// <summary>
    /// Whether a new index was created
    /// </summary>
    public bool IsNewIndex { get; set; }

    /// <summary>
    /// Number of files that were indexed
    /// </summary>
    public int IndexedFileCount { get; set; }

    /// <summary>
    /// Total number of files in the workspace
    /// </summary>
    public int TotalFileCount { get; set; }

    /// <summary>
    /// Time taken for the indexing operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Optional message with additional information
    /// </summary>
    public new string? Message { get; set; }
}