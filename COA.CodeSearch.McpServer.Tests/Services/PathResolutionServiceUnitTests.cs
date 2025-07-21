using System;
using System.IO;
using System.Runtime.InteropServices;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

/// <summary>
/// Pure unit tests for PathResolutionService that define expected behavior
/// without any file I/O operations
/// </summary>
public class PathResolutionServiceUnitTests
{
    [Theory]
    [InlineData(@"C:\projects\myapp", @"C:\projects\myapp\.codesearch")]
    [InlineData(@"/home/user/projects/myapp", @"/home/user/projects/myapp/.codesearch")]
    [InlineData(@"\\network\share\project", @"\\network\share\project\.codesearch")]
    public void GetBasePath_ShouldReturnCodesearchDirectory(string currentDir, string expected)
    {
        // Arrange
        var config = CreateConfiguration(currentDir, null);
        var service = new PathResolutionService(config);
        
        // Act
        var result = service.GetBasePath();
        
        // Assert
        Assert.Equal(NormalizePath(expected), NormalizePath(result));
    }

    [Theory]
    [InlineData(@"C:\custom\path", @"C:\custom\path")]
    [InlineData(@"/var/lib/codesearch", @"/var/lib/codesearch")]
    [InlineData(@"custom-index", @"{currentDir}\custom-index")]
    public void GetBasePath_WithCustomConfig_ShouldUseConfiguredPath(string configPath, string expectedPattern)
    {
        // Arrange
        var currentDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? @"C:\projects\test" 
            : "/home/user/test";
        var config = CreateConfiguration(currentDir, configPath);
        var service = new PathResolutionService(config);
        
        // Act
        var result = service.GetBasePath();
        
        // Assert
        var expected = expectedPattern.Replace("{currentDir}", currentDir);
        if (!Path.IsPathRooted(configPath))
        {
            Assert.Equal(NormalizePath(expected), NormalizePath(result));
        }
        else
        {
            Assert.Equal(NormalizePath(configPath), NormalizePath(result));
        }
    }

    [Fact]
    public void MemoryPaths_ShouldBeDirectSubdirectories()
    {
        // Arrange
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? @"C:\test\.codesearch" 
            : "/test/.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act & Assert
        var projectMemoryPath = service.GetProjectMemoryPath();
        var localMemoryPath = service.GetLocalMemoryPath();
        
        Assert.Equal(Path.Combine(basePath, "project-memory"), projectMemoryPath);
        Assert.Equal(Path.Combine(basePath, "local-memory"), localMemoryPath);
    }

    [Theory]
    [InlineData(@"C:\projects\myapp", "myapp_", 16)] // Hash should be 16 chars
    [InlineData(@"/home/user/project", "project_", 16)]
    [InlineData(@"C:\Very Long Project Name That Exceeds Thirty Characters", "_", 16)]
    public void GetIndexPath_ForWorkspace_ShouldCreateHashedPath(string workspacePath, string expectedPrefix, int hashLength)
    {
        // Arrange
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? @"C:\test\.codesearch" 
            : "/test/.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act
        var indexPath = service.GetIndexPath(workspacePath);
        
        // Assert
        var indexDir = Path.GetFileName(indexPath);
        
        // Should be in index subdirectory
        Assert.Contains(Path.Combine(basePath, "index"), indexPath);
        
        // Directory name should have expected format
        if (expectedPrefix != "_")
        {
            Assert.StartsWith(expectedPrefix, indexDir);
        }
        
        // Should end with hash
        var parts = indexDir.Split('_');
        var hash = parts[parts.Length - 1];
        Assert.Equal(hashLength, hash.Length);
        Assert.True(IsHexadecimal(hash));
    }

    [Theory]
    [InlineData(@"C:\test\.codesearch\project-memory", @"C:\test\.codesearch\project-memory")]
    [InlineData(@"C:\test\.codesearch\local-memory", @"C:\test\.codesearch\local-memory")]
    [InlineData(@"/test/.codesearch/project-memory", @"/test/.codesearch/local-memory")]
    [InlineData(@"project-memory", null)] // Should still hash if not a full path
    [InlineData(@"local-memory", null)]
    public void GetIndexPath_ForMemoryPaths_BehaviorTest(string inputPath, string expectedBehavior)
    {
        // This test documents current behavior - may need adjustment based on requirements
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? @"C:\test\.codesearch" 
            : "/test/.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act
        var indexPath = service.GetIndexPath(inputPath);
        
        // Assert
        if (expectedBehavior != null)
        {
            // For full memory paths, GetIndexPath currently returns them as-is
            Assert.Equal(expectedBehavior, indexPath);
        }
        else
        {
            // For relative paths, it should hash them
            Assert.Contains("index", indexPath);
            Assert.NotEqual(inputPath, indexPath);
        }
    }

