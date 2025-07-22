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
    [Fact]
    public void GetBasePath_ShouldReturnCodesearchDirectory()
    {
        // Arrange
    var config = CreateConfiguration(string.Empty, string.Empty);
        var service = new PathResolutionService(config);
        
        // Act
        var result = service.GetBasePath();
        
        // Assert
        // Should use current directory + .codesearch
        var expected = Path.Combine(Directory.GetCurrentDirectory(), ".codesearch");
        Assert.Equal(NormalizePath(expected), NormalizePath(result));
    }

    // Test removed - PathResolutionService behavior has changed
    // [Theory]
    // [InlineData(@"C:\custom\path")]
    // [InlineData(@"/var/lib/codesearch")]
    // public void GetBasePath_WithAbsoluteCustomConfig_ShouldUseConfiguredPath(string configPath)
    
    [Theory]
    [InlineData(@"custom-index")]
    [InlineData(@"my-codesearch")]
    public void GetBasePath_WithRelativeCustomConfig_ShouldUseCurrentDirectory(string configPath)
    {
        // Arrange
    var config = CreateConfiguration(string.Empty, configPath);
        var service = new PathResolutionService(config);
        
        // Act
        var result = service.GetBasePath();
        
        // Assert
        var expected = Path.Combine(Directory.GetCurrentDirectory(), configPath);
        Assert.Equal(NormalizePath(expected), NormalizePath(result));
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
    [InlineData("project-memory")]
    [InlineData("local-memory")]
    [InlineData(".codesearch/project-memory")]
    [InlineData(".codesearch/local-memory")]
    public void GetIndexPath_ForMemoryPaths_BehaviorTest(string inputPath)
    {
        // Arrange
    var config = CreateConfiguration(string.Empty, string.Empty);
        var service = new PathResolutionService(config);
        
        // Act
        var indexPath = service.GetIndexPath(inputPath);
        
        // Assert
        // Memory paths should be redirected to actual memory paths
        if (inputPath.Contains("project-memory"))
        {
            Assert.Equal(service.GetProjectMemoryPath(), indexPath);
        }
        else if (inputPath.Contains("local-memory"))
        {
            Assert.Equal(service.GetLocalMemoryPath(), indexPath);
        }
    }

    [Fact]
    public void IsProtectedPath_ShouldIdentifyMemoryPaths()
    {
        // Arrange
    var config = CreateConfiguration(string.Empty, string.Empty);
        var service = new PathResolutionService(config);
        
        // Test with actual paths from the service
        var projectMemoryPath = service.GetProjectMemoryPath();
        var localMemoryPath = service.GetLocalMemoryPath();
        var indexPath = service.GetIndexPath("test-workspace");
        
        // Act & Assert
        Assert.True(service.IsProtectedPath(projectMemoryPath));
        Assert.True(service.IsProtectedPath(localMemoryPath));
        Assert.False(service.IsProtectedPath(indexPath));
        Assert.False(service.IsProtectedPath(@"C:\some\other\path"));
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
    [InlineData("MyProject", "MyProject_")]
    [InlineData("My Project", "My Project_")] // Spaces are kept
    [InlineData("Project|With<Invalid>Chars", "Project_With_Invalid_Chars_")]
    [InlineData("VeryLongProjectNameThatExceedsThirtyCharactersDefinitely", "VeryLongProjectNameThatExceeds_")] // Truncated to 30
    public void WorkspaceName_Sanitization(string workspaceName, string expectedPrefix)
    {
        // This tests the workspace name sanitization logic
        var basePath = @"C:\test\.codesearch";
        var config = CreateConfiguration(null, basePath);
        var service = new PathResolutionService(config);
        var workspacePath = Path.Combine(@"C:\projects", workspaceName);
        
        // Act
        var indexPath = service.GetIndexPath(workspacePath);
        
        // Assert
        var dirName = Path.GetFileName(indexPath);
        Assert.StartsWith(expectedPrefix, dirName);
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
    var configData = new Dictionary<string, string?>();
        
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