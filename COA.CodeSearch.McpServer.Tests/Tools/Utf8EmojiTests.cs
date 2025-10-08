using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Models;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.TokenOptimization.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Tests to verify that UTF-8 emoji characters are properly handled without corruption
/// in CodeSearch tools, specifically the replace_lines functionality.
/// </summary>
[TestFixture]
public class Utf8EmojiTests
{
    private EditLinesTool _tool;
    private IPathResolutionService _pathResolutionService;
    private UnifiedFileEditService _fileEditService;
    private ILogger<EditLinesTool> _logger;
    private string _testFilePath;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        // Create temp directory for test files
        _tempDir = Path.Combine(Path.GetTempPath(), "utf8-emoji-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        // Setup mocks
        _pathResolutionService = Substitute.For<IPathResolutionService>();
        _logger = Substitute.For<ILogger<EditLinesTool>>();
        var fileEditLogger = Substitute.For<ILogger<UnifiedFileEditService>>();
        
        // Setup real services
        _fileEditService = new UnifiedFileEditService(fileEditLogger);
        
        // Create the tool
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        _tool = new EditLinesTool(serviceProvider, _pathResolutionService, _fileEditService, _logger);
        
        // Create test file with UTF-8 emojis
        _testFilePath = Path.Combine(_tempDir, "emoji-test.md");
        var testContent = @"# UTF-8 Emoji Test File

## Test Emojis
📊 Chart emoji
🚀 Rocket emoji
✅ Checkmark emoji
⭐ Star emoji

## Complex Sequences
👨‍💻 Man technologist (compound emoji)
💯 Hundred points emoji";

        File.WriteAllText(_testFilePath, testContent, System.Text.Encoding.UTF8);
        
        // Setup path resolution to return our test file  
        _pathResolutionService.GetFullPath("emoji-test.md").Returns(_testFilePath);
        _pathResolutionService.GetFullPath(_testFilePath).Returns(_testFilePath);
    }

