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
    public class ReadSymbolsToolTests : CodeSearchToolTestBase<ReadSymbolsTool>
    {
        private ReadSymbolsTool _tool = null!;
        private Mock<ISQLiteSymbolService>? _sqliteServiceMock;
        private string _testFilePath = null!;

        protected override ReadSymbolsTool CreateTool()
        {
            return CreateToolWithSQLite(null);
        }

        private ReadSymbolsTool CreateToolWithSQLite(Mock<ISQLiteSymbolService>? sqliteMock)
        {
            _sqliteServiceMock = sqliteMock;

            _tool = new ReadSymbolsTool(
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
            _testFilePath = Path.Combine(TestWorkspacePath, "TestFile.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);
        }

        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.ReadSymbols);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("READ SPECIFIC SYMBOLS");
            _tool.Description.Should().Contain("80-95% token savings");
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);

            // Test IPrioritizedTool implementation
            _tool.Priority.Should().Be(92);
            _tool.PreferredScenarios.Should().Contain("implementation_reading");
            _tool.PreferredScenarios.Should().Contain("code_understanding");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_FilePath_Is_Missing()
        {
            // Arrange
            var parameters = new ReadSymbolsParameters
            {
                FilePath = string.Empty,
                SymbolNames = new List<string> { "TestClass" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("READ_SYMBOLS_ERROR");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_SymbolNames_Is_Empty()
        {
            // Arrange
            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                SymbolNames = new List<string>()
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("READ_SYMBOLS_ERROR");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_File_Does_Not_Exist()
        {
            // Arrange
            var parameters = new ReadSymbolsParameters
            {
                FilePath = "/non/existent/file.cs",
                SymbolNames = new List<string> { "TestClass" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("FILE_NOT_FOUND");
            result.Result.Error.Message.Should().Contain("File not found");
            result.Result.Error.Recovery!.Steps.Should().Contain("Use search_files tool to find the correct file");
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

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("WORKSPACE_NOT_INDEXED");
            result.Result.Error.Recovery!.Steps.Should().Contain(s => s.Contains("index_workspace"));
        }

        [Test]
        public async Task ExecuteAsync_Should_Extract_Single_Symbol_Successfully()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""Hello"");
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-class-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 4,
                    StartColumn = 1,
                    EndLine = 10,
                    EndColumn = 2,
                    StartByte = 19, // Adjusted to correct offset for "public class TestClass"
                    EndByte = testCode.Length,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" },
                DetailLevel = "implementation"
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.Should().NotBeNull();

            var readResult = result.Result.Data.Results;
            readResult.FilePath.Should().Be(_testFilePath);
            readResult.Symbols.Should().HaveCount(1);
            readResult.Symbols[0].Name.Should().Be("TestClass");
            readResult.Symbols[0].Code.Should().NotBeNullOrWhiteSpace();
            readResult.Symbols[0].Code.Should().Contain("class TestClass"); // Verify class extraction
            readResult.Symbols[0].Code.Should().Contain("TestMethod"); // Verify method is included
            readResult.NotFoundSymbols.Should().BeEmpty();
        }

        [Test]
        public async Task ExecuteAsync_Should_Extract_Multiple_Symbols()
        {
            // Arrange
            var testCode = @"
public class Circle
{
    public void Draw() { }
}

public class Rectangle
{
    public void Draw() { }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "circle-1",
                    Name = "Circle",
                    Kind = "class",
                    Signature = "public class Circle",
                    FilePath = _testFilePath,
                    StartLine = 2,
                    StartColumn = 1,
                    EndLine = 5,
                    EndColumn = 2,
                    StartByte = 1,
                    EndByte = 50,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "rectangle-1",
                    Name = "Rectangle",
                    Kind = "class",
                    Signature = "public class Rectangle",
                    FilePath = _testFilePath,
                    StartLine = 7,
                    StartColumn = 1,
                    EndLine = 10,
                    EndColumn = 2,
                    StartByte = 52,
                    EndByte = 105,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "Circle", "Rectangle" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols.Should().HaveCount(2);
            readResult.Symbols.Should().Contain(s => s.Name == "Circle");
            readResult.Symbols.Should().Contain(s => s.Name == "Rectangle");
            readResult.NotFoundSymbols.Should().BeEmpty();
        }

        [Test]
        public async Task ExecuteAsync_Should_Track_NotFoundSymbols()
        {
            // Arrange
            var testCode = "public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 1,
                    StartColumn = 1,
                    EndLine = 1,
                    EndColumn = 27,
                    StartByte = 0,
                    EndByte = 27,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass", "NonExistent", "AlsoMissing" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols.Should().HaveCount(1);
            readResult.Symbols[0].Name.Should().Be("TestClass");
            readResult.NotFoundSymbols.Should().HaveCount(2);
            readResult.NotFoundSymbols.Should().Contain("NonExistent");
            readResult.NotFoundSymbols.Should().Contain("AlsoMissing");
        }

        [Test]
        public async Task ExecuteAsync_Should_Support_DetailLevel_Signature()
        {
            // Arrange - DetailLevel = "signature" should extract only signature, not full code
            var testCode = "public class TestClass { public void Method() { } }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 1,
                    StartColumn = 1,
                    EndLine = 1,
                    EndColumn = 52,
                    StartByte = 0,
                    EndByte = 52,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" },
                DetailLevel = "signature" // Signature only
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols[0].Code.Should().Be("public class TestClass");
            // No dependencies, callers, or inheritance with signature level
            readResult.Symbols[0].Dependencies.Should().BeNullOrEmpty();
            readResult.Symbols[0].Callers.Should().BeNullOrEmpty();
            readResult.Symbols[0].Inheritance.Should().BeNull();
        }

        [Test]
        public async Task ExecuteAsync_Should_Include_Dependencies_When_Requested()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""test"");
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-class-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 4,
                    StartColumn = 1,
                    EndLine = 10,
                    EndColumn = 2,
                    StartByte = 18,
                    EndByte = 120,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "test-method-1",
                    Name = "TestMethod",
                    Kind = "method",
                    ParentId = "test-class-1", // Child of TestClass
                    Signature = "public void TestMethod()",
                    FilePath = _testFilePath,
                    StartLine = 6,
                    StartColumn = 5,
                    EndLine = 9,
                    EndColumn = 6,
                    StartByte = 60,
                    EndByte = 115,
                    Language = "csharp"
                }
            };

            var mockIdentifiers = new List<JulieIdentifier>
            {
                new JulieIdentifier
                {
                    Name = "WriteLine",
                    Kind = "call",
                    ContainingSymbolId = "test-method-1", // Called from TestMethod
                    TargetSymbolId = "console-writeline-id",
                    FilePath = _testFilePath,
                    StartLine = 8,
                    StartColumn = 10,
                    EndLine = 8,
                    EndColumn = 20
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetIdentifiersByContainingSymbolAsync(It.IsAny<string>(), "test-class-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<JulieIdentifier>()); // Class itself has no direct identifiers
            sqliteMock.Setup(x => x.GetIdentifiersByContainingSymbolAsync(It.IsAny<string>(), "test-method-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockIdentifiers); // Method calls WriteLine
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" },
                DetailLevel = "full",
                IncludeDependencies = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols[0].Dependencies.Should().NotBeNull();
            readResult.Symbols[0].Dependencies.Should().HaveCount(1);
            readResult.Symbols[0].Dependencies![0].Name.Should().Be("WriteLine");
            readResult.Symbols[0].Dependencies[0].Kind.Should().Be("call");
        }

        [Test]
        public async Task ExecuteAsync_Should_Include_Callers_When_Requested()
        {
            // Arrange
            var testCode = @"
public class TestClass
{
    public void CalledMethod() { }
}

public class CallerClass
{
    public void CallerMethod()
    {
        var t = new TestClass();
        t.CalledMethod();
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-class-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 2,
                    StartColumn = 1,
                    StartByte = 1,
                    EndByte = 60,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "called-method-1",
                    Name = "CalledMethod",
                    Kind = "method",
                    ParentId = "test-class-1",
                    Signature = "public void CalledMethod()",
                    FilePath = _testFilePath,
                    StartLine = 4,
                    StartColumn = 5,
                    StartByte = 30,
                    EndByte = 55,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "caller-class-1",
                    Name = "CallerClass",
                    Kind = "class",
                    Signature = "public class CallerClass",
                    FilePath = _testFilePath,
                    StartLine = 7,
                    StartColumn = 1,
                    StartByte = 65,
                    EndByte = 180,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "caller-method-1",
                    Name = "CallerMethod",
                    Kind = "method",
                    ParentId = "caller-class-1",
                    Signature = "public void CallerMethod()",
                    FilePath = _testFilePath,
                    StartLine = 9,
                    StartColumn = 5,
                    StartByte = 100,
                    EndByte = 175,
                    Language = "csharp"
                }
            };

            var mockCallers = new List<JulieIdentifier>
            {
                new JulieIdentifier
                {
                    Name = "CalledMethod",
                    Kind = "call",
                    ContainingSymbolId = "caller-method-1",
                    TargetSymbolId = "called-method-1", // CallerMethod calls CalledMethod
                    FilePath = _testFilePath,
                    StartLine = 12,
                    StartColumn = 10,
                    EndLine = 12,
                    EndColumn = 25
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            // For callers, the tool uses GetIdentifiersByNameAsync
            sqliteMock.Setup(x => x.GetIdentifiersByNameAsync(It.IsAny<string>(), "CalledMethod", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCallers);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "CalledMethod" },
                DetailLevel = "full",
                IncludeCallers = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols[0].Callers.Should().NotBeNull();
            readResult.Symbols[0].Callers.Should().HaveCount(1);
            readResult.Symbols[0].Callers![0].Name.Should().Be("CalledMethod");
            readResult.Symbols[0].Callers[0].Kind.Should().Be("call");
        }

        [Test]
        public async Task ExecuteAsync_Should_Include_Inheritance_When_Requested()
        {
            // Arrange
            var testCode = @"
public interface IShape { }
public class BaseShape { }
public class Circle : BaseShape, IShape { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "ishape-1",
                    Name = "IShape",
                    Kind = "interface",
                    Signature = "public interface IShape",
                    FilePath = _testFilePath,
                    StartLine = 2,
                    StartColumn = 1,
                    StartByte = 1,
                    EndByte = 25,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "baseshape-1",
                    Name = "BaseShape",
                    Kind = "class",
                    Signature = "public class BaseShape",
                    FilePath = _testFilePath,
                    StartLine = 3,
                    StartColumn = 1,
                    StartByte = 26,
                    EndByte = 50,
                    Language = "csharp"
                },
                new JulieSymbol
                {
                    Id = "circle-1",
                    Name = "Circle",
                    Kind = "class",
                    Signature = "public class Circle : BaseShape, IShape",
                    FilePath = _testFilePath,
                    StartLine = 4,
                    StartColumn = 1,
                    StartByte = 51,
                    EndByte = 95,
                    Language = "csharp"
                }
            };

            // GetRelationshipsForSymbolsAsync returns Dictionary<string, List<JulieRelationship>>
            var mockRelationships = new Dictionary<string, List<JulieRelationship>>
            {
                {
                    "circle-1",
                    new List<JulieRelationship>
                    {
                        new JulieRelationship
                        {
                            FromSymbolId = "circle-1",
                            ToSymbolId = "baseshape-1",
                            Kind = "extends"
                        },
                        new JulieRelationship
                        {
                            FromSymbolId = "circle-1",
                            ToSymbolId = "ishape-1",
                            Kind = "implements"
                        }
                    }
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetRelationshipsForSymbolsAsync(
                    It.IsAny<string>(),
                    It.Is<List<string>>(ids => ids.Contains("circle-1")),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockRelationships);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "Circle" },
                DetailLevel = "full",
                IncludeInheritance = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            readResult.Symbols[0].Inheritance.Should().NotBeNull();
            readResult.Symbols[0].Inheritance!.BaseClass.Should().Be("BaseShape"); // BaseClass is singular
            readResult.Symbols[0].Inheritance.Interfaces.Should().HaveCount(1);
            readResult.Symbols[0].Inheritance.Interfaces[0].Should().Be("IShape");
        }

        [Test]
        public async Task ExecuteAsync_Should_Use_Cache_When_Available()
        {
            // Arrange
            var testCode = "public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            // Setup SQLite mock (won't be called if cache is hit)
            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);

            _tool = CreateToolWithSQLite(sqliteMock);

            var cachedResult = new AIOptimizedResponse<ReadSymbolsResult>
            {
                Success = true,
                Message = "Cached result",
                Data = new AIResponseData<ReadSymbolsResult>
                {
                    Results = new ReadSymbolsResult
                    {
                        FilePath = _testFilePath,
                        Symbols = new List<SymbolCode>
                        {
                            new SymbolCode { Name = "CachedClass", Code = "public class CachedClass { }" }
                        },
                        EstimatedTokens = 50,
                        NotFoundSymbols = new List<string>()
                    }
                }
            };

            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-cache-key");

            ResponseCacheServiceMock.Setup(x => x.GetAsync<AIOptimizedResponse<ReadSymbolsResult>>("test-cache-key"))
                .ReturnsAsync(cachedResult);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" },
                NoCache = false // Enable caching
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Message.Should().Be("Cached result");
            result.Result.Data!.Results.Symbols[0].Name.Should().Be("CachedClass");

            // Verify SQLite was not called (cache was used)
            sqliteMock.Verify(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Skip_Cache_When_NoCache_Is_True()
        {
            // Arrange
            var testCode = "public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = _testFilePath,
                    StartLine = 1,
                    StartColumn = 1,
                    StartByte = 0,
                    EndByte = 27,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" },
                NoCache = true // Disable caching
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();

            // Verify cache was not checked or set
            ResponseCacheServiceMock.Verify(x => x.GetAsync<AIOptimizedResponse<ReadSymbolsResult>>(It.IsAny<string>()), Times.Never);
            ResponseCacheServiceMock.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CacheEntryOptions>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_ByteOffset_Precision()
        {
            // Arrange - Test that byte offsets provide surgical extraction without code bleeding
            var testCode = @"public class Circle { }
public class Rectangle { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "circle-1",
                    Name = "Circle",
                    Kind = "class",
                    Signature = "public class Circle",
                    FilePath = _testFilePath,
                    StartLine = 1,
                    StartColumn = 1,
                    StartByte = 0,
                    EndByte = 23, // Exact end of "public class Circle { }"
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);
            sqliteMock.Setup(x => x.GetFileByPathAsync(It.IsAny<string>(), _testFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileRecord(_testFilePath, testCode, "csharp", testCode.Length, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = _testFilePath,
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "Circle" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var readResult = result.Result!.Data!.Results;
            var extractedCode = readResult.Symbols[0].Code;

            // Should extract ONLY Circle, not Rectangle
            extractedCode.Should().Contain("Circle");
            extractedCode.Should().NotContain("Rectangle"); // No code bleeding!
        }

        [Test]
        public async Task ExecuteAsync_Should_Convert_Relative_Path_To_Absolute()
        {
            // Arrange
            var relativePath = "TestFile.cs";
            var absolutePath = Path.GetFullPath(relativePath);
            var testCode = "public class TestClass { }";
            await File.WriteAllTextAsync(absolutePath, testCode);

            var mockSymbols = new List<JulieSymbol>
            {
                new JulieSymbol
                {
                    Id = "test-1",
                    Name = "TestClass",
                    Kind = "class",
                    Signature = "public class TestClass",
                    FilePath = absolutePath,
                    StartLine = 1,
                    StartColumn = 1,
                    StartByte = 0,
                    EndByte = 27,
                    Language = "csharp"
                }
            };

            var sqliteMock = new Mock<ISQLiteSymbolService>();
            sqliteMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            sqliteMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), absolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSymbols);

            _tool = CreateToolWithSQLite(sqliteMock);

            var parameters = new ReadSymbolsParameters
            {
                FilePath = relativePath, // Relative path
                WorkspacePath = TestWorkspacePath,
                SymbolNames = new List<string> { "TestClass" }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<ReadSymbolsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Data!.Results.FilePath.Should().Be(absolutePath);

            // Cleanup
            File.Delete(absolutePath);
        }
    }
}
