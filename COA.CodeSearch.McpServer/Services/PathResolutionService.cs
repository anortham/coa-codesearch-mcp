using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Hybrid path resolution service for CodeSearch directory operations
/// Manages indexes in primary workspace's .coa/codesearch/indexes/ directory
/// Supports multiple project indexes within a single workspace session
/// </summary>
public class PathResolutionService : IPathResolutionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PathResolutionService> _logger;
    private readonly string _primaryWorkspacePath;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _metadataLocks = new();
    private static readonly SemaphoreSlim _lockCreationSemaphore = new(1, 1);
    
    public PathResolutionService(IConfiguration configuration, ILogger<PathResolutionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _primaryWorkspacePath = InitializePrimaryWorkspace();
    }
    
    private string InitializePrimaryWorkspace()
    {
        // First check if explicitly configured
        var configuredWorkspace = _configuration["CodeSearch:PrimaryWorkspace"];
        
        if (!string.IsNullOrWhiteSpace(configuredWorkspace))
        {
            try
            {
                var fullPath = Path.GetFullPath(configuredWorkspace);
                _logger.LogInformation("Using configured primary workspace: {Workspace}", fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve configured primary workspace: {Workspace}", configuredWorkspace);
            }
        }
        
        // Fall back to current directory
        var currentDir = Environment.CurrentDirectory;
        _logger.LogInformation("Using current directory as primary workspace: {Workspace}", currentDir);
        return currentDir;
    }
    
    public string GetBasePath()
    {
        // Return the .coa/codesearch directory in the primary workspace
        return Path.Combine(_primaryWorkspacePath, PathConstants.BaseDirectoryName, PathConstants.CodeSearchDirectoryName);
    }
    
    public string GetPrimaryWorkspacePath()
    {
        return _primaryWorkspacePath;
    }
    
    public string GetIndexPath(string workspacePath)
    {
        // Validate input path for security
        ValidateWorkspacePath(workspacePath);

        // Get the indexes root directory in primary workspace
        var indexRoot = GetIndexRootPath();

        // Compute workspace hash for uniqueness
        var hash = ComputeWorkspaceHash(workspacePath);

        // Create a descriptive directory name: "workspacename_hash"
        // This makes debugging easier while hash ensures uniqueness
        var workspaceName = GetSafeWorkspaceName(workspacePath);
        var descriptiveName = $"{workspaceName}_{hash}";

        var indexPath = Path.Combine(indexRoot, descriptiveName);

        // DO NOT create directory here - only compute the path
        // Directory creation should happen only when actually creating an index

        _logger.LogDebug("Resolved index path for {Workspace}: {IndexPath}", workspacePath, indexPath);

        return indexPath;
    }

    public string GetLuceneIndexPath(string workspacePath)
    {
        var indexPath = GetIndexPath(workspacePath);
        var lucenePath = Path.Combine(indexPath, "lucene");

        // Ensure lucene directory exists
        if (!Directory.Exists(lucenePath))
        {
            Directory.CreateDirectory(lucenePath);
        }

        return lucenePath;
    }

    public string GetEmbeddingsPath(string workspacePath)
    {
        var indexPath = GetIndexPath(workspacePath);
        var embeddingsPath = Path.Combine(indexPath, "embeddings");

        // Ensure embeddings directory exists
        if (!Directory.Exists(embeddingsPath))
        {
            Directory.CreateDirectory(embeddingsPath);
        }

        return embeddingsPath;
    }
    
    public string ComputeWorkspaceHash(string workspacePath)
    {
        // Normalize path for consistent hashing - use safe wrapper
        var fullPath = GetFullPath(workspacePath);
        var normalizedPath = fullPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        
        // Return truncated hash for directory name
        return hashString.Substring(0, PathConstants.WorkspaceHashLength);
    }
    
    private string GetSafeWorkspaceName(string workspacePath)
    {
        // Get the last directory name from the path - use safe wrappers
        var fullPath = GetFullPath(workspacePath);
        var workspaceName = GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        
        // If empty (e.g., root drive), use "root"
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            workspaceName = "root";
        }
        
        // Sanitize the name for use in file system
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            workspaceName = workspaceName.Replace(c, '_');
        }
        
        // Replace dots and spaces with underscores for cleaner names
        workspaceName = workspaceName.Replace('.', '_').Replace(' ', '_');
        
        // Truncate if too long (leave room for hash and underscore)
        if (workspaceName.Length > PathConstants.MaxSafeWorkspaceName)
        {
            workspaceName = workspaceName.Substring(0, PathConstants.MaxSafeWorkspaceName);
        }
        
        return workspaceName.ToLowerInvariant();
    }
    
    public string GetLogsPath()
    {
        return Path.Combine(GetBasePath(), PathConstants.LogsDirectoryName);
    }
    
    
    
    
    
    public string GetIndexRootPath()
    {
        return Path.Combine(GetBasePath(), PathConstants.IndexDirectoryName);
    }
    
    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
    
    // Safe file system operations implementation
    
    public bool DirectoryExists(string path)
    {
        return ExecuteExistenceCheck(nameof(DirectoryExists), path, () => Directory.Exists(path));
    }
    
    public bool FileExists(string path)
    {
        return ExecuteExistenceCheck(nameof(FileExists), path, () => File.Exists(path));
    }
    
    public string GetFullPath(string path)
    {
        return ExecutePathOperation(nameof(GetFullPath), path, () => Path.GetFullPath(path), path);
    }
    
    public string GetFileName(string path)
    {
        return ExecutePathOperation(nameof(GetFileName), path, () => Path.GetFileName(path) ?? string.Empty, string.Empty);
    }
    
    public string GetExtension(string path)
    {
        return ExecutePathOperation(nameof(GetExtension), path, () => Path.GetExtension(path) ?? string.Empty, string.Empty);
    }
    
    public string GetDirectoryName(string path)
    {
        return ExecutePathOperation(nameof(GetDirectoryName), path, () => Path.GetDirectoryName(path) ?? string.Empty, string.Empty);
    }
    
    public string GetRelativePath(string relativeTo, string path)
    {
        return ExecutePathOperation(nameof(GetRelativePath), relativeTo, path, () => Path.GetRelativePath(relativeTo, path), path);
    }
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        return ExecuteEnumerationOperation(nameof(EnumerateFiles), path, searchPattern, () => Directory.EnumerateFiles(path, searchPattern, searchOption));
    }
    
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        return ExecuteEnumerationOperation(nameof(EnumerateDirectories), path, searchPattern, () => Directory.EnumerateDirectories(path, searchPattern, searchOption));
    }
    
    // Error handling helper methods
    
    /// <summary>
    /// Executes a file system existence check safely, returning a boolean result with consistent error handling
    /// </summary>
    private bool ExecuteExistenceCheck(string operationName, string path, Func<bool> operation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("{Operation} called with null or empty path", operationName);
            return false;
        }

        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} for path: {Path}", operationName.ToLowerInvariant(), path);
            return false;
        }
    }
    
    /// <summary>
    /// Executes a path manipulation operation safely, returning string result with consistent error handling
    /// </summary>
    private string ExecutePathOperation(string operationName, string path, Func<string> operation, string fallbackValue)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("{Operation} called with null or empty path", operationName);
            return fallbackValue;
        }

        try
        {
            return operation() ?? fallbackValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} for path: {Path}", operationName.ToLowerInvariant(), path);
            return fallbackValue;
        }
    }
    
    /// <summary>
    /// Executes a path operation with two parameters safely
    /// </summary>
    private string ExecutePathOperation(string operationName, string path1, string path2, Func<string> operation, string fallbackValue)
    {
        if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
        {
            _logger.LogWarning("{Operation} called with null or empty path - Path1: {Path1}, Path2: {Path2}", operationName, path1, path2);
            return fallbackValue;
        }

        try
        {
            return operation() ?? fallbackValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} from '{Path1}' to '{Path2}'", operationName.ToLowerInvariant(), path1, path2);
            return fallbackValue;
        }
    }
    
    /// <summary>
    /// Executes an enumeration operation safely
    /// </summary>
    private IEnumerable<string> ExecuteEnumerationOperation(string operationName, string path, string pattern, Func<IEnumerable<string>> operation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("{Operation} called with null or empty path", operationName);
            return Enumerable.Empty<string>();
        }

        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} in path: {Path} with pattern: {Pattern}", operationName.ToLowerInvariant(), path, pattern);
            return Enumerable.Empty<string>();
        }
    }
    
    /// <summary>
    /// Validates workspace path for security and basic requirements
    /// </summary>
    /// <param name="workspacePath">The workspace path to validate</param>
    /// <exception cref="ArgumentException">Thrown when path is invalid or potentially dangerous</exception>
    private void ValidateWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path cannot be null or empty", nameof(workspacePath));
        }
        
        // Check for directory traversal attempts
        var normalizedPath = GetFullPath(workspacePath);
        if (normalizedPath.Contains("..") || workspacePath.Contains(".."))
        {
            _logger.LogWarning("Potential directory traversal attempt detected in path: {WorkspacePath}", workspacePath);
            throw new ArgumentException("Path contains invalid directory traversal sequences", nameof(workspacePath));
        }
        
        // Check for excessively long paths - only restrict on Windows (legacy limitation)
        if (OperatingSystem.IsWindows() && normalizedPath.Length > 240) // Allow some buffer for additional components
        {
            _logger.LogWarning("Path too long for Windows: {Length} characters: {WorkspacePath}", normalizedPath.Length, workspacePath);
            throw new ArgumentException($"Path too long for Windows: {normalizedPath.Length} characters. Enable long path support or use shorter paths.", nameof(workspacePath));
        }
        
        // Log validation success for debugging
        _logger.LogDebug("Path validation successful for: {WorkspacePath}", workspacePath);
    }
    
    /// <summary>
    /// Gets or creates a semaphore for thread-safe metadata file operations
    /// </summary>
    /// <param name="metadataPath">The path to the metadata file</param>
    /// <returns>A semaphore for the metadata file</returns>
    private SemaphoreSlim GetMetadataLock(string metadataPath)
    {
        // Use normalized path as key to ensure consistency
        var normalizedPath = GetFullPath(metadataPath).ToLowerInvariant();
        
        return _metadataLocks.GetOrAdd(normalizedPath, _ =>
        {
            _lockCreationSemaphore.Wait();
            try
            {
                // Double-check pattern to prevent race conditions
                return _metadataLocks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));
            }
            finally
            {
                _lockCreationSemaphore.Release();
            }
        });
    }
}