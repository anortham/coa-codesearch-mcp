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
using COA.Mcp.Framework.Exceptions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.IO;
using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class ReplaceLinesToolTests : CodeSearchToolTestBase<ReplaceLinesTool>
    {
        private ReplaceLinesTool _tool = null!;
        private string _testFilePath = null!;
        
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
            // Create a temporary test file
            _testFilePath = Path.GetTempFileName();
            File.WriteAllText(_testFilePath, @"Line 1
    Indented line 2
Line 3
    Indented line 4
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
        public async Task ExecuteAsync_ReplaceSingleLine_ReplacesCorrectly()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 3,
                Content = "New line 3 content",
                PreserveIndentation = false,
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
            result.Data.Results.LinesAdded.Should().Be(1);
            result.Data.Results.OriginalContent.Should().Be("Line 3");

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[2].Should().Be("New line 3 content");
            fileLines.Should().HaveCount(6); // Same total count
        }

        [Test]
        public async Task ExecuteAsync_ReplaceLineRange_ReplacesCorrectRange()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2,
                EndLine = 4,
                Content = "New content line 1\nNew content line 2",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.StartLine.Should().Be(2);
            result.Data.Results.EndLine.Should().Be(4);
            result.Data.Results.LinesRemoved.Should().Be(3); // Lines 2, 3, 4
            result.Data.Results.LinesAdded.Should().Be(2);

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().HaveCount(5); // 6 original - 3 removed + 2 added
            fileLines[0].Should().Be("Line 1");
            fileLines[1].Should().Be("New content line 1");
            fileLines[2].Should().Be("New content line 2");
            fileLines[3].Should().Be("Line 5");
            fileLines[4].Should().Be("Line 6");
        }

        [Test]
                public async Task ExecuteAsync_ReplaceWithMinimalContent_ReplacesCorrectly()
                {
                    // NOTE: This test was originally "ReplaceWithEmptyContent_DeletesLines" 
                    // but the Content field is [Required] and cannot be empty.
                    // For actual line deletion, use DeleteLinesTool instead.
                    
                    var parameters = new ReplaceLinesParameters
                    {
                        FilePath = _testFilePath,
                        StartLine = 3,
                        EndLine = 4,
                        Content = ".", // Minimal content instead of empty
                        PreserveIndentation = false,
                        ContextLines = 2
                    };

                    // Act
                    var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

                    // Assert
                    result.Should().NotBeNull();
                    result.Success.Should().BeTrue();
                    result.Data!.Results.LinesRemoved.Should().Be(2);
                    result.Data.Results.LinesAdded.Should().Be(1);

                    // Verify file content
                    var fileLines = await File.ReadAllLinesAsync(_testFilePath);
                    fileLines.Should().HaveCount(5); // 6 original - 2 deleted + 1 added
                    fileLines[2].Should().Be("."); // The minimal content line
                }

        [Test]
                public async Task ExecuteAsync_WithIndentationPreservation_PreservesIndentation()
                {
                    // Arrange
                    var parameters = new ReplaceLinesParameters
                    {
                        FilePath = _testFilePath,
                        StartLine = 2, // Replace indented line 2
                        Content = "New indented content",
                        PreserveIndentation = true,
                        ContextLines = 2
                    };

                    // Act
                    var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

                    // Assert
                    result.Should().NotBeNull();
                    result.Success.Should().BeTrue();

                    // Verify indentation is preserved (line 2 originally had 4 spaces of indentation)
                    var fileLines = await File.ReadAllLinesAsync(_testFilePath);
                    fileLines[1].Should().Be("    New indented content"); // Correctly preserving indentation
                }

        [Test]
        public async Task ExecuteAsync_WithoutIndentationPreservation_NoIndentation()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2, // Replace indented line 2
                Content = "New unindented content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            // Verify file content has no indentation
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[1].Should().Be("New unindented content"); // Should not have indentation
        }

        [Test]
        public async Task ExecuteAsync_ReplaceMultipleWithMultiple_ReplacesCorrectly()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 3,
                EndLine = 5,
                Content = "Replacement line A\nReplacement line B\nReplacement line C\nReplacement line D",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesRemoved.Should().Be(3); // Lines 3,4,5
            result.Data.Results.LinesAdded.Should().Be(4); // 4 replacement lines

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().HaveCount(7); // 6 - 3 + 4 = 7
            fileLines[2].Should().Be("Replacement line A");
            fileLines[3].Should().Be("Replacement line B");
            fileLines[4].Should().Be("Replacement line C");
            fileLines[5].Should().Be("Replacement line D");
            fileLines[6].Should().Be("Line 6");
        }

        [Test]
        public async Task ExecuteAsync_ContextLines_GeneratesCorrectContext()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 3,
                Content = "New line content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.ContextLines.Should().NotBeEmpty();
            
            // Context should include lines before and after replacement with + marker
            var contextLines = result.Data.Results.ContextLines;
            contextLines.Should().Contain(line => line.Contains("+")); // Replacement line marker
        }

        [Test]
        public async Task ExecuteAsync_InvalidStartLine_ReturnsError()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 0, // Invalid line number
                Content = "Test content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act & Assert
            Assert.ThrowsAsync<ToolExecutionException>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        }

        [Test]
        public async Task ExecuteAsync_StartLineBeyondFile_ReturnsError()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 100, // Way beyond file length
                Content = "Test content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("is out of range");
        }

        [Test]
        public async Task ExecuteAsync_EndLineBeforeStartLine_ReturnsError()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 4,
                EndLine = 2, // EndLine < StartLine
                Content = "Test content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("StartLine");
        }

        [Test]
        public async Task ExecuteAsync_NullContent_ReturnsError()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2,
                Content = null!, // Null content
                PreserveIndentation = false,
                ContextLines = 2
            };
            // Act & Assert
            Assert.ThrowsAsync<ToolExecutionException>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
                        }

                        [Test]
        public async Task ExecuteAsync_NonExistentFile_ReturnsError()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = "non_existent_file.txt",
                StartLine = 1,
                Content = "Test content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Message.Should().Contain("file");
        }

        [Test]
        public async Task ExecuteAsync_EncodingPreservation_MaintainsOriginalEncoding()
        {
            // Arrange - Create file with UTF-8 BOM
            var utf8WithBom = new UTF8Encoding(true);
            var testContent = "Line 1\nLine 2\nLine 3";
            await File.WriteAllTextAsync(_testFilePath, testContent, utf8WithBom);

            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2,
                Content = "Replaced UTF-8 line",
                PreserveIndentation = false,
                ContextLines = 1
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            // Verify encoding is preserved by checking BOM
            var bytes = await File.ReadAllBytesAsync(_testFilePath);
            bytes.Should().StartWith(new byte[] { 0xEF, 0xBB, 0xBF }); // UTF-8 BOM
        }

        [Test]
        public async Task ExecuteAsync_OriginalContentCapture_StoresCorrectContent()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2,
                EndLine = 4,
                Content = "New content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Original content should capture the exact lines that were replaced
            var expectedOriginal = "    Indented line 2" + Environment.NewLine + 
                                   "Line 3" + Environment.NewLine + 
                                   "    Indented line 4";
            result.Data!.Results.OriginalContent.Should().Be(expectedOriginal);
        }

        [Test]
        public async Task ExecuteAsync_EdgeCaseIndentation_HandlesTabsAndSpaces()
        {
            // Arrange - Create file with mixed indentation
            var mixedIndentContent = "Line 1\n\tTab indented\n    Space indented\nNo indent";
            await File.WriteAllTextAsync(_testFilePath, mixedIndentContent);

            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 2, // Replace tab-indented line
                Content = "New content",
                PreserveIndentation = true,
                ContextLines = 1
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Verify it preserved tab indentation
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[1].Should().StartWith("\t"); // Should preserve tab indentation
        }

        [Test]
        public async Task ExecuteAsync_ReplaceEntireFile_WorksCorrectly()
        {
            // Arrange
            var parameters = new ReplaceLinesParameters
            {
                FilePath = _testFilePath,
                StartLine = 1,
                EndLine = 6, // All lines
                Content = "Completely new content\nSecond line\nThird line",
                PreserveIndentation = false,
                ContextLines = 0
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesRemoved.Should().Be(6);
            result.Data.Results.LinesAdded.Should().Be(3);

            // Verify file content is completely replaced
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().HaveCount(3);
            fileLines[0].Should().Be("Completely new content");
            fileLines[1].Should().Be("Second line");
            fileLines[2].Should().Be("Third line");
        }
    }
}
