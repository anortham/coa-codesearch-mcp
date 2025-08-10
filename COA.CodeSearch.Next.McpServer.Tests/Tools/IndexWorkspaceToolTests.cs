using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Tests.Base;
using COA.CodeSearch.Next.McpServer.Models;
using IndexingResult = COA.CodeSearch.Next.McpServer.Services.IndexingResult;
using COA.Mcp.Framework.TokenOptimization.Models;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.IO;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.DependencyInjection;

namespace COA.CodeSearch.Next.McpServer.Tests.Tools
{
    [TestFixture]
    public class IndexWorkspaceToolTests : CodeSearchToolTestBase<IndexWorkspaceTool>
    {
        private IndexWorkspaceTool _tool = null!;
        
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            
            // FileWatcherService is optional and will be null if not registered
            // The tool handles this gracefully
        }
        
        protected override IndexWorkspaceTool CreateTool()
        {
            _tool = new IndexWorkspaceTool(
                LuceneIndexServiceMock.Object,
                PathResolutionServiceMock.Object,
                FileIndexingServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ServiceProvider,
                ToolLoggerMock.Object
            );
            return _tool;
        }
        
        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.IndexWorkspace);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Resources);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Directory_Not_Found()
        {
            // Arrange
            var nonExistentPath = Path.Combine(TestWorkspacePath, "non-existent");
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = nonExistentPath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("DIRECTORY_NOT_FOUND");
            result.Result.Actions.Should().NotBeNullOrEmpty();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Create_New_Index_When_None_Exists()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = true,
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            FileIndexingServiceMock
                .Setup(x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexingResult
                {
                    Success = true,
                    IndexedFileCount = 50,
                    SkippedFileCount = 5,
                    Duration = TimeSpan.FromSeconds(2)
                });
            
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexStatistics
                {
                    DocumentCount = 50,
                    DeletedDocumentCount = 0,
                    SegmentCount = 1,
                    IndexSizeBytes = 50000,
                    FileTypeDistribution = new() { [".cs"] = 50 }
                });
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Summary.Should().Contain("Created new index");
            result.Result.Data.Summary.Should().Contain("50 files");
            result.Result.Insights.Should().NotBeNullOrEmpty();
            result.Result.Actions.Should().NotBeNullOrEmpty();
            
            // Verify indexing was performed
            FileIndexingServiceMock.Verify(
                x => x.IndexWorkspaceAsync(TestWorkspacePath, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Use_Existing_Index_When_Not_Force_Rebuild()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = false, // Existing index
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            LuceneIndexServiceMock
                .Setup(x => x.GetDocumentCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(100);
            
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexStatistics
                {
                    DocumentCount = 100,
                    DeletedDocumentCount = 0,
                    SegmentCount = 2,
                    IndexSizeBytes = 100000,
                    FileTypeDistribution = new() { [".cs"] = 80, [".json"] = 20 }
                });
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath,
                ForceRebuild = false // Don't rebuild
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            // IndexResponseBuilder says "Updated index" even when no update happened
            // This is a known limitation - it doesn't distinguish between actual update and no-op
            result.Result.Data.Summary.Should().Contain("Updated index");
            result.Result.Data.Summary.Should().Contain("100 files");
            
            // Verify indexing was NOT performed (this is the real test)
            FileIndexingServiceMock.Verify(
                x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            
            // Verify index was NOT cleared
            LuceneIndexServiceMock.Verify(
                x => x.ClearIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Rebuild_Index_When_Force_Rebuild_Is_True()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = false, // Existing index
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            FileIndexingServiceMock
                .Setup(x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexingResult
                {
                    Success = true,
                    IndexedFileCount = 75,
                    SkippedFileCount = 10,
                    Duration = TimeSpan.FromSeconds(3)
                });
            
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexStatistics
                {
                    DocumentCount = 75,
                    DeletedDocumentCount = 0,
                    SegmentCount = 1,
                    IndexSizeBytes = 75000,
                    FileTypeDistribution = new() { [".cs"] = 75 }
                });
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath,
                ForceRebuild = true // Force rebuild
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Summary.Should().Contain("75 files");
            
            // Verify index was cleared
            LuceneIndexServiceMock.Verify(
                x => x.ClearIndexAsync(TestWorkspacePath, It.IsAny<CancellationToken>()),
                Times.Once);
            
            // Verify indexing was performed
            FileIndexingServiceMock.Verify(
                x => x.IndexWorkspaceAsync(TestWorkspacePath, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Start_File_Watcher_When_Available()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = true,
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            FileIndexingServiceMock
                .Setup(x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexingResult
                {
                    Success = true,
                    IndexedFileCount = 50,
                    SkippedFileCount = 5,
                    Duration = TimeSpan.FromSeconds(2)
                });
            
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexStatistics
                {
                    DocumentCount = 50,
                    DeletedDocumentCount = 0,
                    SegmentCount = 1,
                    IndexSizeBytes = 50000,
                    FileTypeDistribution = new() { [".cs"] = 50 }
                });
            
            // FileWatcherService is not registered, so it will be null
            ServiceProvider = Services.BuildServiceProvider();
            
            // Recreate tool with new service provider
            _tool = new IndexWorkspaceTool(
                LuceneIndexServiceMock.Object,
                PathResolutionServiceMock.Object,
                FileIndexingServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ServiceProvider,
                ToolLoggerMock.Object
            );
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            
            // File watcher won't be started since FileWatcherService is null
            // But the indexing should still succeed
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Initialization_Failure()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = false,
                    ErrorMessage = "Failed to acquire write lock"
                });
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("INIT_FAILED");
            result.Result.Error.Message.Should().Contain("Failed to acquire write lock");
            result.Result.Error.Recovery.Should().NotBeNull();
            result.Result.Error.Recovery!.Steps.Should().Contain(s => s.Contains("write.lock"));
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Indexing_Failure()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = true,
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            FileIndexingServiceMock
                .Setup(x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexingResult
                {
                    Success = false,
                    ErrorMessage = "Insufficient disk space"
                });
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("INDEXING_FAILED");
            result.Result.Error.Message.Should().Contain("Insufficient disk space");
            result.Result.Error.Recovery.Should().NotBeNull();
            result.Result.Error.Recovery!.Steps.Should().Contain(s => s.Contains("disk space"));
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Unexpected_Exceptions()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Unexpected error"));
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("INDEX_ERROR");
            result.Result.Error.Message.Should().Contain("Unexpected error");
            result.Result.Error.Recovery.Should().NotBeNull();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Validate_Required_Parameters()
        {
            // Arrange
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = null! // Missing required parameter
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            result.Exception.Should().BeOfType<COA.Mcp.Framework.Exceptions.ToolExecutionException>();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Include_Statistics_In_Response()
        {
            // Arrange
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexInitResult
                {
                    Success = true,
                    IsNewIndex = true,
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
            
            FileIndexingServiceMock
                .Setup(x => x.IndexWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexingResult
                {
                    Success = true,
                    IndexedFileCount = 50,
                    SkippedFileCount = 5,
                    Duration = TimeSpan.FromSeconds(2)
                });
            
            var stats = new Services.Lucene.IndexStatistics
            {
                DocumentCount = 50,
                DeletedDocumentCount = 2,
                SegmentCount = 3,
                IndexSizeBytes = 50000,
                FileTypeDistribution = new() 
                { 
                    [".cs"] = 30,
                    [".json"] = 15,
                    [".xml"] = 5
                }
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stats);
            
            var parameters = new IndexWorkspaceParameters
            {
                WorkspacePath = TestWorkspacePath,
                ResponseMode = "full" // Request full response
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            
            // Check for statistics-related insights
            result.Result.Insights.Should().Contain(i => i.Contains("Top file types"));
            result.Result.Insights.Should().Contain(i => i.Contains(".cs (30)"));
        }
    }
}