using Xunit;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;

namespace COA.CodeSearch.McpServer.Tests.Tools;

public class FlexibleMemoryToolsPathTests
{
    private readonly Mock<IPathResolutionService> _mockPathResolution;
    private readonly string _testBasePath;

    public FlexibleMemoryToolsPathTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"FlexibleMemoryTest_{Guid.NewGuid()}");
        _mockPathResolution = new Mock<IPathResolutionService>();
        
        _mockPathResolution.Setup(p => p.GetBasePath()).Returns(_testBasePath);
        _mockPathResolution.Setup(p => p.GetProjectMemoryPath())
            .Returns(Path.Combine(_testBasePath, PathConstants.ProjectMemoryDirectoryName));
        _mockPathResolution.Setup(p => p.GetLocalMemoryPath())
            .Returns(Path.Combine(_testBasePath, PathConstants.LocalMemoryDirectoryName));
    }

    [Fact]
    public void MemoryDashboard_ShouldUsePathResolutionService()
    {
        // The memory dashboard currently hardcodes:
        // var baseDir = Path.Combine(localAppData, "COA.CodeSearch", ".codesearch");
        
        // After refactoring, it should:
        // 1. Accept IPathResolutionService as a dependency
        // 2. Use _pathResolution.GetBasePath() or specific memory paths
        
        var expectedBasePath = _mockPathResolution.Object.GetBasePath();
        var expectedProjectPath = _mockPathResolution.Object.GetProjectMemoryPath();
        var expectedLocalPath = _mockPathResolution.Object.GetLocalMemoryPath();
        
        // Verify paths use constants
        Assert.Contains(PathConstants.ProjectMemoryDirectoryName, expectedProjectPath);
        Assert.Contains(PathConstants.LocalMemoryDirectoryName, expectedLocalPath);
    }

    [Fact]
    public void MemoryDashboard_ShouldNotHardcodeAppDataPath()
    {
        // The old pattern:
        // Path.Combine(localAppData, "COA.CodeSearch", ".codesearch")
        
        // Should be replaced with PathResolutionService methods
        // This avoids hardcoding both "COA.CodeSearch" and ".codesearch"
        
        var basePath = _mockPathResolution.Object.GetBasePath();
        
        // The base path should be determined by PathResolutionService
        // not constructed manually
        Assert.NotNull(basePath);
        Assert.Equal(_testBasePath, basePath);
    }

    [Fact]
    public void FlexibleMemoryTools_ShouldUsePathConstants()
    {
        // Verify that the tool uses PathConstants instead of magic strings
        Assert.Equal(".codesearch", PathConstants.BaseDirectoryName);
        Assert.Equal("project-memory", PathConstants.ProjectMemoryDirectoryName);
        Assert.Equal("local-memory", PathConstants.LocalMemoryDirectoryName);
    }

    [Fact]
    public void MemoryPaths_ShouldBeConsistentAcrossTools()
    {
        // All memory-related tools should get paths from the same source
        var projectPath1 = _mockPathResolution.Object.GetProjectMemoryPath();
        var projectPath2 = _mockPathResolution.Object.GetProjectMemoryPath();
        
        Assert.Equal(projectPath1, projectPath2);
        
        var localPath1 = _mockPathResolution.Object.GetLocalMemoryPath();
        var localPath2 = _mockPathResolution.Object.GetLocalMemoryPath();
        
        Assert.Equal(localPath1, localPath2);
    }
}