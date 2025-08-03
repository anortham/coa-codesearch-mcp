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
    /// Gets the checkpoint ID file path
    /// </summary>
    /// <returns>The full path to the checkpoint.id file</returns>
    string GetCheckpointIdPath();
    
    
    // Safe file system operations
    
    /// <summary>
    /// Safely checks if a directory exists
    /// </summary>
    /// <param name="path">The directory path to check</param>
    /// <returns>True if the directory exists, false otherwise</returns>
    bool DirectoryExists(string path);
    
    /// <summary>
    /// Safely checks if a file exists
    /// </summary>
    /// <param name="path">The file path to check</param>
    /// <returns>True if the file exists, false otherwise</returns>
    bool FileExists(string path);
    
    /// <summary>
    /// Safely gets the full path of a file or directory
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The full path, or the original path if normalization fails</returns>
    string GetFullPath(string path);
    
    /// <summary>
    /// Safely gets the file name from a path
    /// </summary>
    /// <param name="path">The path to extract the filename from</param>
    /// <returns>The filename, or empty string if extraction fails</returns>
    string GetFileName(string path);
    
    /// <summary>
    /// Safely gets the file extension from a path
    /// </summary>
    /// <param name="path">The path to extract the extension from</param>
    /// <returns>The extension, or empty string if extraction fails</returns>
    string GetExtension(string path);
    
    /// <summary>
    /// Safely gets the directory name from a path
    /// </summary>
    /// <param name="path">The path to extract the directory from</param>
    /// <returns>The directory path, or empty string if extraction fails</returns>
    string GetDirectoryName(string path);
    
    /// <summary>
    /// Safely gets a relative path
    /// </summary>
    /// <param name="relativeTo">The base path</param>
    /// <param name="path">The target path</param>
    /// <returns>The relative path, or the original path if calculation fails</returns>
    string GetRelativePath(string relativeTo, string path);
    
    /// <summary>
    /// Safely enumerates files in a directory
    /// </summary>
    /// <param name="path">The directory path</param>
    /// <returns>Enumerable of file paths, empty if enumeration fails</returns>
    IEnumerable<string> EnumerateFiles(string path);
    
    /// <summary>
    /// Safely enumerates directories in a directory
    /// </summary>
    /// <param name="path">The directory path</param>
    /// <returns>Enumerable of directory paths, empty if enumeration fails</returns>
    IEnumerable<string> EnumerateDirectories(string path);
}