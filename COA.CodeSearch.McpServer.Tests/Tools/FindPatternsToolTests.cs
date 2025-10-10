using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class FindPatternsToolTests : CodeSearchToolTestBase<FindPatternsTool>
    {
        private FindPatternsTool _tool = null!;
        private Mock<ISQLiteSymbolService> _sqliteServiceMock = null!;
        private Mock<IPathResolutionService> _pathResolutionServiceMock = null!;
        private string _testFilePath = null!;

        protected override FindPatternsTool CreateTool()
        {
            _pathResolutionServiceMock = CreateMock<IPathResolutionService>();
            _sqliteServiceMock = CreateMock<ISQLiteSymbolService>();

            _pathResolutionServiceMock.Setup(x => x.GetPrimaryWorkspacePath()).Returns(TestWorkspacePath);

            _tool = new FindPatternsTool(
                ServiceProvider,
                _pathResolutionServiceMock.Object,
                ToolLoggerMock.Object,
                _sqliteServiceMock.Object
            );
            return _tool;
        }

        protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            // Configure base services first
            base.ConfigureServices(services);
        }

        [SetUp]
        public new void SetUp()
        {
            // Create a test file with various patterns
            _testFilePath = Path.Combine(TestWorkspacePath, "test-patterns.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);
        }

        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.FindPatterns);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("Tree-sitter");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_FilePath_Is_Missing()
        {
            // Arrange
            var parameters = new FindPatternsParameters
            {
                FilePath = string.Empty
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("PATTERN_DETECTION_ERROR");
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_File_Does_Not_Exist()
        {
            // Arrange
            var parameters = new FindPatternsParameters
            {
                FilePath = "/non/existent/file.cs"
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error!.Message.Should().Contain("File not found");
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Empty_Catch_Blocks()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new Exception();
        }
        catch
        {
            // Empty catch block
        }
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectEmptyCatchBlocks = true,
                DetectAsyncPatterns = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().NotBeEmpty();
            
            var emptyCatchPattern = result.Result.Data.Results.PatternsFound
                .FirstOrDefault(p => p.Type == "EmptyCatchBlock");
            emptyCatchPattern.Should().NotBeNull();
            emptyCatchPattern!.Severity.Should().Be("Error");
            emptyCatchPattern.LineNumber.Should().Be(12); // catch line
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Async_Without_ConfigureAwait()
        {
            // Arrange
            var testCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethodAsync()
    {
        await Task.Delay(1000); // Missing ConfigureAwait(false)
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectAsyncPatterns = true,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().NotBeEmpty();
            
            var asyncPattern = result.Result.Data.Results.PatternsFound
                .FirstOrDefault(p => p.Type == "AsyncWithoutConfigureAwait");
            asyncPattern.Should().NotBeNull();
            asyncPattern!.Severity.Should().Be("Warning");
            asyncPattern.LineNumber.Should().Be(9); // await line
            asyncPattern.Suggestion.Should().Contain("ConfigureAwait(false)");
        }

        [Test]
        public async Task ExecuteAsync_Should_Not_Detect_ConfigureAwait_In_Console_App()
        {
            // Arrange
            var testCode = @"
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        await Task.Delay(1000); // Should not trigger in console app
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectAsyncPatterns = true,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            
            // Should not detect async pattern in console app
            var asyncPattern = result.Result.Data!.Results.PatternsFound
                .FirstOrDefault(p => p.Type == "AsyncWithoutConfigureAwait");
            asyncPattern.Should().BeNull();
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Magic_Numbers()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        var timeout = 5000; // Magic number
        var buffer = new byte[1024]; // Another magic number
        if (timeout > 3600) // Yet another magic number
        {
            Console.WriteLine(""Too long"");
        }
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectMagicNumbers = true,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().HaveCountGreaterThan(0);
            
            var magicNumberPatterns = result.Result.Data.Results.PatternsFound
                .Where(p => p.Type == "MagicNumber").ToList();
            magicNumberPatterns.Should().HaveCountGreaterOrEqualTo(3); // 5000, 1024, 3600
            
            magicNumberPatterns.Should().Contain(p => p.Message.Contains("5000"));
            magicNumberPatterns.Should().Contain(p => p.Message.Contains("1024"));
            magicNumberPatterns.Should().Contain(p => p.Message.Contains("3600"));
        }

        [Test]
        public async Task ExecuteAsync_Should_Not_Detect_Acceptable_Numbers()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        var count = 0; // Should not be flagged
        var increment = 1; // Should not be flagged  
        var decrement = -1; // Should not be flagged
        var minValue = 10; // Single digit should not be flagged
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectMagicNumbers = true,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            
            // Should not detect 0, 1, -1, or single digit numbers
            var magicNumberPatterns = result.Result.Data!.Results.PatternsFound
                .Where(p => p.Type == "MagicNumber").ToList();
            magicNumberPatterns.Should().BeEmpty();
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Large_Methods()
        {
            // Arrange
            var longMethod = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"            // Line {i}"));
            var testCode = $@"
using System;

public class TestClass
{{
    public void LargeMethod()
    {{
{longMethod}
        Console.WriteLine(""Method completed"");
    }}
}}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupSuccessfulTypeExtractionWithMethods();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectLargeMethods = true,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().NotBeEmpty();
            
            var largeMethodPattern = result.Result.Data.Results.PatternsFound
                .FirstOrDefault(p => p.Type == "LargeMethod");
            largeMethodPattern.Should().NotBeNull();
            largeMethodPattern!.Severity.Should().Be("Warning");
            largeMethodPattern.Message.Should().Contain("LargeMethod");
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Unused_Usings()
        {
            // Arrange
            var testCode = @"
using System;
using System.Collections.Generic;
using System.Unused.Namespace; // This should be detected
using UnusedCustom.Something; // This should be detected

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""Hello""); // Uses System
        var list = new List<int>(); // Uses System.Collections.Generic
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectUnusedUsings = true,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            
            var unusedUsingPatterns = result.Result.Data!.Results.PatternsFound
                .Where(p => p.Type == "PotentialUnusedUsing").ToList();
            unusedUsingPatterns.Should().HaveCountGreaterThan(0);
            
            // Should detect the unused custom namespaces but not System namespaces
            unusedUsingPatterns.Should().Contain(p => p.Message.Contains("UnusedCustom.Something"));
        }


        [Test]
        public async Task ExecuteAsync_Should_Respect_MaxResults_Parameter()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        var magic1 = 100;
        var magic2 = 200;
        var magic3 = 300;
        var magic4 = 400;
        var magic5 = 500;
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectMagicNumbers = true,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectLargeMethods = false,
                MaxResults = 2 // Limit to 2 results
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().HaveCount(2);
        }

        [Test]
        public async Task ExecuteAsync_Should_Filter_By_Severity_Levels()
        {
            // Arrange
            var testCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethodAsync()
    {
        try
        {
            await Task.Delay(1000); // Warning: ConfigureAwait
            var magic = 5000; // Info: Magic number
        }
        catch
        {
            // Error: Empty catch
        }
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectAsyncPatterns = true,
                DetectEmptyCatchBlocks = true,
                DetectMagicNumbers = true,
                DetectUnusedUsings = false,
                DetectLargeMethods = false,
                SeverityLevels = new List<string> { "Error" } // Only errors
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().NotBeEmpty();
            result.Result.Data.Results.PatternsFound.Should().OnlyContain(p => p.Severity == "Error");
        }

        [Test]
        public async Task ExecuteAsync_Should_Detect_Custom_Patterns()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        // TODO: Fix this urgent issue
        Console.WriteLine(""Debug output""); // Remove before production
        var deprecated = GetOldMethod(); // @deprecated
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false,
                CustomPatterns = new List<string>
                {
                    @"TODO.*urgent",
                    @"@deprecated",
                    @"Console\.WriteLine"
                }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data!.Results.PatternsFound.Should().HaveCount(3);

            var customPatterns = result.Result.Data.Results.PatternsFound
                .Where(p => p.Type == "CustomPattern").ToList();
            customPatterns.Should().HaveCount(3);

            // Verify each pattern was found
            customPatterns.Should().Contain(p => p.Metadata["matchedText"].ToString()!.Contains("TODO"));
            customPatterns.Should().Contain(p => p.Metadata["matchedText"].ToString()!.Contains("@deprecated"));
            customPatterns.Should().Contain(p => p.Metadata["matchedText"].ToString()!.Contains("Console.WriteLine"));

            // Verify all have Info severity
            customPatterns.Should().OnlyContain(p => p.Severity == "Info");
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_Invalid_Custom_Regex_Pattern()
        {
            // Arrange
            var testCode = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""Test"");
    }
}";
            await File.WriteAllTextAsync(_testFilePath, testCode);

            SetupEmptySymbols();

            var parameters = new FindPatternsParameters
            {
                FilePath = _testFilePath,
                DetectAsyncPatterns = false,
                DetectEmptyCatchBlocks = false,
                DetectUnusedUsings = false,
                DetectMagicNumbers = false,
                DetectLargeMethods = false,
                CustomPatterns = new List<string>
                {
                    @"[invalid(regex" // Invalid regex
                }
            };

            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<FindPatternsResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));

            // Assert
            result.Success.Should().BeTrue();
            result.Result!.Success.Should().BeTrue();

            var invalidPattern = result.Result.Data!.Results.PatternsFound
                .FirstOrDefault(p => p.Type == "InvalidCustomPattern");
            invalidPattern.Should().NotBeNull();
            invalidPattern!.Severity.Should().Be("Error");
            invalidPattern.Message.Should().Contain("Invalid regex pattern");
        }

        [Test]
        public void FindPatternsResult_Should_Calculate_Summaries_Correctly()
        {
            // Arrange
            var result = new FindPatternsResult
            {
                PatternsFound = new List<CodePattern>
                {
                    new CodePattern { Type = "AsyncWithoutConfigureAwait", Severity = "Warning" },
                    new CodePattern { Type = "AsyncWithoutConfigureAwait", Severity = "Warning" },
                    new CodePattern { Type = "EmptyCatchBlock", Severity = "Error" },
                    new CodePattern { Type = "MagicNumber", Severity = "Info" }
                }
            };

            // Act & Assert
            result.PatternSummary.Should().HaveCount(3);
            result.PatternSummary["AsyncWithoutConfigureAwait"].Should().Be(2);
            result.PatternSummary["EmptyCatchBlock"].Should().Be(1);
            result.PatternSummary["MagicNumber"].Should().Be(1);

            result.SeveritySummary.Should().HaveCount(3);
            result.SeveritySummary["Warning"].Should().Be(2);
            result.SeveritySummary["Error"].Should().Be(1);
            result.SeveritySummary["Info"].Should().Be(1);
        }

        private void SetupEmptySymbols()
        {
            // SQLite database doesn't exist, so patterns will work without symbols
            _sqliteServiceMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(false);
        }

        private void SetupSuccessfulTypeExtractionWithMethods()
        {
            // Setup SQLite to return method symbols for large method detection
            _sqliteServiceMock.Setup(x => x.DatabaseExists(It.IsAny<string>())).Returns(true);
            _sqliteServiceMock.Setup(x => x.GetSymbolsForFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<COA.CodeSearch.McpServer.Services.Julie.JulieSymbol>
                {
                    new COA.CodeSearch.McpServer.Services.Julie.JulieSymbol
                    {
                        Name = "LargeMethod",
                        Kind = "method",
                        Language = "csharp",
                        StartLine = 6,
                        EndLine = 70, // 64 lines long
                        FilePath = string.Empty,
                        Id = "test-id"
                    }
                });
        }
    }
}