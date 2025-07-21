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
        var basePath = _configuration["Lucene:IndexBasePath"] ?? ".codesearch";
        
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
        if (workspacePath.Equals("project-memory", StringComparison.OrdinalIgnoreCase) || 
            workspacePath.Equals(".codesearch/project-memory", StringComparison.OrdinalIgnoreCase))
        {
            return GetProjectMemoryPath();
        }
        
        if (workspacePath.Equals("local-memory", StringComparison.OrdinalIgnoreCase) || 
            workspacePath.Equals(".codesearch/local-memory", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalMemoryPath();
        }
        
        // For regular workspace paths
        var indexRoot = Path.Combine(_basePath, "index");
        System.IO.Directory.CreateDirectory(indexRoot);
        
        // Generate a hash-based folder name
        var fullPath = Path.GetFullPath(workspacePath);
        var normalizedPath = fullPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var truncatedHash = hashString.Substring(0, 16);
        
        // Create a folder name that includes part of the workspace name for readability
        var workspaceName = Path.GetFileName(fullPath) ?? "workspace";
        var safeName = string.Join("_", workspaceName.Split(Path.GetInvalidFileNameChars()));
        if (safeName.Length > 30)
        {
            safeName = safeName.Substring(0, 30);
        }
        
        var indexPath = Path.Combine(indexRoot, $"{safeName}_{truncatedHash}");
        System.IO.Directory.CreateDirectory(indexPath);
        
        return indexPath;
    }
    
    public string GetLogsPath()
    {
        var logsPath = Path.Combine(_basePath, "logs");
        System.IO.Directory.CreateDirectory(logsPath);
        return logsPath;
    }
    
    public string GetProjectMemoryPath()
    {
        var path = Path.Combine(_basePath, "project-memory");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
    
    public string GetLocalMemoryPath()
    {
        var path = Path.Combine(_basePath, "local-memory");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
    
    public string GetWorkspaceMetadataPath()
    {
        var indexRoot = Path.Combine(_basePath, "index");
        System.IO.Directory.CreateDirectory(indexRoot);
        return Path.Combine(indexRoot, "workspace_metadata.json");
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
        var backupRoot = Path.Combine(_basePath, "backups");
        System.IO.Directory.CreateDirectory(backupRoot);
        
        if (string.IsNullOrEmpty(timestamp))
        {
            timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
        
        var backupPath = Path.Combine(backupRoot, $"backup_{timestamp}");
        System.IO.Directory.CreateDirectory(backupPath);
        
        return backupPath;
    }
}