using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
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
using COA.Mcp.Framework.Exceptions;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class SymbolSearchToolTests : CodeSearchToolTestBase<SymbolSearchTool>
    {
        private SymbolSearchTool _tool = null!;
        
        protected override SymbolSearchTool CreateTool()
        {
            // Create SmartQueryPreprocessor dependency
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);
            
            _tool = new SymbolSearchTool(
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
        public async Task ExecuteAsync_ValidSymbolName_ReturnsSymbolResults()
        {
            // Arrange
            var parameters = new SymbolSearchParameters
            {
                Symbol = "TestClass",
                WorkspacePath = TestWorkspacePath,
                IncludeReferences = true,
                MaxResults = 10,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            var mockSearchResults = CreateMockSearchResultWithTypeInfo();
            
            // Setup the IndexExistsAsync to return true
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - verify the actual structure returned by the tool
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // The Data property is an AIResponseData<SymbolSearchResult>
            // Access the Results property which contains the actual SymbolSearchResult
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolSearchResult = resultsProperty!.GetValue(result.Data);
            symbolSearchResult.Should().NotBeNull();
            
            // No need for dynamic cast here, just verify we got the result
            
            // Access Symbols collection through reflection or dynamic
            var symbolsProperty = symbolSearchResult!.GetType().GetProperty("Symbols");
            var symbols = symbolsProperty?.GetValue(symbolSearchResult) as System.Collections.IList;
            symbols.Should().NotBeNull();
            symbols!.Count.Should().BeGreaterThan(0);
            
            // Verify the first symbol
            dynamic firstSymbol = symbols[0]!;
            ((string)firstSymbol.Name).Should().Be("TestClass");
            ((string)firstSymbol.Kind).Should().Be("class");
            ((string)firstSymbol.Signature).Should().Be("public class TestClass");
            ((int)firstSymbol.Line).Should().Be(1);
        }

        [Test]
        public async Task ExecuteAsync_EmptySymbolName_ReturnsError()
        {
            // Arrange
            var parameters = new SymbolSearchParameters
            {
                Symbol = "",
                WorkspacePath = TestWorkspacePath,
                IncludeReferences = true,
                MaxResults = 10,
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

        [Test]
        public async Task ExecuteAsync_NoIndexExists_ReturnsError()
        {
            // Arrange
            var parameters = new SymbolSearchParameters
            {
                Symbol = "TestClass",
                WorkspacePath = TestWorkspacePath,
                IncludeReferences = true,
                MaxResults = 10,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - When SearchAsync returns null or throws, we get a generic error
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().Be("SYMBOL_SEARCH_ERROR");
            result.Error.Message.Should().Contain("Failed to search for symbol");
        }
        
        [Test]
        public async Task ExecuteAsync_SearchForMethod_ReturnsMethodDefinition()
        {
            // Arrange
            var parameters = new SymbolSearchParameters
            {
                Symbol = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                IncludeReferences = false,
                MaxResults = 10,
                MaxTokens = 8000,
                NoCache = false,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            var mockSearchResults = CreateMockSearchResultWithTypeInfo();
            
            // Setup the IndexExistsAsync to return true
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - verify the method is found
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SymbolSearchResult
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolSearchResult = resultsProperty!.GetValue(result.Data);
            symbolSearchResult.Should().NotBeNull();
            
            // Access Symbols collection
            var symbolsProperty = symbolSearchResult!.GetType().GetProperty("Symbols");
            var symbols = symbolsProperty?.GetValue(symbolSearchResult) as System.Collections.IList;
            symbols.Should().NotBeNull();
            symbols!.Count.Should().BeGreaterThan(0);
            
            // Verify the method is found
            dynamic firstSymbol = symbols[0]!;
            ((string)firstSymbol.Name).Should().Be("TestMethod");
            ((string)firstSymbol.Kind).Should().Be("method");
            ((string)firstSymbol.ReturnType).Should().Be("void");
            ((string)firstSymbol.ContainingType).Should().Be("TestClass");
        }

        private SearchResult CreateMockSearchResultWithTypeInfo()
        {
            var searchHit = new SearchHit
            {
                FilePath = @"C:\test\TestClass.cs",
                Score = 1.0f,
                Fields = new Dictionary<string, string>
                {
                    ["type_info"] = """
                    {
                        "Success": true,
                        "Types": [
                            {
                                "Name": "TestClass",
                                "Kind": "class",
                                "Signature": "public class TestClass",
                                "Line": 1,
                                "Column": 1,
                                "Modifiers": ["public"],
                                "BaseType": null,
                                "Interfaces": null
                            }
                        ],
                        "Methods": [
                            {
                                "Name": "TestMethod",
                                "Signature": "public void TestMethod()",
                                "ReturnType": "void",
                                "Line": 5,
                                "Column": 5,
                                "ContainingType": "TestClass",
                                "Parameters": [],
                                "Modifiers": ["public"]
                            }
                        ],
                        "Language": "c-sharp"
                    }
                    """,
                    ["type_names"] = "TestClass TestMethod"
                },
                ContextLines = new List<string> 
                { 
                    "public class TestClass",
                    "{",
                    "    public void TestMethod()",
                    "    {",
                    "    }",
                    "}"
                }
            };

            return new SearchResult
            {
                Hits = new List<SearchHit> { searchHit },
                TotalHits = 1,
                SearchTime = TimeSpan.FromMilliseconds(10)
            };
        }
    }
}