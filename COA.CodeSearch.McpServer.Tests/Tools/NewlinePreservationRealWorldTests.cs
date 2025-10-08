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
public class NewlinePreservationRealWorldTests : CodeSearchToolTestBase<EditLinesTool>
{
    private TestFileManager _fileManager = null!;
    private EditLinesTool _deleteTool = null!;
    private EditLinesTool _insertTool = null!;
    private EditLinesTool _replaceTool = null!;

    protected override EditLinesTool CreateTool()
    {
        var unifiedFileEditService = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
            new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
        var deleteLogger = new Mock<ILogger<EditLinesTool>>();
        _deleteTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditService,
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
        var unifiedFileEditServiceForInsert = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
            new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
        var insertLogger = new Mock<ILogger<EditLinesTool>>();
        _insertTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditServiceForInsert,
            insertLogger.Object
        );
        
        var unifiedFileEditServiceForReplace = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
            new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
        var replaceLogger = new Mock<ILogger<EditLinesTool>>();
        _replaceTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditServiceForReplace,
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
            "..", "..", "..", "..", "COA.CodeSearch.McpServer", "Services", "FileLineUtilities.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "DeleteTest_FileLineUtilities.cs");
        
        // Find a method to delete (let's delete the SplitLines method)
        var methodStartLine = FindLineContaining(testFile.OriginalLines, "public static string[] SplitLines");
        methodStartLine.Should().BeGreaterThan(0, "SplitLines method should exist in FileLineUtilities");
        
        var methodEndLine = FindMethodEndLine(testFile.OriginalLines, methodStartLine);
        
        // Act - Delete the method
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
            Operation = "insert",
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
            "..", "..", "..", "..", "COA.CodeSearch.McpServer", "Models", "DeleteLinesModels.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "InsertTest_SearchResults.cs");
        
        // Find insertion point (just before the closing brace of a class)
        var insertionLine = FindLineContaining(testFile.OriginalLines, "}") - 1; // Insert before closing brace
        insertionLine.Should().BeGreaterThan(0, "Should find a closing brace in the file");
        
