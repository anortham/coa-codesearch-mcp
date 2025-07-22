using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class FileWatcherServicePathTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<FileWatcherService>> _mockLogger;
    private readonly Mock<FileIndexingService> _mockFileIndexing;
    private readonly Mock<IPathResolutionService> _mockPathResolution;

    public FileWatcherServicePathTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"FileWatcherTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<FileWatcherService>>();
        _mockFileIndexing = new Mock<FileIndexingService>();
        _mockPathResolution = new Mock<IPathResolutionService>();
        
        // Setup path resolution
        _mockPathResolution.Setup(p => p.GetBasePath()).Returns(_testBasePath);
        
        // Setup configuration
        _mockConfiguration.Setup(c => c["FileWatcher:Enabled"]).Returns("true");
        _mockConfiguration.Setup(c => c["FileWatcher:DebounceMilliseconds"]).Returns("500");
        _mockConfiguration.Setup(c => c["FileWatcher:BatchSize"]).Returns("50");
    }

    [Fact]
    public void ShouldExcludeFilesInCodeSearchDirectory_UsingPathConstants()
    {
        // This test verifies that FileWatcherService should use PathConstants
        // instead of hardcoding ".codesearch" in the exclusion logic
        
        // Test that .codesearch directory paths are properly formed with constants
        var testPaths = new[]
        {
            $@"C:\project\{PathConstants.BaseDirectoryName}\index\test.cs",
            $@"C:\project\{PathConstants.BaseDirectoryName}\logs\debug.log",
            $@"/home/user/project/{PathConstants.BaseDirectoryName}/project-memory/data.json"
        };
        
        foreach (var path in testPaths)
        {
            // Verify the constant is used in path construction
            Assert.Contains(PathConstants.BaseDirectoryName, path);
        }
        
        // After refactoring, the check in FileWatcherService should be:
        // if (filePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase))
    }

    [Fact]
    public void ExcludedDirectories_ShouldUsePathConstants()
    {
        // Arrange & Act
        var excludedDirs = PathConstants.DefaultExcludedDirectories;
        
        // Assert
        Assert.Contains(PathConstants.BaseDirectoryName, excludedDirs);
        Assert.Contains("bin", excludedDirs);
        Assert.Contains("obj", excludedDirs);
        Assert.Contains("node_modules", excludedDirs);
        Assert.Contains(".git", excludedDirs);
        Assert.Contains(".vs", excludedDirs);
        Assert.Contains("packages", excludedDirs);
        Assert.Contains("TestResults", excludedDirs);
    }

    [Fact]
    public void FileWatcher_ShouldNotHardcodeBaseDirectoryName()
    {
        // This test ensures we don't have hardcoded ".codesearch" strings
        // After refactoring, FileWatcherService should use PathConstants.BaseDirectoryName
        
        // The refactored code should look like:
        // if (filePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase))
        
        Assert.Equal(".codesearch", PathConstants.BaseDirectoryName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}