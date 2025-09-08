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
using System.Linq;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Tests that demonstrate the indentation bug we discovered when using replace_lines tool
/// </summary>
[TestFixture]
public class IndentationBugTests : CodeSearchToolTestBase<ReplaceLinesTool>
{
    private TestFileManager _fileManager = null!;
    private ReplaceLinesTool _tool = null!;

    protected override ReplaceLinesTool CreateTool()
    {
        _tool = new ReplaceLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            ToolLoggerMock.Object
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
    public async Task ReplaceLines_ReproducesToolNamesIndentationBug_ShouldNotCreateDuplicatesOrBadIndentation()
    {
        // Arrange: Create a file that mimics the ToolNames.cs structure where we saw the bug
        var originalContent = @"namespace Test;

public static class ToolNames
{
    // Core operations  
    public const string TextSearch = ""text_search"";
    public const string FileSearch = ""file_search"";
    
    // Navigation tools
    public const string SymbolSearch = ""symbol_search"";
    public const string GoToDefinition = ""goto_definition"";
    
    // Editing tools
    public const string InsertAtLine = ""insert_at_line"";
    public const string ReplaceLines = ""replace_lines"";
    public const string DeleteLines = ""delete_lines"";
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "ToolNames.cs");
        
        // Act: Try to add a new tool constant at the end, similar to what we did
        var parameters = new ReplaceLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = 16, // The DeleteLines line (corrected)
            EndLine = 16,   // Just replace this one line
            Content = @"    public const string DeleteLines = ""delete_lines"";
    
    // Advanced semantic tools
    public const string GetSymbolsOverview = ""get_symbols_overview"";",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: The result should be successful
        result.Success.Should().BeTrue($"Replace operation failed: {result.Error?.Message}");

        // Read the modified file
        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        var modifiedLines = modifiedContent.Split('\n', StringSplitOptions.None);

        // Critical assertions to catch the bug we saw:
        
        // 1. Should not have duplicate DeleteLines constants
        var deleteLinesCount = modifiedLines.Count(line => line.Contains(@"""delete_lines"""));
        deleteLinesCount.Should().Be(1, "Should have exactly one DeleteLines constant, not duplicates");

        // 2. Should not have inconsistent indentation (all class members should have same indentation)
        var constantLines = modifiedLines
            .Where(line => line.Contains("public const string"))
            .ToList();

        constantLines.Count.Should().BeGreaterOrEqualTo(4, "Should have at least 4 constants");

        // All constants should have the same indentation
        var expectedIndentation = "    "; // 4 spaces
        foreach (var constantLine in constantLines)
        {
            constantLine.Should().StartWith(expectedIndentation, 
                $"Constant line should start with 4 spaces but was: '{constantLine}'");
            
            // Should not have nested indentation like "        " (8 spaces)
            constantLine.Should().NotStartWith("        ", 
                $"Constant line should not have nested indentation: '{constantLine}'");
        }

        // 3. Should have proper class structure (closing brace should be at root level)
        var closingBraceLines = modifiedLines
            .Select((line, index) => new { Line = line.Trim(), Index = index })
            .Where(x => x.Line == "}")
            .ToList();

        closingBraceLines.Should().HaveCount(1, "Should have exactly one closing brace");
        closingBraceLines[0].Line.Should().Be("}", "Closing brace should not have indentation");

        // 4. The new constant should be properly formatted
        var newConstantLine = modifiedLines.FirstOrDefault(line => line.Contains("GetSymbolsOverview"));
        newConstantLine.Should().NotBeNull();
        newConstantLine!.Should().StartWith(expectedIndentation, 
            "New constant should have proper indentation");

        // Debug: Always output the actual content for analysis
        var debugInfo = $@"
=== ACTUAL MODIFIED CONTENT ===
{modifiedContent}
=== END CONTENT ===

Original file lines: {testFile.OriginalLines.Length}
Modified file lines: {modifiedLines.Length}
DeleteLines count: {deleteLinesCount}
Constant lines: {constantLines.Count}
Expected indentation: '{expectedIndentation}'
";
        Console.WriteLine(debugInfo);
        
        // Check if we have the duplication issue
        if (deleteLinesCount != 1)
        {
            Assert.Fail($"Indentation bug reproduced - duplicate DeleteLines! {debugInfo}");
        }
    }

    [Test]
    public async Task ReplaceLines_WithMixedIndentation_ShouldPreserveConsistentStyle()
    {
        // Arrange: Create content with mixed spaces/tabs to test edge cases
        var originalContent = @"class Test
{
    public string Property1 { get; set; }
	public string Property2 { get; set; }  // Tab indented
    public string Property3 { get; set; }  // Space indented
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "MixedIndent.cs");

        // Act: Replace the middle property
        var parameters = new ReplaceLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = 4, // Property2 line
            EndLine = 4,
            Content = "    public string Property2Modified { get; set; }  // Should use space indentation",
            PreserveIndentation = true,
            ContextLines = 2
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should succeed and maintain consistent indentation
        result.Success.Should().BeTrue($"Replace operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        var modifiedLines = modifiedContent.Split('\n');

        // The replaced line should use consistent space indentation
        var property2Line = modifiedLines.FirstOrDefault(line => line.Contains("Property2Modified"));
        property2Line.Should().NotBeNull();
        property2Line!.Should().StartWith("    ", "Replaced line should use space indentation");
        property2Line.Should().NotStartWith("\t", "Replaced line should not use tab indentation");
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}