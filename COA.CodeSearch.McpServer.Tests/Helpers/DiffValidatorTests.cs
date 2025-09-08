using NUnit.Framework;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Comprehensive tests for DiffValidator to ensure accurate before/after validation.
/// </summary>
[TestFixture]
public class DiffValidatorTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"diffvalidator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public void ValidateEdit_IdenticalFiles_ShouldBeValid()
    {
        // Arrange
        var content = "Line 1\nLine 2\nLine 3\n";
        var originalFile = CreateTestFile("original.txt", content);
        var modifiedFile = CreateTestFile("modified.txt", content);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType>()
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Violations, Is.Empty);
        Assert.That(result.ChangeSummary, Is.Empty);
        Assert.That(result.Metrics!.OriginalLineCount, Is.EqualTo(3));
        Assert.That(result.Metrics.ModifiedLineCount, Is.EqualTo(3));
        Assert.That(result.Metrics.NetLineChange, Is.EqualTo(0));
    }

    [Test]
    public void ValidateEdit_SingleLineAddition_ShouldDetectCorrectly()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3\n";
        var modifiedContent = "Line 1\nLine 2\nNew Line\nLine 3\n";
        
        var originalFile = CreateTestFile("original.txt", originalContent);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition },
            TargetLineRange = (1, 4)
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ChangeSummary, Has.Count.EqualTo(1));
        Assert.That(result.ChangeSummary[0].Type, Is.EqualTo(ChangeType.Addition));
        Assert.That(result.ChangeSummary[0].Content, Is.EqualTo("New Line"));
        Assert.That(result.Metrics!.LinesAdded, Is.EqualTo(1));
        Assert.That(result.Metrics.NetLineChange, Is.EqualTo(1));
    }

    [Test]
    public void ValidateEdit_SingleLineDeletion_ShouldDetectCorrectly()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3\nLine 4\n";
        var modifiedContent = "Line 1\nLine 3\nLine 4\n";
        
        var originalFile = CreateTestFile("original.txt", originalContent);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Deletion },
            TargetLineRange = (1, 4)
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ChangeSummary, Has.Count.EqualTo(1));
        Assert.That(result.ChangeSummary[0].Type, Is.EqualTo(ChangeType.Deletion));
        Assert.That(result.ChangeSummary[0].Content, Is.EqualTo("Line 2"));
        Assert.That(result.Metrics!.LinesRemoved, Is.EqualTo(1));
        Assert.That(result.Metrics.NetLineChange, Is.EqualTo(-1));
    }

    [Test]
    public void ValidateEdit_EncodingChange_ShouldDetectViolation()
    {
        // Arrange
        var content = "Test content with special chars: café";
        var originalFile = CreateTestFile("original.txt", content, Encoding.UTF8);
        var modifiedFile = CreateTestFile("modified.txt", content, Encoding.Unicode);

        var expectation = new EditExpectation
        {
            RequireEncodingPreservation = true
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Violations, Has.Count.EqualTo(1));
        Assert.That(result.Violations[0].Type, Is.EqualTo(DiffViolationType.EncodingMismatch));
        TestContext.Out.WriteLine($"Encoding violation: {result.Violations[0].Message}");
    }

    [Test]
    public void ValidateEdit_LineEndingChange_ShouldDetectViolation()
    {
        // Arrange
        var originalContent = "Line 1\r\nLine 2\r\nLine 3\r\n"; // Windows CRLF
        var modifiedContent = "Line 1\nLine 2\nLine 3\n"; // Unix LF
        
        var originalFile = CreateTestFile("original.txt", originalContent);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent);

        var expectation = new EditExpectation
        {
            RequireLineEndingPreservation = true
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Violations, Has.Count.EqualTo(1));
        Assert.That(result.Violations[0].Type, Is.EqualTo(DiffViolationType.LineEndingMismatch));
        TestContext.Out.WriteLine($"Line ending violation: {result.Violations[0].Message}");
    }

    [Test]
    public void ValidateEdit_UnexpectedChangeOutsideTargetRange_ShouldDetectViolation()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5\n";
        var modifiedContent = "Line 1\nLine 2\nModified Line 3\nLine 4\nModified Line 5\n";
        
        var originalFile = CreateTestFile("original.txt", originalContent);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Modification },
            TargetLineRange = (3, 3) // Only line 3 should be modified
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Violations, Has.Count.GreaterThan(0));
        var unexpectedChanges = result.Violations.Where(v => v.Type == DiffViolationType.UnexpectedChange).ToList();
        Assert.That(unexpectedChanges, Has.Count.GreaterThan(0));
        TestContext.Out.WriteLine($"Unexpected changes detected: {unexpectedChanges.Count}");
    }

    [Test]
    public void ValidateEdit_UnicodeContent_ShouldPreserveCorrectly()
    {
        // Arrange
        var originalContent = "English text\nText with émojis: 🚀 🎉 ñáéíóú\nMore content\n";
        var modifiedContent = "English text\nNew line inserted\nText with émojis: 🚀 🎉 ñáéíóú\nMore content\n";
        
        var originalFile = CreateTestFile("original.txt", originalContent, Encoding.UTF8);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent, Encoding.UTF8);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ChangeSummary, Has.Count.EqualTo(1));
        Assert.That(result.ChangeSummary[0].Type, Is.EqualTo(ChangeType.Addition));
        Assert.That(result.ChangeSummary[0].Content, Is.EqualTo("New line inserted"));
    }

    [Test]
    public void ValidateEdit_FileWithoutTrailingNewline_ShouldHandleCorrectly()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3"; // No trailing newline
        var modifiedContent = "Line 1\nLine 2\nLine 3\nLine 4"; // No trailing newline
        
        var originalFile = CreateTestFileRaw("original.txt", Encoding.UTF8.GetBytes(originalContent));
        var modifiedFile = CreateTestFileRaw("modified.txt", Encoding.UTF8.GetBytes(modifiedContent));

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Metrics!.OriginalLineCount, Is.EqualTo(3));
        Assert.That(result.Metrics.ModifiedLineCount, Is.EqualTo(4));
        Assert.That(result.ChangeSummary, Has.Count.EqualTo(1));
        Assert.That(result.ChangeSummary[0].Content, Is.EqualTo("Line 4"));
    }

    [Test]
    public void GenerateDiffReport_WithViolations_ShouldCreateDetailedReport()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3\n";
        var modifiedContent = "Line 1\nModified Line 2\nLine 3\nExtra Line\n";
        
        var originalFile = CreateTestFile("original.txt", originalContent);
        var modifiedFile = CreateTestFile("modified.txt", modifiedContent, Encoding.Unicode); // Different encoding

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Modification },
            TargetLineRange = (2, 2)
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        var report = DiffValidator.GenerateDiffReport(result);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(report, Does.Contain("=== DIFF VALIDATION REPORT ==="));
        Assert.That(report, Does.Contain("❌ INVALID"));
        Assert.That(report, Does.Contain("=== VIOLATIONS ==="));
        Assert.That(report, Does.Contain("=== METRICS ==="));
        Assert.That(report, Does.Contain("=== CHANGE SUMMARY ==="));
        
        TestContext.Out.WriteLine("=== GENERATED DIFF REPORT ===");
        TestContext.Out.WriteLine(report);
    }

    [Test]
    public void ValidateEdit_ComplexMultiLineEdit_ShouldValidateAccurately()
    {
        // Arrange - Simulate a realistic code editing scenario with clear differences
        var originalContent = "class TestClass\n{\n    public void Method1()\n    {\n        Console.WriteLine(\"Original\");\n    }\n    \n    public void Method2()\n    {\n        // TODO: Implement\n    }\n}\n";

        var modifiedContent = "class TestClass\n{\n    public void Method1()\n    {\n        Console.WriteLine(\"Modified\");\n        Console.WriteLine(\"Additional line\");\n    }\n    \n    public void Method2()\n    {\n        Console.WriteLine(\"Implemented!\");\n    }\n}\n";
        
        var originalFile = CreateTestFile("TestClass_original.cs", originalContent);
        var modifiedFile = CreateTestFile("TestClass_modified.cs", modifiedContent);

        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> 
            { 
                ChangeType.Modification, 
                ChangeType.Addition,
                ChangeType.Deletion
            }
        };

        // Act
        var result = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        var report = DiffValidator.GenerateDiffReport(result);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ChangeSummary, Has.Count.GreaterThan(0));
        
        TestContext.Out.WriteLine("=== COMPLEX EDIT VALIDATION ===");
        TestContext.Out.WriteLine(report);
    }

    private string CreateTestFile(string fileName, string content, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, content, encoding);
        return filePath;
    }

    private string CreateTestFileRaw(string fileName, byte[] bytes)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }
}