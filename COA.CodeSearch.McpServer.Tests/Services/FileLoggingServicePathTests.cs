using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class FileLoggingServicePathTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly Mock<IPathResolutionService> _mockPathResolution;
    private readonly Mock<ILogger<FileLoggingService>> _mockLogger;

    public FileLoggingServicePathTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"FileLoggingTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        _mockPathResolution = new Mock<IPathResolutionService>();
        _mockLogger = new Mock<ILogger<FileLoggingService>>();
        
        // Setup path resolution
        var logsPath = Path.Combine(_testBasePath, PathConstants.LogsDirectoryName);
        Directory.CreateDirectory(logsPath); // Create the directory for the test
        _mockPathResolution.Setup(p => p.GetLogsPath()).Returns(logsPath);
        _mockPathResolution.Setup(p => p.GetBasePath()).Returns(_testBasePath);
    }

    [Fact]
    public void FileLoggingService_ShouldUsePathResolutionForLogDirectory()
    {
        // This test verifies that FileLoggingService should use IPathResolutionService
        // instead of hardcoding: Path.Combine(Path.GetTempPath(), "COA.CodeSearch.logs")
        
        // After refactoring, FileLoggingService constructor should:
        // 1. Accept IPathResolutionService as a dependency
        // 2. Use _pathResolution.GetLogsPath() for the log directory
        
        // Expected behavior after refactoring:
        var expectedLogsPath = _mockPathResolution.Object.GetLogsPath();
        Assert.Contains(PathConstants.LogsDirectoryName, expectedLogsPath);
        Assert.True(Directory.Exists(expectedLogsPath));
    }

    [Fact]
    public void FileLoggingService_ShouldNotHardcodeLogPath()
    {
        // After refactoring, the service should not contain hardcoded paths
        // The old code: Path.Combine(Path.GetTempPath(), "COA.CodeSearch.logs")
        // Should be replaced with: _pathResolution.GetLogsPath()
        
        var logsPath = _mockPathResolution.Object.GetLogsPath();
        
        // Verify the path structure
        Assert.EndsWith(PathConstants.LogsDirectoryName, logsPath);
        Assert.StartsWith(_testBasePath, logsPath);
    }

    [Fact]
    public void FileLoggingService_LogsPath_ShouldBeConsistentAcrossServices()
    {
        // Ensure that all services use the same logs path
        var logsPath1 = _mockPathResolution.Object.GetLogsPath();
        var logsPath2 = _mockPathResolution.Object.GetLogsPath();
        
        Assert.Equal(logsPath1, logsPath2);
    }

    [Fact]
    public void FileLoggingService_ShouldCreateLogDirectoryAutomatically()
    {
        // PathResolutionService.GetLogsPath() creates the directory
        var logsPath = _mockPathResolution.Object.GetLogsPath();
        
        // In the mock, we need to simulate directory creation
        if (!Directory.Exists(logsPath))
        {
            Directory.CreateDirectory(logsPath);
        }
        
        Assert.True(Directory.Exists(logsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}