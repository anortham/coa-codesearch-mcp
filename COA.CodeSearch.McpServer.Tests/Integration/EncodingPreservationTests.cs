using NUnit.Framework;
using COA.CodeSearch.McpServer.Tests.Helpers;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Integration;

/// <summary>
/// Tests that validate encoding preservation capabilities using DiffValidator.
/// These tests focus on the validation framework itself and core encoding scenarios.
/// </summary>
[TestFixture]
public class EncodingPreservationTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"encoding_test_{Guid.NewGuid():N}");
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
    public void DiffValidator_UTF8FilesWithPreservation_ShouldValidateCorrectly()
    {
        // Arrange - Create two UTF-8 files with same encoding
        var originalContent = "// Café application\nusing System;\nnamespace Test\n{\n    class Program { }\n}";
        var modifiedContent = "// Café application\n// Added line: ñáéíóú 🚀\nusing System;\nnamespace Test\n{\n    class Program { }\n}";
        
        var originalFile = CreateTestFile("utf8_original.cs", originalContent, Encoding.UTF8);
        var modifiedFile = CreateTestFile("utf8_modified.cs", modifiedContent, Encoding.UTF8);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition },
            RequireEncodingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        Assert.That(validation.Violations, Is.Empty, "Should have no encoding violations");
        Assert.That(validation.ChangeSummary, Has.Count.EqualTo(1));
        Assert.That(validation.ChangeSummary[0].Type, Is.EqualTo(ChangeType.Addition));
    }

    [Test]
    public void DiffValidator_UTF16EncodingMismatch_ShouldDetectViolation()
    {
        // Arrange - Create files with different encodings to test violation detection
        var content = "// Application título\nusing System;\nnamespace Test { }";
        var originalFile = CreateTestFile("utf16_original.cs", content, Encoding.Unicode);
        var modifiedFile = CreateTestFile("utf8_modified.cs", content, Encoding.UTF8);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Deletion, ChangeType.Modification },
            RequireEncodingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert - Should detect encoding violation
        Assert.That(validation.IsValid, Is.False, "Should detect encoding mismatch");
        Assert.That(validation.Violations, Has.Count.GreaterThan(0));
        Assert.That(validation.Violations.Any(v => v.Type == DiffViolationType.EncodingMismatch), Is.True);
        
        var report = DiffValidator.GenerateDiffReport(validation);
        TestContext.Out.WriteLine(report);
    }

    [Test]
    public void DiffValidator_UTF8BOMPreservation_ShouldValidateCorrectly()
    {
        // Arrange - Create UTF-8 with BOM files
        var originalContent = "// Développeur application\nconst message = 'Héllo Wörld';\nconsole.log(message);";
        var modifiedContent = "// Développeur application\nconst message = '¡Hola Mundo!';\nconsole.log(message);";
        
        var originalFile = CreateTestFileWithBOM("utf8bom_original.js", originalContent);
        var modifiedFile = CreateTestFileWithBOM("utf8bom_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Modification, ChangeType.Addition, ChangeType.Deletion },
            RequireEncodingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify UTF-8 BOM preservation
        var originalBytes = File.ReadAllBytes(originalFile);
        var modifiedBytes = File.ReadAllBytes(modifiedFile);
        
        Assert.That(originalBytes[0], Is.EqualTo(0xEF), "Original should have UTF-8 BOM");
        Assert.That(modifiedBytes[0], Is.EqualTo(0xEF), "Modified should preserve UTF-8 BOM");
    }

    [Test]
    public void DiffValidator_MixedUnicodeContent_ShouldHandleCorrectly()
    {
        // Arrange - File with various Unicode ranges
        var originalContent = @"// File with mixed Unicode: русский, 中文, 日本語, العربية, עברית
using System;

namespace UnicodeTest
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine(""Hello 🌍 World! Café 📚"");
        }
    }
}";
        var modifiedContent = originalContent + "\n// Added Greek: Γειά σου κόσμε! 🎉 Hindi: नमस्ते दुनिया!";
        
        var originalFile = CreateTestFile("unicode_original.cs", originalContent, Encoding.UTF8);
        var modifiedFile = CreateTestFile("unicode_modified.cs", modifiedContent, Encoding.UTF8);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition },
            RequireEncodingPreservation = true
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify content integrity
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("русский"), "Should preserve Russian text");
        Assert.That(modifiedText, Does.Contain("中文"), "Should preserve Chinese text");
        Assert.That(modifiedText, Does.Contain("🌍"), "Should preserve emoji");
        Assert.That(modifiedText, Does.Contain("Γειά σου"), "Should preserve added Greek text");
        Assert.That(modifiedText, Does.Contain("नमस्ते"), "Should preserve added Hindi text");
    }

    private string CreateTestFile(string fileName, string content, Encoding encoding)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, content, encoding);
        return filePath;
    }

    private string CreateTestFileWithBOM(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        // Create UTF-8 with BOM explicitly
        var utf8WithBOM = new UTF8Encoding(true);
        File.WriteAllText(filePath, content, utf8WithBOM);
        return filePath;
    }

    private Encoding DetectFileEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE
        
        // Try to detect encoding by content
        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            Encoding.UTF8.GetBytes(text); // Will throw if invalid UTF-8
            return Encoding.UTF8;
        }
        catch
        {
            return Encoding.ASCII; // Fallback
        }
    }
}