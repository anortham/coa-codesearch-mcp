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
        // Check if this is a memory-related path
        if (workspacePath.Equals(PathConstants.ProjectMemoryDirectoryName, StringComparison.OrdinalIgnoreCase) || 
            workspacePath.Equals($"{PathConstants.BaseDirectoryName}/{PathConstants.ProjectMemoryDirectoryName}", StringComparison.OrdinalIgnoreCase))
        {
            return GetProjectMemoryPath();
        }
        
        if (workspacePath.Equals(PathConstants.LocalMemoryDirectoryName, StringComparison.OrdinalIgnoreCase) || 
            workspacePath.Equals($"{PathConstants.BaseDirectoryName}/{PathConstants.LocalMemoryDirectoryName}", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalMemoryPath();
        }
        
        // For regular workspace paths
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        System.IO.Directory.CreateDirectory(indexRoot);
        
        // Generate a hash-based folder name
        var fullPath = Path.GetFullPath(workspacePath);
        var normalizedPath = fullPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
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
        System.IO.Directory.CreateDirectory(logsPath);
        return logsPath;
    }
    
    public string GetProjectMemoryPath()
    {
        var path = Path.Combine(_basePath, PathConstants.ProjectMemoryDirectoryName);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
    
    public string GetLocalMemoryPath()
    {
        var path = Path.Combine(_basePath, PathConstants.LocalMemoryDirectoryName);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
    
    public string GetWorkspaceMetadataPath()
    {
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        System.IO.Directory.CreateDirectory(indexRoot);
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
        System.IO.Directory.CreateDirectory(backupRoot);
        
        if (string.IsNullOrEmpty(timestamp))
        {
            timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
        
        var backupPath = Path.Combine(backupRoot, string.Format(PathConstants.BackupPrefixFormat, timestamp));
        System.IO.Directory.CreateDirectory(backupPath);
        
        return backupPath;
    }
    
    public string GetIndexRootPath()
    {
        var indexRoot = Path.Combine(_basePath, PathConstants.IndexDirectoryName);
        System.IO.Directory.CreateDirectory(indexRoot);
        return indexRoot;
    }
    
    public string GetTypeScriptInstallPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installPath = Path.Combine(appData, PathConstants.TypeScriptInstallerDirectory, PathConstants.TypeScriptSubDirectory);
        System.IO.Directory.CreateDirectory(installPath);
        return installPath;
    }
}