using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.CodeSearch.Next.McpServer.Tests.Base;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.Next.McpServer.Tests.Tools
{
    [TestFixture]
    public class TextSearchToolTests : CodeSearchToolTestBase<TextSearchTool>
    {
        private TextSearchTool _tool = null!;
        
        protected override TextSearchTool CreateTool()
        {
            _tool = new TextSearchTool(
                LuceneIndexServiceMock.Object,
                PathResolutionServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ToolLoggerMock.Object
            );
            return _tool;
        }
        
        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.TextSearch);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("NO_INDEX");
            result.Result.Actions.Should().NotBeNullOrEmpty();
            result.Result.Actions!.Should().Contain(a => a.Action == ToolNames.IndexWorkspace);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Cached_Results_When_Available()
        {
            // Arrange
            SetupExistingIndex();
            var cachedResult = new TokenOptimizedResult
            {
                Success = true,
                Data = new AIResponseData
                {
                    Summary = "Cached results",
                    Count = 5
                }
            };
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()))
                .ReturnsAsync(cachedResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                NoCache = false
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Summary.Should().Be("Cached results");
            result.Result.Meta?.ExtensionData?.Should().ContainKey("cacheHit");
            
            // Verify that Lucene search was not called
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Bypass_Cache_When_NoCache_Is_True()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(5);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()))
                .ReturnsAsync(new TokenOptimizedResult { Success = true }); // Cached result exists
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                NoCache = true // Bypass cache
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify cache was not checked
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()),
                Times.Never);
            
            // Verify search was performed
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Perform_Search_And_Return_Results()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(10);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    TestWorkspacePath,
                    "test query",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                MaxResults = 50
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Count.Should().Be(10);
            result.Result.Data.Summary.Should().Contain("10 results");
            result.Result.Insights.Should().NotBeNullOrEmpty();
            result.Result.Actions.Should().NotBeNullOrEmpty();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Store_Full_Results_When_Truncated()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(100); // Large result set
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var resourceUri = new Uri("resource://test/12345");
            ResourceStorageServiceMock
                .Setup(x => x.StoreAsync(
                    It.IsAny<object>(),
                    It.IsAny<ResourceStorageOptions>()))
                .ReturnsAsync(resourceUri);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                MaxTokens = 1000 // Small token limit to force truncation
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            
            // Verify storage was called for large result
            ResourceStorageServiceMock.Verify(
                x => x.StoreAsync(It.IsAny<object>(), It.IsAny<ResourceStorageOptions>()),
                Times.AtLeastOnce);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Cache_Successful_Results()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(5);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                NoCache = false
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify result was cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<TokenOptimizedResult>(),
                    It.IsAny<CacheEntryOptions>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Search_Errors_Gracefully()
        {
            // Arrange
            SetupExistingIndex();
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Search failed"));
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue(); // Tool execution succeeded
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse(); // But search failed
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("SEARCH_ERROR");
            result.Result.Error.Message.Should().Contain("Search failed");
            result.Result.Error.Recovery.Should().NotBeNull();
            result.Result.Error.Recovery!.Steps.Should().NotBeNullOrEmpty();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Validate_Required_Parameters()
        {
            // Arrange
            var parameters = new TextSearchParameters
            {
                Query = null!, // Missing required parameter
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            result.Exception.Should().BeOfType<ArgumentNullException>();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Respect_MaxResults_Limit()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(100);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<int>(max => max == 500), // Verify max limit
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                MaxResults = 1000 // Above the 500 limit
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify search was called with max limit of 500
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    500,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Use_Adaptive_Response_Mode_By_Default()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(20);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath
                // ResponseMode not specified - should default to "adaptive"
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                () => _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            
            // Verify response has appropriate content for adaptive mode
            result.Result.Data.Should().NotBeNull();
            result.Result.Insights.Should().NotBeNullOrEmpty();
            result.Result.Actions.Should().NotBeNullOrEmpty();
        }
    }
}