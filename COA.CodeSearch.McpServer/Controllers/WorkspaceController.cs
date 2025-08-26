using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using COA.CodeSearch.McpServer.Models.Api;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;

namespace COA.CodeSearch.McpServer.Controllers;

/// <summary>
/// API controller for workspace management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorkspaceController : ControllerBase
{
    private readonly ILuceneIndexService _luceneService;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IPathResolutionService _pathResolver;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        ILuceneIndexService luceneService,
        IFileIndexingService fileIndexingService,
        IPathResolutionService pathResolver,
        ILogger<WorkspaceController> logger)
    {
        _luceneService = luceneService;
        _fileIndexingService = fileIndexingService;
        _pathResolver = pathResolver;
        _logger = logger;
    }

    /// <summary>
    /// List all indexed workspaces
    /// </summary>
    /// <returns>List of workspace information</returns>
    [HttpGet]
    public Task<ActionResult<WorkspacesResponse>> ListWorkspaces()
    {
        try
        {
            _logger.LogDebug("Listing all indexed workspaces");

            var workspaces = new List<WorkspaceInfo>();
            
            // Get all index directories
            var indexRootPath = _pathResolver.GetIndexRootPath();
            if (!Directory.Exists(indexRootPath))
            {
                return Task.FromResult<ActionResult<WorkspacesResponse>>(Ok(new WorkspacesResponse
                {
                    Workspaces = workspaces,
                    TotalCount = 0
                }));
            }

            var indexDirectories = Directory.GetDirectories(indexRootPath);
            
            foreach (var indexDir in indexDirectories)
            {
                try
                {
                    // Try to resolve the original workspace path from the index directory
                    var originalPath = _pathResolver.TryResolveWorkspacePath(indexDir);
                    if (string.IsNullOrEmpty(originalPath))
                    {
                        // Fall back to directory name if resolution fails, but mark it as unresolved
                        var dirName = Path.GetFileName(indexDir);
                        originalPath = dirName?.Contains('_') == true 
                            ? $"[Unresolved: {dirName}]" // Clearly mark hashed names as unresolved
                            : dirName ?? "[Unknown]";
                        _logger.LogWarning("Could not resolve original workspace path for index directory: {IndexDir}", indexDir);
                    }
                    
                    var metadataFile = Path.Combine(indexDir, "workspace_metadata.json");
                    
                    var workspaceInfo = new WorkspaceInfo
                    {
                        Path = originalPath, // Now returns the actual workspace path
                        IsIndexed = true,
                        FileCount = 0,
                        LastIndexed = Directory.GetLastWriteTime(indexDir),
                        IndexSizeBytes = GetDirectorySize(indexDir)
                    };

                    // Try to get more detailed info from metadata if available
                    if (System.IO.File.Exists(metadataFile))
                    {
                        try
                        {
                            var json = System.IO.File.ReadAllText(metadataFile);
                            var metadata = System.Text.Json.JsonSerializer.Deserialize<Models.WorkspaceIndexInfo>(json);
                            if (metadata != null)
                            {
                                workspaceInfo.Path = metadata.OriginalPath; // Use metadata path if available
                                workspaceInfo.FileCount = (int)metadata.DocumentCount;
                                workspaceInfo.LastIndexed = metadata.LastModified;
                                workspaceInfo.IndexSizeBytes = metadata.IndexSizeBytes; // Use metadata index size if available
                            }
                        }
                        catch (Exception metadataEx)
                        {
                            _logger.LogWarning(metadataEx, "Failed to parse metadata for workspace: {IndexDir}", indexDir);
                            // Continue with directory-based info
                            workspaceInfo.FileCount = Directory.GetFiles(indexDir, "*", SearchOption.AllDirectories).Length;
                        }
                    }

                    workspaces.Add(workspaceInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get info for workspace index: {IndexDir}", indexDir);
                }
            }

            var response = new WorkspacesResponse
            {
                Workspaces = workspaces.OrderBy(w => w.Path).ToList(),
                TotalCount = workspaces.Count
            };

            return Task.FromResult<ActionResult<WorkspacesResponse>>(Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing workspaces");
            return Task.FromResult<ActionResult<WorkspacesResponse>>(StatusCode(500, new { error = "Internal server error while listing workspaces" }));
        }
    }

    /// <summary>
    /// Get status information for a specific workspace
    /// </summary>
    /// <param name="workspacePath">Path to the workspace</param>
    /// <returns>Workspace status information</returns>
    [HttpGet("status")]
    public async Task<ActionResult<WorkspaceInfo>> GetWorkspaceStatus(
        [FromQuery, Required] string workspacePath)
    {
        try
        {
            _logger.LogDebug("Getting status for workspace: {WorkspacePath}", workspacePath);

            var normalizedPath = Path.GetFullPath(workspacePath);
            var indexPath = _pathResolver.GetIndexPath(normalizedPath);
            
            var workspaceInfo = new WorkspaceInfo
            {
                Path = normalizedPath,
                IsIndexed = Directory.Exists(indexPath),
                FileCount = 0,
                LastIndexed = null,
                IndexSizeBytes = 0
            };

            if (workspaceInfo.IsIndexed)
            {
                workspaceInfo.LastIndexed = Directory.GetLastWriteTime(indexPath);
                workspaceInfo.IndexSizeBytes = GetDirectorySize(indexPath);
                
                // Try to get file count from index
                try
                {
                    workspaceInfo.FileCount = await _luceneService.GetDocumentCountAsync(normalizedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read document count from index for workspace: {Path}", normalizedPath);
                }
            }

            return Ok(workspaceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workspace status: {WorkspacePath}", workspacePath);
            return StatusCode(500, new { error = "Internal server error while getting workspace status" });
        }
    }

    /// <summary>
    /// Index or re-index a workspace
    /// </summary>
    /// <param name="workspacePath">Path to the workspace to index</param>
    /// <param name="force">Whether to force re-indexing if already indexed</param>
    /// <returns>Indexing operation result</returns>
    [HttpPost("index")]
    public Task<ActionResult> IndexWorkspace(
        [FromQuery, Required] string workspacePath,
        [FromQuery] bool force = false)
    {
        try
        {
            _logger.LogInformation("Starting indexing for workspace: {WorkspacePath}, Force: {Force}", 
                workspacePath, force);

            var normalizedPath = Path.GetFullPath(workspacePath);
            
            if (!Directory.Exists(normalizedPath))
            {
                return Task.FromResult<ActionResult>(BadRequest(new { error = $"Workspace path does not exist: {normalizedPath}" }));
            }

            // Check if already indexed
            var indexPath = _pathResolver.GetIndexPath(normalizedPath);
            if (!force && Directory.Exists(indexPath))
            {
                return Task.FromResult<ActionResult>(Conflict(new { error = "Workspace is already indexed. Use force=true to re-index." }));
            }

            // Start indexing operation
            var indexingTask = _fileIndexingService.IndexWorkspaceAsync(normalizedPath);
            
            // For API, we'll start the indexing and return immediately
            // In a real implementation, you might want to track indexing status
            _ = Task.Run(async () =>
            {
                try
                {
                    await indexingTask;
                    _logger.LogInformation("Indexing completed for workspace: {WorkspacePath}", normalizedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Indexing failed for workspace: {WorkspacePath}", normalizedPath);
                }
            });

            return Task.FromResult<ActionResult>(Accepted(new 
            { 
                message = "Indexing started", 
                workspace = normalizedPath,
                indexPath = indexPath
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workspace indexing: {WorkspacePath}", workspacePath);
            return Task.FromResult<ActionResult>(StatusCode(500, new { error = "Internal server error while starting workspace indexing" }));
        }
    }

    /// <summary>
    /// Refresh the index for a workspace (incremental update)
    /// </summary>
    /// <param name="workspacePath">Path to the workspace to refresh</param>
    /// <returns>Refresh operation result</returns>
    [HttpPost("refresh")]
    public Task<ActionResult> RefreshWorkspace(
        [FromQuery, Required] string workspacePath)
    {
        try
        {
            _logger.LogInformation("Refreshing workspace: {WorkspacePath}", workspacePath);

            var normalizedPath = Path.GetFullPath(workspacePath);
            
            if (!Directory.Exists(normalizedPath))
            {
                return Task.FromResult<ActionResult>(BadRequest(new { error = $"Workspace path does not exist: {normalizedPath}" }));
            }

            var indexPath = _pathResolver.GetIndexPath(normalizedPath);
            if (!Directory.Exists(indexPath))
            {
                return Task.FromResult<ActionResult>(BadRequest(new { error = "Workspace is not indexed. Use POST /index to create initial index." }));
            }

            // For now, treat refresh as a re-index
            // TODO: Implement proper incremental indexing
            var indexingTask = _fileIndexingService.IndexWorkspaceAsync(normalizedPath);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await indexingTask;
                    _logger.LogInformation("Refresh completed for workspace: {WorkspacePath}", normalizedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Refresh failed for workspace: {WorkspacePath}", normalizedPath);
                }
            });

            return Task.FromResult<ActionResult>(Accepted(new 
            { 
                message = "Workspace refresh started", 
                workspace = normalizedPath 
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing workspace: {WorkspacePath}", workspacePath);
            return Task.FromResult<ActionResult>(StatusCode(500, new { error = "Internal server error while refreshing workspace" }));
        }
    }

    /// <summary>
    /// Remove index for a workspace
    /// </summary>
    /// <param name="workspacePath">Path to the workspace to remove from index</param>
    /// <returns>Removal operation result</returns>
    [HttpDelete("index")]
    public async Task<ActionResult> RemoveWorkspaceIndex(
        [FromQuery, Required] string workspacePath)
    {
        try
        {
            _logger.LogInformation("Removing index for workspace: {WorkspacePath}", workspacePath);

            var normalizedPath = Path.GetFullPath(workspacePath);
            var indexPath = _pathResolver.GetIndexPath(normalizedPath);
            
            if (!Directory.Exists(indexPath))
            {
                return NotFound(new { error = "Workspace index does not exist" });
            }

            // Clear the index first (this should close any open readers/writers)
            try
            {
                await _luceneService.ClearIndexAsync(normalizedPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear index before removal: {Path}", normalizedPath);
            }
            
            // Remove the index directory
            Directory.Delete(indexPath, recursive: true);
            
            _logger.LogInformation("Successfully removed index for workspace: {WorkspacePath}", normalizedPath);

            return Ok(new 
            { 
                message = "Workspace index removed successfully", 
                workspace = normalizedPath 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing workspace index: {WorkspacePath}", workspacePath);
            return StatusCode(500, new { error = "Internal server error while removing workspace index" });
        }
    }

    #region Private Helper Methods

    private long GetDirectorySize(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return 0;

            var directoryInfo = new DirectoryInfo(directoryPath);
            return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate directory size: {Path}", directoryPath);
            return 0;
        }
    }

    #endregion
}