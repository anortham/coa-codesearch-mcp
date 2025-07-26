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
        
        // Setup ForceMergeAsync to complete successfully
        mockService.Setup(x => x.ForceMergeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup DefragmentIndexAsync to return a mock result
        mockService.Setup(x => x.DefragmentIndexAsync(It.IsAny<string>(), It.IsAny<IndexDefragmentationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDefragmentationResult
            {
                StartTime = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow.AddSeconds(1),
                Duration = TimeSpan.FromSeconds(1),
                Success = true,
                ActionTaken = DefragmentationAction.Skipped,
                Reason = "Mock defragmentation",
                InitialFragmentationLevel = 10,
                FinalFragmentationLevel = 5,
                FragmentationReduction = 5,
                InitialSegmentCount = 3,
                FinalSegmentCount = 1,
                SegmentReduction = 2,
                InitialSizeBytes = 2048,
                FinalSizeBytes = 1024,
                SizeReductionBytes = 1024
            });
        
        // Setup ClearIndexAsync to complete successfully
        mockService.Setup(x => x.ClearIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup GetPhysicalIndexPathAsync to return a test path
        mockService.Setup(x => x.GetPhysicalIndexPathAsync(It.IsAny<string>()))
            .ReturnsAsync<string, ILuceneIndexService, string>(workspace => Path.Combine(Path.GetTempPath(), "mock_index", workspace));
        
        return mockService;
    }
    
    /// <summary>
    /// Creates a mock ILuceneWriterManager for testing
    /// </summary>
    public static Mock<ILuceneWriterManager> CreateWriterManagerMock()
    {
        var mockManager = new Mock<ILuceneWriterManager>();
        var mockWriter = new Mock<IndexWriter>();
        
        // Setup GetOrCreateWriterAsync to return a mock writer
        mockManager.Setup(x => x.GetOrCreateWriterAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockWriter.Object);
        
        // Setup CloseWriterAsync to complete successfully
        mockManager.Setup(x => x.CloseWriterAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // DisposeAllWriters doesn't exist on the interface - remove this setup
        
        return mockManager;
    }
}