using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.Next.McpServer.Constants;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Centralized path resolution service for all CodeSearch directory operations
/// Manages paths in ~/.coa/codesearch for centralized multi-workspace support
/// </summary>
public class PathResolutionService : IPathResolutionService
{
    private readonly IConfiguration _configuration;
    private readonly string _basePath;
    
    public PathResolutionService(IConfiguration configuration)
    {
        _configuration = configuration;
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
        
        // Return the index path (hash only for cleaner structure)
        var indexPath = Path.Combine(indexRoot, hash);
        
        // DO NOT create directory here - only compute the path
        // Directory creation should happen only when actually creating an index
        
        return indexPath;
    }
    
    public string ComputeWorkspaceHash(string workspacePath)
    {
        // Normalize path for consistent hashing
        var fullPath = Path.GetFullPath(workspacePath);
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
        catch
        {
            return false;
        }
    }
    
    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
    
    public string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
    
    public string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    
    public string GetExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch
        {
            return string.Empty;
        }
    }
    
    public string GetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    
    public string GetRelativePath(string relativeTo, string path)
    {
        try
        {
            return Path.GetRelativePath(relativeTo, path);
        }
        catch
        {
            return path;
        }
    }
    
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
    
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern, searchOption);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
}