        // Act - Insert a new method
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
            Operation = "delete",
            StartLine = insertionLine,
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
            "..", "..", "..", "..", "COA.CodeSearch.McpServer", "Services", "FileLineUtilities.cs");
        
        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, "ReplaceTest_FileLineUtilities.cs");
        
        // Find a method to replace (DetectEncoding method)
        var methodStartLine = FindLineContaining(testFile.OriginalLines, "public static Encoding DetectEncoding");
        methodStartLine.Should().BeGreaterThan(0, "DetectLineEnding method should exist");
        
        var methodEndLine = FindMethodEndLine(testFile.OriginalLines, methodStartLine);
        
        // Act - Replace with a modified version
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
            Operation = "insert",
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
        modifiedContent.Should().Contain("Enhanced encoding detection", "Method should be replaced with enhanced version");
        modifiedContent.Should().Contain("Modified by newline preservation test", "Replacement marker should be present");
        
        // Verify C# syntax is still valid
        ValidateCSharpSyntax(modifiedContent).Should().BeTrue("Modified file should still be valid C#");
    }

    /// <summary>
    /// Reproduces the indentation corruption issue where inserting pre-indented content
    /// results in double indentation due to improper indentation handling.
    /// </summary>
    [Test]
    public async Task InsertAtLine_PreIndentedContent_ShouldNotDoubleIndent()
    {
        // Arrange - Create a temporary test file with proper C# class structure
        var testContent = @"using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void ExistingMethod()
        {
            Console.WriteLine(""existing code"");
        }
        
        // Insert point - new method should be added here with proper indentation
    }
}";
        
        var tempFilePath = Path.GetTempFileName();
        tempFilePath = Path.ChangeExtension(tempFilePath, ".cs");
        await File.WriteAllTextAsync(tempFilePath, testContent);
        
        try
        {
            // Content to insert - already properly indented for a class member
            var contentToInsert = @"        public void NewMethod()
        {
            Console.WriteLine(""new method"");
            if (true)
            {
                Console.WriteLine(""nested block"");
            }
        }";
            
            // Act - Insert the pre-indented content
            var parameters = new EditLinesParameters
            {
                FilePath = tempFilePath,
                Operation = "replace",
                StartLine = 12, // Insert after the comment line
                Content = contentToInsert,
                PreserveIndentation = true
            };
            
            var result = await _insertTool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert - Verify the operation succeeded
            result.Should().NotBeNull();
            result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");
            result.Error.Should().BeNull();
            
            // Read the modified content
            var modifiedContent = await File.ReadAllTextAsync(tempFilePath);
            
            // Verify no double indentation occurred
            // The new method should have exactly 8 spaces (2 levels: namespace + class)
            modifiedContent.Should().Contain("        public void NewMethod()"); // 8 spaces exactly
            modifiedContent.Should().NotContain("            public void NewMethod()"); // 12 spaces would be wrong
            
            // Verify nested content maintains relative indentation
            modifiedContent.Should().Contain("            Console.WriteLine(\"new method\");"); // 12 spaces for method body
            modifiedContent.Should().Contain("                Console.WriteLine(\"nested block\");"); // 16 spaces for nested block
            
            // Verify the file is still valid C# syntax
            ValidateCSharpSyntax(modifiedContent).Should().BeTrue("The modified file should have valid C# syntax");
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    /// <summary>
    /// Test that mixed indentation scenarios work correctly (tabs vs spaces, different levels).
    /// </summary>
    [Test]
    public async Task InsertAtLine_MixedIndentationScenarios_HandlesCorrectly()
    {
        var testContent = @"namespace TestNamespace
{
    public class TestClass
    {
        // Insert various indentation scenarios here
    }
}";
        
        var tempFilePath = Path.GetTempFileName();
        tempFilePath = Path.ChangeExtension(tempFilePath, ".cs");
        await File.WriteAllTextAsync(tempFilePath, testContent);
        
        try
        {
            // Test Case 1: Content with no indentation should get base indentation applied
            var unindentedContent = @"public void UnindentedMethod()
{
    Console.WriteLine(""test"");
}";
            
            var parameters1 = new EditLinesParameters
            {
                FilePath = tempFilePath,
                Operation = "insert",
                StartLine = 5,
                Content = unindentedContent,
                PreserveIndentation = true
            };
            
            var result1 = await _insertTool.ExecuteAsync(parameters1, CancellationToken.None);
            result1.Success.Should().BeTrue();
            
            // Verify unindented content gets proper base indentation
            var modifiedContent1 = await File.ReadAllTextAsync(tempFilePath);
            modifiedContent1.Should().Contain("        public void UnindentedMethod()"); // 8 spaces
            
            // Test Case 2: Add content that should preserve its existing indentation
            var preIndentedContent = @"        public void AlreadyIndentedMethod()
        {
            Console.WriteLine(""already indented"");
        }";
            
            var parameters2 = new EditLinesParameters
            {
                FilePath = tempFilePath,
                Operation = "insert",
                StartLine = 6,
                Content = preIndentedContent,
                PreserveIndentation = true
            };
            
            var result2 = await _insertTool.ExecuteAsync(parameters2, CancellationToken.None);
            result2.Success.Should().BeTrue();
            
            // Verify pre-indented content is preserved as-is
            var modifiedContent2 = await File.ReadAllTextAsync(tempFilePath);
            modifiedContent2.Should().Contain("        public void AlreadyIndentedMethod()"); // Original 8 spaces preserved
            modifiedContent2.Should().NotContain("            public void AlreadyIndentedMethod()"); // No double indentation
            
            ValidateCSharpSyntax(modifiedContent2).Should().BeTrue("File should remain syntactically valid");
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
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
