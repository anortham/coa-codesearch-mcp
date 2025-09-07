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
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);
            
            _tool = new TextSearchTool(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                queryPreprocessor,
                null, // IQueryTypeDetector is optional
                projectKnowledgeServiceMock.Object,
                smartDocumentationService,
                VSCodeBridgeMock.Object,
                smartQueryPreprocessor,
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
            
            // Verify that Lucene search was not called (check both overloads)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
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
                    It.IsAny<bool>(),
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
            
            // Verify search was performed (5-parameter version)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
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
            result.Result.Error.Message.Should().Contain("Error performing search: Search failed");
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            
            // Verify response has appropriate content for adaptive mode
            result.Result.Data.Should().NotBeNull();
            result.Result.Insights.Should().NotBeNullOrEmpty();
            result.Result.Actions.Should().NotBeNullOrEmpty();
        }
        
        #region Type-First Workflow Tests - Critical for preventing Claude's rewrite cycles
        
        [Test]
        public async Task WhenSearchingForClassName_TypeDefinitionShouldBeFirst()
        {
            // This test ensures Claude sees type definitions FIRST, preventing rewrites
            
            // Arrange
            SetupExistingIndex();
            
            // Create mock search results with type definition boosting
            var typeDefHit = new SearchHit
            {
                FilePath = @"C:\test\Services\LuceneIndexService.cs",
                Score = 10.0f, // High score due to TypeDefinitionBoostFactor
                LineNumber = 21,
                Fields = new Dictionary<string, string>
                {
                    ["type_info"] = "{\"types\":[{\"Name\":\"LuceneIndexService\",\"Kind\":\"class\",\"Signature\":\"public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable\",\"Line\":21,\"Column\":1,\"Modifiers\":[\"public\"]}],\"methods\":[{\"Name\":\"SearchAsync\",\"Signature\":\"Task<SearchResult> SearchAsync(string workspacePath, Query query)\",\"Line\":257}],\"language\":\"c-sharp\"}"
                },
                ContextLines = new List<string>
                {
                    "/// <summary>",
                    "/// Thread-safe Lucene index service", 
                    "/// </summary>",
                    "public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable",
                    "{"
                },
                Snippet = "public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable",
                EnhancedSnippet = "📦 LuceneIndexService - public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable",
                TypeContext = new TypeContext 
                { 
                    ContainingType = "LuceneIndexService",
                    Language = "c-sharp"
                }
            };
            
            var regularHit = new SearchHit
            {
                FilePath = @"C:\test\SomeOtherFile.cs",
                Score = 0.5f, // Lower score
                LineNumber = 10,
                Fields = new Dictionary<string, string>(),
                Snippet = "// Some reference to LuceneIndexService"
            };
            
            var searchResult = new SearchResult
            {
                TotalHits = 2,
                Hits = new List<SearchHit> { typeDefHit, regularHit }, // Type definition should be FIRST
                Query = "LuceneIndexService"
            };
            searchResult.SearchTime = TimeSpan.FromMilliseconds(10);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "LuceneIndexService",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert - Critical for Claude's workflow
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            
            var data = result.Result!.Data;
            data.Should().NotBeNull();
#pragma warning disable CS8602 // Dereference of a possibly null reference - data is asserted not null above
            var hits = data!.Results.Hits;
#pragma warning restore CS8602
            hits.Should().NotBeNull().And.NotBeEmpty();
            
            var firstHit = hits!.First();
            firstHit.Score.Should().Be(10.0f, "Type definitions must have highest score to appear first");
            firstHit.EnhancedSnippet.Should().NotBeNullOrEmpty("Enhanced snippet should show prominent type info");
            firstHit.EnhancedSnippet.Should().Contain("📦", "Type information must be visually prominent");
            firstHit.TypeContext.Should().NotBeNull("Type context must be populated for type queries");
            firstHit.TypeContext!.ContainingType.Should().NotBeNullOrEmpty("Containing type must be identified");
        }
        
        [Test]
        public async Task TypeContext_ShouldAlwaysBePopulated_ForTypeQueries()
        {
            // Ensures Claude gets type info even for method searches
            
            // Arrange
            SetupExistingIndex();
            
            var methodSearchHit = new SearchHit
            {
                FilePath = @"C:\test\Services\LuceneIndexService.cs",
                Score = 1.0f,
                LineNumber = 257, // Inside SearchAsync method
                Fields = new Dictionary<string, string>
                {
                    ["type_info"] = "{\"types\":[{\"Name\":\"LuceneIndexService\",\"Kind\":\"class\",\"Line\":21}],\"methods\":[{\"Name\":\"SearchAsync\",\"Signature\":\"Task<SearchResult> SearchAsync(string workspacePath, Query query)\",\"Line\":257,\"ContainingType\":\"LuceneIndexService\"}],\"language\":\"c-sharp\"}"
                },
                Snippet = "public async Task<SearchResult> SearchAsync(string workspacePath, Query query)",
                EnhancedSnippet = "🔧 SearchAsync - public async Task<SearchResult> SearchAsync(string workspacePath, Query query)",
                TypeContext = new TypeContext 
                { 
                    ContainingType = "LuceneIndexService",
                    Language = "c-sharp",
                    NearbyMethods = new List<COA.CodeSearch.McpServer.Services.TypeExtraction.MethodInfo>
                    {
                        new() { Name = "SearchAsync", Signature = "Task<SearchResult> SearchAsync(string workspacePath, Query query)" }
                    }
                }
            };
            
            var searchResult = new SearchResult
            {
                TotalHits = 1,
                Hits = new List<SearchHit> { methodSearchHit },
                Query = "SearchAsync"
            };
            searchResult.SearchTime = TimeSpan.FromMilliseconds(5);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new TextSearchParameters
            {
                Query = "SearchAsync",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert - Critical for preventing Claude guessing
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            
            var data = result.Result!.Data;
            data.Should().NotBeNull();
#pragma warning disable CS8602 // Dereference of a possibly null reference - data is asserted not null above
            var hits = data!.Results.Hits;
#pragma warning restore CS8602
            hits.Should().NotBeNull().And.NotBeEmpty();
            
            var hit = hits!.First();
            hit.TypeContext.Should().NotBeNull("Type context must ALWAYS be populated when type_info exists");
            hit.TypeContext!.ContainingType.Should().NotBeNullOrEmpty("Claude needs to know which class contains the method");
            hit.TypeContext!.NearbyMethods.Should().NotBeEmpty("Claude needs to see available methods");
            hit.EnhancedSnippet.Should().Contain("🔧", "Method signatures must be prominently displayed");
        }
        
        [Test]
        public Task QueryTypeDetector_ShouldCatchClaudePatterns()
        {
            // Ensures our expanded patterns catch Claude's common searches
            
            // Arrange - Create tool with real QueryTypeDetector
            var queryDetectorLogger = new Mock<ILogger<COA.CodeSearch.McpServer.Services.TypeExtraction.QueryTypeDetector>>();
            var queryTypeDetector = new COA.CodeSearch.McpServer.Services.TypeExtraction.QueryTypeDetector(queryDetectorLogger.Object);
            
            // Test patterns Claude commonly uses before writing code
            var claudePatterns = new[]
            {
                "new LuceneIndexService", // About to instantiate
                "Task<SearchResult>", // Return type
                "List<string>", // Collection type
                "ILuceneIndexService", // Interface reference
                "SearchAsync(", // Method signature
                "public async Task", // Method definition
                ": IDisposable", // Type annotation
                "await someService", // Async call
                "Dictionary<string, object>" // Complex generic
            };
            
            // Act & Assert
            foreach (var pattern in claudePatterns)
            {
                var isTypeQuery = queryTypeDetector.IsLikelyTypeQuery(pattern);
                isTypeQuery.Should().BeTrue($"Pattern '{pattern}' should be detected as type query");
            }
            
            return Task.CompletedTask;
        }
        
        #endregion Type-First Workflow Tests
    }
}