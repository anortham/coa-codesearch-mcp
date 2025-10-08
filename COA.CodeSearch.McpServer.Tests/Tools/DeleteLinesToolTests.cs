using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.IO;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class DeleteLinesToolTests : CodeSearchToolTestBase<EditLinesTool>
    {
        private EditLinesTool _tool = null!;
        private string _testFilePath = null!;
        
        protected override EditLinesTool CreateTool()
        {
            var unifiedFileEditService = new COA.CodeSearch.McpServer.Services.UnifiedFileEditService(
                new Mock<Microsoft.Extensions.Logging.ILogger<COA.CodeSearch.McpServer.Services.UnifiedFileEditService>>().Object);
            var deleteLogger = new Mock<Microsoft.Extensions.Logging.ILogger<EditLinesTool>>();
            _tool = new EditLinesTool(
                ServiceProvider,
                PathResolutionServiceMock.Object,
                unifiedFileEditService,
                deleteLogger.Object
            );
            return _tool;
        }

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            // Create a temporary test file
            _testFilePath = Path.GetTempFileName();
            File.WriteAllText(_testFilePath, @"Line 1
Line 2
Line 3
Line 4
Line 5
Line 6");
        }

        [TearDown]
        public override void TearDown()
        {
            // Clean up test file
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
            base.TearDown();
        }

        [Test]
        public async Task ExecuteAsync_SingleLine_DeletesCorrectLine()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 3,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results.Success.Should().BeTrue();
            result.Data.Results.StartLine.Should().Be(3);
            result.Data.Results.EndLine.Should().Be(3);
            result.Data.Results.LinesRemoved.Should().Be(1);
            result.Data.Results.DeletedContent.Should().Be("Line 3");

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().Equal("Line 1", "Line 2", "Line 4", "Line 5", "Line 6");
        }

        [Test]
        public async Task ExecuteAsync_LineRange_DeletesCorrectRange()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 2,
                EndLine = 4,
                ContextLines = 1
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.Success.Should().BeTrue();
            result.Data.Results.StartLine.Should().Be(2);
            result.Data.Results.EndLine.Should().Be(4);
            result.Data.Results.LinesRemoved.Should().Be(3);
            result.Data.Results.DeletedContent.Should().Be($"Line 2{Environment.NewLine}Line 3{Environment.NewLine}Line 4");

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().Equal("Line 1", "Line 5", "Line 6");
        }

        [Test]
        public async Task ExecuteAsync_DeleteFirstLine_WorksCorrectly()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 1,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesRemoved.Should().Be(1);
            result.Data.Results.DeletedContent.Should().Be("Line 1");

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().Equal("Line 2", "Line 3", "Line 4", "Line 5", "Line 6");
        }

        [Test]
        public async Task ExecuteAsync_DeleteLastLine_WorksCorrectly()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 6,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesRemoved.Should().Be(1);
            result.Data.Results.DeletedContent.Should().Be("Line 6");

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().Equal("Line 1", "Line 2", "Line 3", "Line 4", "Line 5");
        }

        [Test]
        public async Task ExecuteAsync_DeleteAllLines_WorksCorrectly()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 1,
                EndLine = 6,
                ContextLines = 0
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesRemoved.Should().Be(6);

            // Verify file is empty
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().BeEmpty();
        }

        [Test]
        public async Task ExecuteAsync_InvalidStartLine_ReturnsError()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 10, // Beyond file length
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("StartLine 10 is out of range");
        }

        [Test]
        public async Task ExecuteAsync_InvalidEndLine_ReturnsError()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 2,
                EndLine = 10, // Beyond file length
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("EndLine 10 is out of range");
        }

        [Test]
        public async Task ExecuteAsync_StartLineGreaterThanEndLine_ReturnsError()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 5,
                EndLine = 3,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("EndLine must be >= StartLine");
        }

        [Test]
        public async Task ExecuteAsync_NonExistentFile_ReturnsError()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = "nonexistent-file.txt",
                Operation = "delete",
                StartLine = 1,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("File not found");
        }

        [Test]
        public async Task ExecuteAsync_WithContextLines_GeneratesCorrectContext()
        {
            // Arrange
            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 3,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            var contextLines = result.Data!.Results.ContextLines;
            contextLines.Should().NotBeNull();
            contextLines.Should().HaveCount(5); // 2 before + deletion marker + 2 after

            // Verify context format
            contextLines![0].Should().Contain("Line 1");
            contextLines[1].Should().Contain("Line 2");
            contextLines[2].Should().Contain("[Deleted line 3]");
            contextLines[3].Should().Contain("Line 4");
            contextLines[4].Should().Contain("Line 5");
        }

        [Test]
        public async Task ExecuteAsync_WithUTF8BOM_PreservesEncoding()
        {
            // Arrange
            var utf8WithBom = new UTF8Encoding(true);
            var testContent = "Line 1\nLine 2\nLine 3";
            await File.WriteAllTextAsync(_testFilePath, testContent, utf8WithBom);

            var parameters = new EditLinesParameters
            {
                FilePath = _testFilePath,
                Operation = "delete",
                StartLine = 2,
                ContextLines = 1
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();

            // Verify encoding is preserved
            var fileBytes = await File.ReadAllBytesAsync(_testFilePath);
            fileBytes.Should().StartWith(new byte[] { 0xEF, 0xBB, 0xBF }); // UTF-8 BOM
        }

        [Test]
        [Ignore("This test file now uses EditLinesTool (unified tool). Old DeleteLinesTool metadata no longer applies.")]
        public void Name_ReturnsCorrectValue()
        {
            // Act & Assert
            _tool.Name.Should().Be(ToolNames.DeleteLines);
        }

        [Test]
        public void Category_ReturnsCorrectValue()
        {
            // Act & Assert
            _tool.Category.Should().Be(ToolCategory.Refactoring);
        }

        [Test]
        [Ignore("This test file now uses EditLinesTool (unified tool). Old DeleteLinesTool description no longer applies.")]
        public void Description_ContainsKeyPhrases()
        {
            // Act & Assert
            var description = _tool.Description;
            description.Should().Contain("DELETE LINES WITHOUT READ");
            description.Should().Contain("precise line positioning");
            description.Should().Contain("surgical deletions");
        }
    }
}