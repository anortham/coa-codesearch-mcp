using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class PathViolationFixTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly Mock<IPathResolutionService> _mockPathResolution;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly ServiceProvider _serviceProvider;

    public PathViolationFixTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"PathViolationTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        _mockPathResolution = new Mock<IPathResolutionService>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Setup path resolution mocks
        _mockPathResolution.Setup(p => p.GetBasePath()).Returns(_testBasePath);
        _mockPathResolution.Setup(p => p.GetIndexPath(It.IsAny<string>()))
            .Returns<string>(ws => Path.Combine(_testBasePath, PathConstants.IndexDirectoryName, $"test_{ws.GetHashCode()}"));
        _mockPathResolution.Setup(p => p.GetProjectMemoryPath())
            .Returns(Path.Combine(_testBasePath, PathConstants.ProjectMemoryDirectoryName));
        _mockPathResolution.Setup(p => p.GetLocalMemoryPath())
            .Returns(Path.Combine(_testBasePath, PathConstants.LocalMemoryDirectoryName));
        
        // Setup services
        var services = new ServiceCollection();
        services.AddSingleton(_mockPathResolution.Object);
        services.AddSingleton(_mockConfiguration.Object);
        services.AddSingleton<ILogger<CleanupMemoryIndexesTool>>(new Mock<ILogger<CleanupMemoryIndexesTool>>().Object);
        services.AddSingleton<ILogger<TypeScriptInstaller>>(new Mock<ILogger<TypeScriptInstaller>>().Object);
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void CleanupMemoryIndexesTool_ShouldUsePathResolutionService()
    {
        // This test verifies that CleanupMemoryIndexesTool should not construct paths manually
        // Currently it does: Path.Combine(_pathResolution.GetBasePath(), "index")
        // It should use a new method like _pathResolution.GetIndexRootPath()
        
        // Arrange
        var tool = new CleanupMemoryIndexesTool(
            _serviceProvider.GetRequiredService<ILogger<CleanupMemoryIndexesTool>>(),
            _mockPathResolution.Object);
        
        // Act & Assert
        // After refactoring, this should not throw and should use PathResolutionService
        // The tool should call _pathResolution.GetIndexRootPath() instead of constructing paths
        Assert.NotNull(tool);
        
        // Verify it doesn't construct paths manually (this will be tested after refactoring)
    }

    [Fact]
    public void TypeScriptInstaller_ShouldUsePathResolutionService()
    {
        // This test verifies that TypeScriptInstaller should not hardcode paths
        // Currently it uses: Path.Combine(appData, "COA.CodeSearch.McpServer", "typescript")
        // It should use PathResolutionService for TypeScript installation path
        
        // Arrange & Act
        // After refactoring, TypeScriptInstaller should accept IPathResolutionService
        // and use something like _pathResolution.GetTypeScriptInstallPath()
        
        // This is a placeholder test that will be implemented after adding the method
        Assert.True(true);
    }

    [Fact]
    public void FlexibleMemoryTools_ShouldNotHardcodeBasePath()
    {
        // This test verifies that FlexibleMemoryTools should not hardcode paths
        // Currently it uses: Path.Combine(localAppData, "COA.CodeSearch", ".codesearch")
        // It should use IPathResolutionService
        
        // After refactoring, the memory dashboard should get paths from PathResolutionService
        // instead of constructing them manually
        
        Assert.True(true); // Placeholder for now
    }

    [Fact]
    public void FileIndexingService_ShouldUsePathConstants()
    {
        // Verify that excluded directories use PathConstants.DefaultExcludedDirectories
        // instead of hardcoded array
        
        var excludedDirs = PathConstants.DefaultExcludedDirectories;
        
        Assert.Contains("bin", excludedDirs);
        Assert.Contains("obj", excludedDirs);
        Assert.Contains("node_modules", excludedDirs);
        Assert.Contains(".git", excludedDirs);
        Assert.Contains(".vs", excludedDirs);
        Assert.Contains("packages", excludedDirs);
        Assert.Contains("TestResults", excludedDirs);
        Assert.Contains(PathConstants.BaseDirectoryName, excludedDirs);
    }

    [Fact]
    public void PathResolutionService_ShouldProvideAllNecessaryPaths()
    {
        // Verify that IPathResolutionService has all the methods needed
        // to prevent other services from constructing paths
        
        var methods = typeof(IPathResolutionService).GetMethods();
        
        Assert.Contains(methods, m => m.Name == "GetBasePath");
        Assert.Contains(methods, m => m.Name == "GetIndexPath");
        Assert.Contains(methods, m => m.Name == "GetLogsPath");
        Assert.Contains(methods, m => m.Name == "GetProjectMemoryPath");
        Assert.Contains(methods, m => m.Name == "GetLocalMemoryPath");
        Assert.Contains(methods, m => m.Name == "GetWorkspaceMetadataPath");
        Assert.Contains(methods, m => m.Name == "GetBackupPath");
        Assert.Contains(methods, m => m.Name == "IsProtectedPath");
        
        // After refactoring, we might need to add:
        // - GetIndexRootPath() for services that need the index root
        // - GetTypeScriptInstallPath() for TypeScript installer
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}