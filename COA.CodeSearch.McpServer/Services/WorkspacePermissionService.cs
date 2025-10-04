using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing workspace permissions and tracking allowed editing locations.
/// Uses workspace.metadata.json files stored in index directories for persistence.
/// </summary>
public class WorkspacePermissionService : IWorkspacePermissionService
{
    private readonly IPathResolutionService _pathResolver;
    private readonly ILogger<WorkspacePermissionService> _logger;
    
    // Cache for workspace permission metadata
    private readonly ConcurrentDictionary<string, WorkspacePermissionMetadata> _metadataCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    // File names
    private const string PERMISSION_METADATA_FILE = "workspace.permissions.json";
    
    public WorkspacePermissionService(
        IPathResolutionService pathResolver,
        ILogger<WorkspacePermissionService> logger)
    {
        _pathResolver = pathResolver;
        _logger = logger;
    }

    /// <summary>
    /// Checks if editing is allowed for the specified file path
    /// </summary>
    public async Task<EditPermissionResult> IsEditAllowedAsync(EditPermissionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.GetFullPath(request.FilePath);
            
            // Find which workspace this file belongs to
            var workspace = await FindWorkspaceForFileAsync(filePath, cancellationToken);
            
            if (workspace == null)
            {
                // File is not in any known workspace
                return new EditPermissionResult
                {
                    Allowed = false,
                    Reason = $"File '{filePath}' is not in any indexed workspace",
                    Suggestions = 
                    [
                        $"Index the workspace containing this file first",
                        $"Or explicitly add workspace permissions for the directory containing this file"
                    ]
                };
            }

            // Check if editing is allowed in this workspace
            if (!workspace.Editable || !workspace.IsActive)
            {
                return new EditPermissionResult
                {
                    Allowed = false,
                    Reason = workspace.IsActive 
                        ? $"Editing is disabled for workspace '{workspace.Path}'"
                        : $"Workspace '{workspace.Path}' is inactive",
                    Workspace = workspace,
                    Suggestions = 
                    [
                        "Enable editing for this workspace",
                        "Reactivate the workspace if it was disabled"
                    ]
                };
            }

            // Edit is allowed
            return new EditPermissionResult
            {
                Allowed = true,
                Reason = $"File is in editable workspace '{workspace.Path}'",
                Workspace = workspace
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking edit permissions for {FilePath}", request.FilePath);
            return new EditPermissionResult
            {
                Allowed = false,
                Reason = $"Error checking permissions: {ex.Message}",
                Suggestions = ["Check file path accessibility and workspace configuration"]
            };
        }
    }

    /// <summary>
    /// Adds a workspace to the allowed list for editing
    /// </summary>
    public async Task<bool> AddAllowedWorkspaceAsync(string workspacePath, string reason, string addedBy = "user", CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(workspacePath);
            
            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Cannot add non-existent workspace: {WorkspacePath}", fullPath);
                return false;
            }

            // Get or create permission metadata for the primary workspace
            var primaryWorkspace = _pathResolver.GetPrimaryWorkspacePath();
            var metadata = await GetOrCreatePermissionMetadataAsync(primaryWorkspace, cancellationToken);
            
            // Check if workspace is already allowed
            var existingWorkspace = metadata.AllowedWorkspaces.FirstOrDefault(w => 
                string.Equals(w.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            
            if (existingWorkspace != null)
            {
                // Update existing entry
                existingWorkspace.Reason = reason;
                existingWorkspace.AddedBy = addedBy;
                existingWorkspace.IsActive = true;
                _logger.LogDebug("Updated existing allowed workspace: {WorkspacePath}", fullPath);
            }
            else
            {
                // Add new workspace
                var newWorkspace = new AllowedWorkspace
                {
                    Path = fullPath,
                    Hash = _pathResolver.ComputeWorkspaceHash(fullPath),
                    Indexed = IsWorkspaceIndexed(fullPath),
                    Editable = true,
                    AddedAt = DateTime.UtcNow,
                    Reason = reason,
                    AddedBy = addedBy,
                    IsActive = true
                };
                
                metadata.AllowedWorkspaces.Add(newWorkspace);
                _logger.LogInformation("Added new allowed workspace: {WorkspacePath}", fullPath);
            }

            metadata.LastUpdated = DateTime.UtcNow;
            
            // Save the updated metadata
            await SavePermissionMetadataAsync(primaryWorkspace, metadata, cancellationToken);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding allowed workspace {WorkspacePath}", workspacePath);
            return false;
        }
    }

    /// <summary>
    /// Gets the permission metadata for a workspace
    /// </summary>
    public async Task<WorkspacePermissionMetadata> GetPermissionMetadataAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(workspacePath);
        
