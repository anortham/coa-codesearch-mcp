using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class PathResolutionServiceTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly PathResolutionService _service;
    private readonly IConfiguration _configuration;
    
    public PathResolutionServiceTests()
    {
        // Create a unique test directory for each test run
        _testBasePath = Path.Combine(Path.GetTempPath(), $"PathResolutionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        // Setup configuration
        var configData = new Dictionary<string, string?>
        {
            ["Lucene:IndexBasePath"] = _testBasePath,
            ["ClaudeMemory:ProjectMemoryPath"] = "project-memory",
            ["ClaudeMemory:LocalMemoryPath"] = "local-memory"
        };
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
            
        _service = new PathResolutionService(_configuration);
    }
    
    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
    
    [Fact]
    public void GetBasePath_ReturnsConfiguredPath()
    {
        // Act
        var basePath = _service.GetBasePath();
        
        // Assert
        Assert.Equal(_testBasePath, basePath);
    }
    
    [Fact]
    public void GetBasePath_CreatesAbsolutePathFromRelative()
    {
        // Arrange
        var relativeConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lucene:IndexBasePath"] = ".codesearch"
            })
            .Build();
        var service = new PathResolutionService(relativeConfig);
        
        // Act
        var basePath = service.GetBasePath();
        
        // Assert
        Assert.True(Path.IsPathRooted(basePath));
        Assert.Contains(".codesearch", basePath);
    }
    
    [Fact]
    public void GetProjectMemoryPath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetProjectMemoryPath();
        
        // Assert
        Assert.Equal(Path.Combine(_testBasePath, "project-memory"), path);
        // Directory creation is handled by memory services, not by path resolution
    }
    
    [Fact]
    public void GetLocalMemoryPath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetLocalMemoryPath();
        
        // Assert
        Assert.Equal(Path.Combine(_testBasePath, "local-memory"), path);
        // Directory creation is handled by memory services, not by path resolution
    }
    
    [Fact]
    public void GetLogsPath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetLogsPath();
        
        // Assert
        Assert.Equal(Path.Combine(_testBasePath, "logs"), path);
        // Directory creation is handled by logging service, not by path resolution
    }
    
    [Theory]
    [InlineData("project-memory", "project-memory")]
    [InlineData("local-memory", "local-memory")]
    [InlineData(".codesearch/project-memory", "project-memory")]
    [InlineData(".codesearch/local-memory", "local-memory")]
    [InlineData("PROJECT-MEMORY", "project-memory")] // Case insensitive
    [InlineData("LOCAL-MEMORY", "local-memory")]
    public void GetIndexPath_HandlesMemoryPaths(string input, string expectedSubPath)
    {
        // Act
        var path = _service.GetIndexPath(input);
        
        // Assert
        Assert.Equal(Path.Combine(_testBasePath, expectedSubPath), path);
    }
    
    [Theory]
    [InlineData(@"C:\source\project")]
    [InlineData(@"C:\source\my project with spaces")]
    [InlineData(@"/home/user/projects/myproject")]
    [InlineData(@"\\network\share\project")]
    public void GetIndexPath_CreatesUniqueHashedPaths(string workspacePath)
    {
        // Act
        var indexPath = _service.GetIndexPath(workspacePath);
        
        // Assert
        Assert.True(Path.IsPathRooted(indexPath));
        Assert.Contains("index", indexPath);
        // Directory creation is handled by services that use the path, not by GetIndexPath itself
        
        // Verify path contains workspace name and hash
        var folderName = Path.GetFileName(indexPath);
        Assert.Matches(@"^[^_]+_[a-f0-9]{16}$", folderName);
    }
    
    [Fact]
    public void GetIndexPath_ConsistentHashForSamePath()
    {
        // Arrange
        var workspacePath = @"C:\source\test project";
        
        // Act
        var path1 = _service.GetIndexPath(workspacePath);
        var path2 = _service.GetIndexPath(workspacePath);
        
        // Assert
        Assert.Equal(path1, path2);
    }
    
    [Fact]
    public void GetIndexPath_NormalizesForwardSlashes()
    {
        // Forward/backward slash normalization should work
        var indexPath1 = _service.GetIndexPath(@"C:/source/project");
        var indexPath2 = _service.GetIndexPath(@"C:\source\project");
        
        Assert.Equal(indexPath1, indexPath2);
    }
    
    [Fact] 
    public void GetIndexPath_HandlesTrailingSlashDifferently()
    {
        // Path.GetFullPath actually preserves trailing directory separators on Windows,
        // so these will hash differently
        var indexPath1 = _service.GetIndexPath(@"C:\source\project\");
        var indexPath2 = _service.GetIndexPath(@"C:\source\project");
        
        // These will be different because GetFullPath preserves the trailing slash
        Assert.NotEqual(indexPath1, indexPath2);
    }
    
    [Fact]
    public void GetIndexPath_CaseSensitiveOnWindows()
    {
        // On Windows, file paths are case-insensitive at the OS level,
        // but GetFullPath preserves the case as given, so these will hash differently
        var indexPath1 = _service.GetIndexPath(@"C:\source\project");
        var indexPath2 = _service.GetIndexPath(@"C:\source\PROJECT");
        
        // These should be different because GetFullPath preserves case
        Assert.NotEqual(indexPath1, indexPath2);
    }
    
    [Fact]
    public void GetWorkspaceMetadataPath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetWorkspaceMetadataPath();
        
        // Assert
        Assert.Equal(Path.Combine(_testBasePath, "index", "workspace_metadata.json"), path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)), "Parent directory should be created");
    }
    
    [Fact]
    public void IsProtectedPath_IdentifiesMemoryPaths()
    {
        // Arrange
        var projectMemoryPath = _service.GetProjectMemoryPath();
        var localMemoryPath = _service.GetLocalMemoryPath();
        var regularIndexPath = _service.GetIndexPath(@"C:\some\project");
        
        // Act & Assert
        Assert.True(_service.IsProtectedPath(projectMemoryPath), "Project memory should be protected");
        Assert.True(_service.IsProtectedPath(localMemoryPath), "Local memory should be protected");
        Assert.False(_service.IsProtectedPath(regularIndexPath), "Regular index should not be protected");
    }
    
    [Theory]
    [InlineData("project-memory")]
    [InlineData("local-memory")]
    [InlineData(".codesearch/project-memory")]
    [InlineData(".codesearch/local-memory")]
    public void GetIndexPath_HandlesMemoryPathsSpecially(string memoryPath)
    {
        // Act
        var indexPath = _service.GetIndexPath(memoryPath);
        
        // Assert - should return the direct memory path without hashing
        var fileName = Path.GetFileName(indexPath);
        Assert.False(fileName.Contains("_"), "Memory paths should not have hash suffixes");
        Assert.True(indexPath.EndsWith("project-memory") || indexPath.EndsWith("local-memory"), 
            "Should return direct memory path");
    }
    
    [Theory]
    [InlineData(@"C:\path\with trailing slash\")]
    [InlineData(@"C:\path\with trailing slash")]
    [InlineData(@"C:/path/with/forward/slashes/")]
    [InlineData(@"C:/path/with/forward/slashes")]
    public void IsProtectedPath_HandlesTrailingSlashes(string pathVariation)
    {
        // Arrange
        var projectMemoryPath = _service.GetProjectMemoryPath();
        var testPath = pathVariation.Replace(@"C:\path\with trailing slash", projectMemoryPath)
                                   .Replace(@"C:/path/with/forward/slashes", projectMemoryPath);
        
        // Act
        var isProtected = _service.IsProtectedPath(testPath);
        
        // Assert
        Assert.True(isProtected, $"Path {testPath} should be protected");
    }
    
    [Theory]
    [InlineData("My Project!@#$%")]
    [InlineData("Project (2025)")]
    [InlineData("Project & Company")]
    [InlineData("Café-Project")]
    public void GetIndexPath_HandlesSpecialCharacters(string projectName)
    {
        // Arrange
        var workspacePath = Path.Combine(@"C:\source", projectName);
        
        // Act
        var indexPath = _service.GetIndexPath(workspacePath);
        
        // Assert
        Assert.True(Path.IsPathRooted(indexPath));
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), Path.GetFileName(indexPath));
        // Directory creation is handled by services that use the path, not by GetIndexPath itself
    }
    
    [Fact]
    public void GetIndexPath_TruncatesLongProjectNames()
    {
        // Arrange
        var longProjectName = new string('a', 100); // Very long project name
        var workspacePath = Path.Combine(@"C:\source", longProjectName);
        
        // Act
        var indexPath = _service.GetIndexPath(workspacePath);
        var folderName = Path.GetFileName(indexPath);
        
        // Assert
        Assert.True(folderName.Length <= 47, "Folder name should be truncated (30 chars + underscore + 16 char hash)");
    }
    
    // Real-world examples from services that use PathResolutionService
    
    [Fact]
    public void ClaudeMemoryService_PathResolution()
    {
        // This simulates how ClaudeMemoryService uses the paths
        var projectMemoryWorkspace = "project-memory";
        var localMemoryWorkspace = "local-memory";
        
        // Act
        var projectPath = _service.GetIndexPath(projectMemoryWorkspace);
        var localPath = _service.GetIndexPath(localMemoryWorkspace);
        
        // Assert
        Assert.Equal(_service.GetProjectMemoryPath(), projectPath);
        Assert.Equal(_service.GetLocalMemoryPath(), localPath);
    }
    
    [Fact]
    public void MemoryBackupService_PathResolution()
    {
        // This simulates how MemoryBackupService uses the paths
        var backupDbPath = Path.Combine(_service.GetBasePath(), "memories.db");
        
        // Act & Assert
        Assert.True(Path.IsPathRooted(backupDbPath));
        Assert.Equal(Path.Combine(_testBasePath, "memories.db"), backupDbPath);
    }
    
    [Fact]
    public void LuceneIndexService_WorkspacePathResolution()
    {
        // This simulates how LuceneIndexService resolves workspace paths
        var workspaces = new[]
        {
            @"C:\projects\WebAPI",
            @"D:\source\repos\MicroserviceA",
            @"\\network\share\TeamProject",
            "project-memory",
            "local-memory"
        };
        
        // Act
        var resolvedPaths = workspaces.Select(w => _service.GetIndexPath(w)).ToList();
        
        // Assert
        Assert.All(resolvedPaths, path =>
        {
            Assert.True(Path.IsPathRooted(path));
            // Directory existence is not guaranteed by path resolution - only path computation
        });
        
        // Verify memory paths are resolved correctly
        Assert.Equal(_service.GetProjectMemoryPath(), resolvedPaths[3]);
        Assert.Equal(_service.GetLocalMemoryPath(), resolvedPaths[4]);
    }
    
    [Fact]
    public void FileLoggingService_LogPathResolution()
    {
        // This simulates how FileLoggingService uses the logs path
        var logsPath = _service.GetLogsPath();
        var logFileName = $"codesearch_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        var fullLogPath = Path.Combine(logsPath, logFileName);
        
        // Act & Assert
        Assert.True(Directory.Exists(logsPath));
        Assert.True(Path.IsPathRooted(fullLogPath));
        Assert.Equal(Path.Combine(_testBasePath, "logs", logFileName), fullLogPath);
    }
}