using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Lucene.Net.Search;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class TextSearchToolTests : CodeSearchToolTestBase<TextSearchTool>
    {
        private TextSearchTool _tool = null!;
        
        protected override TextSearchTool CreateTool()
        {
            var queryPreprocessorLoggerMock = new Mock<ILogger<QueryPreprocessor>>();
            var queryPreprocessor = new QueryPreprocessor(queryPreprocessorLoggerMock.Object);
            var projectKnowledgeServiceMock = CreateMock<IProjectKnowledgeService>();
            var smartDocLoggerMock = new Mock<ILogger<SmartDocumentationService>>();
            var smartDocumentationService = new SmartDocumentationService(smartDocLoggerMock.Object);
            
            _tool = new TextSearchTool(
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                queryPreprocessor,
                projectKnowledgeServiceMock.Object,
                smartDocumentationService,
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
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
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
            var cachedResult = new AIOptimizedResponse<SearchResult>
            {
                Success = true,
                Data = new AIResponseData<SearchResult>
                {
                    Summary = "Cached results",
                    Count = 5,
                    Results = new SearchResult { TotalHits = 5 }
                }
            };
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()))
                .ReturnsAsync(cachedResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                NoCache = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Summary.Should().Be("Cached results");
            result.Result.Meta?.ExtensionData?.Should().ContainKey("cacheHit");
            
            // Verify that Lucene search was not called
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()))
                .ReturnsAsync(new AIOptimizedResponse<SearchResult> 
                { 
                    Success = true,
                    Data = new AIResponseData<SearchResult>
                    {
                        Results = new SearchResult { TotalHits = 0 }
                    }
                }); // Cached result exists
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                NoCache = true // Bypass cache
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify cache was not checked
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<SearchResult>(It.IsAny<string>()),
                Times.Never);
            
            // Verify search was performed
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Count.Should().Be(10);
            result.Result.Data.Summary.Should().Contain("10 hits");
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
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var resourceUri = new ResourceUri("mcp-resource://memory/search-results/12345");
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
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
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
                    It.IsAny<Lucene.Net.Search.Query>(),
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
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify result was cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<AIOptimizedResponse<SearchResult>>(),
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
                    It.IsAny<Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Search failed"));
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
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
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            result.Exception.Should().BeOfType<COA.Mcp.Framework.Exceptions.ToolExecutionException>();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Use_ResponseMode_Based_MaxResults()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(100);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                ResponseMode = "full" // Should use token-aware limiting (15 for full mode)
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify search was called with token-aware limit (10 for full mode)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    10, // Full mode token-aware limit
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Use_Summary_Mode_MaxResults()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(50);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "test query",
                WorkspacePath = TestWorkspacePath,
                ResponseMode = "summary" // Should use token-aware limiting (3 for summary mode)
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify search was called with token-aware limit (2 for summary mode)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    2, // Summary mode token-aware limit
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
                    It.IsAny<Lucene.Net.Search.Query>(),
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
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            
            // Verify search was called with token-aware default limit (3 for adaptive/default mode)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    3, // Adaptive mode token-aware limit
                    It.IsAny<CancellationToken>()),
                Times.Once);
            
            // Verify response has appropriate content for adaptive mode
            result.Result.Data.Should().NotBeNull();
            result.Result.Insights.Should().NotBeNullOrEmpty();
            result.Result.Actions.Should().NotBeNullOrEmpty();
        }
    }
}