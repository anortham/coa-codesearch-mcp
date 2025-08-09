using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for indexing a workspace directory for search operations
/// </summary>
public class IndexWorkspaceTool : McpToolBase<IndexWorkspaceParameters, IndexWorkspaceResult>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly FileWatcherService? _fileWatcherService;
    private readonly ILogger<IndexWorkspaceTool> _logger;

    public IndexWorkspaceTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IFileIndexingService fileIndexingService,
        IServiceProvider serviceProvider,
        ILogger<IndexWorkspaceTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _fileIndexingService = fileIndexingService;
        _fileWatcherService = serviceProvider.GetService<FileWatcherService>();
        _logger = logger;
    }

    public override string Name => "index_workspace";
    public override string Description => "Index a workspace directory to enable fast text search. Creates or updates the search index for all supported files in the specified directory.";
    public override ToolCategory Category => ToolCategory.Resources;

    protected override async Task<IndexWorkspaceResult> ExecuteInternalAsync(
        IndexWorkspaceParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        if (!Directory.Exists(workspacePath))
        {
            return new IndexWorkspaceResult
            {
                Success = false,
                Error = CreateValidationErrorResult(
                    "index_workspace",
                    nameof(parameters.WorkspacePath),
                    $"Directory does not exist: {workspacePath}"
                ),
                WorkspacePath = workspacePath,
                WorkspaceHash = string.Empty,
                IndexedFileCount = 0,
                TotalFileCount = 0,
                Duration = TimeSpan.Zero
            };
        }

        var startTime = DateTime.UtcNow;
        
        try
        {
            // Initialize the index for this workspace
            var initResult = await _luceneIndexService.InitializeIndexAsync(workspacePath, cancellationToken);
            
            if (!initResult.Success)
            {
                return new IndexWorkspaceResult
                {
                    Success = false,
                    Error = CreateErrorResult(
                        "index_workspace",
                        initResult.ErrorMessage ?? "Failed to initialize index"
                    ),
                    WorkspacePath = workspacePath,
                    WorkspaceHash = initResult.WorkspaceHash,
                    IndexedFileCount = 0,
                    TotalFileCount = 0,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Check if force rebuild is requested or if it's a new index
            if (parameters.ForceRebuild == true || initResult.IsNewIndex)
            {
                _logger.LogInformation("Starting full index for workspace: {WorkspacePath}", workspacePath);
                
                // Clear existing index if force rebuild
                if (parameters.ForceRebuild == true && !initResult.IsNewIndex)
                {
                    await _luceneIndexService.ClearIndexAsync(workspacePath, cancellationToken);
                }
                
                // Index all files in the workspace
                var indexResult = await _fileIndexingService.IndexWorkspaceAsync(workspacePath, cancellationToken);
                
                if (!indexResult.Success)
                {
                    return new IndexWorkspaceResult
                    {
                        Success = false,
                        Error = CreateErrorResult(
                            "index_workspace",
                            indexResult.ErrorMessage ?? "Failed to index files"
                        ),
                        WorkspacePath = workspacePath,
                        WorkspaceHash = initResult.WorkspaceHash,
                        IndexedFileCount = indexResult.IndexedFileCount,
                        TotalFileCount = 0,
                        Duration = indexResult.Duration
                    };
                }
                
                var result = new IndexWorkspaceResult
                {
                    Success = true,
                    WorkspacePath = workspacePath,
                    WorkspaceHash = initResult.WorkspaceHash,
                    IndexPath = initResult.IndexPath,
                    IsNewIndex = initResult.IsNewIndex,
                    IndexedFileCount = indexResult.IndexedFileCount,
                    TotalFileCount = indexResult.IndexedFileCount,
                    Duration = indexResult.Duration,
                    Message = $"Indexed {indexResult.IndexedFileCount} files in {indexResult.Duration.TotalSeconds:F2} seconds"
                };
                
                // Start watching this workspace for changes
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watcher for workspace: {WorkspacePath}", workspacePath);
                }
                
                return result;
            }
            else
            {
                // Index already exists and no force rebuild requested
                var documentCount = await _luceneIndexService.GetDocumentCountAsync(workspacePath, cancellationToken);
                
                var result = new IndexWorkspaceResult
                {
                    Success = true,
                    WorkspacePath = workspacePath,
                    WorkspaceHash = initResult.WorkspaceHash,
                    IndexPath = initResult.IndexPath,
                    IsNewIndex = false,
                    IndexedFileCount = documentCount,
                    TotalFileCount = documentCount,
                    Duration = DateTime.UtcNow - startTime,
                    Message = $"Index already exists with {documentCount} documents. Use ForceRebuild to rebuild."
                };
                
                // Start watching this workspace for changes (if not already watching)
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watcher for workspace: {WorkspacePath}", workspacePath);
                }
                
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index workspace: {WorkspacePath}", workspacePath);
            
            return new IndexWorkspaceResult
            {
                Success = false,
                Error = CreateErrorResult(
                    "index_workspace",
                    ex.Message,
                    "Check logs for details and ensure the workspace path is accessible"
                ),
                WorkspacePath = workspacePath,
                WorkspaceHash = _pathResolutionService.ComputeWorkspaceHash(workspacePath),
                IndexedFileCount = 0,
                TotalFileCount = 0,
                Duration = DateTime.UtcNow - startTime
            };
        }
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
}

/// <summary>
/// Result from the IndexWorkspace tool
/// </summary>
public class IndexWorkspaceResult : ToolResultBase
{
    public override string Operation => "index_workspace";

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