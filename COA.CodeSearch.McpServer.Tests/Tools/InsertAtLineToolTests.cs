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
    public class InsertAtLineToolTests : CodeSearchToolTestBase<InsertAtLineTool>
    {
        private InsertAtLineTool _tool = null!;
        private string _testFilePath = null!;
        
        protected override InsertAtLineTool CreateTool()
        {
            _tool = new InsertAtLineTool(
                ServiceProvider,
                PathResolutionServiceMock.Object,
                WorkspaceRegistryServiceMock.Object,
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
    Line 2 indented
Line 3
    Line 4 indented
Line 5");
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
        public async Task ExecuteAsync_InsertAtBeginning_InsertsCorrectly()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 1,
                                Content = "New first line",
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
            result.Data.Results.InsertedAtLine.Should().Be(1);
            result.Data.Results.LinesInserted.Should().Be(1);

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[0].Should().Be("New first line");
            fileLines[1].Should().Be("Line 1");
        }

        [Test]
        public async Task ExecuteAsync_InsertAtEnd_InsertsCorrectly()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 6, // After last line
                Content = "New last line",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.InsertedAtLine.Should().Be(6);
            result.Data.Results.LinesInserted.Should().Be(1);

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().HaveCount(6);
            fileLines[5].Should().Be("New last line");
        }

        [Test]
        public async Task ExecuteAsync_InsertMultipleLines_InsertsCorrectly()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 3,
                Content = "New line 1\nNew line 2\nNew line 3",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.LinesInserted.Should().Be(3);

            // Verify file content
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines.Should().HaveCount(8); // 5 original + 3 inserted
            fileLines[2].Should().Be("New line 1");
            fileLines[3].Should().Be("New line 2");
            fileLines[4].Should().Be("New line 3");
            fileLines[5].Should().Be("Line 3"); // Original line 3 moved down
        }

        [Test]
        public async Task ExecuteAsync_WithIndentationPreservation_PreservesIndentation()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 3, // Insert before "Line 3" after indented line
                Content = "New indented content",
                PreserveIndentation = true,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.DetectedIndentation.Should().Be("'    '"); // 4 spaces from Line 2

            // Verify file content maintains indentation
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[2].Should().Be("    New indented content"); // Should have 4-space indentation
        }

        [Test]
        public async Task ExecuteAsync_WithoutIndentationPreservation_NoIndentation()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 3,
                Content = "New unindented content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.DetectedIndentation.Should().Be("none");

            // Verify file content has no indentation
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[2].Should().Be("New unindented content"); // Should have no indentation
        }

        [Test]
        public async Task ExecuteAsync_ContextLines_GeneratesCorrectContext()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 3,
                Content = "Inserted line",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.ContextLines.Should().NotBeEmpty();
            
            // Context should include lines before and after insertion
            var contextLines = result.Data.Results.ContextLines;
            contextLines.Should().Contain(line => line.Contains("→")); // Inserted line marker
        }

        [Test]
        public async Task ExecuteAsync_InvalidLineNumber_ReturnsError()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 0, // Invalid line number
                Content = "Test content",
                PreserveIndentation = false,
                ContextLines = 2
            };

            // Act & Assert
            Assert.ThrowsAsync<ToolExecutionException>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        }

        [Test]
        public async Task ExecuteAsync_LineNumberBeyondFile_ReturnsError()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 100, // Way beyond file length
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
            result.Error!.Message.Should().Contain("exceeds file length");
        }

        [Test]
        public async Task ExecuteAsync_EmptyContent_ReturnsError()
        {
            // Arrange
            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 2,
                Content = "", // Empty content
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
            var parameters = new InsertAtLineParameters
            {
                FilePath = "non_existent_file.txt",
                LineNumber = 1,
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
            result.Error!.Message.Should().Contain("File not found");
        }

        [Test]
        public async Task ExecuteAsync_EncodingPreservation_MaintainsOriginalEncoding()
        {
            // Arrange - Create file with UTF-8 BOM
            var utf8WithBom = new UTF8Encoding(true);
            var testContent = "Line 1\nLine 2\nLine 3";
            await File.WriteAllTextAsync(_testFilePath, testContent, utf8WithBom);

            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 2,
                Content = "Inserted UTF-8 line",
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
        public async Task ExecuteAsync_EdgeCaseIndentation_HandlesTabsAndSpaces()
        {
            // Arrange - Create file with mixed indentation
            var mixedIndentContent = "Line 1\n\tTab indented\n    Space indented\nNo indent";
            await File.WriteAllTextAsync(_testFilePath, mixedIndentContent);

            var parameters = new InsertAtLineParameters
            {
                FilePath = _testFilePath,
                LineNumber = 2, // Insert before tab-indented line
                Content = "New content",
                PreserveIndentation = true,
                ContextLines = 1
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Verify it detected tab indentation from surrounding context
            var fileLines = await File.ReadAllLinesAsync(_testFilePath);
            fileLines[1].Should().StartWith("\t"); // Should preserve tab indentation
        }
    }
}
