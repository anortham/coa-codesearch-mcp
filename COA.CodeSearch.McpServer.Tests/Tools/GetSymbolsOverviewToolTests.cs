using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class GetSymbolsOverviewToolTests : CodeSearchToolTestBase<GetSymbolsOverviewTool>
    {
        private GetSymbolsOverviewTool _tool = null!;
        private Mock<ISQLiteSymbolService>? _sqliteServiceMock;
        private string _testFilePath = null!;

        protected override GetSymbolsOverviewTool CreateTool()
        {
            return CreateToolWithSQLite(null);
        }

        private GetSymbolsOverviewTool CreateToolWithSQLite(Mock<ISQLiteSymbolService>? sqliteMock)
        {
            _sqliteServiceMock = sqliteMock;

            _tool = new GetSymbolsOverviewTool(
                ServiceProvider,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                PathResolutionServiceMock.Object,
                ToolLoggerMock.Object,
                sqliteMock?.Object
            );
            return _tool;
        }

        [SetUp]
        public new void SetUp()
        {
            // Create a test file path
            _testFilePath = Path.Combine(TestWorkspacePath, "test-symbols.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);
        }

        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.GetSymbolsOverview);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("Tree-sitter");
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
            
            // Test IPrioritizedTool implementation
            _tool.Priority.Should().Be(95);
            _tool.PreferredScenarios.Should().Contain("file_exploration");
            _tool.PreferredScenarios.Should().Contain("code_understanding");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_FilePath_Is_Missing()
        {
            // Arrange
            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = string.Empty
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("SYMBOLS_OVERVIEW_ERROR");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_File_Does_Not_Exist()
        {
            // Arrange
            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = "/non/existent/file.cs"
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("FILE_NOT_FOUND");
            result.Result.Error.Message.Should().Contain("File not found");
            result.Result.Error.Recovery!.Steps.Should().Contain("Use file_search tool to find the correct file");
        }

        [Test]
        public async Task ExecuteAsync_Should_Extract_Symbols_Successfully()
        {
            // Arrange
            var testCode = @"
using System;

namespace TestNamespace
{
    public class TestClass : BaseClass, ITestInterface
    {
        public void TestMethod(string parameter)
        {
        }

        private int _field;
    }

    public interface ITestInterface
    {
        void InterfaceMethod();
    }

    public struct TestStruct
    {
        public string Name { get; set; }
    }

    public enum TestEnum
    {
        Value1,
        Value2
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = CreateMockSQLiteSymbolsForFile();
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeMethods = true,
                IncludeInheritance = true,
                IncludeLineNumbers = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.Should().NotBeNull();

            var overview = result.Result.Data.Results;
            overview.FilePath.Should().Be(_testFilePath);
            overview.Language.Should().Be("csharp");
            overview.Success.Should().BeTrue();
            overview.TotalSymbols.Should().BeGreaterThan(0);
        }

        [Test]
        public async Task ExecuteAsync_Should_Categorize_Types_Correctly()
        {
            // Arrange
            var testCode = @"
public class TestClass { }
public interface ITestInterface { }
public struct TestStruct { }
public enum TestEnum { }";

            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol { Name = "TestClass", Kind = "class", Signature = "public class TestClass", StartLine = 2, StartColumn = 1, Language = "csharp", FilePath = _testFilePath },
                new JulieSymbol { Name = "ITestInterface", Kind = "interface", Signature = "public interface ITestInterface", StartLine = 3, StartColumn = 1, Language = "csharp", FilePath = _testFilePath },
                new JulieSymbol { Name = "TestStruct", Kind = "struct", Signature = "public struct TestStruct", StartLine = 4, StartColumn = 1, Language = "csharp", FilePath = _testFilePath },
                new JulieSymbol { Name = "TestEnum", Kind = "enum", Signature = "public enum TestEnum", StartLine = 5, StartColumn = 1, Language = "csharp", FilePath = _testFilePath }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeMethods = true,
                IncludeLineNumbers = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            overview.Classes.Should().HaveCount(1);
            overview.Classes[0].Name.Should().Be("TestClass");
            overview.Classes[0].Kind.Should().Be("class");
            overview.Classes[0].Line.Should().Be(2);

            overview.Interfaces.Should().HaveCount(1);
            overview.Interfaces[0].Name.Should().Be("ITestInterface");

            overview.Structs.Should().HaveCount(1);
            overview.Structs[0].Name.Should().Be("TestStruct");

            overview.Enums.Should().HaveCount(1);
            overview.Enums[0].Name.Should().Be("TestEnum");

            overview.TotalSymbols.Should().Be(4);
        }

        [Test]
        public async Task ExecuteAsync_Should_Include_Methods_When_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass
{
    public void Method1() { }
    private int Method2(string param) { return 0; }
}

public static void GlobalMethod() { }";

            await File.WriteAllTextAsync(_testFilePath, testCode);

            // Note: Julie returns flat symbols - methods are NOT nested inside classes
            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    StartLine = 2,
                    StartColumn = 1,
                    Language = "csharp",
                    FilePath = _testFilePath
                },
                new JulieSymbol
                {
                    Name = "Method1",
                    Kind = "method",
                    Signature = "public void Method1()",
                    StartLine = 4,
                    StartColumn = 5,
                    Language = "csharp",
                    FilePath = _testFilePath
                },
                new JulieSymbol
                {
                    Name = "Method2",
                    Kind = "method",
                    Signature = "private int Method2(string param)",
                    StartLine = 5,
                    StartColumn = 5,
                    Language = "csharp",
                    FilePath = _testFilePath
                },
                new JulieSymbol
                {
                    Name = "GlobalMethod",
                    Kind = "function",
                    Signature = "public static void GlobalMethod()",
                    StartLine = 8,
                    StartColumn = 1,
                    Language = "csharp",
                    FilePath = _testFilePath
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeMethods = true,
                IncludeLineNumbers = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            // Julie returns flat symbols - methods are separate, not nested
            overview.Classes.Should().HaveCount(1);
            overview.Classes[0].Name.Should().Be("TestClass");
            overview.Classes[0].Methods.Should().BeEmpty(); // No nested methods in Julie output
            overview.Classes[0].MethodCount.Should().Be(0);

            // All methods appear at top level (2 methods + 1 function)
            overview.Methods.Should().HaveCount(3);
            overview.Methods.Should().Contain(m => m.Name == "Method1");
            overview.Methods.Should().Contain(m => m.Name == "Method2");
            overview.Methods.Should().Contain(m => m.Name == "GlobalMethod");
        }

        [Test]
        public async Task ExecuteAsync_Should_Exclude_Methods_When_Not_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass
{
    public void Method1() { }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol { Name = "TestClass", Kind = "class", Signature = "public class TestClass", StartLine = 2, StartColumn = 1, Language = "csharp", FilePath = _testFilePath },
                new JulieSymbol { Name = "Method1", Kind = "method", Signature = "public void Method1()", StartLine = 4, StartColumn = 5, Language = "csharp", FilePath = _testFilePath }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeMethods = false // Don't include methods
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            overview.Classes[0].Methods.Should().BeEmpty();
            overview.Classes[0].MethodCount.Should().Be(0);
            overview.Methods.Should().BeEmpty(); // Methods excluded when IncludeMethods=false
        }

        [Test]
        public async Task ExecuteAsync_Should_Include_Inheritance_When_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass : BaseClass, ITestInterface
{
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            // Note: Julie doesn't extract BaseType or Interfaces - only in signature
            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass : BaseClass, ITestInterface",
                    StartLine = 2,
                    StartColumn = 1,
                    Language = "csharp",
                    FilePath = _testFilePath
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeInheritance = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            // Julie doesn't extract BaseType/Interfaces separately - only in signature
            // IncludeInheritance parameter doesn't affect Julie output (always null)
            overview.Classes[0].BaseType.Should().BeNull();
            overview.Classes[0].Interfaces.Should().BeNull();
            overview.Classes[0].Signature.Should().Contain("BaseClass");
            overview.Classes[0].Signature.Should().Contain("ITestInterface");
        }

        [Test]
        public async Task ExecuteAsync_Should_Exclude_Inheritance_When_Not_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass : BaseClass, ITestInterface
{
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass : BaseClass, ITestInterface",
                    StartLine = 2,
                    StartColumn = 1,
                    Language = "csharp",
                    FilePath = _testFilePath
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeInheritance = false // Don't include inheritance
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            // Julie doesn't extract BaseType/Interfaces separately (always null)
            overview.Classes[0].BaseType.Should().BeNull();
            overview.Classes[0].Interfaces.Should().BeNull();
        }

        [Test]
        public async Task ExecuteAsync_Should_Exclude_Line_Numbers_When_Not_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass
{
    public void Method1() { }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol { Name = "TestClass", Kind = "class", Signature = "public class TestClass", StartLine = 2, StartColumn = 1, Language = "csharp", FilePath = _testFilePath },
                new JulieSymbol { Name = "Method1", Kind = "method", Signature = "public void Method1()", StartLine = 4, StartColumn = 5, Language = "csharp", FilePath = _testFilePath }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                IncludeLineNumbers = false, // Don't include line numbers
                IncludeMethods = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;

            overview.Classes[0].Line.Should().Be(0);
            overview.Classes[0].Column.Should().Be(0);
            overview.Methods[0].Line.Should().Be(0); // Julie returns flat symbols, methods not nested
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_File_Read_Error()
        {
            // Arrange - Test that file read errors are handled gracefully
            // Note: With SQLite-only architecture, file read errors would occur during SQLite query
            // This test now verifies the workspace must be indexed first
            var lockedFilePath = Path.Combine(TestWorkspacePath, "locked-file.cs");

            // Create the file
            await File.WriteAllTextAsync(lockedFilePath, "test content");

            // SQLite mock returns database exists but throws on query
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), lockedFilePath, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("File read error"));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = lockedFilePath,
                WorkspacePath = TestWorkspacePath
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("SYMBOLS_OVERVIEW_ERROR");
            result.Result.Error.Message.Should().Contain("File read error");
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_Workspace_Not_Indexed()
        {
            // Arrange
            var testCode = "public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            // SQLite database doesn't exist
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(false);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("WORKSPACE_NOT_INDEXED");
            result.Result.Error.Recovery!.Steps.Should().Contain(s => s.Contains("index_workspace"));
        }

        [Test]
        public async Task ExecuteAsync_Should_Use_Cache_When_Available()
        {
            // Arrange
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            // Setup SQLite mock (won't be called if cache is hit)
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);

            _tool = CreateToolWithSQLite(sqliteMock);

            var cachedResult = new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = true,
                Message = "Cached result",
                Data = new AIResponseData<SymbolsOverviewResult>
                {
                    Results = new SymbolsOverviewResult
                    {
                        FilePath = _testFilePath,
                        Language = "csharp",
                        Classes = new List<TypeOverview>
                        {
                            new TypeOverview { Name = "CachedClass" }
                        },
                        TotalSymbols = 1
                    }
                }
            };

            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-cache-key");

            ResponseCacheServiceMock.Setup(x => x.GetAsync<AIOptimizedResponse<SymbolsOverviewResult>>("test-cache-key"))
                .ReturnsAsync(cachedResult);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                NoCache = false // Enable caching
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Message.Should().Be("Cached result");
            result.Result.Data!.Results.Classes[0].Name.Should().Be("CachedClass");

            // Verify SQLite was not called (cache was used)
            sqliteMock.Verify(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Skip_Cache_When_NoCache_Is_True()
        {
            // Arrange
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol { Name = "TestClass", Kind = "class", Signature = "public class TestClass", StartLine = 1, StartColumn = 1, Language = "csharp", FilePath = _testFilePath }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                NoCache = true // Disable caching
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();

            // Verify cache was not checked or set
            ResponseCacheServiceMock.Verify(x => x.GetAsync<AIOptimizedResponse<SymbolsOverviewResult>>(It.IsAny<string>()), Times.Never);
            ResponseCacheServiceMock.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CacheEntryOptions>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Convert_Relative_Path_To_Absolute()
        {
            // Arrange
            var relativePath = "test-symbols.cs";
            var absolutePath = Path.GetFullPath(relativePath);
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(absolutePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol { Name = "TestClass", Kind = "class", Signature = "public class TestClass", StartLine = 1, StartColumn = 1, Language = "csharp", FilePath = absolutePath }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), absolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = relativePath, // Relative path
                WorkspacePath = TestWorkspacePath
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Data!.Results.FilePath.Should().Be(absolutePath);

            // Cleanup
            File.Delete(absolutePath);
        }

        /// <summary>
        /// Creates mock SQLite symbols for file-level symbol extraction tests
        /// </summary>
        private List<JulieSymbol> CreateMockSQLiteSymbolsForFile()
        {
            return new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-class-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 5,
                    StartColumn = 1,
                    EndLine = 15,
                    EndColumn = 2,
                    Language = "csharp",
                    Visibility = "public"
                },
                new JulieSymbol
                {
                    Id = "test-interface-1",
                    Name = "ITestInterface",
                    Kind = "interface",
                    Signature = "public interface ITestInterface",
                    FilePath = _testFilePath,
                    StartLine = 17,
                    StartColumn = 1,
                    EndLine = 20,
                    EndColumn = 2,
                    Language = "csharp",
                    Visibility = "public"
                },
                new JulieSymbol
                {
                    Id = "test-method-1",
                    Name = "TestMethod",
                    Kind = "method",
                    Signature = "public void TestMethod(string parameter)",
                    FilePath = _testFilePath,
                    StartLine = 7,
                    StartColumn = 5,
                    EndLine = 10,
                    EndColumn = 5,
                    Language = "csharp",
                    Visibility = "public"
                }
            };
        }
    }
}