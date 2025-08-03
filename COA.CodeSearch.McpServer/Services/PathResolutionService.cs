using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Centralized path resolution service for all .codesearch directory operations
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
        var basePath = _configuration[PathConstants.IndexBasePathConfigKey] ?? PathConstants.BaseDirectoryName;
        
        if (!Path.IsPathRooted(basePath))
        {
            basePath = Path.Combine(Directory.GetCurrentDirectory(), basePath);
        }
        
        return Path.GetFullPath(basePath);
    }
    
    public string GetBasePath()
    {
        return _basePath;
    }
    
    public string GetIndexPath(string workspacePath)
    {
        // Normalize the workspace path to handle both forward and backward slashes
        var normalizedPath = workspacePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        
        // Check if this is a memory-related path
        if (normalizedPath.Equals(PathConstants.ProjectMemoryDirectoryName, StringComparison.OrdinalIgnoreCase) || 
            normalizedPath.Equals(Path.Combine(PathConstants.BaseDirectoryName, PathConstants.ProjectMemoryDirectoryName), StringComparison.OrdinalIgnoreCase))
        {
            return GetProjectMemoryPath();
        }
        
        if (normalizedPath.Equals(PathConstants.LocalMemoryDirectoryName, StringComparison.OrdinalIgnoreCase) || 
            normalizedPath.Equals(Path.Combine(PathConstants.BaseDirectoryName, PathConstants.LocalMemoryDirectoryName), StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalMemoryPath();
        }
        
        // Check if this is already a full path to a memory directory
        var projectMemoryPath = GetProjectMemoryPath();
        var localMemoryPath = GetLocalMemoryPath();
        
        if (string.Equals(Path.GetFullPath(normalizedPath), Path.GetFullPath(projectMemoryPath), StringComparison.OrdinalIgnoreCase))
        {
            return projectMemoryPath;
        }
        
        if (string.Equals(Path.GetFullPath(normalizedPath), Path.GetFullPath(localMemoryPath), StringComparison.OrdinalIgnoreCase))
        {
            return localMemoryPath;
        }
        
        // For regular workspace paths
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        
        // Generate a hash-based folder name with robust path normalization
        var fullPath = Path.GetFullPath(workspacePath);
        var normalizedFullPath = fullPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedFullPath));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var truncatedHash = hashString.Substring(0, PathConstants.WorkspaceHashLength);
        
        // Create a folder name that includes part of the workspace name for readability
        var workspaceName = Path.GetFileName(fullPath) ?? "workspace";
        var safeName = string.Join("_", workspaceName.Split(Path.GetInvalidFileNameChars()));
        if (safeName.Length > PathConstants.MaxSafeWorkspaceName)
        {
            safeName = safeName.Substring(0, PathConstants.MaxSafeWorkspaceName);
        }
        
        var indexPath = Path.Combine(indexRoot, $"{safeName}_{truncatedHash}");
        // DO NOT create directory here - only compute the path
        // Directory creation should happen only when actually creating an index
        
        return indexPath;
    }
    
    public string GetLogsPath()
    {
        var logsPath = Path.Combine(_basePath, PathConstants.LogsDirectoryName);
        return logsPath;
    }
    
    public string GetProjectMemoryPath()
    {
        var path = Path.Combine(_basePath, PathConstants.ProjectMemoryDirectoryName);
        return path;
    }
    
    public string GetLocalMemoryPath()
    {
        var path = Path.Combine(_basePath, PathConstants.LocalMemoryDirectoryName);
        return path;
    }
    
    public string GetWorkspaceMetadataPath()
    {
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        return Path.Combine(indexRoot, PathConstants.WorkspaceMetadataFileName);
    }
    
    public bool IsProtectedPath(string indexPath)
    {
        var protectedPaths = new[]
        {
            GetProjectMemoryPath(),
            GetLocalMemoryPath()
        };
        
        var normalizedIndexPath = Path.GetFullPath(indexPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        return protectedPaths.Any(protectedPath =>
        {
            var normalizedProtectedPath = Path.GetFullPath(protectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedIndexPath, normalizedProtectedPath, StringComparison.OrdinalIgnoreCase);
        });
    }
    
    public string GetBackupPath(string? timestamp = null)
    {
        var backupRoot = Path.Combine(_basePath, PathConstants.BackupsDirectoryName);
        
        if (string.IsNullOrEmpty(timestamp))
        {
            timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
        
        var backupPath = Path.Combine(backupRoot, string.Format(PathConstants.BackupPrefixFormat, timestamp));
        
        return backupPath;
    }
    
    public string GetIndexRootPath()
    {
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        return indexRoot;
    }
    
    public string GetCheckpointIdPath()
    {
        return Path.Combine(_basePath, "checkpoint.id");
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
    
    public IEnumerable<string> EnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
    
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
}