    [TearDown]
    public void TearDown()
    {
        // Cleanup temp directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public async Task ReplaceLinesTool_WithEmojiContent_PreservesUtf8Encoding()
    {
        // Arrange - Replace line 4 which contains "📊 Chart emoji"
        var parameters = new EditLinesParameters
        {
            FilePath = _testFilePath, // Use actual temp file path
            Operation = "replace",
            StartLine = 4,
            Content = "📊 Chart emoji - UTF-8 encoding preserved! 🎉",
            ContextLines = 2,
            PreserveIndentation = false
        };

        // Act
        var typedTool = _tool as IMcpTool<EditLinesParameters, AIOptimizedResponse<EditLinesResult>>;
        var result = await typedTool.ExecuteAsync(parameters, default);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data.Results.Should().NotBeNull();
        
        // If the operation failed, show the error message for debugging
        if (!result.Data.Results.Success)
        {
            throw new Exception($"ReplaceLinesTool failed: {result.Data.Results.ErrorMessage ?? "Unknown error"}");
        }
        
        result.Data.Results.Success.Should().BeTrue();
        
        // UTF-8 encoding is now properly preserved thanks to JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        // The contextLines should contain proper UTF-8 emojis
        var contextLines = result.Data.Results.ContextLines;
        contextLines.Should().NotBeNull();
        contextLines.Should().NotBeEmpty();
        
        // Find the line that was replaced (should have → prefix in new unified tool)
        var replacedLine = contextLines.FirstOrDefault(line => line.Contains("→ "));
        replacedLine.Should().NotBeNull("replaced line should be present in context");
        
        // These assertions now pass - UTF-8 corruption has been fixed
        replacedLine.Should().Contain("📊", "chart emoji should be preserved in context");
        replacedLine.Should().Contain("🎉", "party emoji should be preserved in context");
        replacedLine.Should().NotContain("≡ƒ", "should not contain UTF-8 corruption patterns");
        replacedLine.Should().NotContain("Γ£", "should not contain UTF-8 corruption patterns");
        
        // Verify file content is also properly written
        var fileContent = await File.ReadAllTextAsync(_testFilePath, System.Text.Encoding.UTF8);
        fileContent.Should().Contain("📊 Chart emoji - UTF-8 encoding preserved! 🎉");
        fileContent.Should().NotContain("≡ƒ", "file should not contain UTF-8 corruption");
    }

    [Test]
    public async Task ReplaceLinesTool_WithComplexEmojiSequences_PreservesUtf8Encoding()
    {
        // Arrange - Replace line with complex emoji sequences
        var parameters = new EditLinesParameters
        {
            FilePath = _testFilePath, // Use actual temp file path
            Operation = "replace",
            StartLine = 10,
            Content = "👨‍💻 Developer + 🚀 Deploy = 💯 Success",
            ContextLines = 2,
            PreserveIndentation = false
        };

        // Act
        var typedTool = _tool as IMcpTool<EditLinesParameters, AIOptimizedResponse<EditLinesResult>>;
        var result = await typedTool.ExecuteAsync(parameters, default);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data.Results.Should().NotBeNull();
        result.Data.Results.Success.Should().BeTrue();
        
        // Find the replaced line in context
        var contextLines = result.Data.Results.ContextLines;
        contextLines.Should().NotBeNull();
        var replacedLine = contextLines!.FirstOrDefault(line => line.Contains("→ "));
        replacedLine.Should().NotBeNull();
        
        // These now pass - complex emoji sequences are preserved
        replacedLine.Should().Contain("👨‍💻", "compound emoji sequence should be preserved");
        replacedLine.Should().Contain("🚀", "rocket emoji should be preserved");
        replacedLine.Should().Contain("💯", "hundred points emoji should be preserved");
        
        // Verify no corruption patterns
        replacedLine.Should().NotContain("≡ƒæ¿ΓÇì≡ƒÆ╗", "should not contain corrupted compound emoji");
        replacedLine.Should().NotContain("≡ƒÜÇ", "should not contain corrupted rocket emoji");
        replacedLine.Should().NotContain("≡ƒÆ»", "should not contain corrupted hundred points emoji");
    }

    [Test] 
    public async Task ReplaceLinesTool_ContextLines_ShowOriginalEmojisWithoutCorruption()
    {
        // Arrange - This test focuses on the context lines that show existing emoji content
        var parameters = new EditLinesParameters
        {
            FilePath = _testFilePath, // Use actual temp file path
            Operation = "replace",
            StartLine = 6, // Replace "✅ Checkmark emoji"
            Content = "✅ Checkmark - test successful",
            ContextLines = 3,
            PreserveIndentation = false
        };

        // Act
        var typedTool = _tool as IMcpTool<EditLinesParameters, AIOptimizedResponse<EditLinesResult>>;
        var result = await typedTool.ExecuteAsync(parameters, default);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data.Results.Should().NotBeNull();
        result.Data.Results.Success.Should().BeTrue();
        
        var contextLines = result.Data.Results.ContextLines;
        contextLines.Should().HaveCountGreaterThan(3);
        
        // The context should show surrounding lines with emojis intact
        var chartLine = contextLines.FirstOrDefault(line => line.Contains("Chart"));
        var rocketLine = contextLines.FirstOrDefault(line => line.Contains("Rocket"));
        
        // These now pass - context display preserves emojis correctly
        if (chartLine != null)
        {
            chartLine.Should().Contain("📊", "context should show chart emoji correctly");
            chartLine.Should().NotContain("≡ƒôè", "context should not show corrupted chart emoji");
        }
        
        if (rocketLine != null) 
        {
            rocketLine.Should().Contain("🚀", "context should show rocket emoji correctly");
            rocketLine.Should().NotContain("≡ƒÜÇ", "context should not show corrupted rocket emoji");
        }
    }
}
