using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing the global workspace registry
/// Provides centralized workspace metadata management with caching and async operations
/// </summary>
public interface IWorkspaceRegistryService
{
    #region Registry Operations
    
    /// <summary>
    /// Load the global registry from disk (with caching)
    /// </summary>
    /// <returns>The workspace registry</returns>
    Task<WorkspaceRegistry> LoadRegistryAsync();
    
    /// <summary>
    /// Save the registry to disk (atomic operation)
    /// </summary>
    /// <param name="registry">Registry to save</param>
    Task SaveRegistryAsync(WorkspaceRegistry registry);
    
    /// <summary>
    /// Get registry or create empty one if it doesn't exist
    /// </summary>
    /// <returns>The workspace registry</returns>
    Task<WorkspaceRegistry> GetOrCreateRegistryAsync();
    
    /// <summary>
    /// Invalidate cache and force reload from disk
    /// </summary>
    Task RefreshRegistryAsync();
    
    #endregion
    
    #region Workspace Management
    
    /// <summary>
    /// Register a workspace in the global registry
    /// </summary>
    /// <param name="workspacePath">Original workspace path</param>
    /// <returns>The registered workspace entry, or null if registration failed</returns>
    Task<WorkspaceEntry?> RegisterWorkspaceAsync(string workspacePath);
    
    /// <summary>
    /// Unregister a workspace from the registry
    /// </summary>
    /// <param name="workspaceHash">Hash of workspace to unregister</param>
    /// <returns>True if workspace was unregistered</returns>
    Task<bool> UnregisterWorkspaceAsync(string workspaceHash);
    
    /// <summary>
    /// Get workspace entry by hash
    /// </summary>
    /// <param name="hash">Workspace hash</param>
    /// <returns>Workspace entry or null if not found</returns>
    Task<WorkspaceEntry?> GetWorkspaceByHashAsync(string hash);
    
    /// <summary>
    /// Get workspace entry by original path
    /// </summary>
    /// <param name="path">Original workspace path</param>
    /// <returns>Workspace entry or null if not found</returns>
    Task<WorkspaceEntry?> GetWorkspaceByPathAsync(string path);
    
    /// <summary>
    /// Get workspace entry by index directory name
    /// </summary>
    /// <param name="directoryName">Index directory name</param>
    /// <returns>Workspace entry or null if not found</returns>
    Task<WorkspaceEntry?> GetWorkspaceByDirectoryNameAsync(string directoryName);
    
    /// <summary>
    /// Get all registered workspaces
    /// </summary>
    /// <returns>List of all workspace entries</returns>
    Task<IReadOnlyList<WorkspaceEntry>> GetAllWorkspacesAsync();
    
    /// <summary>
    /// Check if a workspace is registered
    /// </summary>
    /// <param name="workspacePath">Workspace path to check</param>
    /// <returns>True if workspace is registered</returns>
    Task<bool> IsWorkspaceRegisteredAsync(string workspacePath);
    
    /// <summary>
    /// Check if a directory name corresponds to a registered workspace
    /// </summary>
    /// <param name="directoryName">Directory name to check</param>
    /// <returns>True if directory belongs to registered workspace</returns>
    Task<bool> IsDirectoryRegisteredAsync(string directoryName);
    
    #endregion
    
    #region Workspace Status Updates
    
    /// <summary>
    /// Update workspace status
    /// </summary>
    /// <param name="hash">Workspace hash</param>
    /// <param name="status">New status</param>
    Task UpdateWorkspaceStatusAsync(string hash, WorkspaceStatus status);
    
    /// <summary>
    /// Update last accessed time for workspace
    /// </summary>
    /// <param name="hash">Workspace hash</param>
    Task UpdateLastAccessedAsync(string hash);
    
    /// <summary>
    /// Update workspace statistics (document count, index size)
    /// </summary>
    /// <param name="hash">Workspace hash</param>
    /// <param name="documentCount">Number of documents</param>
    /// <param name="indexSizeBytes">Size in bytes</param>
    Task UpdateWorkspaceStatisticsAsync(string hash, int documentCount, long indexSizeBytes);
    
    
    #endregion
    
    #region Orphan Management
    
    /// <summary>
    /// Mark an index directory as orphaned
    /// </summary>
    /// <param name="directoryName">Directory name</param>
    /// <param name="reason">Reason for orphaning</param>
    /// <param name="attemptedPath">Path that was attempted to be resolved (optional)</param>
    /// <returns>The orphaned index entry</returns>
    Task<OrphanedIndex> MarkAsOrphanedAsync(string directoryName, OrphanReason reason, string? attemptedPath = null);
    
    /// <summary>
    /// Get all orphaned indexes
    /// </summary>
    /// <returns>List of orphaned indexes</returns>
    Task<IReadOnlyList<OrphanedIndex>> GetOrphanedIndexesAsync();
    
    /// <summary>
    /// Remove orphaned index from registry (when cleaned up)
    /// </summary>
    /// <param name="directoryName">Directory name of orphaned index</param>
    /// <returns>True if orphaned index was removed</returns>
    Task<bool> RemoveOrphanedIndexAsync(string directoryName);
    
    /// <summary>
    /// Get orphaned indexes that are ready for cleanup (past scheduled deletion time)
    /// </summary>
    /// <returns>List of orphaned indexes ready for deletion</returns>
    Task<IReadOnlyList<OrphanedIndex>> GetOrphansReadyForCleanupAsync();
    
    #endregion
    
    
    #region Migration
    
    /// <summary>
    /// Migrate from individual metadata files to global registry
    /// </summary>
    /// <returns>Migration result with statistics</returns>
    Task<MigrationResult> MigrateFromIndividualMetadataAsync();
    
    /// <summary>
    /// Check if migration is needed
    /// </summary>
    /// <returns>True if registry doesn't exist and migration is needed</returns>
    Task<bool> IsMigrationNeededAsync();
    
    #endregion
}

/// <summary>
/// Result of migrating from individual metadata files
/// </summary>
public class MigrationResult
{
    /// <summary>
    /// Whether migration was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Number of workspaces migrated successfully
    /// </summary>
    public int WorkspacesMigrated { get; set; }
    
    /// <summary>
    /// Number of orphaned indexes discovered
    /// </summary>
    public int OrphansDiscovered { get; set; }
    
    /// <summary>
    /// Any errors encountered during migration
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Detailed migration log
    /// </summary>
    public List<string> Log { get; set; } = new();
}