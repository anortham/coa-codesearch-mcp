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
using COA.Mcp.Framework.Exceptions;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class GoToDefinitionToolTests : CodeSearchToolTestBase<GoToDefinitionTool>
    {
        private GoToDefinitionTool _tool = null!;
        
        protected override GoToDefinitionTool CreateTool()
        {
            // Create SmartQueryPreprocessor dependency
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);
            
            _tool = new GoToDefinitionTool(
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
        public async Task ExecuteAsync_ValidSymbolName_ReturnsDefinition()
        {
            // Arrange
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestInterface",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
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
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - GoToDefinitionTool returns a single SymbolDefinition wrapped in AIOptimizedResponse
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SymbolDefinition
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();
            
            // Verify the symbol definition properties using dynamic
            dynamic definition = symbolDef!;
            ((string)definition.Name).Should().Be("TestInterface");
            ((string)definition.Kind).Should().Be("interface");
            ((string)definition.Signature).Should().Be("public interface TestInterface");
            ((string)definition.FilePath).Should().Contain("TestInterface.cs");
            ((int)definition.Line).Should().Be(10);  // Match the actual line in our mock data
            ((int)definition.Column).Should().Be(1);
        }

        [Test]
        public async Task ExecuteAsync_SymbolNotFound_ReturnsNoResults()
        {
            // Arrange
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "NonExistentClass",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
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

            // Assert - When symbol not found, response builder returns Success with null Results
            result.Should().NotBeNull();
            result.Success.Should().BeTrue(); // GoToDefinitionResponseBuilder returns success even when not found
            result.Data.Should().NotBeNull();
            
            // Access the Results property which should be null
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().BeNull(); // No definition found
            
            // But we should have insights explaining no results
            result.Insights.Should().NotBeNull();
            result.Insights.Should().Contain(i => i.Contains("not found"));
        }

        [Test]
        public async Task ExecuteAsync_EmptySymbolName_ReturnsError()
        {
            // Arrange
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
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
        public async Task ExecuteAsync_SearchForMethod_ReturnsMethodDefinition()
        {
            // Arrange
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
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
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - verify method definition is found
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SymbolDefinition
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();
            
            // Verify the method definition properties using dynamic
            dynamic definition = symbolDef!;
            ((string)definition.Name).Should().Be("TestMethod");
            ((string)definition.Kind).Should().Be("method");
            ((string)definition.Signature).Should().Be("void TestMethod();");
            ((string)definition.ReturnType).Should().Be("void");
            ((string)definition.ContainingType).Should().Be("TestInterface");
            ((int)definition.Line).Should().Be(12);
        }

        [Test]
        public async Task ExecuteAsync_WithFullContext_IncludesSnippet()
        {
            // Arrange
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestInterface",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = true,
                ContextLines = 10,
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
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            // Access the Results property which contains the actual SymbolDefinition
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            resultsProperty.Should().NotBeNull();
            
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();
            
            // Verify snippet is included
            dynamic definition = symbolDef!;
            string? snippet = definition.Snippet as string;
            snippet.Should().NotBeNull();
            snippet.Should().Contain("interface TestInterface");
        }

        private SearchResult CreateMockSearchResultWithTypeInfo()
        {
            var searchHit = new SearchHit
            {
                FilePath = @"C:\test\TestInterface.cs",
                Score = 1.0f,
                Fields = new Dictionary<string, string>
                {
                    ["type_info"] = """
                    {
                        "Success": true,
                        "Types": [
                            {
                                "Name": "TestInterface",
                                "Kind": "interface",
                                "Signature": "public interface TestInterface",
                                "Line": 10,
                                "Column": 1,
                                "Modifiers": ["public"],
                                "BaseType": null,
                                "Interfaces": null
                            }
                        ],
                        "Methods": [
                            {
                                "Name": "TestMethod",
                                "Signature": "void TestMethod();",
                                "ReturnType": "void",
                                "Line": 12,
                                "Column": 5,
                                "ContainingType": "TestInterface",
                                "Parameters": [],
                                "Modifiers": []
                            }
                        ],
                        "Language": "c-sharp"
                    }
                    """,
                    ["type_names"] = "TestInterface TestMethod",
                    ["content"] = """
                    namespace TestNamespace
                    {
                        /// <summary>
                        /// Test interface for unit testing
                        /// </summary>
                        public interface TestInterface
                        {
                            void TestMethod();
                        }
                    }
                    """
                },
                // Provide context lines for snippet generation
                ContextLines = new List<string>
                {
                    "namespace TestNamespace",
                    "{",
                    "    /// <summary>",
                    "    /// Test interface for unit testing",
                    "    /// </summary>",
                    "    public interface TestInterface",
                    "    {",
                    "        void TestMethod();",
                    "    }",
                    "}"
                },
                StartLine = 8,
                EndLine = 17,
                LineNumber = 10
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