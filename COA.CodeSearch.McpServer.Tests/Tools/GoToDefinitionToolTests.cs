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

            _tool = new GoToDefinitionTool(
                ServiceProvider,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ToolLoggerMock.Object,
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
            };

            var mockSymbols = CreateMockSQLiteSymbols();
            var mockFile = CreateMockSQLiteFile();

            // Setup SQLite mock
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock
                .Setup(x => x.DatabaseExists(It.IsAny<string>()))
                .Returns(true);
            sqliteMock
                .Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "TestInterface", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols.Where(s => s.Name == "TestInterface").ToList());
            sqliteMock
                .Setup(x => x.GetAllFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<FileRecord> { mockFile });

            _tool = CreateToolWithSQLite(sqliteMock);

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
        public async Task ExecuteAsync_SymbolNotFound_ReturnsError()
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
            };

            // Setup SQLite mock to return empty list (symbol not found)
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock
                .Setup(x => x.DatabaseExists(It.IsAny<string>()))
                .Returns(true);
            sqliteMock
                .Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "NonExistentClass", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieSymbol>());

            _tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - When symbol not found in SQLite, should return error (no Lucene fallback)
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("SYMBOL_NOT_FOUND");
            result.Error.Message.Should().Contain("NonExistentClass");
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
            };

            var mockSymbols = CreateMockSQLiteSymbols();
            var mockFile = CreateMockSQLiteFile();

            // Setup SQLite mock
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock
                .Setup(x => x.DatabaseExists(It.IsAny<string>()))
                .Returns(true);
            sqliteMock
                .Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "TestMethod", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols.Where(s => s.Name == "TestMethod").ToList());
            sqliteMock
                .Setup(x => x.GetAllFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<FileRecord> { mockFile });

            _tool = CreateToolWithSQLite(sqliteMock);

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
            ((int)definition.Line).Should().Be(12);
            // Note: ReturnType and ContainingType are not extracted by tree-sitter, only in signature
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
            };

            var mockSymbols = CreateMockSQLiteSymbols();
            var mockFile = CreateMockSQLiteFile();

            // Setup SQLite mock
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock
                .Setup(x => x.DatabaseExists(It.IsAny<string>()))
                .Returns(true);
            sqliteMock
                .Setup(x => x.GetSymbolsByNameAsync(It.IsAny<string>(), "TestInterface", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols.Where(s => s.Name == "TestInterface").ToList());
            sqliteMock
                .Setup(x => x.GetAllFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<FileRecord> { mockFile });

            _tool = CreateToolWithSQLite(sqliteMock);

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

        private List<JulieSymbol> CreateMockSQLiteSymbols()
        {
            return new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-interface-1",
                    Name = "TestInterface",
                    Kind = "interface",
                    Signature = "public interface TestInterface",
                    FilePath = @"C:\test\TestInterface.cs",
                    StartLine = 10,
                    StartColumn = 1,
                    EndLine = 14,
                    EndColumn = 2,
                    Language = "c-sharp",
                    Visibility = "public"
                },
                new JulieSymbol
                {
                    Id = "test-method-1",
                    Name = "TestMethod",
                    Kind = "method",
                    Signature = "void TestMethod();",
                    FilePath = @"C:\test\TestInterface.cs",
                    StartLine = 12,
                    StartColumn = 5,
                    EndLine = 12,
                    EndColumn = 30,
                    Language = "c-sharp",
                    Visibility = "public"
                }
            };
        }

        private FileRecord CreateMockSQLiteFile()
        {
            return new FileRecord(
                Path: @"C:\test\TestInterface.cs",
                Content: """
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
                    """,
                Language: "c-sharp",
                Size: 300,
                LastModified: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            );
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
        public async Task ExecuteAsync_SQLiteNotAvailable_ReturnsError()
        {
            // Arrange - SQLite not available, should return error (no fallback)
            var parameters = new GoToDefinitionParameters
            {
                Symbol = "TestInterface",
                WorkspacePath = TestWorkspacePath,
                IncludeFullContext = false,
                ContextLines = 5,
                NoCache = true,
                CaseSensitive = false,
            };

            // Setup SQLite mock to return database doesn't exist
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(false);

            // Create tool with SQLite mock that returns false
            var tool = CreateToolWithSQLite(sqliteMock);

            // Act
            var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Should return error since workspace not indexed
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("WORKSPACE_NOT_INDEXED");
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

        // Tests for Lucene fallback removed - GoToDefinitionTool now uses SQLite only (source of truth)
        // Lucene index cannot exist without SQLite data, so fallback is impossible and was dead code

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