        // Check cache first
        if (_metadataCache.TryGetValue(fullPath, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_metadataCache.TryGetValue(fullPath, out cachedMetadata))
                return cachedMetadata;

            var metadata = await LoadPermissionMetadataAsync(fullPath, cancellationToken);
            _metadataCache[fullPath] = metadata;
            return metadata;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Updates the index status for a workspace
    /// </summary>
    public async Task UpdateWorkspaceIndexStatusAsync(string workspacePath, bool indexed, CancellationToken cancellationToken = default)
    {
        try
        {
            var primaryWorkspace = _pathResolver.GetPrimaryWorkspacePath();
            var metadata = await GetPermissionMetadataAsync(primaryWorkspace, cancellationToken);
            
            var workspace = metadata.AllowedWorkspaces.FirstOrDefault(w =>
                string.Equals(w.Path, workspacePath, StringComparison.OrdinalIgnoreCase));
                
            if (workspace != null)
            {
                workspace.Indexed = indexed;
                metadata.LastUpdated = DateTime.UtcNow;
                
                await SavePermissionMetadataAsync(primaryWorkspace, metadata, cancellationToken);
                
                // Update cache
                _metadataCache[primaryWorkspace] = metadata;
                
                _logger.LogDebug("Updated index status for workspace {WorkspacePath}: {Indexed}", workspacePath, indexed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating index status for workspace {WorkspacePath}", workspacePath);
        }
    }

    #region Private Methods

    /// <summary>
    /// Finds which allowed workspace contains the specified file
    /// </summary>
    private async Task<AllowedWorkspace?> FindWorkspaceForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var primaryWorkspace = _pathResolver.GetPrimaryWorkspacePath();
        var metadata = await GetPermissionMetadataAsync(primaryWorkspace, cancellationToken);
        
        // Find the workspace that contains this file (longest path match)
        return metadata.AllowedWorkspaces
            .Where(w => w.IsActive && filePath.StartsWith(w.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.Path.Length) // Longest path wins
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets or creates permission metadata for a workspace
    /// </summary>
    private async Task<WorkspacePermissionMetadata> GetOrCreatePermissionMetadataAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var metadata = await GetPermissionMetadataAsync(workspacePath, cancellationToken);
        
        // Ensure the primary workspace itself is in the allowed list
        var primaryPath = Path.GetFullPath(workspacePath);
        if (!metadata.AllowedWorkspaces.Any(w => string.Equals(w.Path, primaryPath, StringComparison.OrdinalIgnoreCase)))
        {
            metadata.AllowedWorkspaces.Insert(0, new AllowedWorkspace
            {
                Path = primaryPath,
                Hash = _pathResolver.ComputeWorkspaceHash(primaryPath),
                Indexed = IsWorkspaceIndexed(primaryPath),
                Editable = true,
                AddedAt = DateTime.UtcNow,
                Reason = "Primary workspace - automatically added",
                AddedBy = "system",
                IsActive = true
            });
        }
        
        return metadata;
    }

    /// <summary>
    /// Loads permission metadata from file or creates default
    /// </summary>
    private async Task<WorkspacePermissionMetadata> LoadPermissionMetadataAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var metadataPath = GetPermissionMetadataPath(workspacePath);
        
        if (!File.Exists(metadataPath))
        {
            return new WorkspacePermissionMetadata
            {
                PrimaryWorkspace = Path.GetFullPath(workspacePath),
                AllowedWorkspaces = new List<AllowedWorkspace>()
            };
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<WorkspacePermissionMetadata>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            return metadata ?? new WorkspacePermissionMetadata 
            { 
                PrimaryWorkspace = Path.GetFullPath(workspacePath) 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading permission metadata from {MetadataPath}, using default", metadataPath);
            return new WorkspacePermissionMetadata
            {
                PrimaryWorkspace = Path.GetFullPath(workspacePath)
            };
        }
    }

    /// <summary>
    /// Saves permission metadata to file
    /// </summary>
    private async Task SavePermissionMetadataAsync(string workspacePath, WorkspacePermissionMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataPath = GetPermissionMetadataPath(workspacePath);
        var metadataDir = Path.GetDirectoryName(metadataPath);
        
        if (!string.IsNullOrEmpty(metadataDir) && !Directory.Exists(metadataDir))
        {
            Directory.CreateDirectory(metadataDir);
        }

        // Use centralized JSON configuration with UTF-8 support and indenting for readability
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        
        // Update cache
        _metadataCache[Path.GetFullPath(workspacePath)] = metadata;
    }

    /// <summary>
    /// Gets the path to the permission metadata file for a workspace
    /// </summary>
    private string GetPermissionMetadataPath(string workspacePath)
    {
        var basePath = _pathResolver.GetBasePath();
        return Path.Combine(basePath, PERMISSION_METADATA_FILE);
    }

    /// <summary>
    /// Checks if a workspace has been indexed
    /// </summary>
    private bool IsWorkspaceIndexed(string workspacePath)
    {
        try
        {
            var luceneIndexPath = _pathResolver.GetLuceneIndexPath(workspacePath);
            return Directory.Exists(luceneIndexPath) && Directory.GetFiles(luceneIndexPath, "*.cfs").Any();
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

/// <summary>
/// Interface for workspace permission management
/// </summary>
public interface IWorkspacePermissionService
{
    Task<EditPermissionResult> IsEditAllowedAsync(EditPermissionRequest request, CancellationToken cancellationToken = default);
    Task<bool> AddAllowedWorkspaceAsync(string workspacePath, string reason, string addedBy = "user", CancellationToken cancellationToken = default);
    Task<WorkspacePermissionMetadata> GetPermissionMetadataAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task UpdateWorkspaceIndexStatusAsync(string workspacePath, bool indexed, CancellationToken cancellationToken = default);
}