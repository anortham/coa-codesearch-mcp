using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tests.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Test DeleteLinesTool for newline handling bugs similar to InsertAtLineTool
/// </summary>
[TestFixture]
public class DeleteLinesNewlineBugTest : CodeSearchToolTestBase<DeleteLinesTool>
{
    private TestFileManager _fileManager = null!;
    private DeleteLinesTool _tool = null!;

    protected override DeleteLinesTool CreateTool()
    {
        var unifiedFileEditService = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
            new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
        var deleteLogger = new Mock<Microsoft.Extensions.Logging.ILogger<DeleteLinesTool>>();
        _tool = new DeleteLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditService,
            deleteLogger.Object
        );
        return _tool;
    }

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _fileManager = new TestFileManager();
    }

    [Test]
    [Ignore("Replaced by NewlinePreservationRealWorldTests.DeleteLines_RemoveMethodsFromRealFile_PreservesNewlinesAndFormat")]
    public async Task DeleteLines_LargeDeletion_ShouldNotAddExtraNewlines()
    {
        // Arrange: File with many lines to delete, testing if deletion corrupts newlines
        var originalContent = @"class TestClass
{
    public void Method1() { }";

        // Add many lines to delete
        for (int i = 0; i < 50; i++)
        {
            originalContent += $"\n    public void ToDelete{i}() {{ /* Delete me */ }}";
        }
        
        originalContent += @"
    public void Method2() { }
}" + "\n";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "DeleteTest.cs");
        
        // Act: Delete lines 4-53 (all the ToDelete methods)
        var parameters = new DeleteLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = 4,
            EndLine = 53,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Check for newline corruption after large deletion
        result.Success.Should().BeTrue($"Delete operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        Console.WriteLine("=== DELETE TOOL - RESULT ===");
        Console.WriteLine($"'{modifiedContent}'");
        Console.WriteLine("=== END ===");
        
        Console.WriteLine($"=== DEBUG INFO ===");
        Console.WriteLine($"Length: {modifiedContent.Length}");
        Console.WriteLine($"Ends with }}\\n: {modifiedContent.EndsWith("}\n")}");
        Console.WriteLine($"Last 5 chars: '{modifiedContent.Substring(Math.Max(0, modifiedContent.Length - 5))}'");
        Console.WriteLine($"Last 5 bytes: [{string.Join(",", System.Text.Encoding.UTF8.GetBytes(modifiedContent.Substring(Math.Max(0, modifiedContent.Length - 5))))}]");
        Console.WriteLine($"Expected: '}}\\n' [{string.Join(",", System.Text.Encoding.UTF8.GetBytes("}\n"))}]");
        
        // Key test: Should preserve the original line endings and not add extra newlines
        // First determine what line ending the original file actually used
        var originalFileContent = testFile.OriginalContent;
        var expectedLineEnding = originalFileContent.Contains("\r\n") ? "\r\n" : "\n";
        var expectedEnding = "}" + expectedLineEnding;
        
        Console.WriteLine($"Original line ending detected: {(expectedLineEnding == "\r\n" ? "CRLF" : "LF")}");
        Console.WriteLine($"Expected ending: [{string.Join(",", System.Text.Encoding.UTF8.GetBytes(expectedEnding))}]");
        
        modifiedContent.Should().EndWith(expectedEnding, "DeleteLinesTool should preserve original line endings");
        modifiedContent.Should().NotEndWith("}" + expectedLineEnding + expectedLineEnding, "Should not add extra newlines");
        
        // Verify deletion worked correctly
        modifiedContent.Should().NotContain("ToDelete", "All ToDelete methods should be gone");
        modifiedContent.Should().Contain("Method1", "Method1 should remain");
        modifiedContent.Should().Contain("Method2", "Method2 should remain");
        
        // Should be a clean, simple file now - use actual line ending from the file
        var actualLineEnding = originalFileContent.Contains("\r\n") ? "\r\n" : "\n";
        var expectedResult = @"class TestClass
{
    public void Method1() { }
    public void Method2() { }
}" + actualLineEnding;
        modifiedContent.Should().Be(expectedResult, "File should be clean after deletion");
                        }

                        [Test]
                        [Ignore("Replaced by NewlinePreservationRealWorldTests and GoldenMasterEditingTests")]
                        public async Task DeleteLines_SingleLineAtEnd_ShouldNotCorruptStructure()
    {
        // Arrange: Test deleting the last line before closing brace
                    var originalContent = @"class TestClass
            {
                public void Method1() { }
                public void ToDelete() { }
            }" + "\n";
        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "DeleteEndTest.cs");
        
        // Act: Delete line 4 (ToDelete method)
        var parameters = new DeleteLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = 4,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should maintain clean structure
        result.Success.Should().BeTrue($"Delete operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        Console.WriteLine("=== DELETE SINGLE LINE - RESULT ===");
        Console.WriteLine($"'{modifiedContent}'");
        Console.WriteLine("=== END ===");
        
                // Should maintain exact expected format
                var expectedResult = @"class TestClass
        {
            public void Method1() { }
        }" + "\n";
        modifiedContent.Should().Be(expectedResult, "Should have exact clean structure after single line deletion");
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}
