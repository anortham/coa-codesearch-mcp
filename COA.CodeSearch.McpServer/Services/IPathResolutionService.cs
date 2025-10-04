namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides centralized path resolution for all CodeSearch directory operations
/// </summary>
public interface IPathResolutionService
{
    /// <summary>
    /// Gets the base CodeSearch directory path (e.g., "~/.coa/codesearch")
    /// </summary>
    string GetBasePath();
    
    /// <summary>
    /// Gets the primary workspace path (configured or current directory)
    /// </summary>
    /// <returns>The primary workspace path</returns>
    string GetPrimaryWorkspacePath();
    
    /// <summary>
    /// Gets the index directory path for a specific workspace
    /// </summary>
    /// <param name="workspacePath">The workspace path to get the index for</param>
    /// <returns>The full path to the index directory</returns>
    string GetIndexPath(string workspacePath);

    /// <summary>
    /// Gets the Lucene index subdirectory path for a workspace
    /// </summary>
    /// <param name="workspacePath">The workspace path</param>
    /// <returns>The full path to the lucene subdirectory</returns>
    string GetLuceneIndexPath(string workspacePath);

    /// <summary>
    /// Gets the embeddings subdirectory path for a workspace
    /// </summary>
    /// <param name="workspacePath">The workspace path</param>
    /// <returns>The full path to the embeddings subdirectory</returns>
    string GetEmbeddingsPath(string workspacePath);
    
    /// <summary>
    /// Gets the logs directory path
    /// </summary>
    /// <returns>The full path to the logs directory</returns>
    string GetLogsPath();
    
    
    /// <summary>
    /// Gets the root index directory path (without workspace-specific subdirectory)
    /// </summary>
    /// <returns>The full path to the index root directory</returns>
    string GetIndexRootPath();
    
    /// <summary>
    /// Computes a hash for the workspace path to use as directory name
    /// </summary>
    /// <param name="workspacePath">The workspace path to hash</param>
    /// <returns>8-character hash string</returns>
    string ComputeWorkspaceHash(string workspacePath);
    
    /// <summary>
    /// Ensures a directory exists, creating it if necessary
    /// </summary>
    /// <param name="path">The directory path to ensure exists</param>
    void EnsureDirectoryExists(string path);
    
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
    /// <param name="searchPattern">Optional search pattern (default: "*")</param>
    /// <param name="searchOption">Search option for subdirectories</param>
    /// <returns>Enumerable of file paths, empty if enumeration fails</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
    
    /// <summary>
    /// Safely enumerates directories in a directory
    /// </summary>
    /// <param name="path">The directory path</param>
    /// <param name="searchPattern">Optional search pattern (default: "*")</param>
    /// <param name="searchOption">Search option for subdirectories</param>
    /// <returns>Enumerable of directory paths, empty if enumeration fails</returns>
    IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
}