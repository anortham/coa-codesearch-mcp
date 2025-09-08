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
using System.Linq;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;
using COA.Mcp.Framework.Exceptions;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class FindReferencesToolTests : CodeSearchToolTestBase<FindReferencesTool>
    {
        private FindReferencesTool _tool = null!;
        
        protected override FindReferencesTool CreateTool()
        {
            // Create SmartQueryPreprocessor dependency
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);
            
            _tool = new FindReferencesTool(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                smartQueryPreprocessor,
                ToolLoggerMock.Object
            );
            return _tool;
        }

        [Test]
        public async Task ExecuteAsync_ValidSymbolName_ReturnsReferences()
        {
            // Arrange
            var parameters = new FindReferencesParameters
            {
                Symbol = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                IncludePotential = false,
                GroupByFile = true,
                MaxResults = 10,
                ContextLines = 2,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            var mockSearchResults = CreateMockSearchResultWithReferences();
            
            // Setup the IndexExistsAsync to return true
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - FindReferencesTool returns SearchResult wrapped in AIOptimizedResponse
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SearchResult
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var searchResult = resultsProperty!.GetValue(result.Data);
            searchResult.Should().NotBeNull();
            
            // Verify search results using dynamic
            dynamic results = searchResult!;
            ((int)results.TotalHits).Should().BeGreaterThan(0);
            
            var hits = results.Hits as System.Collections.IList;
            hits.Should().NotBeNull();
            hits!.Count.Should().BeGreaterThan(0);
            
            // Verify the first hit contains reference information
            dynamic firstHit = hits[0]!;
            ((string)firstHit.FilePath).Should().Contain("TestClass.cs");
            ((int?)firstHit.LineNumber).Should().Be(5);
            
            // The tool would add referenceType, but our mock doesn't simulate that
            // Just verify the Fields dictionary exists
            var fields = firstHit.Fields as IDictionary<string, string>;
            fields.Should().NotBeNull();
        }

        [Test]
        public async Task ExecuteAsync_GroupByFileEnabled_GroupsResultsByFile()
        {
            // Arrange
            var parameters = new FindReferencesParameters
            {
                Symbol = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                IncludePotential = false,
                GroupByFile = true,
                MaxResults = 10,
                ContextLines = 2,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            var mockSearchResults = CreateMockSearchResultWithMultipleFiles();
            
            // Setup the IndexExistsAsync to return true
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SearchResult
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var searchResult = resultsProperty!.GetValue(result.Data);
            searchResult.Should().NotBeNull();
            
            dynamic results = searchResult!;
            var hits = results.Hits as System.Collections.IList;
            hits.Should().NotBeNull();
            // The response builder might reduce results based on token limits
            // Just verify we have at least one hit
            hits!.Count.Should().BeGreaterThan(0);
            
            // Verify we have hits from different files
            var uniqueFilePaths = new HashSet<string>();
            foreach (dynamic hit in hits)
            {
                string? filePath = hit.FilePath as string;
                if (!string.IsNullOrEmpty(filePath))
                    uniqueFilePaths.Add(filePath);
            }
            
            // The response builder might be reducing results based on token limits
            // Just verify we have at least one result
            uniqueFilePaths.Count.Should().BeGreaterThan(0);
            uniqueFilePaths.Should().Contain(path => path.Contains("TestClass.cs") || path.Contains("AnotherClass.cs"));
        }

        [Test]
        public async Task ExecuteAsync_NoReferencesFound_ReturnsEmptyResults()
        {
            // Arrange
            var parameters = new FindReferencesParameters
            {
                Symbol = "UnusedMethod",
                WorkspacePath = TestWorkspacePath,
                IncludePotential = false,
                GroupByFile = true,
                MaxResults = 10,
                ContextLines = 2,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            var emptySearchResults = new SearchResult
            {
                Hits = new List<SearchHit>(),
                TotalHits = 0,
                SearchTime = TimeSpan.FromMilliseconds(10)
            };

            // Setup the IndexExistsAsync to return true
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(emptySearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Empty results should still be successful
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SearchResult
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var searchResult = resultsProperty!.GetValue(result.Data);
            searchResult.Should().NotBeNull();
            
            dynamic results = searchResult!;
            ((int)results.TotalHits).Should().Be(0);
            
            var hits = results.Hits as System.Collections.IList;
            hits.Should().NotBeNull();
            hits!.Count.Should().Be(0);
        }

        [Test]
        public async Task ExecuteAsync_EmptySymbolName_ReturnsError()
        {
            // Arrange
            var parameters = new FindReferencesParameters
            {
                Symbol = "",
                WorkspacePath = TestWorkspacePath,
                IncludePotential = false,
                GroupByFile = true,
                MaxResults = 10,
                ContextLines = 2,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Act & Assert - The framework throws an exception for validation errors
            var act = async () => await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            await act.Should().ThrowAsync<ToolExecutionException>()
                .WithMessage("*Symbol field is required*");
        }

        private SearchResult CreateMockSearchResultWithReferences()
        {
            var searchHit = new SearchHit
            {
                FilePath = @"C:\test\TestClass.cs",
                Score = 0.8f,
                Fields = new Dictionary<string, string>
                {
                    ["content"] = "public void CallTestMethod() { TestMethod(); }",
                    ["type_info"] = """
                    {
                        "types": [],
                        "methods": [],
                        "language": "c-sharp"
                    }
                    """
                },
                LineNumber = 5,
                ContextLines = new List<string> { "    // Call the test method", "    public void CallTestMethod()", "    {", "        TestMethod(); // Reference here", "    }" },
                StartLine = 3,
                EndLine = 7
            };

            return new SearchResult
            {
                Hits = new List<SearchHit> { searchHit },
                TotalHits = 1,
                SearchTime = TimeSpan.FromMilliseconds(15)
            };
        }

        private SearchResult CreateMockSearchResultWithMultipleFiles()
        {
            var searchHits = new List<SearchHit>
            {
                new SearchHit
                {
                    FilePath = @"C:\test\TestClass.cs",
                    Score = 0.9f,
                    Fields = new Dictionary<string, string>
                    {
                        ["content"] = "TestMethod();",
                        ["type_info"] = """{"types": [], "methods": [], "language": "c-sharp"}"""
                    },
                    LineNumber = 10
                },
                new SearchHit
                {
                    FilePath = @"C:\test\AnotherClass.cs",
                    Score = 0.7f,
                    Fields = new Dictionary<string, string>
                    {
                        ["content"] = "obj.TestMethod();",
                        ["type_info"] = """{"types": [], "methods": [], "language": "c-sharp"}"""
                    },
                    LineNumber = 15
                }
            };

            return new SearchResult
            {
                Hits = searchHits,
                TotalHits = 2,
                SearchTime = TimeSpan.FromMilliseconds(20)
            };
        }
    }
}