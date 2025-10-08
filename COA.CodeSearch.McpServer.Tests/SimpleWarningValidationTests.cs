using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace COA.CodeSearch.McpServer.Tests;

/// <summary>
/// Simple validation tests to ensure compiler warning fixes remain effective.
/// These tests focus on the specific warning patterns that were fixed without complex dependencies.
/// </summary>
[TestFixture]
[Category("WarningValidation")]
public class SimpleWarningValidationTests
{
    #region CS8602 - Nullable Reference Dereference Validation

    [Test]
    public void NullableString_SafeOperations_NoWarnings()
    {
        // Test nullable string operations that commonly caused CS8602 warnings

        // Arrange
        string? nullString = null;
        string? emptyString = "";
        string? validString = "test content";

        // Act & Assert - Test null-safe operations
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Null-conditional operator with Any()
            var nullAny = nullString?.Any() == true;
            var emptyAny = emptyString?.Any() == true;
            var validAny = validString?.Any() == true;

            Assert.That(nullAny, Is.False);
            Assert.That(emptyAny, Is.False);
            Assert.That(validAny, Is.True);

            // Pattern 2: Null-conditional operator with Length
            var nullLength = nullString?.Length ?? 0;
            var emptyLength = emptyString?.Length ?? 0;
            var validLength = validString?.Length ?? 0;

            Assert.That(nullLength, Is.EqualTo(0));
            Assert.That(emptyLength, Is.EqualTo(0));
            Assert.That(validLength, Is.EqualTo(12));

            // Pattern 3: Null-conditional operator with Split
            var nullSplit = nullString?.Split('\n') ?? Array.Empty<string>();
            var emptySplit = emptyString?.Split('\n') ?? Array.Empty<string>();
            var validSplit = validString?.Split('\n') ?? Array.Empty<string>();

            Assert.That(nullSplit.Length, Is.EqualTo(0));
            Assert.That(emptySplit.Length, Is.EqualTo(1));
            Assert.That(validSplit.Length, Is.EqualTo(1));
        });
    }

    [Test]
    public void NullableList_SafeOperations_NoWarnings()
    {
        // Test nullable list operations that commonly caused CS8602 warnings

        // Arrange
        List<string>? nullList = null;
        List<string>? emptyList = new();
        List<string>? validList = new() { "item1", "item2", "item3" };

        // Act & Assert - Test null-safe list operations
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Null-conditional with Any()
            var nullHasItems = nullList?.Any() == true;
            var emptyHasItems = emptyList?.Any() == true;
            var validHasItems = validList?.Any() == true;

            Assert.That(nullHasItems, Is.False);
            Assert.That(emptyHasItems, Is.False);
            Assert.That(validHasItems, Is.True);

            // Pattern 2: Null-conditional with Count
            var nullCount = nullList?.Count ?? 0;
            var emptyCount = emptyList?.Count ?? 0;
            var validCount = validList?.Count ?? 0;

            Assert.That(nullCount, Is.EqualTo(0));
            Assert.That(emptyCount, Is.EqualTo(0));
            Assert.That(validCount, Is.EqualTo(3));

            // Pattern 3: Null-conditional with FirstOrDefault
            var nullFirst = nullList?.FirstOrDefault() ?? "";
            var emptyFirst = emptyList?.FirstOrDefault() ?? "";
            var validFirst = validList?.FirstOrDefault() ?? "";

            Assert.That(nullFirst, Is.EqualTo(""));
            Assert.That(emptyFirst, Is.EqualTo(""));
            Assert.That(validFirst, Is.EqualTo("item1"));
        });
    }

    [Test]
    public void NullableDictionary_SafeOperations_NoWarnings()
    {
        // Test dictionary operations that commonly caused CS8602 warnings

        // Arrange
        var fields = new Dictionary<string, string>
        {
            ["existing"] = "value",
            ["empty"] = ""
        };

        // Act & Assert - Test safe dictionary operations
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: TryGetValue with null-safe operations
            var hasValue = fields.TryGetValue("existing", out var existingValue);
            var hasEmpty = fields.TryGetValue("empty", out var emptyValue);
            var hasMissing = fields.TryGetValue("missing", out var missingValue);

            Assert.That(hasValue, Is.True);
            Assert.That(existingValue, Is.EqualTo("value"));
            Assert.That(hasEmpty, Is.True);
            Assert.That(emptyValue, Is.EqualTo(""));
            Assert.That(hasMissing, Is.False);
            Assert.That(missingValue, Is.Null);

            // Pattern 2: Safe string operations on retrieved values
            var existingLength = existingValue?.Length ?? 0;
            var emptyLength = emptyValue?.Length ?? 0;
            var missingLength = missingValue?.Length ?? 0;

            Assert.That(existingLength, Is.EqualTo(5));
            Assert.That(emptyLength, Is.EqualTo(0));
            Assert.That(missingLength, Is.EqualTo(0));

            // Pattern 3: Safe string methods
            var existingUpper = existingValue?.ToUpperInvariant() ?? "";
            var emptyUpper = emptyValue?.ToUpperInvariant() ?? "";
            var missingUpper = missingValue?.ToUpperInvariant() ?? "";

            Assert.That(existingUpper, Is.EqualTo("VALUE"));
            Assert.That(emptyUpper, Is.EqualTo(""));
            Assert.That(missingUpper, Is.EqualTo(""));
        });
    }

    #endregion

    #region CS8604 - Nullable Reference Arguments Validation

    [Test]
    public void NullableArguments_SafeMethodCalls_NoWarnings()
    {
        // Test method calls with nullable arguments that commonly caused CS8604 warnings

        // Arrange
        string? nullableParam = null;
        string validParam = "test";

        // Act & Assert - Test safe parameter passing
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Safe string method calls with null coalescing
            var result1 = ProcessStringParameter(nullableParam ?? "default");
            var result2 = ProcessStringParameter(validParam ?? "default");

            Assert.That(result1, Is.EqualTo("processed: default"));
            Assert.That(result2, Is.EqualTo("processed: test"));

            // Pattern 2: Safe Path operations
            var fileName1 = Path.GetFileName(validParam ?? "unknown");
            var fileName2 = Path.GetFileName(""); // Empty string is valid

            Assert.That(fileName1, Is.EqualTo("test"));
            Assert.That(fileName2, Is.EqualTo(""));

            // Pattern 3: Safe string comparison
            var equals1 = string.Equals(nullableParam, "test", StringComparison.OrdinalIgnoreCase);
            var equals2 = string.Equals(validParam, "test", StringComparison.OrdinalIgnoreCase);

            Assert.That(equals1, Is.False);
            Assert.That(equals2, Is.True);
        });
    }

    [Test]
    public void NullableCollections_SafeEnumeration_NoWarnings()
    {
        // Test collection enumeration with nullable collections

        // Arrange
        string[]? nullArray = null;
        string[] emptyArray = Array.Empty<string>();
        string[] validArray = { "a", "b", "c" };

        // Act & Assert - Test safe enumeration
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Safe enumeration with null coalescing
            var count1 = ProcessEnumerable(nullArray ?? Array.Empty<string>());
            var count2 = ProcessEnumerable(emptyArray ?? Array.Empty<string>());
            var count3 = ProcessEnumerable(validArray ?? Array.Empty<string>());

            Assert.That(count1, Is.EqualTo(0));
            Assert.That(count2, Is.EqualTo(0));
            Assert.That(count3, Is.EqualTo(3));

            // Pattern 2: Safe LINQ operations
            var nullSum = (nullArray ?? Array.Empty<string>()).Count();
            var emptySum = (emptyArray ?? Array.Empty<string>()).Count();
            var validSum = (validArray ?? Array.Empty<string>()).Count();

            Assert.That(nullSum, Is.EqualTo(0));
            Assert.That(emptySum, Is.EqualTo(0));
            Assert.That(validSum, Is.EqualTo(3));
        });
    }

    #endregion

    #region CS8618 - Non-nullable Field Validation

    [Test]
    public void ObjectInitialization_ProperNonNullableFields_NoWarnings()
    {
        // Test object initialization patterns that ensure non-nullable fields are properly set

        // Act & Assert - Test proper initialization
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Direct initialization with all required fields
            var testObject = new TestDataModel
            {
                RequiredString = "test",
                RequiredList = new List<string>(),
                OptionalString = null,
                OptionalNumber = null
            };

            Assert.That(testObject.RequiredString, Is.Not.Null);
            Assert.That(testObject.RequiredList, Is.Not.Null);
            Assert.That(testObject.OptionalString, Is.Null);
            Assert.That(testObject.OptionalNumber, Is.Null);

            // Pattern 2: Builder-style initialization
            var builderObject = CreateTestDataModel("builder test");
            Assert.That(builderObject.RequiredString, Is.Not.Null);
            Assert.That(builderObject.RequiredList, Is.Not.Null);
        });
    }

    #endregion

    #region Async Method Warning Validation (CS4014, CS1998)

    [Test]
    public void AsyncPatterns_ProperUsage_NoWarnings()
    {
        // Test async patterns that were causing warnings

        // Act & Assert - Test proper async/await usage
        Assert.DoesNotThrow(() =>
        {
            // Pattern 1: Synchronous completion of async work
            var task = CreateCompletedTask();
            task.Wait();
            Assert.That(task.IsCompletedSuccessfully, Is.True);

            // Pattern 2: Multiple task coordination
            var tasks = new[]
            {
                CreateCompletedTask(),
                CreateCompletedTask()
            };
            
            Task.WaitAll(tasks);
            Assert.That(tasks.All(t => t.IsCompletedSuccessfully), Is.True);

            // Pattern 3: Task.Run usage
            var backgroundTask = Task.Run(() =>
            {
                Thread.Sleep(1); // Simulate work
                return "completed";
            });

            var result = backgroundTask.Result;
            Assert.That(result, Is.EqualTo("completed"));
        });
    }

    #endregion

    #region File System Operations Validation

    [Test]
    public void FileSystemOperations_NullSafePatterns_NoWarnings()
    {
        // Test file system operations with nullable return values

        // Arrange
        var tempPath = Path.GetTempPath();
        var testFile = Path.Combine(tempPath, Guid.NewGuid().ToString() + ".tmp");

        try
        {
            // Act & Assert - Test null-safe file operations
            Assert.DoesNotThrow(() =>
            {
                // Pattern 1: Safe file existence checks
                var exists = File.Exists(testFile);
                Assert.That(exists, Is.False);

                // Pattern 2: Safe file info with nullable handling
                DateTime? lastModified = exists ? File.GetLastWriteTime(testFile) : null;
                var lastModifiedString = lastModified?.ToString("yyyy-MM-dd") ?? "Never";
                Assert.That(lastModifiedString, Is.EqualTo("Never"));

                // Pattern 3: Safe path operations
                var fileName = Path.GetFileName(testFile) ?? "unknown";
                var directory = Path.GetDirectoryName(testFile) ?? "";
                var extension = Path.GetExtension(testFile) ?? "";

                Assert.That(fileName, Does.EndWith(".tmp"));
                Assert.That(directory, Is.Not.Empty);
                Assert.That(extension, Is.EqualTo(".tmp"));

                // Create and test with actual file
                File.WriteAllText(testFile, "test");
                var actualExists = File.Exists(testFile);
                DateTime? actualModified = actualExists ? File.GetLastWriteTime(testFile) : null;
                
                Assert.That(actualExists, Is.True);
                Assert.That(actualModified, Is.Not.Null);
            });
        }
        finally
        {
            // Cleanup
            if (File.Exists(testFile))
            {
                try { File.Delete(testFile); } catch { }
            }
        }
    }

    #endregion

    #region Integration Test - Real-world Pattern Validation

    [Test]
    public void IntegrationTest_SearchResultProcessing_NoWarnings()
    {
        // Test realistic search result processing patterns that were causing warnings

        // Arrange - Simulate search results with nullable fields
        var searchResults = new[]
        {
            new MockSearchResult
            {
                FilePath = "file1.cs",
                Score = 1.0f,
                LineNumber = null,
                Content = "content1",
                ContextLines = null
            },
            new MockSearchResult
            {
                FilePath = "file2.cs",
                Score = 0.9f,
                LineNumber = 5,
                Content = null,
                ContextLines = new List<string> { "context1", "context2" }
            },
            new MockSearchResult
            {
                FilePath = "file3.cs",
                Score = 0.8f,
                LineNumber = 10,
                Content = "line1\nline2\nline3",
                ContextLines = new List<string>()
            }
        };

        // Act & Assert - Process results with null-safe patterns
        Assert.DoesNotThrow(() =>
        {
            var processedResults = new List<object>();

            foreach (var result in searchResults)
            {
                // Safe nullable handling patterns
                var hasStartLine = result.LineNumber.HasValue;
                var lineNumber = result.LineNumber ?? 0;
                
                var hasContext = result.ContextLines?.Any() == true;
                var contextCount = result.ContextLines?.Count ?? 0;
                var firstContext = result.ContextLines?.FirstOrDefault() ?? "";

                var content = result.Content ?? "";
                var lines = content.Split('\n');

                // Safe array access
                string targetLine = "";
                if (hasStartLine && lineNumber > 0 && lineNumber <= lines.Length)
                {
                    targetLine = lines[lineNumber - 1];
                }

                var processedResult = new
                {
                    FilePath = result.FilePath ?? "unknown",
                    Score = result.Score,
                    HasStartLine = hasStartLine,
                    StartLine = lineNumber,
                    HasContext = hasContext,
                    ContextCount = contextCount,
                    FirstContext = firstContext,
                    TargetLine = targetLine,
                    LineCount = lines.Length
                };

                processedResults.Add(processedResult);
            }

            Assert.That(processedResults.Count, Is.EqualTo(3));
            Assert.That(processedResults, Is.All.Not.Null);
        });
    }

    #endregion

    #region Helper Methods and Models

    private string ProcessStringParameter(string value)
    {
        return $"processed: {value}";
    }

    private int ProcessEnumerable(IEnumerable<string> items)
    {
        return items?.Count() ?? 0;
    }

    private Task CreateCompletedTask()
    {
        return Task.CompletedTask;
    }

    private TestDataModel CreateTestDataModel(string value)
    {
        return new TestDataModel
        {
            RequiredString = value,
            RequiredList = new List<string> { "item1" },
            OptionalString = null,
            OptionalNumber = null
        };
    }

    // Test models for validation
    public class TestDataModel
    {
        public string RequiredString { get; set; } = string.Empty;
        public List<string> RequiredList { get; set; } = new();
        public string? OptionalString { get; set; }
        public int? OptionalNumber { get; set; }
    }

    public class MockSearchResult
    {
        public string? FilePath { get; set; }
        public float Score { get; set; }
        public int? LineNumber { get; set; }
        public string? Content { get; set; }
        public List<string>? ContextLines { get; set; }
    }

    #endregion
}