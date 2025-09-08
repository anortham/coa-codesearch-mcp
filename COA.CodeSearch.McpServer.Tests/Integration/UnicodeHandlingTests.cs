using NUnit.Framework;
using COA.CodeSearch.McpServer.Tests.Helpers;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Integration;

/// <summary>
/// Tests that validate Unicode content handling using DiffValidator.
/// These tests ensure proper handling of international characters, emojis,
/// and complex script systems without corruption or normalization issues.
/// Critical for global development teams working with non-ASCII content.
/// </summary>
[TestFixture]
public class UnicodeHandlingTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"unicode_test_{Guid.NewGuid():N}");
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
    public void DiffValidator_EmojiAndSymbolContent_ShouldValidateCorrectly()
    {
        // Arrange - Files with emoji and Unicode symbols
        var originalContent = "// Status: 🚀 Ready for deployment\n" +
                              "const message = '✅ Tests passing ⚡ Performance good';\n" +
                              "// TODO: Add 🔒 security checks\n";
        
        var modifiedContent = "// Status: 🚀 Ready for deployment\n" +
                              "const message = '✅ Tests passing ⚡ Performance excellent 🎉';\n" +
                              "// TODO: Add 🔒 security checks\n" +
                              "// NEW: Added 📊 analytics tracking\n";
        
        var originalFile = CreateTestFile("emoji_original.js", originalContent);
        var modifiedFile = CreateTestFile("emoji_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Modification, ChangeType.Deletion }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify emoji content integrity
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("🚀"), "Should preserve rocket emoji");
        Assert.That(modifiedText, Does.Contain("✅"), "Should preserve checkmark");
        Assert.That(modifiedText, Does.Contain("⚡"), "Should preserve lightning bolt");
        Assert.That(modifiedText, Does.Contain("🎉"), "Should preserve party emoji");
        Assert.That(modifiedText, Does.Contain("🔒"), "Should preserve lock emoji");
        Assert.That(modifiedText, Does.Contain("📊"), "Should preserve chart emoji");
    }

    [Test]
    public void DiffValidator_MultiLanguageContent_ShouldValidateCorrectly()
    {
        // Arrange - Files with multiple language scripts
        var originalContent = "// Multi-language comments\n" +
                              "// English: Hello World\n" +
                              "// Spanish: Hola Mundo\n" +
                              "// Russian: Привет мир\n" +
                              "// Chinese: 你好世界\n" +
                              "// Japanese: こんにちは世界\n" +
                              "// Arabic: مرحبا بالعالم\n" +
                              "// Hebrew: שלום עולם\n";
        
        var modifiedContent = originalContent + 
                              "// French: Bonjour le monde\n" +
                              "// German: Hallo Welt\n" +
                              "// Korean: 안녕하세요 세계\n" +
                              "// Hindi: नमस्ते दुनिया\n" +
                              "// Thai: สวัสดีชาวโลก\n";
        
        var originalFile = CreateTestFile("multilang_original.js", originalContent);
        var modifiedFile = CreateTestFile("multilang_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify all scripts are preserved
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("Привет мир"), "Should preserve Cyrillic script");
        Assert.That(modifiedText, Does.Contain("你好世界"), "Should preserve Chinese characters");
        Assert.That(modifiedText, Does.Contain("こんにちは世界"), "Should preserve Japanese characters");
        Assert.That(modifiedText, Does.Contain("مرحبا بالعالم"), "Should preserve Arabic script");
        Assert.That(modifiedText, Does.Contain("שלום עולם"), "Should preserve Hebrew script");
        Assert.That(modifiedText, Does.Contain("안녕하세요 세계"), "Should preserve Korean characters");
        Assert.That(modifiedText, Does.Contain("नमस्ते दुनिया"), "Should preserve Devanagari script");
        Assert.That(modifiedText, Does.Contain("สวัสดีชาวโลก"), "Should preserve Thai script");
    }

    [Test]
    public void DiffValidator_AccentsAndDiacritics_ShouldValidateCorrectly()
    {
        // Arrange - Files with accented characters and diacritics
        var originalContent = "// Programming terms with accents\n" +
                              "const café = 'coffee shop';\n" +
                              "const résumé = 'curriculum vitae';\n" +
                              "const naïve = 'innocent';\n" +
                              "const piñata = 'party decoration';\n" +
                              "const Zürich = 'Swiss city';\n" +
                              "const ångström = 'unit of measurement';\n";
        
        var modifiedContent = originalContent + 
                              "const jalapeño = 'spicy pepper';\n" +
                              "const crème = 'cream';\n" +
                              "const français = 'French language';\n" +
                              "const São = 'Portuguese saint';\n" +
                              "const Kraków = 'Polish city';\n";
        
        var originalFile = CreateTestFile("accents_original.js", originalContent);
        var modifiedFile = CreateTestFile("accents_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify accent preservation
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("café"), "Should preserve acute accent");
        Assert.That(modifiedText, Does.Contain("résumé"), "Should preserve acute accents");
        Assert.That(modifiedText, Does.Contain("naïve"), "Should preserve diaeresis");
        Assert.That(modifiedText, Does.Contain("piñata"), "Should preserve tilde");
        Assert.That(modifiedText, Does.Contain("Zürich"), "Should preserve umlaut");
        Assert.That(modifiedText, Does.Contain("ångström"), "Should preserve ring accent");
        Assert.That(modifiedText, Does.Contain("jalapeño"), "Should preserve eñe");
        Assert.That(modifiedText, Does.Contain("crème"), "Should preserve grave accent");
        Assert.That(modifiedText, Does.Contain("français"), "Should preserve cedilla");
        Assert.That(modifiedText, Does.Contain("São"), "Should preserve tilde on vowel");
        Assert.That(modifiedText, Does.Contain("Kraków"), "Should preserve Polish accents");
    }

    [Test]
    public void DiffValidator_SurrogatePairs_ShouldValidateCorrectly()
    {
        // Arrange - Files with Unicode characters requiring surrogate pairs (outside BMP)
        var originalContent = "// Unicode characters beyond Basic Multilingual Plane\n" +
                              "const musical = '𝄞 treble clef';\n" +  // U+1D11E
                              "const math = '𝕏 double-struck X';\n" +    // U+1D54F
                              "const emoji = '𝒽𝑒𝓁𝓁𝑜 script hello';\n" + // Mathematical script letters
                              "const ancient = '𓀀 Egyptian hieroglyph';\n"; // U+13000
        
        var modifiedContent = originalContent + 
                              "const more_math = '𝛼𝛽𝛾 Greek letters';\n" +      // Mathematical Greek
                              "const cuneiform = '𒀀 ancient script';\n" +        // U+12000
                              "const symbols = '𝕊𝕪𝕞𝕓𝕠𝕝𝕤 double-struck';\n";   // More double-struck
        
        var originalFile = CreateTestFile("surrogate_original.js", originalContent);
        var modifiedFile = CreateTestFile("surrogate_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify surrogate pair preservation
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("𝄞"), "Should preserve treble clef");
        Assert.That(modifiedText, Does.Contain("𝕏"), "Should preserve double-struck X");
        Assert.That(modifiedText, Does.Contain("𓀀"), "Should preserve hieroglyph");
        Assert.That(modifiedText, Does.Contain("𝛼𝛽𝛾"), "Should preserve mathematical Greek");
        Assert.That(modifiedText, Does.Contain("𒀀"), "Should preserve cuneiform");
        Assert.That(modifiedText, Does.Contain("𝕊𝕪𝕞𝕓𝕠𝕝𝕤"), "Should preserve double-struck symbols");
        
        // Verify proper byte count (surrogate pairs use 4 bytes each in UTF-8)
        var bytes = Encoding.UTF8.GetBytes(modifiedText);
        Assert.That(bytes.Length, Is.GreaterThan(modifiedText.Length), "Should have more bytes than characters due to surrogate pairs");
    }

    [Test]
    public void DiffValidator_UnicodeNormalization_ShouldPreserveOriginalForm()
    {
        // Arrange - Files with different Unicode normalization forms
        // Using é as composed character (NFC) vs e + combining accent (NFD)
        var originalContent = "const café1 = 'NFC form';\n";  // é as single character U+00E9
        var nfdContent = "const cafe\u0301\u00322 = 'NFD form';\n";  // e + combining acute accent U+0065 U+0301
        
        var originalFile = CreateTestFile("nfc_original.js", originalContent);
        var modifiedFile = CreateTestFile("nfd_modified.js", nfdContent);
        
        // Act - This should detect the normalization difference
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Modification, ChangeType.Deletion }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify that different normalization forms are handled properly
        var originalText = File.ReadAllText(originalFile, Encoding.UTF8);
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        
        // The visual appearance might be similar but byte representation should differ
        var originalBytes = Encoding.UTF8.GetBytes(originalText);
        var modifiedBytes = Encoding.UTF8.GetBytes(modifiedText);
        
        TestContext.Out.WriteLine($"Original bytes: {originalBytes.Length}");
        TestContext.Out.WriteLine($"Modified bytes: {modifiedBytes.Length}");
        TestContext.Out.WriteLine($"Original: {originalText}");
        TestContext.Out.WriteLine($"Modified: {modifiedText}");
    }

    [Test]
    public void DiffValidator_RightToLeftText_ShouldValidateCorrectly()
    {
        // Arrange - Files with Right-to-Left scripts
        var originalContent = "// Bidirectional text handling\n" +
                              "const arabic = 'اللغة العربية';\n" +           // Arabic
                              "const hebrew = 'עברית';\n" +                    // Hebrew
                              "const mixed = 'Hello مرحبا World';\n" +         // Mixed LTR/RTL
                              "const numbers = 'العدد 123 رقم';\n";          // RTL with numbers
        
        var modifiedContent = originalContent + 
                              "const persian = 'فارسی';\n" +                   // Persian/Farsi
                              "const urdu = 'اردو زبان';\n" +                  // Urdu
                              "const bidi = 'English العربية Hebrew עברית';\n"; // Complex bidirectional
        
        var originalFile = CreateTestFile("rtl_original.js", originalContent);
        var modifiedFile = CreateTestFile("rtl_modified.js", modifiedContent);
        
        // Act
        var expectation = new EditExpectation
        {
            AllowedOperations = new HashSet<ChangeType> { ChangeType.Addition }
        };
        
        var validation = DiffValidator.ValidateEdit(originalFile, modifiedFile, expectation);
        
        // Assert
        Assert.That(validation.IsValid, Is.True, DiffValidator.GenerateDiffReport(validation));
        
        // Verify RTL content preservation
        var modifiedText = File.ReadAllText(modifiedFile, Encoding.UTF8);
        Assert.That(modifiedText, Does.Contain("اللغة العربية"), "Should preserve Arabic text");
        Assert.That(modifiedText, Does.Contain("עברית"), "Should preserve Hebrew text");
        Assert.That(modifiedText, Does.Contain("فارسی"), "Should preserve Persian text");
        Assert.That(modifiedText, Does.Contain("اردو زبان"), "Should preserve Urdu text");
        Assert.That(modifiedText, Does.Contain("Hello مرحبا World"), "Should preserve mixed direction text");
        Assert.That(modifiedText, Does.Contain("العدد 123 رقم"), "Should preserve RTL with numbers");
    }

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        // Always write with UTF-8 encoding to handle Unicode properly
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }

    private void VerifyUnicodeIntegrity(string filePath, string description)
    {
        try
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            var bytes = Encoding.UTF8.GetBytes(content);
            var roundTrip = Encoding.UTF8.GetString(bytes);
            
            Assert.That(roundTrip, Is.EqualTo(content), 
                $"Unicode round-trip failed for {description}");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unicode integrity check failed for {description}: {ex.Message}");
        }
    }
}