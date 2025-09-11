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
/// Quick test to demonstrate the specific insert_at_line bug that corrupts files
/// </summary>
[TestFixture]
public class QuickInsertAtLineBugTest : CodeSearchToolTestBase<InsertAtLineTool>
{
    private TestFileManager _fileManager = null!;
    private InsertAtLineTool _tool = null!;

    protected override InsertAtLineTool CreateTool()
    {
        _tool = new InsertAtLineTool(
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
    public async Task InsertAtLine_LargeContentInClass_ShouldMaintainClassStructure()
    {
        // Arrange: Simple class that should stay valid after large insertion
                        var originalContent = @"class BigTest
                {
                    public void Start() { }
                }" + "\n";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "BigTest.cs");
        
        // Act: Insert 100 methods (not 1000 to reduce test time)
        var contentToInsert = "";
        for (int i = 0; i < 100; i++)
        {
            contentToInsert += $"    public void GeneratedMethod{i}() {{ Console.WriteLine(\"Method {i}\"); }}\n";
        }

        var parameters = new InsertAtLineParameters
        {
            FilePath = testFile.FilePath,
            LineNumber = 3, // Insert before closing brace
            Content = contentToInsert,
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should maintain valid class structure
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        Console.WriteLine("=== MODIFIED CONTENT (first 500 chars) ===");
        Console.WriteLine(modifiedContent.Substring(0, Math.Min(500, modifiedContent.Length)));
        Console.WriteLine("=== MODIFIED CONTENT (last 200 chars) ===");
        Console.WriteLine(modifiedContent.Substring(Math.Max(0, modifiedContent.Length - 200)));
        Console.WriteLine("=== END ===");
        
        // The key assertion that's failing: file should end properly
        modifiedContent.Should().StartWith("class BigTest");
        modifiedContent.Should().EndWith("}\n", "Class should end with proper closure");
        
        // Should contain all generated methods
        modifiedContent.Should().Contain("GeneratedMethod0");
        modifiedContent.Should().Contain("GeneratedMethod99");
        modifiedContent.Should().Contain("public void Start()");
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}
