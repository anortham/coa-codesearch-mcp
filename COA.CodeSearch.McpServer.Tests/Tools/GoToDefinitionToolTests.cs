using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Julie;
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
    public class GoToDefinitionToolTests : CodeSearchToolTestBase<GoToDefinitionTool>
    {
        private GoToDefinitionTool _tool = null!;
        private Mock<ISQLiteSymbolService>? _sqliteServiceMock;

        protected override GoToDefinitionTool CreateTool()
        {
            return CreateToolWithSQLite(null);
        }

        /// <summary>
        /// Helper to create tool with optional SQLite mock
        /// </summary>
        private GoToDefinitionTool CreateToolWithSQLite(Mock<ISQLiteSymbolService>? sqliteMock)
        {
            _sqliteServiceMock = sqliteMock;

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
                ToolLoggerMock.Object,
                CodeAnalyzer,
                sqliteMock?.Object
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

        #region SQLite Integration Tests

        [Test]
        public async Task ExecuteAsync_WithSQLite_UsesFastPath()
        {
            // Arrange - Test simplest case: SQLite returns exact symbol match
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestClass",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true, // Disable cache for testing
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to return JulieSymbol
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "TestClass", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieSymbol>
                {
                    new JulieSymbol
                    {
                        Id = "test-class-id",
                        Name = "TestClass",
                        Kind = "class",
                        Language = "csharp",
                        FilePath = "/test/TestClass.cs",
                        StartLine = 10,
                        StartColumn = 1,
                        EndLine = 20,
                        EndColumn = 1,
                        Signature = "public class TestClass",
                        Visibility = "public"
                    }
                });

            // Create tool with SQLite mock
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Should find symbol via SQLite fast-path
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();

            // Verify result contains our symbol
            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();

            dynamic definition = symbolDef!;
            ((string)definition.Name).Should().Be("TestClass");
            ((string)definition.Kind).Should().Be("class");

            // Verify Lucene was NOT called (SQLite fast-path was used)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_SQLiteNotAvailable_FallsBackToLucene()
        {
            // Arrange - SQLite not available, should fall back to Lucene
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestInterface",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to return database doesn't exist
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(false);

            // Setup Lucene mock to return results (existing behavior)
            var mockSearchResults = CreateMockSearchResultWithTypeInfo();
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Create tool with SQLite mock that returns false
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Should find symbol via Lucene fallback
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            // Verify Lucene was called
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_SQLiteCaseInsensitive_FindsSymbol()
        {
            // Arrange - Test case-insensitive matching via SQLite
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "testclass", // lowercase
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false, // Case-insensitive
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to return "TestClass" (different case)
            // With caseSensitive=false, SQL COLLATE NOCASE should find it
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "testclass", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieSymbol>
                {
                    new JulieSymbol
                    {
                        Id = "test-class-id",
                        Name = "TestClass", // Different case!
                        Kind = "class",
                        Language = "csharp",
                        FilePath = "/test/TestClass.cs",
                        StartLine = 10,
                        StartColumn = 1,
                        EndLine = 20,
                        EndColumn = 1,
                        Signature = "public class TestClass",
                        Visibility = "public"
                    }
                });

            // Create tool with SQLite mock
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();

            dynamic definition = symbolDef!;
            // Should find "TestClass" even though we searched for "testclass"
            ((string)definition.Name).Should().Be("TestClass");
        }

        [Test]
        public async Task ExecuteAsync_SQLiteSymbolNotFound_FallsBackToLucene()
        {
            // Arrange - SQLite returns empty list, should fall back to Lucene
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "NonExistentClass",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to return empty list
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "NonExistentClass", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieSymbol>()); // Empty list

            // Setup Lucene fallback
            var mockSearchResults = CreateMockSearchResultWithTypeInfo();
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Create tool with SQLite mock
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Should fall back to Lucene when SQLite finds nothing
            result.Should().NotBeNull();

            // Verify Lucene was called as fallback
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_SQLiteThrowsException_FallsBackToLuceneGracefully()
        {
            // Arrange - SQLite throws exception, should catch and fall back
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestClass",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to throw exception
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "TestClass", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Database error"));

            // Setup Lucene fallback
            var mockSearchResults = CreateMockSearchResultWithTypeInfo();
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSearchResults);

            // Create tool with SQLite mock
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Should not throw, should fall back gracefully
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            // Verify Lucene was called as fallback
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_SQLiteMultipleMatches_ReturnsFirstMatch()
        {
            // Arrange - Multiple symbols with same name (overloads, nested types)
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "Add",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false,
                NavigateToFirstResult = false
            };

            // Setup SQLite mock to return multiple JulieSymbols
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "Add", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieSymbol>
                {
                    new JulieSymbol
                    {
                        Id = "list-add",
                        Name = "Add",
                        Kind = "method",
                        Language = "csharp",
                        FilePath = "/test/List.cs",
                        StartLine = 10,
                        StartColumn = 5,
                        EndLine = 12,
                        EndColumn = 5,
                        Signature = "void Add(T item)"
                    },
                    new JulieSymbol
                    {
                        Id = "dict-add",
                        Name = "Add",
                        Kind = "method",
                        Language = "csharp",
                        FilePath = "/test/Dictionary.cs",
                        StartLine = 15,
                        StartColumn = 5,
                        EndLine = 17,
                        EndColumn = 5,
                        Signature = "void Add(K key, V value)"
                    }
                });

            // Create tool with SQLite mock
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            var dataType = result.Data.GetType();
            var resultsProperty = dataType.GetProperty("Results");
            var symbolDef = resultsProperty!.GetValue(result.Data);
            symbolDef.Should().NotBeNull();

            // Should return a single definition (first/best match)
            dynamic definition = symbolDef!;
            ((string)definition.Name).Should().Be("Add");
        }

        #endregion
    }
}