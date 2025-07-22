using Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class PathResolutionServiceConstantsTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly PathResolutionService _service;

    public PathResolutionServiceConstantsTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"PathResolutionConstantsTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c[PathConstants.IndexBasePathConfigKey]).Returns(_testBasePath);
        
        _service = new PathResolutionService(_mockConfiguration.Object);
    }

    [Fact]
    public void GetBasePath_UsesConfigurationConstant()
    {
        // Act
        var basePath = _service.GetBasePath();
        
        // Assert
        Assert.Equal(_testBasePath, basePath);
        _mockConfiguration.Verify(c => c[PathConstants.IndexBasePathConfigKey], Times.AtLeastOnce());
    }

    [Fact]
    public void GetIndexPath_UsesIndexDirectoryConstant()
    {
        // Arrange
        var workspace = "test-workspace";
        
        // Act
        var indexPath = _service.GetIndexPath(workspace);
        
        // Assert
        Assert.Contains(PathConstants.IndexDirectoryName, indexPath);
        var expectedRoot = Path.Combine(_testBasePath, PathConstants.IndexDirectoryName);
        Assert.StartsWith(expectedRoot, indexPath);
    }

    [Fact]
    public void GetLogsPath_UsesLogsDirectoryConstant()
    {
        // Act
        var logsPath = _service.GetLogsPath();
        
        // Assert
        var expected = Path.Combine(_testBasePath, PathConstants.LogsDirectoryName);
        Assert.Equal(expected, logsPath);
        Assert.True(Directory.Exists(logsPath));
    }

    [Fact]
    public void GetProjectMemoryPath_UsesProjectMemoryConstant()
    {
        // Act
        var projectMemoryPath = _service.GetProjectMemoryPath();
        
        // Assert
        var expected = Path.Combine(_testBasePath, PathConstants.ProjectMemoryDirectoryName);
        Assert.Equal(expected, projectMemoryPath);
        Assert.True(Directory.Exists(projectMemoryPath));
    }

    [Fact]
    public void GetLocalMemoryPath_UsesLocalMemoryConstant()
    {
        // Act
        var localMemoryPath = _service.GetLocalMemoryPath();
        
        // Assert
        var expected = Path.Combine(_testBasePath, PathConstants.LocalMemoryDirectoryName);
        Assert.Equal(expected, localMemoryPath);
        Assert.True(Directory.Exists(localMemoryPath));
    }

    [Fact]
    public void GetWorkspaceMetadataPath_UsesMetadataFileConstant()
    {
        // Act
        var metadataPath = _service.GetWorkspaceMetadataPath();
        
        // Assert
        Assert.EndsWith(PathConstants.WorkspaceMetadataFileName, metadataPath);
        var expectedPath = Path.Combine(_testBasePath, PathConstants.IndexDirectoryName, PathConstants.WorkspaceMetadataFileName);
        Assert.Equal(expectedPath, metadataPath);
    }

    [Fact]
    public void GetBackupPath_UsesBackupConstants()
    {
        // Arrange
        var timestamp = "20240122_120000";
        
        // Act
        var backupPath = _service.GetBackupPath(timestamp);
        
        // Assert
        Assert.Contains(PathConstants.BackupsDirectoryName, backupPath);
        Assert.EndsWith(string.Format(PathConstants.BackupPrefixFormat, timestamp), backupPath);
        Assert.True(Directory.Exists(backupPath));
    }

    [Fact]
    public void GetIndexPath_RecognizesMemoryPathsWithConstants()
    {
        // Test project memory
        var projectPath1 = _service.GetIndexPath(PathConstants.ProjectMemoryDirectoryName);
        var projectPath2 = _service.GetIndexPath($"{PathConstants.BaseDirectoryName}/{PathConstants.ProjectMemoryDirectoryName}");
        
        Assert.Equal(_service.GetProjectMemoryPath(), projectPath1);
        Assert.Equal(_service.GetProjectMemoryPath(), projectPath2);
        
        // Test local memory
        var localPath1 = _service.GetIndexPath(PathConstants.LocalMemoryDirectoryName);
        var localPath2 = _service.GetIndexPath($"{PathConstants.BaseDirectoryName}/{PathConstants.LocalMemoryDirectoryName}");
        
        Assert.Equal(_service.GetLocalMemoryPath(), localPath1);
        Assert.Equal(_service.GetLocalMemoryPath(), localPath2);
    }

    [Fact]
    public void GetIndexPath_UsesCorrectHashLength()
    {
        // Arrange
        var workspace = "C:\\source\\very-long-workspace-name-that-exceeds-maximum-safe-length";
        
        // Act
        var indexPath = _service.GetIndexPath(workspace);
        var dirName = Path.GetFileName(indexPath);
        
        // Assert
        // Directory name format: safeName_hash
        var parts = dirName.Split('_');
        var hash = parts[parts.Length - 1];
        
        Assert.Equal(PathConstants.WorkspaceHashLength, hash.Length);
    }

    [Fact]
    public void GetIndexPath_TruncatesLongWorkspaceNames()
    {
        // Arrange
        var longWorkspaceName = new string('a', 50); // Create a 50-character name
        var workspace = $"C:\\source\\{longWorkspaceName}";
        
        // Act
        var indexPath = _service.GetIndexPath(workspace);
        var dirName = Path.GetFileName(indexPath);
        
        // Assert
        var parts = dirName.Split('_');
        var safeName = string.Join("_", parts.Take(parts.Length - 1));
        
        Assert.True(safeName.Length <= PathConstants.MaxSafeWorkspaceName);
    }

    [Fact]
    public void GetIndexRootPath_ReturnsCorrectPath()
    {
        // Act
        var indexRootPath = _service.GetIndexRootPath();
        
        // Assert
        var expected = Path.Combine(_testBasePath, PathConstants.IndexDirectoryName);
        Assert.Equal(expected, indexRootPath);
        Assert.True(Directory.Exists(indexRootPath));
    }

    [Fact]
    public void GetTypeScriptInstallPath_ReturnsCorrectPath()
    {
        // Act
        var tsInstallPath = _service.GetTypeScriptInstallPath();
        
        // Assert
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.Combine(appData, PathConstants.TypeScriptInstallerDirectory, PathConstants.TypeScriptSubDirectory);
        Assert.Equal(expected, tsInstallPath);
        Assert.True(Directory.Exists(tsInstallPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
        
        // Clean up TypeScript install directory created during tests
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tsPath = Path.Combine(appData, PathConstants.TypeScriptInstallerDirectory);
        if (Directory.Exists(tsPath) && Directory.GetFiles(tsPath).Length == 0 && Directory.GetDirectories(tsPath).Length == 1)
        {
            try
            {
                Directory.Delete(tsPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}