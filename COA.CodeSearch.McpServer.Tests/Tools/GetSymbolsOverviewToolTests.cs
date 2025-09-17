using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class GetSymbolsOverviewToolTests : CodeSearchToolTestBase<GetSymbolsOverviewTool>
    {
        private GetSymbolsOverviewTool _tool = null!;
        private Mock<ITypeExtractionService> _typeExtractionServiceMock = null!;
        private string _testFilePath = null!;

        protected override GetSymbolsOverviewTool CreateTool()
        {
            _typeExtractionServiceMock = CreateMock<ITypeExtractionService>();
            
            _tool = new GetSymbolsOverviewTool(
                ServiceProvider,
                _typeExtractionServiceMock.Object,
                LuceneIndexServiceMock.Object,
                CodeAnalyzer,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ToolLoggerMock.Object
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

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
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

            _typeExtractionServiceMock.Setup(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TypeExtractionResult
                {
                    Success = true,
                    Language = "csharp",
                    Types = new List<TypeInfo>
                    {
                        new TypeInfo { Name = "TestClass", Kind = "class", Signature = "public class TestClass", Line = 2 },
                        new TypeInfo { Name = "ITestInterface", Kind = "interface", Signature = "public interface ITestInterface", Line = 3 },
                        new TypeInfo { Name = "TestStruct", Kind = "struct", Signature = "public struct TestStruct", Line = 4 },
                        new TypeInfo { Name = "TestEnum", Kind = "enum", Signature = "public enum TestEnum", Line = 5 }
                    },
                    Methods = new List<MethodInfo>()
                });

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
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

            _typeExtractionServiceMock.Setup(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TypeExtractionResult
                {
                    Success = true,
                    Language = "csharp",
                    Types = new List<TypeInfo>
                    {
                        new TypeInfo 
                        { 
                            Name = "TestClass", 
                            Kind = "class", 
                            Line = 2,
                            Signature = "public class TestClass"
                        }
                    },
                    Methods = new List<MethodInfo>
                    {
                        new MethodInfo 
                        { 
                            Name = "Method1", 
                            ContainingType = "TestClass", 
                            Line = 4,
                            Signature = "public void Method1()",
                            ReturnType = "void",
                            Parameters = new List<string>(),
                            Modifiers = new List<string> { "public" }
                        },
                        new MethodInfo 
                        { 
                            Name = "Method2", 
                            ContainingType = "TestClass", 
                            Line = 5,
                            Signature = "private int Method2(string param)",
                            ReturnType = "int",
                            Parameters = new List<string> { "string param" },
                            Modifiers = new List<string> { "private" }
                        },
                        new MethodInfo 
                        { 
                            Name = "GlobalMethod", 
                            ContainingType = "", 
                            Line = 8,
                            Signature = "public static void GlobalMethod()",
                            ReturnType = "void",
                            Parameters = new List<string>(),
                            Modifiers = new List<string> { "public", "static" }
                        }
                    }
                });

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                IncludeMethods = true,
                IncludeLineNumbers = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;
            
            // Class should have 2 methods
            overview.Classes.Should().HaveCount(1);
            overview.Classes[0].Methods.Should().HaveCount(2);
            overview.Classes[0].MethodCount.Should().Be(2);
            
            var method1 = overview.Classes[0].Methods.First(m => m.Name == "Method1");
            method1.ReturnType.Should().Be("void");
            method1.Line.Should().Be(4);
            method1.Modifiers.Should().Contain("public");
            
            var method2 = overview.Classes[0].Methods.First(m => m.Name == "Method2");
            method2.ReturnType.Should().Be("int");
            method2.Parameters.Should().Contain("string param");
            
            // Should have 1 standalone method
            overview.Methods.Should().HaveCount(1);
            overview.Methods[0].Name.Should().Be("GlobalMethod");
            overview.Methods[0].ContainingType.Should().BeNullOrEmpty();
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

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
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
            overview.Methods.Should().BeEmpty();
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

            _typeExtractionServiceMock.Setup(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TypeExtractionResult
                {
                    Success = true,
                    Language = "csharp",
                    Types = new List<TypeInfo>
                    {
                        new TypeInfo 
                        { 
                            Name = "TestClass", 
                            Kind = "class", 
                            Signature = "public class TestClass : BaseClass, ITestInterface",
                            Line = 2,
                            BaseType = "BaseClass",
                            Interfaces = new List<string> { "ITestInterface" }
                        }
                    },
                    Methods = new List<MethodInfo>()
                });

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                IncludeInheritance = true
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;
            
            overview.Classes[0].BaseType.Should().Be("BaseClass");
            overview.Classes[0].Interfaces.Should().Contain("ITestInterface");
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

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
                IncludeInheritance = false // Don't include inheritance
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            var overview = result.Result!.Data!.Results;
            
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

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
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
            overview.Classes[0].Methods[0].Line.Should().Be(0);
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_File_Read_Error()
        {
            // Arrange - Use a file path that exists but will cause read error
            var lockedFilePath = Path.Combine(TestWorkspacePath, "locked-file.cs");
            
            // Create the file
            await File.WriteAllTextAsync(lockedFilePath, "test content");
            
            // Lock the file by opening it exclusively
            using var fileStream = File.Open(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
            
            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = lockedFilePath
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("FILE_READ_ERROR");
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_Type_Extraction_Failure()
        {
            // Arrange
            var testCode = "invalid code that cannot be parsed";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            _typeExtractionServiceMock.Setup(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TypeExtractionResult
                {
                    Success = false,
                    Language = "csharp"
                });

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Code.Should().Be("TYPE_EXTRACTION_FAILED");
            result.Result.Error.Recovery!.Steps.Should().Contain("Check if the file contains valid code");
        }

        [Test]
        public async Task ExecuteAsync_Should_Use_Cache_When_Available()
        {
            // Arrange
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

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
            
            // Verify type extraction service was not called (cache was used)
            _typeExtractionServiceMock.Verify(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Skip_Cache_When_NoCache_Is_True()
        {
            // Arrange
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = _testFilePath,
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
            var testCode = @"public class TestClass { }";
            await File.WriteAllTextAsync(Path.Combine(Environment.CurrentDirectory, relativePath), testCode);

            SetupSuccessfulTypeExtraction();

            var parameters = new GetSymbolsOverviewParameters
            {
                FilePath = relativePath // Relative path
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SymbolsOverviewResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Data!.Results.FilePath.Should().Be(Path.GetFullPath(relativePath));
            
            // Cleanup
            File.Delete(Path.Combine(Environment.CurrentDirectory, relativePath));
        }

        private void SetupSuccessfulTypeExtraction()
        {
            _typeExtractionServiceMock.Setup(x => x.ExtractTypes(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TypeExtractionResult
                {
                    Success = true,
                    Language = "csharp",
                    Types = new List<TypeInfo>
                    {
                        new TypeInfo 
                        { 
                            Name = "TestClass", 
                            Kind = "class", 
                            Line = 2,
                            Signature = "public class TestClass",
                            Modifiers = new List<string> { "public" }
                        }
                    },
                    Methods = new List<MethodInfo>
                    {
                        new MethodInfo 
                        { 
                            Name = "TestMethod", 
                            ContainingType = "TestClass", 
                            Line = 4,
                            Signature = "public void TestMethod()",
                            ReturnType = "void",
                            Parameters = new List<string>(),
                            Modifiers = new List<string> { "public" }
                        }
                    }
                });
        }
    }
}