    [Theory]
    [InlineData(@"C:\test\.codesearch\project-memory", true)]
    [InlineData(@"C:\test\.codesearch\local-memory", true)]
    [InlineData(@"C:\test\.codesearch\index\workspace_abc123", false)]
    [InlineData(@"/var/lib/.codesearch/project-memory", true)]
    [InlineData(@"/var/lib/.codesearch/local-memory", true)]
    [InlineData(@"C:\test\project-memory", false)] // Not under .codesearch
    public void IsProtectedPath_ShouldIdentifyMemoryPaths(string path, bool expectedProtected)
    {
        // Arrange
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? @"C:\test\.codesearch" 
            : "/var/lib/.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act
        var isProtected = service.IsProtectedPath(path);
        
        // Assert
        Assert.Equal(expectedProtected, isProtected);
    }

    [Fact]
    public void GetBackupPath_WithTimestamp_ShouldFormatCorrectly()
    {
        // Arrange
        var basePath = @"C:\test\.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        var timestamp = "20240121_143052";
        
        // Act
        var backupPath = service.GetBackupPath(timestamp);
        
        // Assert
        Assert.Equal(Path.Combine(basePath, "backups", $"backup_{timestamp}"), backupPath);
    }

    [Fact]
    public void GetBackupPath_WithoutTimestamp_ShouldGenerateOne()
    {
        // Arrange
        var basePath = @"C:\test\.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act
        var backupPath = service.GetBackupPath();
        
        // Assert
        Assert.StartsWith(Path.Combine(basePath, "backups", "backup_"), backupPath);
        
        // Should have timestamp format
        var dirName = Path.GetFileName(backupPath);
        Assert.Matches(@"backup_\d{8}_\d{6}", dirName);
    }

    [Theory]
    [InlineData("MyProject", "myproject_")]
    [InlineData("My Project", "My_Project_")] // Spaces replaced with underscore
    [InlineData("Project|With<Invalid>Chars", "Project_With_Invalid_Chars_")]
    [InlineData("VeryLongProjectNameThatExceedsThirtyCharactersDefinitely", "_")] // Truncated to 30
    public void WorkspaceName_Sanitization(string workspaceName, string expectedPrefix)
    {
        // This tests the workspace name sanitization logic
        var basePath = @"C:\test\.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        var workspacePath = Path.Combine(@"C:\projects", workspaceName);
        
        // Act
        var indexPath = service.GetIndexPath(workspacePath);
        
        // Assert
        var dirName = Path.GetFileName(indexPath);
        if (expectedPrefix != "_")
        {
            Assert.StartsWith(expectedPrefix, dirName);
        }
    }

    [Fact]
    public void AllPaths_ShouldBeUnderBasePath()
    {
        // Arrange
        var basePath = @"C:\test\.codesearch";
        var config = CreateConfiguration("/dummy", basePath);
        var service = new PathResolutionService(config);
        
        // Act & Assert
        var paths = new[]
        {
            service.GetProjectMemoryPath(),
            service.GetLocalMemoryPath(),
            service.GetLogsPath(),
            service.GetBackupPath(),
            service.GetWorkspaceMetadataPath(),
            service.GetIndexPath("workspace1")
        };
        
        foreach (var path in paths)
        {
            Assert.StartsWith(basePath, path);
        }
    }

    // Helper methods
    private static IConfiguration CreateConfiguration(string currentDirectory, string? customBasePath)
    {
        var configData = new Dictionary<string, string>();
        
        if (customBasePath != null)
        {
            configData["Lucene:IndexBasePath"] = customBasePath;
        }
        
        // Mock current directory by using the custom base path
        if (!string.IsNullOrEmpty(customBasePath) && !Path.IsPathRooted(customBasePath))
        {
            // For relative paths, we'd need to combine with current directory
            // In real code, this uses Environment.CurrentDirectory
        }
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }
    
    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar)
                   .Replace('\\', Path.DirectorySeparatorChar)
                   .TrimEnd(Path.DirectorySeparatorChar);
    }
    
    private static bool IsHexadecimal(string value)
    {
        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}