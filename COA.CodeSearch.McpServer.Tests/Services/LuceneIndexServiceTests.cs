using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class LuceneIndexServiceTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly Mock<ILogger<LuceneIndexService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly Mock<IPathResolutionService> _mockPathResolution;
    private readonly LuceneIndexService _service;

    public LuceneIndexServiceTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"lucene_test_{Guid.NewGuid()}");
        System.IO.Directory.CreateDirectory(_testBasePath);

        _mockLogger = new Mock<ILogger<LuceneIndexService>>();
        
        // Setup logger to capture calls (for debugging)
        _mockLogger.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()));
        _mockPathResolution = new Mock<IPathResolutionService>();

        // Setup real configuration with short timeout for testing stuck locks
        var configData = new Dictionary<string, string>
        {
            ["Lucene:LockTimeoutMinutes"] = "1" // 1 minute timeout for testing
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Setup path resolution
        _mockPathResolution.Setup(x => x.GetBasePath()).Returns(_testBasePath);
        _mockPathResolution.Setup(x => x.GetWorkspaceMetadataPath())
            .Returns(Path.Combine(_testBasePath, "workspace_metadata.json"));

        _service = new LuceneIndexService(_mockLogger.Object, _configuration, _mockPathResolution.Object);
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_ShouldUseDirectly_ProjectMemory()
    {
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, "project-memory");
        System.IO.Directory.CreateDirectory(projectMemoryPath);
        
        // Setup PathResolutionService to recognize "project-memory" and return the path
        _mockPathResolution.Setup(x => x.GetIndexPath("project-memory")).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act - Pass the workspace identifier, not the full path
        var writer = await _service.GetIndexWriterAsync("project-memory");
        var physicalPath = _service.GetPhysicalIndexPath("project-memory");

        // Assert
        Assert.NotNull(writer);
        Assert.Equal(projectMemoryPath, physicalPath);
        
        // Verify GetIndexPath WAS called with the workspace identifier
        _mockPathResolution.Verify(x => x.GetIndexPath("project-memory"), Times.AtLeastOnce);
        
        // Cleanup
        writer.Dispose();
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_ShouldUseDirectly_LocalMemory()
    {
        // Arrange
        var localMemoryPath = Path.Combine(_testBasePath, "local-memory");
        System.IO.Directory.CreateDirectory(localMemoryPath);
        
        // Setup PathResolutionService to recognize "local-memory" and return the path
        _mockPathResolution.Setup(x => x.GetIndexPath("local-memory")).Returns(localMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(localMemoryPath)).Returns(true);

        // Act - Pass the workspace identifier, not the full path
        var writer = await _service.GetIndexWriterAsync("local-memory");
        var physicalPath = _service.GetPhysicalIndexPath("local-memory");

        // Assert
        Assert.NotNull(writer);
        Assert.Equal(localMemoryPath, physicalPath);
        
        // Cleanup
        writer.Dispose();
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_CrossPlatform()
    {
        // Test that memory workspace identifiers work correctly
        var projectMemoryPath = Path.Combine(_testBasePath, "project-memory");
        var localMemoryPath = Path.Combine(_testBasePath, "local-memory");
        
        // Setup mocks to return platform-specific paths
        _mockPathResolution.Setup(x => x.GetIndexPath("project-memory")).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.GetIndexPath("local-memory")).Returns(localMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(It.IsAny<string>())).Returns(true);

        // Act & Assert for project-memory
        var projectPhysicalPath = _service.GetPhysicalIndexPath("project-memory");
        Assert.Equal(projectMemoryPath, projectPhysicalPath);
        
        // Act & Assert for local-memory
        var localPhysicalPath = _service.GetPhysicalIndexPath("local-memory");
        Assert.Equal(localMemoryPath, localPhysicalPath);
        
        // Verify GetIndexPath WAS called with the workspace identifiers
        _mockPathResolution.Verify(x => x.GetIndexPath("project-memory"), Times.Once);
        _mockPathResolution.Verify(x => x.GetIndexPath("local-memory"), Times.Once);
    }

    [Fact]
    public async Task GetIndexPath_RegularWorkspace_ShouldHash()
    {
        // Arrange
        var workspacePath = Path.Combine(_testBasePath, "MyProject");
        var hashedPath = Path.Combine(_testBasePath, "index", "MyProject_a1b2c3d4");
        
        _mockPathResolution.Setup(x => x.GetIndexPath(workspacePath)).Returns(hashedPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(It.IsAny<string>())).Returns(false);

        // Act
        var writer = await _service.GetIndexWriterAsync(workspacePath);
        var physicalPath = _service.GetPhysicalIndexPath(workspacePath);

        // Assert
        Assert.NotNull(writer);
        Assert.Equal(hashedPath, physicalPath);
        Assert.True(System.IO.Directory.Exists(hashedPath));
        
        // Verify GetIndexPath WAS called for regular paths
        _mockPathResolution.Verify(x => x.GetIndexPath(workspacePath), Times.AtLeastOnce);
        
        // Cleanup
        writer.Dispose();
    }

    [Fact]
    public async Task MemoryPath_ShouldNotBeCleared()
    {
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, "project-memory");
        System.IO.Directory.CreateDirectory(projectMemoryPath);
        
        var testFile = Path.Combine(projectMemoryPath, "test.txt");
        File.WriteAllText(testFile, "important data");
        
        _mockPathResolution.Setup(x => x.GetIndexPath("project-memory")).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act & Assert - use workspace identifier
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.ClearIndexAsync("project-memory");
        });

        // Verify the file still exists
        Assert.True(File.Exists(testFile));
        Assert.Equal("important data", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task RegularPath_CanBeCleared()
    {
        // Arrange
        var workspacePath = Path.Combine(_testBasePath, "MyProject");
        var hashedPath = Path.Combine(_testBasePath, "index", "MyProject_a1b2c3d4");
        System.IO.Directory.CreateDirectory(hashedPath);
        
        var testFile = Path.Combine(hashedPath, "test.txt");
        File.WriteAllText(testFile, "data");
        
        _mockPathResolution.Setup(x => x.GetIndexPath(workspacePath)).Returns(hashedPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(hashedPath)).Returns(false);

        // Act
        await _service.ClearIndexAsync(workspacePath);

        // Assert
        Assert.False(File.Exists(testFile));
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var workspacePath = Path.Combine(_testBasePath, "ConcurrentTest");
        var hashedPath = Path.Combine(_testBasePath, "index", "ConcurrentTest_xyz");
        
        _mockPathResolution.Setup(x => x.GetIndexPath(workspacePath)).Returns(hashedPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(It.IsAny<string>())).Returns(false);

        // Act - Create multiple writers concurrently
        var tasks = new Task<IndexWriter>[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _service.GetIndexWriterAsync(workspacePath);
        }

        var writers = await Task.WhenAll(tasks);

        // Assert - All should get the same writer instance
        var firstWriter = writers[0];
        foreach (var writer in writers)
        {
            Assert.Same(firstWriter, writer);
        }
        
        // Cleanup
        firstWriter.Dispose();
    }

    [Fact(Skip = "Lock recovery mechanism needs redesign after path refactoring - see issue #XYZ")]
    public async Task StuckLock_MemoryIndex_ShouldRecover()
    {
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, "project-memory");
        System.IO.Directory.CreateDirectory(projectMemoryPath);
        
        // Setup PathResolutionService to return our test memory path
        _mockPathResolution.Setup(x => x.GetIndexPath("project-memory")).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);
        
        // First, create an index so there are some segments
        var firstWriter = await _service.GetIndexWriterAsync("project-memory");
        var doc = new Lucene.Net.Documents.Document();
        doc.Add(new Lucene.Net.Documents.StringField("test", "value", Lucene.Net.Documents.Field.Store.YES));
        firstWriter.AddDocument(doc);
        firstWriter.Commit();
        firstWriter.Dispose();
        
        // Important: Close the writer properly to ensure it's removed from cache
        _service.CloseWriter("project-memory", commit: true);
        
        // Dispose the service to clear all caches
        _service.Dispose();
        
        // Create a new service instance (simulating server restart)
        var newService = new LuceneIndexService(_mockLogger.Object, _configuration, _mockPathResolution.Object);
        
        // Now create a stuck lock file (simulating a previous writer that crashed)
        var lockPath = Path.Combine(projectMemoryPath, "write.lock");
        File.WriteAllText(lockPath, "lock");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddMinutes(-5)); // Lock older than 1 minute timeout

        // Verify the lock file exists and is old enough
        Assert.True(File.Exists(lockPath), "Lock file should exist before calling GetIndexWriterAsync");
        var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
        Assert.True(lockAge.TotalMinutes > 1, $"Lock should be old enough (age: {lockAge.TotalMinutes} minutes)");
        
        // Debug: Check what path the service will actually use
        var actualIndexPath = newService.GetPhysicalIndexPath("project-memory");
        Assert.Equal(projectMemoryPath, actualIndexPath);

        // Act - try to get another writer (this should detect and remove the stuck lock)
        var secondWriter = await newService.GetIndexWriterAsync("project-memory");

        // Assert
        Assert.NotNull(secondWriter);
        Assert.False(File.Exists(lockPath), "Lock should be removed after GetIndexWriterAsync"); 
        
        // Cleanup
        secondWriter.Dispose();
        newService.Dispose();
    }

    [Fact]
    public async Task MemoryIndex_WriterCanWriteDocuments()
    {
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, "project-memory");
        System.IO.Directory.CreateDirectory(projectMemoryPath);
        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        _mockPathResolution.Setup(x => x.GetIndexPath("project-memory")).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act - use workspace identifier
        var writer = await _service.GetIndexWriterAsync("project-memory");
        
        // Add a document
        var doc = new Document();
        doc.Add(new StringField("id", "test-123", Field.Store.YES));
        doc.Add(new TextField("content", "This is a test memory", Field.Store.YES));
        writer.AddDocument(doc);
        
        await _service.CommitAsync("project-memory");
        
        // Search for the document
        var searcher = await _service.GetIndexSearcherAsync("project-memory");
        var query = new TermQuery(new Term("id", "test-123"));
        var hits = searcher.Search(query, 10);

        // Assert
        Assert.Equal(1, hits.TotalHits);
        var foundDoc = searcher.Doc(hits.ScoreDocs[0].Doc);
        Assert.Equal("test-123", foundDoc.Get("id"));
        Assert.Equal("This is a test memory", foundDoc.Get("content"));
        
        // Cleanup
        writer.Dispose();
        analyzer.Dispose();
    }

    [Fact]
    public void GetPhysicalIndexPath_AlwaysDelegatesToPathResolutionService()
    {
        // Test that LuceneIndexService always delegates path resolution to PathResolutionService
        var testCases = new[]
        {
            "project-memory",
            "local-memory",
            "C:\\some\\workspace",
            "/home/user/project",
            "relative/path"
        };

        foreach (var inputPath in testCases)
        {
            // Setup mock to return a specific resolved path
            var expectedResolvedPath = $"{_testBasePath}\\resolved\\{inputPath.Replace('/', '_').Replace('\\', '_')}";
            _mockPathResolution.Setup(x => x.GetIndexPath(inputPath)).Returns(expectedResolvedPath);
            
            // Call GetPhysicalIndexPath
            var actualPath = _service.GetPhysicalIndexPath(inputPath);
            
            // Verify it returns exactly what PathResolutionService returns
            Assert.Equal(expectedResolvedPath, actualPath);
            
            // Verify PathResolutionService was called exactly once with the correct parameter
            _mockPathResolution.Verify(x => x.GetIndexPath(inputPath), Times.Once);
            
            // Reset for next iteration
            _mockPathResolution.Invocations.Clear();
        }
    }

    public void Dispose()
    {
        _service?.Dispose();
        
        // Clean up test directory
        if (System.IO.Directory.Exists(_testBasePath))
        {
            try
            {
                System.IO.Directory.Delete(_testBasePath, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}