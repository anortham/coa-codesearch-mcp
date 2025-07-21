using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Helper class to create properly configured mock Lucene services for testing
/// </summary>
public static class MockLuceneIndexService
{
    /// <summary>
    /// Creates a mock ILuceneIndexService that simulates successful operations without actual index creation
    /// </summary>
    public static Mock<ILuceneIndexService> CreateMock()
    {
        var mockService = new Mock<ILuceneIndexService>();
        
        // Note: We don't create shared mock writers/searchers here
        // because each test needs to set up its own to capture operations
        
        // Setup CommitAsync to complete successfully
        mockService.Setup(x => x.CommitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup OptimizeAsync to complete successfully
        mockService.Setup(x => x.OptimizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup ClearIndexAsync to complete successfully
        mockService.Setup(x => x.ClearIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup GetPhysicalIndexPath to return a test path
        mockService.Setup(x => x.GetPhysicalIndexPath(It.IsAny<string>()))
            .Returns<string>(workspace => Path.Combine(Path.GetTempPath(), "mock_index", workspace));
        
        return mockService;
    }
    
    /// <summary>
    /// Creates a mock ILuceneWriterManager for testing
    /// </summary>
    public static Mock<ILuceneWriterManager> CreateWriterManagerMock()
    {
        var mockManager = new Mock<ILuceneWriterManager>();
        var mockWriter = new Mock<IndexWriter>();
        
        // Setup GetOrCreateWriter to return a mock writer
        mockManager.Setup(x => x.GetOrCreateWriter(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(mockWriter.Object);
        
        // Setup CloseWriter to complete successfully
        mockManager.Setup(x => x.CloseWriter(It.IsAny<string>(), It.IsAny<bool>()));
        
        // DisposeAllWriters doesn't exist on the interface - remove this setup
        
        return mockManager;
    }
}