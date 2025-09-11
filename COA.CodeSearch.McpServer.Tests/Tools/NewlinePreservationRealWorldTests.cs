using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Helpers;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Real-world newline preservation tests using actual source files.
/// Uses copy-original-edit-diff pattern to validate that editing tools preserve file format correctly.
/// </summary>
[TestFixture]
public class NewlinePreservationRealWorldTests : CodeSearchToolTestBase<DeleteLinesTool>
{
    private TestFileManager _fileManager = null!;
    private DeleteLinesTool _deleteTool = null!;
    private InsertAtLineTool _insertTool = null!;
    private ReplaceLinesTool _replaceTool = null!;

    protected override DeleteLinesTool CreateTool()
    {
        var deleteLogger = new Mock<ILogger<DeleteLinesTool>>();
        _deleteTool = new DeleteLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            deleteLogger.Object
        );
        return _deleteTool;
    }

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _fileManager = new TestFileManager();
        
        // Create additional tools
        var insertLogger = new Mock<ILogger<InsertAtLineTool>>();
        _insertTool = new InsertAtLineTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            insertLogger.Object
        );
        
        var replaceLogger = new Mock<ILogger<ReplaceLinesTool>>();
        _replaceTool = new ReplaceLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            replaceLogger.Object
        );
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }

    [Test]
    public async Task DeleteLines_RemoveMethodsFromRealFile_PreservesNewlinesAndFormat()
    {
        // Arrange - Use a real C# file from our codebase
        var sourceFile = Path.Combine(TestContext.CurrentContext.TestDirectory, 
            @"..\..\..\..\COA.CodeSearch.McpServer\Services\FileLineUtilities.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "DeleteTest_FileLineUtilities.cs");
        
        // Find a method to delete (let's delete the SplitLines method)
        var methodStartLine = FindLineContaining(testFile.OriginalLines, "public static string[] SplitLines");
        methodStartLine.Should().BeGreaterThan(0, "SplitLines method should exist in FileLineUtilities");
        
        var methodEndLine = FindMethodEndLine(testFile.OriginalLines, methodStartLine);
        
        // Act - Delete the method
        var parameters = new DeleteLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = methodStartLine,
            EndLine = methodEndLine,
            ContextLines = 3
        };

        var result = await _deleteTool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert - Comprehensive validation using TestFileManager
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"Delete operation failed: {result.Error?.Message}");
        result.Error.Should().BeNull();

        // Validate file integrity - this checks encoding, line endings, and format preservation
        var validation = await _fileManager.ValidateEditAsync(testFile);
        validation.Success.Should().BeTrue($"File integrity check failed: {string.Join("; ", validation.Issues)}");

        // Verify the method was actually deleted
        var modifiedContent = validation.CurrentContent;
        modifiedContent.Should().NotContain("public static string[] SplitLines", "SplitLines method should be deleted");
        
        // Verify file structure is maintained (class declaration should still exist)
        modifiedContent.Should().Contain("public static class FileLineUtilities", "Class declaration should be preserved");
        
        // Verify C# syntax is still valid
        ValidateCSharpSyntax(modifiedContent).Should().BeTrue("Modified file should still be valid C#");
    }

    [Test]
    public async Task InsertAtLine_AddMethodToRealFile_PreservesNewlinesAndFormat()
    {
        // Arrange - Use a real C# file
        var sourceFile = Path.Combine(TestContext.CurrentContext.TestDirectory, 
            @"..\..\..\..\COA.CodeSearch.McpServer\Models\DeleteLinesModels.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "InsertTest_SearchResults.cs");
        
        // Find insertion point (just before the closing brace of a class)
        var insertionLine = FindLineContaining(testFile.OriginalLines, "}") - 1; // Insert before closing brace
        insertionLine.Should().BeGreaterThan(0, "Should find a closing brace in the file");
        
        // Act - Insert a new method
        var parameters = new InsertAtLineParameters
        {
            FilePath = testFile.FilePath,
            LineNumber = insertionLine,
            Content = @"    /// <summary>
    /// Test method added by newline preservation test.
    /// </summary>
    public void TestMethod()
    {
        // This is a test method
    }",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _insertTool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert - Comprehensive validation
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");
        result.Error.Should().BeNull();

        // Validate file integrity
        var validation = await _fileManager.ValidateEditAsync(testFile);
        validation.Success.Should().BeTrue($"File integrity check failed: {string.Join("; ", validation.Issues)}");

        // Verify the method was inserted
        var modifiedContent = validation.CurrentContent;
        modifiedContent.Should().Contain("Test method added by newline preservation test", "New method should be inserted");
        modifiedContent.Should().Contain("public void TestMethod()", "Method signature should be present");
        
        // Verify C# syntax is still valid
        ValidateCSharpSyntax(modifiedContent).Should().BeTrue("Modified file should still be valid C#");
    }

    [Test]
    public async Task ReplaceLines_ReplaceMethodInRealFile_PreservesNewlinesAndFormat()
    {
        // Arrange - Use a real C# file
        var sourceFile = Path.Combine(TestContext.CurrentContext.TestDirectory, 
            @"..\..\..\..\COA.CodeSearch.McpServer\Services\FileLineUtilities.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "ReplaceTest_FileLineUtilities.cs");
        
        // Find a method to replace (DetectEncoding method)
        var methodStartLine = FindLineContaining(testFile.OriginalLines, "public static Encoding DetectEncoding");
        methodStartLine.Should().BeGreaterThan(0, "DetectLineEnding method should exist");
        
        var methodEndLine = FindMethodEndLine(testFile.OriginalLines, methodStartLine);
        
        // Act - Replace with a modified version
        var parameters = new ReplaceLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = methodStartLine,
            EndLine = methodEndLine,
            Content = @"    /// <summary>
    /// Enhanced encoding detection with additional format support.
    /// Modified by newline preservation test.
    /// </summary>
    /// <param name=""bytes"">File bytes</param>
    /// <returns>Detected encoding</returns>
    public static Encoding DetectEncoding(byte[] bytes)
    {
        // Enhanced BOM detection with additional formats
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return Encoding.UTF32; // UTF-32 BE
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Encoding.UTF32; // UTF-32 LE
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true); // UTF-8 WITH BOM
        
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE
        }
        
        // Default to UTF-8 without BOM
        return new UTF8Encoding(false);
    }",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _replaceTool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert - Comprehensive validation
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"Replace operation failed: {result.Error?.Message}");
        result.Error.Should().BeNull();

        // Validate file integrity
        var validation = await _fileManager.ValidateEditAsync(testFile);
        validation.Success.Should().BeTrue($"File integrity check failed: {string.Join("; ", validation.Issues)}");

        // Verify the method was replaced
        var modifiedContent = validation.CurrentContent;
        modifiedContent.Should().Contain("Enhanced line ending detection", "Method should be replaced with enhanced version");
        modifiedContent.Should().Contain("Modified by newline preservation test", "Replacement marker should be present");
        
        // Verify C# syntax is still valid
        ValidateCSharpSyntax(modifiedContent).Should().BeTrue("Modified file should still be valid C#");
    }

    #region Helper Methods

    private int FindLineContaining(string[] lines, string searchText)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(searchText))
                return i + 1; // Convert to 1-based line number
        }
        return -1;
    }

    private int FindMethodEndLine(string[] lines, int methodStartLine)
    {
        int braceCount = 0;
        bool foundOpenBrace = false;
        
        // Start from method start line (convert to 0-based)
        for (int i = methodStartLine - 1; i < lines.Length; i++)
        {
            var line = lines[i];
            
            foreach (char c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    foundOpenBrace = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (foundOpenBrace && braceCount == 0)
                    {
                        return i + 1; // Convert to 1-based line number
                    }
                }
            }
        }
        
        throw new InvalidOperationException($"Could not find end of method starting at line {methodStartLine}");
    }

    private bool ValidateCSharpSyntax(string content)
    {
        // Basic validation - check for balanced braces
        int braceCount = 0;
        foreach (char c in content)
        {
            if (c == '{') braceCount++;
            else if (c == '}') braceCount--;
        }
        return braceCount == 0;
    }

    #endregion
}