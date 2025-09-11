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

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Test ReplaceLinesTool for the same newline bug found in InsertAtLineTool
/// </summary>
[TestFixture]
public class ReplaceLinesNewlineBugTest : CodeSearchToolTestBase<ReplaceLinesTool>
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
    [Ignore("Replaced by NewlinePreservationRealWorldTests and ComprehensiveEditingValidationTests")]
    public async Task ReplaceLines_LargeReplacement_ShouldNotAddExtraNewlines()
    {
        // Arrange: Simple class that should maintain exact newline format
                    var originalContent = @"class TestClass
            {
                public void Method1() { }
                public void OldMethod() { }
                public void Method2() { }
            }" + "\n";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "ReplaceTest.cs");
        
        // Act: Replace line 4 (OldMethod) with large content
        var largeReplacement = "";
        for (int i = 0; i < 50; i++)
        {
            largeReplacement += $"    public void GeneratedMethod{i}() {{ Console.WriteLine(\"Method {i}\"); }}\n";
        }

        var parameters = new ReplaceLinesParameters
        {
            FilePath = testFile.FilePath,
            StartLine = 4,
            Content = largeReplacement,
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Check for the same newline bug as InsertAtLineTool
        result.Success.Should().BeTrue($"Replace operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        Console.WriteLine("=== REPLACE TOOL - LAST 100 CHARS ===");
        Console.WriteLine($"'{modifiedContent.Substring(Math.Max(0, modifiedContent.Length - 100))}'");
        Console.WriteLine("=== END ===");
        
        // Key test: Should end with exactly "}\n" not "}\n\n"
        modifiedContent.Should().EndWith("}\n", "ReplaceLinesTool should not add extra newlines like InsertAtLineTool does");
        modifiedContent.Should().NotEndWith("}\n\n", "Should not have the same newline bug as InsertAtLineTool");
        
        // Verify replacement worked
        modifiedContent.Should().Contain("GeneratedMethod0");
        modifiedContent.Should().Contain("GeneratedMethod49");
        modifiedContent.Should().NotContain("OldMethod", "Original content should be replaced");
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}
