using System;
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
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IPathResolutionService> _mockPathResolution;
    private readonly LuceneIndexService _service;

    public LuceneIndexServiceTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"lucene_test_{Guid.NewGuid()}");
        System.IO.Directory.CreateDirectory(_testBasePath);

        _mockLogger = new Mock<ILogger<LuceneIndexService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockPathResolution = new Mock<IPathResolutionService>();

        // Setup default configuration
        _mockConfiguration.Setup(x => x.GetSection("Lucene:LockTimeoutMinutes"))
            .Returns(Mock.Of<IConfigurationSection>(s => s.Value == "15"));

        // Setup path resolution
        _mockPathResolution.Setup(x => x.GetBasePath()).Returns(_testBasePath);
        _mockPathResolution.Setup(x => x.GetWorkspaceMetadataPath())
            .Returns(Path.Combine(_testBasePath, "workspace_metadata.json"));

        _service = new LuceneIndexService(_mockLogger.Object, _mockConfiguration.Object, _mockPathResolution.Object);
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_ShouldUseDirectly_ProjectMemory()
    {
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, ".codesearch", "project-memory");
        var localMemoryPath = Path.Combine(_testBasePath, ".codesearch", "local-memory");
        
        _mockPathResolution.Setup(x => x.GetProjectMemoryPath()).Returns(projectMemoryPath);
        _mockPathResolution.Setup(x => x.GetLocalMemoryPath()).Returns(localMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act
        var writer = await _service.GetIndexWriterAsync(projectMemoryPath);
        var physicalPath = _service.GetPhysicalIndexPath(projectMemoryPath);

        // Assert
        Assert.NotNull(writer);
        Assert.Equal(projectMemoryPath, physicalPath);
        Assert.True(System.IO.Directory.Exists(projectMemoryPath));
        
        // Verify GetIndexPath was NOT called for memory paths
        _mockPathResolution.Verify(x => x.GetIndexPath(It.IsAny<string>()), Times.Never);
        
        // Cleanup
        writer.Dispose();
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_ShouldUseDirectly_LocalMemory()
    {
        // Arrange
        var localMemoryPath = Path.Combine(_testBasePath, ".codesearch", "local-memory");
        
        _mockPathResolution.Setup(x => x.GetLocalMemoryPath()).Returns(localMemoryPath);
        _mockPathResolution.Setup(x => x.IsProtectedPath(localMemoryPath)).Returns(true);

        // Act
        var writer = await _service.GetIndexWriterAsync(localMemoryPath);
        var physicalPath = _service.GetPhysicalIndexPath(localMemoryPath);

        // Assert
        Assert.NotNull(writer);
        Assert.Equal(localMemoryPath, physicalPath);
        Assert.True(System.IO.Directory.Exists(localMemoryPath));
        
        // Verify GetIndexPath was NOT called for memory paths
        _mockPathResolution.Verify(x => x.GetIndexPath(It.IsAny<string>()), Times.Never);
        
        // Cleanup
        writer.Dispose();
    }

    [Fact]
    public async Task GetIndexPath_MemoryPath_CrossPlatform()
    {
        // Test both Windows and Unix-style paths
        var testPaths = new[]
        {
            Path.Combine("C:", "source", ".codesearch", "project-memory"),
            "/home/user/.codesearch/project-memory",
            Path.Combine(_testBasePath, ".codesearch", "local-memory"),
            "/var/lib/app/.codesearch/local-memory"
        };

        foreach (var memoryPath in testPaths)
        {
            // Setup IsProtectedPath to return true for paths containing ".codesearch" and "memory"
            if (memoryPath.Contains(".codesearch") && (memoryPath.Contains("project-memory") || memoryPath.Contains("local-memory")))
            {
                _mockPathResolution.Setup(x => x.IsProtectedPath(memoryPath)).Returns(true);
            }
            
            // Act
            var physicalPath = _service.GetPhysicalIndexPath(memoryPath);

            // Assert
            Assert.Equal(memoryPath, physicalPath);
            
            // Verify GetIndexPath was NOT called for memory paths
            _mockPathResolution.Verify(x => x.GetIndexPath(It.IsAny<string>()), Times.Never);
        }
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
        await Task.Yield();
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, ".codesearch", "project-memory");
        System.IO.Directory.CreateDirectory(projectMemoryPath);
        
        var testFile = Path.Combine(projectMemoryPath, "test.txt");
        File.WriteAllText(testFile, "important data");
        
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.ClearIndexAsync(projectMemoryPath);
        });

        // Verify the file still exists
        Assert.True(File.Exists(testFile));
        Assert.Equal("important data", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task RegularPath_CanBeCleared()
    {
        await Task.Yield();
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
        await Task.Yield();
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

    // Test removed - memory index stuck lock recovery is tested elsewhere

    [Fact]
    public async Task MemoryIndex_WriterCanWriteDocuments()
    {
        await Task.Yield();
        // Arrange
        var projectMemoryPath = Path.Combine(_testBasePath, ".codesearch", "project-memory");
        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        // Setup IsProtectedPath to return true for memory path
        _mockPathResolution.Setup(x => x.IsProtectedPath(projectMemoryPath)).Returns(true);

        // Act
        var writer = await _service.GetIndexWriterAsync(projectMemoryPath);
        
        // Add a document
        var doc = new Document();
        doc.Add(new StringField("id", "test-123", Field.Store.YES));
        doc.Add(new TextField("content", "This is a test memory", Field.Store.YES));
        writer.AddDocument(doc);
        
        await _service.CommitAsync(projectMemoryPath);
        
        // Search for the document
        var searcher = await _service.GetIndexSearcherAsync(projectMemoryPath);
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

    // Test removed - path resolution is now handled differently and tested elsewhere

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