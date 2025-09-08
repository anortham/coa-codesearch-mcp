using NUnit.Framework;
using COA.CodeSearch.McpServer.Tests.Helpers;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Integration;

/// <summary>
/// Tests that validate line ending preservation capabilities using DiffValidator.
/// These tests ensure cross-platform compatibility and prevent line ending corruption.
/// Critical for teams working across Windows/Unix environments.
/// </summary>
[TestFixture]
public class LineEndingPreservationTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"lineending_test_{Guid.NewGuid():N}");
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
    public void DiffValidator_WindowsCRLFPreservation_ShouldValidateCorrectly()
    {
        // Arrange - Create files with Windows CRLF line endings
        var originalContent = "Line 1\r\nLine 2\r\nLine 3\r\n";
        var modifiedContent = "Line 1\r\nModified Line 2\r\nLine 3\r\n";
        
        var originalFile = CreateTestFileWithLineEndings("crlf_original.cs", originalContent);
        var modifiedFile = CreateTestFileWithLineEndings("crlf_modified.cs", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Modification, ChangeType.Addition, ChangeType.Deletion },
            RequireLineEndingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        Assert.That(validation.Violations, Is.Empty, "Should have no line ending violations");
        
        // Verify CRLF preservation
        var modifiedBytes = File.ReadAllBytes(modifiedFile);
        var modifiedText = Encoding.UTF8.GetString(modifiedBytes);
        Assert.That(modifiedText, Does.Contain("\r\n"), "Should preserve CRLF line endings");
        Assert.That(modifiedText, Does.Not.Contain("\n\r"), "Should not have reversed line endings");
        Assert.That(modifiedText.Split('\n').Length, Is.EqualTo(4), "Should have correct number of lines"); // 3 lines + final empty
    }

    [Test]
    public void DiffValidator_UnixLFPreservation_ShouldValidateCorrectly()
    {
        // Arrange - Create files with Unix LF line endings
        var originalContent = "Line 1\nLine 2\nLine 3\n";
        var modifiedContent = "Line 1\nModified Line 2\nLine 3\nAdded Line 4\n";
        
        var originalFile = CreateTestFileWithLineEndings("lf_original.py", originalContent);
        var modifiedFile = CreateTestFileWithLineEndings("lf_modified.py", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Modification, ChangeType.Deletion },
            RequireLineEndingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify LF preservation
        var modifiedBytes = File.ReadAllBytes(modifiedFile);
        var modifiedText = Encoding.UTF8.GetString(modifiedBytes);
        Assert.That(modifiedText, Does.Not.Contain("\r"), "Should not contain carriage returns");
        Assert.That(modifiedText, Does.Contain("\n"), "Should contain Unix line feeds");
    }

    [Test]
    public void DiffValidator_LineEndingMismatch_ShouldDetectViolation()
    {
        // Arrange - Create files with different line endings to test violation detection
        var content = "Line 1\nLine 2\nLine 3";
        var originalFile = CreateTestFileWithLineEndings("lf_original.js", content + "\n");      // LF endings
        var modifiedFile = CreateTestFileWithLineEndings("crlf_modified.js", content + "\r\n");  // CRLF endings
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Modification, ChangeType.Deletion },
            RequireLineEndingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert - Should detect line ending violation
        Assert.That(validation.IsValid, Is.False, "Should detect line ending mismatch");
        Assert.That(validation.Violations, Has.Count.GreaterThan(0));
        Assert.That(validation.Violations.Any(v => v.Type == DiffViolationType.LineEndingMismatch), Is.True);
        
        var report = DiffValidator.GenerateDiffReport(validation);
        TestContext.Out.WriteLine(report);
        Assert.That(report, Does.Contain("Line ending").IgnoreCase, "Report should mention line ending issue");
    }

    [Test]
    public void DiffValidator_MixedLineEndingsConsistency_ShouldValidateCorrectly()
    {
        // Arrange - Files with consistent mixed line endings (edge case but should be preserved)
        var originalContent = "Line 1\r\nLine 2\nLine 3\r\n";  // Mixed but consistent pattern
        var modifiedContent = "Line 1\r\nModified Line 2\nLine 3\r\nNew Line 4\r\n"; // Same mixed pattern
        
        var originalFile = CreateTestFileWithLineEndings("mixed_original.txt", originalContent);
        var modifiedFile = CreateTestFileWithLineEndings("mixed_modified.txt", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Modification, ChangeType.Deletion },
            RequireLineEndingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify the mixed pattern is preserved
        var modifiedText = File.ReadAllText(modifiedFile);
        Assert.That(modifiedText, Does.Contain("\r\n"), "Should preserve CRLF where they existed");
        Assert.That(modifiedText, Does.Contain("Line 2\nLine 3"), "Should preserve LF where it existed");
    }

    [Test]
    public void DiffValidator_NoTrailingNewlinePreservation_ShouldValidateCorrectly()
    {
        // Arrange - Files without trailing newlines (common in some codebases)
        var originalContent = "Line 1\nLine 2\nLine 3"; // No trailing newline
        var modifiedContent = "Line 1\nModified Line 2\nLine 3"; // Still no trailing newline
        
        var originalFile = CreateTestFileWithLineEndings("no_newline_original.md", originalContent);
        var modifiedFile = CreateTestFileWithLineEndings("no_newline_modified.md", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Modification, ChangeType.Addition, ChangeType.Deletion },
            RequireLineEndingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify no trailing newline is preserved
        var modifiedText = File.ReadAllText(modifiedFile);
        Assert.That(modifiedText, Does.Not.EndWith("\n"), "Should not add trailing newline");
        Assert.That(modifiedText, Does.Not.EndWith("\r\n"), "Should not add trailing CRLF");
    }

    private string CreateTestFileWithLineEndings(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        // Write as bytes to preserve exact line endings
        var bytes = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private string DetectLineEnding(string filePath)
    {
        var content = File.ReadAllText(filePath);
        if (content.Contains("\r\n")) return "CRLF";
        if (content.Contains("\n")) return "LF";
        if (content.Contains("\r")) return "CR";
        return "None";
    }
}