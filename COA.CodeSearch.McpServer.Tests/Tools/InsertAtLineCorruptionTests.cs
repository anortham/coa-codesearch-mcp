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
using System.Text;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Tests that attempt to reproduce catastrophic insert_at_line failures that corrupt files so badly they need to be deleted
/// These are the "mess so bad you have to start over" scenarios
/// </summary>
[TestFixture]
public class InsertAtLineCorruptionTests : CodeSearchToolTestBase<EditLinesTool>
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
    public async Task InsertAtLine_BeyondFileEnd_ShouldNotCorruptFile()
    {
        // Arrange: Small file, attempt to insert way beyond the end
        var originalContent = @"class Test
{
    public void Method() { }
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "Test.cs");
        
        // Act: Try to insert at line 100 when file only has 4 lines
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 100, // Way beyond file end
            Content = "public void NewMethod() { }",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should either fail gracefully OR handle it correctly, but not corrupt
        if (result.Success)
        {
            // If it succeeds, the file should still be valid C#
            var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
            modifiedContent.Should().Contain("class Test");
            modifiedContent.Should().Contain("public void Method()");
            
            // Should not create malformed structure
            modifiedContent.Should().NotContain("}{"); // Common corruption pattern
            modifiedContent.Should().NotMatch(@"}\s*public void NewMethod"); // Method outside class
        }
        else
        {
            // If it fails, original file should be unchanged
            var unchangedContent = await File.ReadAllTextAsync(testFile.FilePath);
            unchangedContent.Should().Be(originalContent);
        }
        
        // Debug output to see what actually happened
        var finalContent = await File.ReadAllTextAsync(testFile.FilePath);
        Console.WriteLine($"=== FINAL CONTENT ===\n{finalContent}\n=== END ===");
        Console.WriteLine($"Result Success: {result.Success}");
        if (!result.Success && result.Error != null)
        {
            Console.WriteLine($"Error: {result.Error.Message}");
        }
    }

    [Test]
    public async Task InsertAtLine_LargeContentWithSpecialCharacters_ShouldNotCorruptFile()
    {
        // Arrange: Try to trigger encoding/corruption issues with special content
        var originalContent = @"using System;

public class UnicodeTest
{
    public string Test() => ""Hello"";
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "UnicodeTest.cs");
        
        // Act: Insert content with problematic characters that might cause corruption
        var problematicContent = @"    // 测试 unicode content with emoji 🚀
    // Special chars: ""quotes"" 'apostrophes' <brackets> & ampersands
    // Potentially problematic: \r\n\t null chars and control sequences
    public void ProblematicMethod()
    {
        var data = @""This is a verbatim string with 
multiple lines and ""quotes"" inside"";
        Console.WriteLine($""Template {data} with interpolation"");
    }";

        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 4, // Insert before the closing brace
            Content = problematicContent,
            PreserveIndentation = true,
            ContextLines = 5
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: File should remain valid even with special characters
        result.Success.Should().BeTrue($"Insert operation failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        // Should still be valid C# structure
        modifiedContent.Should().Contain("public class UnicodeTest");
        modifiedContent.Should().Contain("public string Test()");
        modifiedContent.Should().Contain("ProblematicMethod");
        
        // Should preserve unicode content
        modifiedContent.Should().Contain("测试 unicode");
        modifiedContent.Should().Contain("🚀");
        
        // Should not have corrupted the class structure
        var lines = modifiedContent.Split('\n');
        var openBraces = lines.Count(line => line.Contains('{'));
        var closeBraces = lines.Count(line => line.Contains('}'));
        openBraces.Should().Be(closeBraces, "Braces should be balanced after insertion");
        
        Console.WriteLine($"=== MODIFIED CONTENT ===\n{modifiedContent}\n=== END ===");
    }

    [Test]
    [Ignore("Replaced by NewlinePreservationRealWorldTests and ComprehensiveEditingValidationTests")]
    public async Task InsertAtLine_VeryLargeContent_ShouldNotCauseMemoryIssues()
    {
        // Arrange: Small file, huge content insertion
        var originalContent = @"class BigTest
{
    public void Start() { }
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "BigTest.cs");
        
        // Act: Insert massive content that might cause memory/performance issues
        var hugeContent = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            hugeContent.AppendLine($"    public void GeneratedMethod{i}() {{ Console.WriteLine(\"Method {i}\"); }}");
        }

        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 3, // Insert before closing brace
            Content = hugeContent.ToString(),
            PreserveIndentation = true,
            ContextLines = 3
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
        stopwatch.Stop();

        // Assert: Should handle large content without corruption or excessive time
        result.Success.Should().BeTrue($"Large content insert failed: {result.Error?.Message}");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Large insert should complete within 5 seconds");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        // Should contain all generated methods
        modifiedContent.Should().Contain("GeneratedMethod0");
        modifiedContent.Should().Contain("GeneratedMethod999");
        modifiedContent.Should().Contain("public void Start()");
        
        // Should still be structurally valid
        modifiedContent.Should().StartWith("class BigTest");
        modifiedContent.Should().EndWith("}\n") ; // Proper class closure
        
        Console.WriteLine($"Large content insert took: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Final file size: {modifiedContent.Length} characters");
    }

    [Test]
    public async Task InsertAtLine_NestedClassBoundaries_ShouldMaintainStructure()
    {
        // Arrange: Complex nested class structure that might confuse boundary detection
        var originalContent = @"namespace TestNamespace
{
    public class OuterClass
    {
        public class InnerClass
        {
            public void InnerMethod() { }
        }
        
        public void OuterMethod() { }
    }
    
    public class SiblingClass
    {
        public void SiblingMethod() { }
    }
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "NestedTest.cs");
        
        // Act: Insert into the inner class
        var parameters = new EditLinesParameters
        {
            FilePath = testFile.FilePath,
                Operation = "insert",
            StartLine = 7, // After InnerMethod, before inner class closing brace
            Content = @"            
            public void NewInnerMethod()
            {
                Console.WriteLine(""Added to inner class"");
            }",
            PreserveIndentation = true,
            ContextLines = 3
        };

        var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: Should maintain proper nesting structure
        result.Success.Should().BeTrue($"Nested insert failed: {result.Error?.Message}");

        var modifiedContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        // Should preserve all original methods
        modifiedContent.Should().Contain("InnerMethod");
        modifiedContent.Should().Contain("OuterMethod");
        modifiedContent.Should().Contain("SiblingMethod");
        modifiedContent.Should().Contain("NewInnerMethod");
        
        // Should maintain proper nesting - NewInnerMethod should be inside InnerClass
        var lines = modifiedContent.Split('\n').Select((line, index) => new { line, index }).ToList();
        
        var innerClassStart = lines.First(x => x.line.Contains("public class InnerClass")).index;
        var innerClassEnd = -1;
        var braceCount = 0;
        for (int i = innerClassStart; i < lines.Count; i++)
        {
            braceCount += lines[i].line.Count(c => c == '{');
            braceCount -= lines[i].line.Count(c => c == '}');
            if (braceCount == 0 && i > innerClassStart)
            {
                innerClassEnd = i;
                break;
            }
        }
        
        var newMethodLine = lines.FirstOrDefault(x => x.line.Contains("NewInnerMethod"))?.index ?? -1;
        
        newMethodLine.Should().BeGreaterThan(innerClassStart, "NewInnerMethod should be after InnerClass start");
        newMethodLine.Should().BeLessThan(innerClassEnd, "NewInnerMethod should be before InnerClass end");
        
        Console.WriteLine($"=== NESTED STRUCTURE ===\n{modifiedContent}\n=== END ===");
    }

    [Test]
    public async Task InsertAtLine_ConcurrentOperations_ShouldNotCorruptFile()
    {
        // Arrange: Try to trigger race conditions with simultaneous inserts
        var originalContent = @"class ConcurrentTest
{
    public void Method1() { }
    public void Method2() { }
    public void Method3() { }
}";

        var testFile = await _fileManager.CreateTestFileAsync(originalContent, "ConcurrentTest.cs");
        
        // Act: Attempt multiple simultaneous insertions (this might expose threading issues)
        var tasks = new List<Task<COA.Mcp.Framework.TokenOptimization.Models.AIOptimizedResponse<COA.CodeSearch.McpServer.Models.EditLinesResult>>>();
        
        for (int i = 0; i < 5; i++)
        {
            var methodNumber = i;
            var task = Task.Run(async () =>
            {
                var parameters = new EditLinesParameters
                {
                    FilePath = testFile.FilePath,
                Operation = "insert",
                    StartLine = 3 + methodNumber, // Different lines
                    Content = $"    public void ConcurrentMethod{methodNumber}() {{ /* Added concurrently */ }}",
                    PreserveIndentation = true,
                    ContextLines = 2
                };

                return await _tool.ExecuteAsync(parameters, CancellationToken.None);
            });
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert: At least some operations should succeed, and file should not be corrupted
        var successCount = results.Count(r => r.Success);
        successCount.Should().BeGreaterThan(0, "At least some concurrent operations should succeed");

        var finalContent = await File.ReadAllTextAsync(testFile.FilePath);
        
        // File should still be valid C#
        finalContent.Should().Contain("class ConcurrentTest");
        finalContent.Should().Contain("Method1");
        finalContent.Should().Contain("Method2");
        finalContent.Should().Contain("Method3");
        
        // Should not have completely mangled structure
        finalContent.Should().Match("*class ConcurrentTest*{*}*", "Should maintain basic class structure");
        
        Console.WriteLine($"=== CONCURRENT RESULT ===");
        Console.WriteLine($"Successful operations: {successCount}/5");
        Console.WriteLine($"Final content:\n{finalContent}");
        Console.WriteLine($"=== END ===");
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}