using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Services;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Tests.Services
{
    [TestFixture]
    public class FileLineUtilitiesTests
    {
        private string _testFilePath = null!;

        [SetUp]
        public void SetUp()
        {
            _testFilePath = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }

        [Test]
        public void SplitLines_ConsistentEmptyLineHandling_ReturnsConsistentResults()
        {
            // Arrange - Test cases that previously caused inconsistencies
            var testCases = new[]
            {
                "Line1\nLine2\nLine3",        // No trailing newline
                "Line1\nLine2\nLine3\n",      // Single trailing newline  
                "Line1\nLine2\nLine3\n\n",    // Double trailing newline
                "Line1\r\nLine2\r\nLine3",    // Windows line endings, no trailing
                "Line1\r\nLine2\r\nLine3\r\n", // Windows line endings with trailing
                "",                           // Empty content
                "\n",                         // Just newline
                "SingleLine"                  // Single line, no newline
            };

            foreach (var content in testCases)
            {
                // Act
                                var lines = FileLineUtilities.SplitLines(content);
                                
                                // Assert - Should never have empty trailing line unless content is intentionally double newlines
                                if (!string.IsNullOrEmpty(content) && !content.Equals("\n") && !content.EndsWith("\n\n"))
                                {
                                    if (lines.Length > 0)
                                    {
                                        lines[^1].Should().NotBe("", 
                                            $"Content '{content.Replace("\n", "\\n").Replace("\r", "\\r")}' should not produce empty trailing line");
                                    }
                                }
                                            }
                                        }


        [Test]
        public async Task ReadFileWithEncodingAsync_ConsistentLineCounts_MatchesSplitLines()
        {
            // Arrange
            var testContents = new[]
            {
                "Line1\nLine2\nLine3",
                "Line1\nLine2\nLine3\n",
                "Line1\r\nLine2\r\nLine3\r\n"
            };

                        foreach (var content in testContents)
                        {
                            await File.WriteAllTextAsync(_testFilePath, content, new UTF8Encoding(false)); // No BOM
                
                // Act
                var (fileLines, _) = await FileLineUtilities.ReadFileWithEncodingAsync(_testFilePath);
                var splitLines = FileLineUtilities.SplitLines(content);
                
                // Assert - File reading and string splitting should produce identical results
                fileLines.Should().BeEquivalentTo(splitLines, 
                    $"File reading and SplitLines should produce identical results for content: '{content.Replace("\n", "\\n").Replace("\r", "\\r")}'");
            }
        }

        [Test]
        public void ExtractIndentation_VariousWhitespace_ExtractsCorrectly()
        {
            // Test cases for indentation extraction
            var testCases = new[]
            {
                ("    code", "    "),           // 4 spaces
                ("\t\tcode", "\t\t"),          // 2 tabs  
                (" \t code", " \t "),          // Mixed spaces and tabs
                ("code", ""),                  // No indentation
                ("", ""),                      // Empty line
                ("    ", "    ")               // Only whitespace
            };

            foreach (var (input, expected) in testCases)
            {
                // Act
                var result = FileLineUtilities.ExtractIndentation(input);
                
                // Assert
                result.Should().Be(expected, $"Input: '{input.Replace(" ", "·").Replace("\t", "→")}'");
            }
        }

        [Test]
        public void ApplyIndentation_ConsistentBehavior_PreservesLogic()
        {
            // Arrange
            var contentLines = new[] { "line1", "", "line2" };
            var indentation = "    ";

            // Act
            var result = FileLineUtilities.ApplyIndentation(contentLines, indentation);

            // Assert
            result.Should().HaveCount(3);
            result[0].Should().Be("    line1");  // Non-empty line gets indented
            result[1].Should().Be("");           // Empty line stays empty
            result[2].Should().Be("    line2");  // Non-empty line gets indented
        }

        [Test]
        public void ValidateAndResolvePath_ValidPath_ReturnsAbsolutePath()
        {
            // Act
            var result = FileLineUtilities.ValidateAndResolvePath(_testFilePath);

            // Assert
            result.Should().Be(Path.GetFullPath(_testFilePath));
        }

        [Test]
        public void ValidateAndResolvePath_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.txt");

            // Act & Assert
                        // Act & Assert
                        Action act = () => FileLineUtilities.ValidateAndResolvePath(nonExistentPath);
                        act.Should().Throw<FileNotFoundException>()
                            .WithMessage("*File not found*");
        }

        /// <summary>
        /// REGRESSION TEST: Ensures all line editing tools use the same line handling logic.
        /// This test prevents the critical bug where different tools used different approaches
        /// for removing trailing empty lines, causing cascading corruption.
        /// </summary>
        [Test]
        public void RegressionTest_PreventLineHandlingInconsistency()
        {
            // Arrange - Content that previously caused issues
            var problematicContent = "Line1\nLine2\n";  // Content with trailing newline
            
            // Act - Multiple calls should produce identical results
            var result1 = FileLineUtilities.SplitLines(problematicContent);
            var result2 = FileLineUtilities.SplitLines(problematicContent);
            
            // Assert - Results must be identical
            result1.Should().BeEquivalentTo(result2, 
                "Multiple calls to SplitLines with same content must produce identical results");
            
            // Assert - Should not have empty trailing line
            if (result1.Length > 0)
            {
                result1[^1].Should().NotBe("", 
                    "SplitLines should not produce empty trailing lines for normal content");
            }
            
            // Assert - Should have expected line count
            result1.Should().HaveCount(2, "Content with trailing newline should produce 2 lines");
            result1[0].Should().Be("Line1");
            result1[1].Should().Be("Line2");
        }
    }
}
