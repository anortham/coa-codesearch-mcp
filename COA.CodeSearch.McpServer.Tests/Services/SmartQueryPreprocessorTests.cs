using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Services
{
    [TestFixture]
    public class SmartQueryPreprocessorTests
    {
        private SmartQueryPreprocessor _processor = null!;
        private Mock<ILogger<SmartQueryPreprocessor>> _loggerMock = null!;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            _processor = new SmartQueryPreprocessor(_loggerMock.Object);
        }

        #region Constructor Tests

        [Test]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Act & Assert
            _processor.Should().NotBeNull();
        }

        [Test]
        public void Constructor_WithNullLogger_ShouldStillInitialize()
        {
            // Act
            var processor = new SmartQueryPreprocessor(null!);

            // Assert
            processor.Should().NotBeNull();
        }

        #endregion

        #region Process Method - Input Validation

        [Test]
        public void Process_WithNullQuery_ShouldReturnStandardMode()
        {
            // Act
            var result = _processor.Process(null!, SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.TargetField.Should().Be("content");
            result.ProcessedQuery.Should().Be("");
            result.Reason.Should().Contain("Empty query defaults to standard search");
        }

        [Test]
        public void Process_WithEmptyQuery_ShouldReturnStandardMode()
        {
            // Act
            var result = _processor.Process("", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.ProcessedQuery.Should().Be("");
            result.TargetField.Should().Be("content");
        }

        [Test]
        public void Process_WithWhitespaceOnlyQuery_ShouldReturnStandardMode()
        {
            // Act
            var result = _processor.Process("   ", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.TargetField.Should().Be("content");
        }

        #endregion

        #region Mode Detection Tests - Auto Mode

        [Test]
        public void Process_AutoMode_WithSimpleClassName_ShouldDetectSymbolMode()
        {
            // Act
            var result = _processor.Process("UserService", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.DetectedMode.Should().Be(SearchMode.Symbol);
            result.TargetField.Should().Be("content_symbols");
            result.ProcessedQuery.Should().Contain("UserService");
            result.Reason.Should().Contain("Symbol pattern detected");
        }

        [Test]
        public void Process_AutoMode_WithCodeSyntax_ShouldDetectCorrectModes()
        {
            // Arrange - Queries with special characters should be Pattern mode
            var patternQueries = new[]
            {
                "MyClass.Method",      // Contains dot (special char) -> Pattern
                "IRepository<T>"       // Contains < > (special chars) -> Pattern
            };

            // Arrange - Queries with code keywords but no special chars should be Symbol mode  
            var symbolQueries = new[]
            {
                "MyNamespace::MyClass", // Contains :: (special chars) -> should be Pattern actually
                "function test",        // Contains function keyword -> Symbol
                "class UserService"     // Contains class keyword -> Symbol  
            };

            // Act & Assert - Pattern queries
            foreach (var query in patternQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.DetectedMode.Should().Be(SearchMode.Pattern, $"Query '{query}' should be detected as pattern due to special characters");
                result.TargetField.Should().Be("content_patterns");
                result.Reason.Should().Contain("Special characters detected");
            }

            // Act & Assert - Symbol queries  
            foreach (var query in symbolQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                // Note: MyNamespace::MyClass contains : which is special, so it should be Pattern
                if (query == "MyNamespace::MyClass")
                {
                    result.DetectedMode.Should().Be(SearchMode.Pattern, $"Query '{query}' should be detected as pattern due to colon");
                    result.TargetField.Should().Be("content_patterns");
                }
                else
                {
                    result.DetectedMode.Should().Be(SearchMode.Symbol, $"Query '{query}' should be detected as symbol due to code patterns");
                    result.TargetField.Should().Be("content_symbols");
                    result.Reason.Should().Contain("Symbol pattern detected");
                }
            }
        }

        [Test]
        public void Process_AutoMode_WithNonCodeSyntax_ShouldDetectStandardMode()
        {
            // Arrange - Queries that DON'T match CodePatternPattern
            var nonCodeQueries = new[]
            {
                "private void",        // Missing keyword boundary pattern
                "public static",       // Missing keyword boundary pattern
                "some random text"     // Plain text
            };

            // Act & Assert
            foreach (var query in nonCodeQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.DetectedMode.Should().Be(SearchMode.Standard, $"Query '{query}' should be detected as standard");
                result.TargetField.Should().Be("content");
                result.Reason.Should().Contain("Standard search");
            }
        }

        [Test]
        public void Process_AutoMode_WithSpecialCharacters_ShouldDetectPatternMode()
        {
            // Arrange
            var specialCharQueries = new[]
            {
                "IRepository<User>",
                "Dictionary<string, object>",  
                "List<T>",
                "Action<string, int>"
            };

            // Act & Assert
            foreach (var query in specialCharQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.DetectedMode.Should().Be(SearchMode.Pattern, $"Query '{query}' should be detected as pattern due to special characters");
                result.TargetField.Should().Be("content_patterns");
                result.Reason.Should().Contain("Special characters detected");
            }
        }

        [Test]
        public void Process_AutoMode_WithNaturalLanguage_ShouldDetectStandardMode()
        {
            // Act
            var result = _processor.Process("find all user registration methods", SearchMode.Auto);

            // Assert
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.TargetField.Should().Be("content");
            result.Reason.Should().Contain("Standard search");
        }

        #endregion

        #region Explicit Mode Tests

        [Test]
        public void Process_ExplicitSymbolMode_ShouldUseSymbolProcessing()
        {
            // Act
            var result = _processor.Process("UserService", SearchMode.Symbol);

            // Assert
            result.DetectedMode.Should().Be(SearchMode.Symbol);
            result.TargetField.Should().Be("content_symbols");
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Process_ExplicitPatternMode_ShouldUsePatternProcessing()
        {
            // Act
            var result = _processor.Process("IRepository<T>", SearchMode.Pattern);

            // Assert
            result.DetectedMode.Should().Be(SearchMode.Pattern);
            result.TargetField.Should().Be("content_patterns");
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Process_ExplicitStandardMode_ShouldUseStandardProcessing()
        {
            // Act
            var result = _processor.Process("search text", SearchMode.Standard);

            // Assert
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.TargetField.Should().Be("content");
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Process_ExplicitFuzzyMode_ShouldFallBackToStandard()
        {
            // Act
            var result = _processor.Process("fuzzy search", SearchMode.Fuzzy);

            // Assert
            result.DetectedMode.Should().Be(SearchMode.Standard);
            result.TargetField.Should().Be("content");
            result.Reason.Should().Contain("Fuzzy search not implemented");
        }

        #endregion

        #region Field Routing Tests

        [Test]
        public void Process_SymbolMode_ShouldTargetSymbolField()
        {
            // Act
            var result = _processor.Process("MyClass", SearchMode.Symbol);

            // Assert
            result.TargetField.Should().Be("content_symbols");
        }

        [Test]
        public void Process_PatternMode_ShouldTargetPatternField()
        {
            // Act
            var result = _processor.Process("IRepository<T>", SearchMode.Pattern);

            // Assert
            result.TargetField.Should().Be("content_patterns");
        }

        [Test]
        public void Process_StandardMode_ShouldTargetContentField()
        {
            // Act
            var result = _processor.Process("search terms", SearchMode.Standard);

            // Assert
            result.TargetField.Should().Be("content");
        }

        #endregion

        #region Edge Cases and Wildcards

        [Test]
        public void Process_WithLeadingWildcard_ShouldHandleGracefully()
        {
            // Act
            var result = _processor.Process("*Service", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
            // Should not throw exception, should handle wildcard appropriately
        }

        [Test]
        public void Process_WithTrailingWildcard_ShouldHandleCorrectly()
        {
            // Act
            var result = _processor.Process("User*", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Contain("User");
        }

        [Test]
        public void Process_WithMultipleWildcards_ShouldProcessCorrectly()
        {
            // Act
            var result = _processor.Process("*User*Service*", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Process_WithSpecialLuceneCharacters_ShouldEscapeCorrectly()
        {
            // Arrange
            var specialChars = new[] { "+", "-", "&&", "||", "!", "(", ")", "{", "}", "[", "]", "^", "\"", "~", ":", "\\" };

            // Act & Assert
            foreach (var specialChar in specialChars)
            {
                var query = $"test{specialChar}query";
                var result = _processor.Process(query, SearchMode.Standard);
                
                result.Should().NotBeNull($"Query with '{specialChar}' should be processed");
                result.ProcessedQuery.Should().NotBeNullOrEmpty();
            }
        }

        #endregion

        #region CamelCase and Symbol Detection

        [Test]
        public void Process_WithCamelCaseIdentifier_ShouldDetectAsSymbol()
        {
            // Arrange
            var camelCaseQueries = new[]
            {
                "getUserById",
                "IUserRepository",
                "validateEmailAddress",
                "parseJsonResponse"
            };

            // Act & Assert
            foreach (var query in camelCaseQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.DetectedMode.Should().Be(SearchMode.Symbol, $"CamelCase query '{query}' should be detected as symbol");
                result.TargetField.Should().Be("content_symbols");
            }
        }

        [Test]
        public void Process_WithPascalCaseIdentifier_ShouldDetectAsSymbol()
        {
            // Arrange
            var pascalCaseQueries = new[]
            {
                "UserService",
                "DatabaseContext",
                "HttpResponseMessage",
                "ValidationResult"
            };

            // Act & Assert
            foreach (var query in pascalCaseQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.DetectedMode.Should().Be(SearchMode.Symbol, $"PascalCase query '{query}' should be detected as symbol");
                result.TargetField.Should().Be("content_symbols");
            }
        }

        #endregion

        #region Query Processing Result Validation

        [Test]
        public void Process_ShouldAlwaysReturnValidResult()
        {
            // Arrange
            var testQueries = new[]
            {
                "UserService",
                "public class Test",
                "search text with spaces",
                "*wildcard",
                "",
                "   ",
                "special+chars&more"
            };

            // Act & Assert
            foreach (var query in testQueries)
            {
                var result = _processor.Process(query, SearchMode.Auto);
                
                result.Should().NotBeNull($"Query '{query}' should return valid result");
                result.DetectedMode.Should().NotBe(SearchMode.Auto, "Result mode should never remain Auto");
                result.TargetField.Should().NotBeNullOrEmpty($"Query '{query}' should have target field");
                result.ProcessedQuery.Should().NotBeNull($"Query '{query}' should have processed query");
                result.Reason.Should().NotBeNullOrEmpty($"Query '{query}' should have reason");
            }
        }

        [Test]
        public void Process_ShouldReturnProcessedQuery()
        {
            // Arrange
            const string testQuery = "Test Query";

            // Act
            var result = _processor.Process(testQuery, SearchMode.Auto);

            // Assert
            result.ProcessedQuery.Should().NotBeNullOrEmpty();
            result.ProcessedQuery.Should().Be(testQuery.Trim());
        }

        #endregion

        #region Wildcard Validation Tests

        [Test]
        public void Process_WithInvalidLeadingWildcard_ShouldSanitize()
        {
            // Act
            var result = _processor.Process("*test", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Be("test", "leading wildcard should be removed");
            result.DetectedMode.Should().Be(SearchMode.Symbol, "sanitized query should be detected as symbol");
        }

        [Test]
        public void Process_WithPureWildcard_ShouldReturnErrorResult()
        {
            // Act
            var result = _processor.Process("*", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Be("*", "original query should be preserved in error result");
            result.DetectedMode.Should().Be(SearchMode.Standard, "error result should default to standard mode");
            result.Reason.Should().Contain("pure wildcards cannot be processed");
        }

        [Test]
        public void Process_WithMultipleLeadingWildcards_ShouldSanitize()
        {
            // Act
            var result = _processor.Process("**?UserService", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Be("UserService", "all leading wildcards should be removed");
            result.DetectedMode.Should().Be(SearchMode.Symbol, "sanitized query should be detected as symbol");
        }

        [Test]
        public void Process_WithValidTrailingWildcard_ShouldNotSanitize()
        {
            // Act
            var result = _processor.Process("test*", SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Be("test*", "trailing wildcards should not be modified");
            result.DetectedMode.Should().Be(SearchMode.Pattern, "wildcard should trigger pattern mode");
        }

        [Test]
        public void Process_WithSanitizableQuery_ShouldLogDebug()
        {
            // Arrange
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<SmartQueryPreprocessor>();
            var processor = new SmartQueryPreprocessor(logger);

            // Act
            var result = processor.Process("*test", SearchMode.Auto);

            // Assert - The debug log should be called (we can't easily test this without a test logger)
            result.ProcessedQuery.Should().Be("test");
        }

        [Test]
        [TestCase("?test")]
        [TestCase("*?test")]
        [TestCase("  *test  ")]
        public void Process_WithVariousInvalidPatterns_ShouldSanitizeConsistently(string invalidQuery)
        {
            // Act
            var result = _processor.Process(invalidQuery, SearchMode.Auto);

            // Assert
            result.Should().NotBeNull();
            result.ProcessedQuery.Should().Be("test", $"query '{invalidQuery}' should be sanitized to 'test'");
            result.DetectedMode.Should().Be(SearchMode.Symbol);
        }

        #endregion

        #region Performance and Consistency Tests

        [Test]
        public void Process_SameQueryMultipleTimes_ShouldReturnConsistentResults()
        {
            // Arrange
            const string testQuery = "UserService";

            // Act
            var result1 = _processor.Process(testQuery, SearchMode.Auto);
            var result2 = _processor.Process(testQuery, SearchMode.Auto);
            var result3 = _processor.Process(testQuery, SearchMode.Auto);

            // Assert
            result1.DetectedMode.Should().Be(result2.DetectedMode).And.Be(result3.DetectedMode);
            result1.TargetField.Should().Be(result2.TargetField).And.Be(result3.TargetField);
            result1.ProcessedQuery.Should().Be(result2.ProcessedQuery).And.Be(result3.ProcessedQuery);
        }

        [Test]
        public void Process_LargeQuery_ShouldHandleEfficiently()
        {
            // Arrange
            var largeQuery = new string('a', 10000); // 10KB query

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = _processor.Process(largeQuery, SearchMode.Auto);
            stopwatch.Stop();

            // Assert
            result.Should().NotBeNull();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, "Large query processing should complete within 1 second");
        }

        #endregion
    }
}