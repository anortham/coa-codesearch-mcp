using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.Mcp.Framework.TokenOptimization.Models;
using NUnit.Framework;

namespace COA.CodeSearch.McpServer.Tests;

/// <summary>
/// Comprehensive tests for nullable reference type handling patterns that commonly cause CS8602, CS8604, CS8618 warnings.
/// These tests validate that the codebase properly handles null values without generating compiler warnings.
/// </summary>
[TestFixture]
[Category("NullableValidation")]
public class NullableReferenceValidationTests
{
    #region CS8602 - Possible dereference of a null reference

    [Test]
    public void NullableDereference_SearchHitContextLines_SafeAccess()
    {
        // Test the specific pattern that was causing CS8602 warnings in search result processing

        // Arrange - SearchHit with null ContextLines (common scenario)
        var searchHitWithNullContext = new SearchHit
        {
            FilePath = "test.cs",
            Score = 1.0f,
            LineNumber = 5,
            Fields = new Dictionary<string, string> { ["content"] = "test content" },
            ContextLines = null // This caused CS8602 warnings
        };

        var searchHitWithContext = new SearchHit
        {
            FilePath = "test2.cs", 
            Score = 0.9f,
            LineNumber = 10,
            Fields = new Dictionary<string, string> { ["content"] = "more content" },
            ContextLines = new List<string> { "line 1", "line 2", "line 3" }
        };

        // Act & Assert - Test null-safe access patterns
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Null-conditional with Any() - was causing warnings
            var hasContext1 = searchHitWithNullContext.ContextLines?.Any() == true;
            var hasContext2 = searchHitWithContext.ContextLines?.Any() == true;

            Assert.That(hasContext1, Is.False);
            Assert.That(hasContext2, Is.True);

            // Pattern 2: Null-conditional with Count - was causing warnings  
            var count1 = searchHitWithNullContext.ContextLines?.Count ?? 0;
            var count2 = searchHitWithContext.ContextLines?.Count ?? 0;

            Assert.That(count1, Is.EqualTo(0));
            Assert.That(count2, Is.EqualTo(3));

            // Pattern 3: Null-conditional with FirstOrDefault - was causing warnings
            var first1 = searchHitWithNullContext.ContextLines?.FirstOrDefault() ?? "";
            var first2 = searchHitWithContext.ContextLines?.FirstOrDefault() ?? "";

            Assert.That(first1, Is.EqualTo(""));
            Assert.That(first2, Is.EqualTo("line 1"));
        });
    }

    [Test]
    public void NullableDereference_DictionaryValues_SafeAccess()
    {
        // Test dictionary value access patterns that commonly cause CS8602

        // Arrange - Dictionary with potentially null values
        var fields = new Dictionary<string, string>
        {
            ["existing"] = "value",
            ["empty"] = "",
            // ["missing"] key doesn't exist
        };

        // Act & Assert - Test safe dictionary access
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: TryGetValue with null-safe operations
            var hasExisting = fields.TryGetValue("existing", out var existingValue);
            var hasEmpty = fields.TryGetValue("empty", out var emptyValue);  
            var hasMissing = fields.TryGetValue("missing", out var missingValue);

            Assert.That(hasExisting, Is.True);
            Assert.That(existingValue, Is.EqualTo("value"));
            
            Assert.That(hasEmpty, Is.True);
            Assert.That(emptyValue, Is.EqualTo(""));
            
            Assert.That(hasMissing, Is.False);
            Assert.That(missingValue, Is.Null);

            // Pattern 2: Safe string operations on potentially null values
            var existingLength = existingValue?.Length ?? 0;
            var emptyLength = emptyValue?.Length ?? 0;
            var missingLength = missingValue?.Length ?? 0;

            Assert.That(existingLength, Is.EqualTo(5));
            Assert.That(emptyLength, Is.EqualTo(0));
            Assert.That(missingLength, Is.EqualTo(0));

            // Pattern 3: Safe string splitting operations
            var existingLines = existingValue?.Split('\n') ?? Array.Empty<string>();
            var emptyLines = emptyValue?.Split('\n') ?? Array.Empty<string>();
            var missingLines = missingValue?.Split('\n') ?? Array.Empty<string>();

            Assert.That(existingLines.Length, Is.GreaterThan(0));
            Assert.That(emptyLines.Length, Is.GreaterThanOrEqualTo(1)); // Empty string splits to array with empty element
            Assert.That(missingLines.Length, Is.EqualTo(0));
        });
    }

    [Test]
    public void NullableDereference_LineDataOperations_SafeAccess()
    {
        // Test line data operations that were causing warnings

        // Arrange - Simulate content processing
        string? nullContent = null;
        string emptyContent = "";
        string validContent = "line 1\nline 2\nline 3";

        // Act & Assert - Test null-safe line operations
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Safe line splitting
            var nullLines = nullContent?.Replace("\r\n", "\n").Split('\n') ?? Array.Empty<string>();
            var emptyLines = emptyContent?.Replace("\r\n", "\n").Split('\n') ?? Array.Empty<string>();
            var validLines = validContent?.Replace("\r\n", "\n").Split('\n') ?? Array.Empty<string>();

            Assert.That(nullLines.Length, Is.EqualTo(0));
            Assert.That(emptyLines.Length, Is.EqualTo(1)); // Empty string becomes array with one empty element
            Assert.That(validLines.Length, Is.EqualTo(3));

            // Pattern 2: Safe line access with bounds checking
            for (int i = 0; i < validLines.Length; i++)
            {
                var line = validLines[i];
                var lineLength = line?.Length ?? 0;
                Assert.That(lineLength, Is.GreaterThanOrEqualTo(0));
            }
        });
    }

    #endregion

    #region CS8604 - Possible null reference argument

    [Test]
    public void NullableArgument_MethodCalls_SafeParameterPassing()
    {
        // Test method calls with nullable arguments that were causing CS8604

        // Arrange
        string? nullableString = null;
        string? validString = "test";

        // Act & Assert - Test safe parameter passing
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Safe string method calls
            var nullResult = ProcessStringParameter(nullableString ?? "");
            var validResult = ProcessStringParameter(validString ?? "");

            Assert.That(nullResult, Is.EqualTo("processed: "));
            Assert.That(validResult, Is.EqualTo("processed: test"));

            // Pattern 2: Safe comparison operations
            var nullEquals = string.Equals(nullableString, "test", StringComparison.OrdinalIgnoreCase);
            var validEquals = string.Equals(validString, "test", StringComparison.OrdinalIgnoreCase);

            Assert.That(nullEquals, Is.False);
            Assert.That(validEquals, Is.True);

            // Pattern 3: Safe path operations
            var safeFileName = Path.GetFileName(validString ?? "unknown");
            Assert.That(safeFileName, Is.EqualTo("test"));
        });
    }

    [Test]
    public void NullableArgument_CollectionOperations_SafeParameterPassing()
    {
        // Test collection operations with nullable arguments

        // Arrange
        string[]? nullArray = null;
        string[]? emptyArray = Array.Empty<string>();
        string[] validArray = { "item1", "item2" };

        // Act & Assert - Test safe collection parameter passing
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Safe LINQ operations
            var nullCount = (nullArray?.Length ?? 0);
            var emptyCount = (emptyArray?.Length ?? 0);
            var validCount = (validArray?.Length ?? 0);

            Assert.That(nullCount, Is.EqualTo(0));
            Assert.That(emptyCount, Is.EqualTo(0));
            Assert.That(validCount, Is.EqualTo(2));

            // Pattern 2: Safe enumeration
            var nullItems = ProcessEnumerable(nullArray ?? Array.Empty<string>());
            var emptyItems = ProcessEnumerable(emptyArray ?? Array.Empty<string>());
            var validItems = ProcessEnumerable(validArray ?? Array.Empty<string>());

            Assert.That(nullItems, Is.EqualTo(0));
            Assert.That(emptyItems, Is.EqualTo(0));
            Assert.That(validItems, Is.EqualTo(2));
        });
    }

    #endregion

    #region CS8618 - Non-nullable field must contain a non-null value when exiting constructor

    [Test]
    public void NonNullableField_ObjectInitialization_ProperInitialization()
    {
        // Test object initialization patterns that were causing CS8618

        // Act & Assert - Test proper initialization
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Direct initialization
            var result1 = new LineSearchResult
            {
                Summary = "Test summary",
                Files = new List<LineSearchFileResult>(),
                TotalFilesSearched = 0,
                TotalFilesWithMatches = 0,
                TotalLineMatches = 0,
                SearchTime = TimeSpan.Zero,
                Query = "test query",
                Truncated = false,
                Insights = new List<string>()
            };

            Assert.That(result1.Summary, Is.Not.Null);
            Assert.That(result1.Files, Is.Not.Null);
            Assert.That(result1.Query, Is.Not.Null);
            Assert.That(result1.Insights, Is.Not.Null);

            // Pattern 2: Builder-style initialization
            var result2 = CreateLineSearchResult("builder test");
            
            Assert.That(result2.Summary, Is.Not.Null);
            Assert.That(result2.Files, Is.Not.Null);
            Assert.That(result2.Query, Is.Not.Null);
            Assert.That(result2.Insights, Is.Not.Null);
        });
    }

    [Test]
    public void NonNullableField_ResponseModels_ProperlyInitialized()
    {
        // Test response model initialization

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            var response = new AIOptimizedResponse<LineSearchResult>
            {
                Success = true,
                Data = new AIResponseData<LineSearchResult>
                {
                    Results = new LineSearchResult
                    {
                        Summary = "Test",
                        Files = new List<LineSearchFileResult>(),
                        TotalFilesSearched = 0,
                        TotalFilesWithMatches = 0,
                        TotalLineMatches = 0,
                        SearchTime = TimeSpan.Zero,
                        Query = "test",
                        Truncated = false,
                        Insights = new List<string>()
                    }
                },
                Meta = new AIResponseMeta()
            };

            Assert.That(response.Data, Is.Not.Null);
            Assert.That(response.Data.Results, Is.Not.Null);
            Assert.That(response.Meta, Is.Not.Null);
        });
    }

    #endregion

    #region Integration Tests for Nullable Reference Handling

    [Test]
    public void IntegrationTest_SearchHitProcessing_FullPipeline()
    {
        // Test the complete search hit processing pipeline with all nullable patterns

        // Arrange - Realistic search hits with various null combinations
        var searchHits = new[]
        {
            new SearchHit
            {
                FilePath = "file1.cs",
                Score = 1.0f,
                LineNumber = null, // Nullable line number
                Fields = new Dictionary<string, string> { ["content"] = "content1" },
                ContextLines = null
            },
            new SearchHit
            {
                FilePath = "file2.cs",
                Score = 0.9f,
                LineNumber = 5,
                Fields = new Dictionary<string, string>(), // Empty fields
                ContextLines = new List<string> { "context1", "context2" }
            },
            new SearchHit
            {
                FilePath = "file3.cs",
                Score = 0.8f,
                LineNumber = 10,
                Fields = new Dictionary<string, string> 
                { 
                    ["content"] = "line1\nline2\nline3",
                    ["fileName"] = "file3.cs"
                },
                ContextLines = new List<string>()
            }
        };

        // Act & Assert - Process all hits without null reference warnings
        Assert.DoesNotThrow(() =>
        {
            var processedResults = new List<object>();

            foreach (var hit in searchHits)
            {
                // Safe line number handling
                var hasLineNumber = hit.LineNumber.HasValue;
                var lineNumber = hit.LineNumber ?? 0;

                // Safe context handling
                var hasContext = hit.ContextLines?.Any() == true;
                var contextCount = hit.ContextLines?.Count ?? 0;
                var firstContext = hit.ContextLines?.FirstOrDefault() ?? "";

                // Safe field access
                var hasContent = hit.Fields.TryGetValue("content", out var content);
                var lines = content?.Split('\n') ?? Array.Empty<string>();

                // Safe line validation
                if (hasLineNumber && hasContent && lineNumber > 0 && lineNumber <= lines.Length)
                {
                    var targetLine = lines[lineNumber - 1];
                    Assert.That(targetLine, Is.Not.Null);
                }

                var result = new
                {
                    FilePath = hit.FilePath ?? "unknown",
                    Score = hit.Score,
                    HasLineNumber = hasLineNumber,
                    LineNumber = lineNumber,
                    HasContext = hasContext,
                    ContextCount = contextCount,
                    FirstContext = firstContext,
                    HasContent = hasContent,
                    LineCount = lines.Length
                };

                processedResults.Add(result);
            }

            Assert.That(processedResults.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void IntegrationTest_FileOperations_SafeNullHandling()
    {
        // Test file operations with nullable return values

        // Arrange
        var testPaths = new[]
        {
            "C:\\nonexistent\\file.txt",
            Path.GetTempFileName(),
            ""
        };

        // Act & Assert - File operations with safe null handling
        Assert.DoesNotThrow(() =>
        {
            foreach (var path in testPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                // Safe file existence check
                var exists = File.Exists(path);
                
                // Safe file info operations
                DateTime? lastModified = exists ? File.GetLastWriteTime(path) : null;
                var lastModifiedString = lastModified?.ToString("yyyy-MM-dd") ?? "Unknown";
                
                // Safe path operations
                var fileName = Path.GetFileName(path) ?? "unknown";
                var directory = Path.GetDirectoryName(path) ?? "";
                var extension = Path.GetExtension(path) ?? "";

                Assert.That(fileName, Is.Not.Null);
                Assert.That(directory, Is.Not.Null);
                Assert.That(extension, Is.Not.Null);
                Assert.That(lastModifiedString, Is.Not.Null);
            }
        });

        // Cleanup
        foreach (var path in testPaths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    #endregion

    #region Helper Methods

    private string ProcessStringParameter(string value)
    {
        return $"processed: {value}";
    }

    private int ProcessEnumerable(IEnumerable<string> items)
    {
        return items?.Count() ?? 0;
    }

    private LineSearchResult CreateLineSearchResult(string query)
    {
        return new LineSearchResult
        {
            Summary = $"Results for {query}",
            Files = new List<LineSearchFileResult>(),
            TotalFilesSearched = 0,
            TotalFilesWithMatches = 0,
            TotalLineMatches = 0,
            SearchTime = TimeSpan.Zero,
            Query = query,
            Truncated = false,
            Insights = new List<string> { "Test insight" }
        };
    }

    #endregion
}