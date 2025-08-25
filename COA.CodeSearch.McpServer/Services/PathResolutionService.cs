using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Constants;

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
    
    public PathResolutionService(IConfiguration configuration, ILogger<PathResolutionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _basePath = InitializeBasePath();
    }
    
    private string InitializeBasePath()
    {
        var configuredPath = _configuration[PathConstants.BasePathConfigKey] ?? PathConstants.DefaultBasePath;
        
        // Handle tilde expansion for cross-platform support
        if (configuredPath.StartsWith("~/"))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configuredPath = Path.Combine(homeDirectory, configuredPath.Substring(2));
        }
        
        return Path.GetFullPath(configuredPath);
    }
    
    public string GetBasePath()
    {
        return _basePath;
    }
    
    public string GetIndexPath(string workspacePath)
    {
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
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check directory existence for path: {Path}", path);
            return false;
        }
    }
    
    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check file existence for path: {Path}", path);
            return false;
        }
    }
    
    public string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get full path for: {Path}", path);
            return path;
        }
    }
    
    public string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file name for path: {Path}", path);
            return string.Empty;
        }
    }
    
    public string GetExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get extension for path: {Path}", path);
            return string.Empty;
        }
    }
    
    public string GetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get directory name for path: {Path}", path);
            return string.Empty;
        }
    }
    
    public string GetRelativePath(string relativeTo, string path)
    {
        try
        {
            return Path.GetRelativePath(relativeTo, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get relative path from '{RelativeTo}' to '{Path}'", relativeTo, path);
            return path;
        }
    }
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate files in path: {Path} with pattern: {Pattern}", path, searchPattern);
            return Enumerable.Empty<string>();
        }
    }
    
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern, searchOption);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate directories in path: {Path} with pattern: {Pattern}", path, searchPattern);
            return Enumerable.Empty<string>();
        }
    }
}