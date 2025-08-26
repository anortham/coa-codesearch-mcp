using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Constants;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Centralized path resolution service for all CodeSearch directory operations
/// Manages paths in ~/.coa/codesearch for centralized multi-workspace support
/// </summary>
public class PathResolutionService : IPathResolutionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PathResolutionService> _logger;
    private readonly string _basePath;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _metadataLocks = new();
    private static readonly SemaphoreSlim _lockCreationSemaphore = new(1, 1);
    
    public PathResolutionService(IConfiguration configuration, ILogger<PathResolutionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _basePath = InitializeBasePath();
    }
    
    private string InitializeBasePath()
    {
        var configuredPath = _configuration[PathConstants.BasePathConfigKey] ?? PathConstants.DefaultBasePath;
        
        // Validate configuration value
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            _logger.LogWarning("Base path configuration is null or empty, using default: {DefaultPath}", PathConstants.DefaultBasePath);
            configuredPath = PathConstants.DefaultBasePath;
        }
        
        // Handle tilde expansion for cross-platform support
        if (configuredPath.StartsWith("~/"))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(homeDirectory))
            {
                _logger.LogError("Unable to determine user profile directory for tilde expansion");
                throw new InvalidOperationException("Unable to determine user profile directory for tilde expansion");
            }
            configuredPath = Path.Combine(homeDirectory, configuredPath.Substring(2));
        }
        
        try
        {
            var fullPath = Path.GetFullPath(configuredPath);
            _logger.LogInformation("Initialized base path: {BasePath}", fullPath);
            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve full path for configured base path: {ConfiguredPath}", configuredPath);
            throw new InvalidOperationException($"Invalid base path configuration: {configuredPath}", ex);
        }
    }
    
    public string GetBasePath()
    {
        return _basePath;
    }
    
    public string GetIndexPath(string workspacePath)
    {
        // Validate input path for security
        ValidateWorkspacePath(workspacePath);
        
        // Normalize the workspace path
        var normalizedPath = workspacePath.Replace('/', Path.DirectorySeparatorChar)
                                        .Replace('\\', Path.DirectorySeparatorChar);
        
        // Get the index root directory
        var indexRoot = GetIndexRootPath();
        
        // Compute workspace hash
        var hash = ComputeWorkspaceHash(workspacePath);
        
        // Create a descriptive directory name: "workspacename_hash"
        // This makes debugging easier while hash ensures uniqueness
        var workspaceName = GetSafeWorkspaceName(workspacePath);
        var descriptiveName = $"{workspaceName}_{hash}";
        
        var indexPath = Path.Combine(indexRoot, descriptiveName);
        
        // DO NOT create directory here - only compute the path
        // Directory creation should happen only when actually creating an index
        
        return indexPath;
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
        return Path.Combine(_basePath, PathConstants.LogsDirectoryName);
    }
    
    public string GetWorkspaceMetadataPath()
    {
        var indexRoot = GetIndexRootPath();
        return Path.Combine(indexRoot, PathConstants.WorkspaceMetadataFileName);
    }
    
    /// <summary>
    /// Gets the metadata file path for a specific workspace
    /// </summary>
    /// <param name="workspacePath">The workspace path</param>
    /// <returns>Path to the workspace-specific metadata file</returns>
    public string GetWorkspaceMetadataPath(string workspacePath)
    {
        var indexPath = GetIndexPath(workspacePath);
        return Path.Combine(indexPath, "workspace_metadata.json");
    }
    
    /// <summary>
    /// Attempts to resolve the original workspace path from an index directory
    /// </summary>
    /// <param name="indexDirectory">The index directory path</param>
    /// <returns>The original workspace path if found, null otherwise</returns>
    public string? TryResolveWorkspacePath(string indexDirectory)
    {
        var metadataFile = Path.Combine(indexDirectory, "workspace_metadata.json");
        var semaphore = GetMetadataLock(metadataFile);
        
        try
        {
            // First, try to read from metadata file with thread safety
            if (File.Exists(metadataFile))
            {
                semaphore.Wait();
                try
                {
                    var json = File.ReadAllText(metadataFile);
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<Models.WorkspaceIndexInfo>(json);
                    if (!string.IsNullOrEmpty(metadata?.OriginalPath) && Directory.Exists(metadata.OriginalPath))
                    {
                        return metadata.OriginalPath;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            
            // Fallback: Try to reconstruct from directory name
            var directoryName = Path.GetFileName(indexDirectory);
            var parts = directoryName.Split('_');
            
            if (parts.Length >= 2)
            {
                // The directory name format is "workspacename_hash"
                var workspaceName = string.Join("_", parts.Take(parts.Length - 1));
                var hash = parts.Last();
                
                // Common workspace locations to check
                var possibleLocations = new[]
                {
                    Path.Combine("C:\\source", workspaceName.Replace('_', ' ')),
                    Path.Combine("C:\\source", workspaceName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", workspaceName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), workspaceName),
                    // Add current directory as well
                    Path.Combine(Directory.GetCurrentDirectory(), workspaceName)
                };

                foreach (var location in possibleLocations)
                {
                    if (Directory.Exists(location))
                    {
                        // Verify this is the correct workspace by computing its hash
                        var expectedHash = ComputeWorkspaceHash(location);
                        if (expectedHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                        {
                            return location;
                        }
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve workspace path for index directory: {IndexDirectory}", indexDirectory);
            return null;
        }
    }
    
    /// <summary>
    /// Stores workspace metadata for future resolution
    /// </summary>
    /// <param name="workspacePath">The original workspace path</param>
    public void StoreWorkspaceMetadata(string workspacePath)
    {
        var metadataPath = GetWorkspaceMetadataPath(workspacePath);
        var semaphore = GetMetadataLock(metadataPath);
        
        try
        {
            semaphore.Wait();
            
            var metadata = new Models.WorkspaceIndexInfo
            {
                OriginalPath = GetFullPath(workspacePath),
                HashPath = ComputeWorkspaceHash(workspacePath),
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                DocumentCount = 0, // Will be updated during indexing
                IndexSizeBytes = 0 // Will be updated during indexing
            };
            
            var directory = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectoryExists(directory);
            }
            
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            // Use atomic write operation for better reliability
            var tempPath = metadataPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, metadataPath, true); // Atomic replacement
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store metadata for workspace: {WorkspacePath}, Path: {MetadataPath}", 
                workspacePath, metadataPath);
            // Don't throw - metadata storage is optional
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    public string GetIndexRootPath()
    {
        return Path.Combine(_basePath, PathConstants.IndexDirectoryName);
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
        
        // Check for excessively long paths that could cause issues
        if (normalizedPath.Length > 240) // Allow some buffer for additional components
        {
            _logger.LogWarning("Path too long: {Length} characters: {WorkspacePath}", normalizedPath.Length, workspacePath);
            throw new ArgumentException($"Path too long: {normalizedPath.Length} characters", nameof(workspacePath));
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