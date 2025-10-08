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
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Tests that demonstrate and verify the indentation bug fixes for InsertAtLineTool
/// </summary>
[TestFixture]
public class InsertAtLineIndentationTests : CodeSearchToolTestBase<EditLinesTool>
{
    private TestFileManager _fileManager = null!;
    private EditLinesTool _tool = null!;

    protected override EditLinesTool CreateTool()
    {
        var unifiedFileEditService = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
            new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
        var insertLogger = new Mock<Microsoft.Extensions.Logging.ILogger<EditLinesTool>>();
        _tool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditService,
            insertLogger.Object
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
    public async Task InsertAtLine_PrioritizesSurroundingContext_ShouldNotUseInconsistentTargetLineIndentation()
    {
        // Arrange: Create a file that mimics a scenario where the insertion line has inconsistent indentation
        var originalContent = @"namespace Test;

public static class ToolNames
{
    // Core operations  
    public const string TextSearch = ""text_search"";
    public const string FileSearch = ""file_search"";
	// This line has TAB indentation (inconsistent)
    public const string SymbolSearch = ""symbol_search"";
    public const string GoToDefinition = ""goto_definition"";
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "ToolNames.cs");
        
        // Act: Insert a new constant at line 8 (the line with tab indentation)
        // The tool should prioritize surrounding lines (space indentation) over the target line (tab indentation)
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 8, // Insert before the tab-indented line
            Content = @"    // Navigation tools (added by test)",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: The result should be successful
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

        // Read the modified file
        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        var modifiedLines = modifiedContent.Split('\n', StringSplitOptions.None);

        // Critical assertion: The inserted line should use SPACE indentation from surrounding context,
        // NOT tab indentation from the target line
        var insertedLine = modifiedLines[7]; // 0-based, inserted at line 8
        insertedLine.Should().StartWith("    ", // 4 spaces
            "Inserted line should use space indentation from surrounding context");
        insertedLine.Should().NotStartWith("\t", 
            "Inserted line should not use tab indentation from target line");

        // Verify all constants still have consistent indentation (spaces)
        var constantLines = modifiedLines
            .Where(line => line.Contains("public const string"))
            .ToList();

        foreach (var constantLine in constantLines)
        {
            constantLine.Should().StartWith("    ", // 4 spaces
                $"Constant line should use space indentation: '{constantLine}'");
        }

        // Debug output
        Console.WriteLine($"=== MODIFIED CONTENT ===\n{modifiedContent}\n=== END ===");
    }

    [Test]
    public async Task InsertAtLine_WithMixedTabSpaceContext_ShouldNormalizeToSpaces()
    {
        // Arrange: Create content with mixed tabs and spaces to test normalization
        var originalContent = @"class Test
{
    public string Property1 { get; set; }
	public string Property2 { get; set; }  // Tab indented
    public string Property3 { get; set; }  // Space indented
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "MixedIndent.cs");

        // Act: Insert a new property between Property2 and Property3
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 5, // Insert before Property3
            Content = "public string InsertedProperty { get; set; }  // Should use normalized space indentation",
            PreserveIndentation = true,
            ContextLines = 2
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should succeed and maintain consistent indentation
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        var modifiedLines = modifiedContent.Split('\n');

        // The inserted line should use consistent space indentation (normalized from surrounding context)
        var insertedPropertyLine = modifiedLines.FirstOrDefault(line => line.Contains("InsertedProperty"));
        insertedPropertyLine.Should().NotBeNull();
        insertedPropertyLine!.Should().StartWith("    ", "Inserted line should use normalized space indentation");
        insertedPropertyLine.Should().NotStartWith("\t", "Inserted line should not use tab indentation");

        // Verify the inserted line is properly positioned
        var allPropertyLines = modifiedLines
            .Where(line => line.Contains("public string"))
            .ToList();

        allPropertyLines.Should().HaveCount(4, "Should have 4 property lines after insertion");
        
        // Check that InsertedProperty appears in the right position
        var insertedIndex = allPropertyLines.FindIndex(line => line.Contains("InsertedProperty"));
        insertedIndex.Should().Be(2, "InsertedProperty should be at index 2 (between Property2 and Property3)");
    }

    [Test]
    public async Task InsertAtLine_AtEndOfClass_ShouldDetectIndentationFromNearbyLines()
    {
        // Arrange: Test insertion at end of class where target line doesn't exist
        var originalContent = @"public class TestClass
{
    public void Method1()
    {
        Console.WriteLine(""Hello"");
    }

    public void Method2()
    {
        Console.WriteLine(""World"");
    }
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "TestClass.cs");

        // Act: Insert a new method at the end of the class (before closing brace)
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 12, // Insert before the closing brace
                        Content = @"
            public void Method3()
            {
                Console.WriteLine(""Added method"");
            }",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        var modifiedLines = modifiedContent.Split('\n');

        // Verify the new method has proper class-level indentation
        var method3Line = modifiedLines.FirstOrDefault(line => line.Contains("Method3"));
        method3Line.Should().NotBeNull();
        method3Line!.Should().StartWith("    ", "Method3 should have proper class-level indentation (4 spaces)");

                        // Verify the method body has proper indentation
                        var consoleLine = modifiedLines.FirstOrDefault(line => line.Contains("Added method"));
                        consoleLine.Should().NotBeNull();
                        consoleLine!.Should().StartWith("        ", "Method body should have proper method-level indentation (8 spaces)");
    }
        [Test]
        public async Task InsertAtLine_InsertingMethodsNearClassEnd_ShouldMaintainClassBoundaries()
        {
            // Arrange: This test reproduces the boundary violation issue where methods were inserted outside the class
            var originalContent = @"public class ResponseBuilder
    {
        private readonly ILogger _logger;
        
        public ResponseBuilder(ILogger logger)
        {
            _logger = logger;
        }
        
        public string BuildSummary(object data)
        {
            return $""Summary for {data}"";
        }
    }";

            var testFile = await _fileManager.CreateTestFileAsync(originalContent, "ResponseBuilder.cs");

            // Act: Insert abstract method implementations near the end of the class
            // This simulates the exact scenario that caused the boundary violation
            var parameters = new EditLinesParameters
            {
                FilePath = testFile.FilePath,
                Operation = "insert",
                StartLine = 13, // Insert before the closing brace of the class
                Content = @"    
        /// <summary>
        /// Generate insights for the base response builder pattern
        /// </summary>
        protected virtual List<string> GenerateInsights(object data, string responseMode)
        {
            return new List<string> { ""Default insight"" };
        }",
                PreserveIndentation = true,
                ContextLines = 3
            };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert: The method should be inserted INSIDE the class, not outside
            result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

            var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
            Console.WriteLine($"=== MODIFIED CONTENT ===\n{modifiedContent}\n=== END ===");

            // Critical assertion: Verify the new method is inside the class boundaries
            var lines = modifiedContent.Split('\n', StringSplitOptions.None);
            
            // Find the GenerateInsights method
            var methodStartIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("GenerateInsights"))
                {
                    methodStartIndex = i;
                    break;
                }
            }
            
            methodStartIndex.Should().BeGreaterThan(-1, "GenerateInsights method should be found in the file");
            
            // Find the class closing brace
            var classEndIndex = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Trim() == "}")
                {
                    classEndIndex = i;
                    break;
                }
            }
            
            classEndIndex.Should().BeGreaterThan(-1, "Class closing brace should be found");
            
            // CRITICAL: The method should be positioned BEFORE the class closing brace
            methodStartIndex.Should().BeLessThan(classEndIndex, 
                "GenerateInsights method must be inside the class boundaries (before the closing brace)");
                
            // Verify proper indentation - class-level methods should have 4-space indentation
            var methodLine = lines[methodStartIndex];
            methodLine.Trim().Should().StartWith("protected virtual", 
                "Method declaration should be properly formatted");
            methodLine.Should().StartWith("    ", 
                "Method should have proper class-level indentation (4 spaces)");
                
            // Verify the method body is also properly indented
            var returnLine = lines.FirstOrDefault(line => line.Contains("return new List<string>"));
            returnLine.Should().NotBeNull("Method body should contain the return statement");
            returnLine!.Should().StartWith("        ", 
                "Method body should have proper method-level indentation (8 spaces)");
        }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}
