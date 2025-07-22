namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides centralized path resolution for all .codesearch directory operations
/// </summary>
public interface IPathResolutionService
{
    /// <summary>
    /// Gets the base .codesearch directory path (e.g., "C:\source\project\.codesearch")
    /// </summary>
    string GetBasePath();
    
    /// <summary>
    /// Gets the index directory path for a specific workspace
    /// </summary>
    /// <param name="workspacePath">The workspace path to get the index for</param>
    /// <returns>The full path to the index directory</returns>
    string GetIndexPath(string workspacePath);
    
    /// <summary>
    /// Gets the logs directory path
    /// </summary>
    /// <returns>The full path to the logs directory (e.g., ".codesearch/logs")</returns>
    string GetLogsPath();
    
    /// <summary>
    /// Gets the project memory index path
    /// </summary>
    /// <returns>The full path to the project memory index</returns>
    string GetProjectMemoryPath();
    
    /// <summary>
    /// Gets the local memory index path
    /// </summary>
    /// <returns>The full path to the local memory index</returns>
    string GetLocalMemoryPath();
    
    /// <summary>
    /// Gets the workspace metadata file path
    /// </summary>
    /// <returns>The full path to the workspace metadata file</returns>
    string GetWorkspaceMetadataPath();
    
    /// <summary>
    /// Checks if a path is a protected memory index that should not be deleted
    /// </summary>
    /// <param name="indexPath">The path to check</param>
    /// <returns>True if the path is protected</returns>
    bool IsProtectedPath(string indexPath);
    
    /// <summary>
    /// Gets the backup directory path for a specific timestamp
    /// </summary>
    /// <param name="timestamp">Optional timestamp string (defaults to current UTC time)</param>
    /// <returns>The full path to the backup directory</returns>
    string GetBackupPath(string? timestamp = null);
    
    /// <summary>
    /// Gets the root index directory path (without workspace-specific subdirectory)
    /// </summary>
    /// <returns>The full path to the index root directory</returns>
    string GetIndexRootPath();
    
    /// <summary>
    /// Gets the TypeScript installation directory path
    /// </summary>
    /// <returns>The full path to the TypeScript installation directory</returns>
    string GetTypeScriptInstallPath();
}