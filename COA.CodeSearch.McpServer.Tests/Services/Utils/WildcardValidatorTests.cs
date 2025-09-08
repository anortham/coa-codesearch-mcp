using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Services.Utils;

namespace COA.CodeSearch.McpServer.Tests.Services.Utils
{
    [TestFixture]
    public class WildcardValidatorTests
    {
        #region IsInvalidWildcardQuery Tests

        [Test]
        public void IsInvalidWildcardQuery_WithNullQuery_ShouldReturnFalse()
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery(null!);

            // Assert
            result.Should().BeFalse("null queries should be handled by other validation");
        }

        [Test]
        public void IsInvalidWildcardQuery_WithEmptyQuery_ShouldReturnFalse()
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery("");

            // Assert
            result.Should().BeFalse("empty queries should be handled by other validation");
        }

        [Test]
        public void IsInvalidWildcardQuery_WithWhitespaceQuery_ShouldReturnFalse()
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery("   ");

            // Assert
            result.Should().BeFalse("whitespace-only queries should be handled by other validation");
        }

        [Test]
        [TestCase("*test")]
        [TestCase("*")]
        [TestCase("*UserService")]
        public void IsInvalidWildcardQuery_WithLeadingAsterisk_ShouldReturnTrue(string query)
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery(query);

            // Assert
            result.Should().BeTrue($"query '{query}' has leading asterisk which is invalid in Lucene");
        }

        [Test]
        [TestCase("?test")]
        [TestCase("?")]
        [TestCase("?UserService")]
        public void IsInvalidWildcardQuery_WithLeadingQuestionMark_ShouldReturnTrue(string query)
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery(query);

            // Assert
            result.Should().BeTrue($"query '{query}' has leading question mark which is invalid in Lucene");
        }

        [Test]
        [TestCase("*")]
        [TestCase("?")]
        [TestCase("***")]
        [TestCase("???")]
        [TestCase("*?*")]
        [TestCase("  *  ")]
        [TestCase("  ?  ")]
        public void IsInvalidWildcardQuery_WithPureWildcards_ShouldReturnTrue(string query)
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery(query);

            // Assert
            result.Should().BeTrue($"query '{query}' contains only wildcards which is invalid");
        }

        [Test]
        [TestCase("test*")]
        [TestCase("UserService*")]
        [TestCase("test?")]
        [TestCase("test*.cs")]
        [TestCase("User*Service")]
        [TestCase("test")]
        [TestCase("UserService")]
        public void IsInvalidWildcardQuery_WithValidQueries_ShouldReturnFalse(string query)
        {
            // Act
            var result = WildcardValidator.IsInvalidWildcardQuery(query);

            // Assert
            result.Should().BeFalse($"query '{query}' should be valid");
        }

        #endregion

        #region SanitizeWildcardQuery Tests

        [Test]
        public void SanitizeWildcardQuery_WithNullQuery_ShouldReturnNull()
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery(null!);

            // Assert
            result.Should().BeNull();
        }

        [Test]
        public void SanitizeWildcardQuery_WithEmptyQuery_ShouldReturnEmpty()
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery("");

            // Assert
            result.Should().Be("");
        }

        [Test]
        public void SanitizeWildcardQuery_WithWhitespace_ShouldReturnWhitespace()
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery("   ");

            // Assert
            result.Should().Be("   ");
        }

        [Test]
        [TestCase("*test", "test")]
        [TestCase("**test", "test")]
        [TestCase("?test", "test")]
        [TestCase("??test", "test")]
        [TestCase("*?test", "test")]
        [TestCase("  *test  ", "test")]
        public void SanitizeWildcardQuery_WithLeadingWildcards_ShouldRemoveThem(string input, string expected)
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery(input);

            // Assert
            result.Should().Be(expected);
        }

        [Test]
        [TestCase("*")]
        [TestCase("?")]
        [TestCase("***")]
        [TestCase("*?*")]
        [TestCase("  *  ")]
        public void SanitizeWildcardQuery_WithPureWildcards_ShouldReturnNull(string query)
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery(query);

            // Assert
            result.Should().BeNull($"pure wildcard query '{query}' cannot be sanitized");
        }

        [Test]
        [TestCase("test*")]
        [TestCase("UserService")]
        [TestCase("test*.cs")]
        public void SanitizeWildcardQuery_WithValidQueries_ShouldReturnUnchanged(string query)
        {
            // Act
            var result = WildcardValidator.SanitizeWildcardQuery(query);

            // Assert
            result.Should().Be(query.Trim());
        }

        #endregion

        #region HasSafeWildcardUsage Tests

        [Test]
        public void HasSafeWildcardUsage_WithNullQuery_ShouldReturnTrue()
        {
            // Act
            var result = WildcardValidator.HasSafeWildcardUsage(null!);

            // Assert
            result.Should().BeTrue("null queries are considered safe");
        }

        [Test]
        public void HasSafeWildcardUsage_WithEmptyQuery_ShouldReturnTrue()
        {
            // Act
            var result = WildcardValidator.HasSafeWildcardUsage("");

            // Assert
            result.Should().BeTrue("empty queries are considered safe");
        }

        [Test]
        [TestCase("*test")]
        [TestCase("?test")]
        [TestCase("*")]
        [TestCase("?")]
        public void HasSafeWildcardUsage_WithUnsafePatterns_ShouldReturnFalse(string query)
        {
            // Act
            var result = WildcardValidator.HasSafeWildcardUsage(query);

            // Assert
            result.Should().BeFalse($"query '{query}' has unsafe wildcard usage");
        }

        [Test]
        [TestCase("test*")]
        [TestCase("test?")]
        [TestCase("User*Service")]
        [TestCase("test")]
        [TestCase("UserService")]
        [TestCase("te*st")]
        [TestCase("te?st")]
        public void HasSafeWildcardUsage_WithSafePatterns_ShouldReturnTrue(string query)
        {
            // Act
            var result = WildcardValidator.HasSafeWildcardUsage(query);

            // Assert
            result.Should().BeTrue($"query '{query}' has safe wildcard usage");
        }

        #endregion

        #region Edge Cases and Integration Tests

        [Test]
        public void AllMethods_ShouldHandleWhitespaceConsistently()
        {
            // Arrange
            var queries = new[] { "  *test  ", "  test*  ", "  test  " };

            // Act & Assert
            foreach (var query in queries)
            {
                // All methods should handle whitespace trimming consistently
                var isInvalid = WildcardValidator.IsInvalidWildcardQuery(query);
                var sanitized = WildcardValidator.SanitizeWildcardQuery(query);
                var isSafe = WildcardValidator.HasSafeWildcardUsage(query);

                // The trimmed versions should behave the same as untrimmed for validation
                var trimmed = query.Trim();
                var isInvalidTrimmed = WildcardValidator.IsInvalidWildcardQuery(trimmed);
                var isSafeTrimmed = WildcardValidator.HasSafeWildcardUsage(trimmed);

                isInvalid.Should().Be(isInvalidTrimmed, $"IsInvalidWildcardQuery should handle whitespace consistently for '{query}'");
                isSafe.Should().Be(isSafeTrimmed, $"HasSafeWildcardUsage should handle whitespace consistently for '{query}'");
            }
        }

        [Test]
        public void SanitizeAndValidate_ShouldWorkTogether()
        {
            // Arrange
            var problematicQueries = new[] { "*test", "**test", "?test", "*?test" };

            // Act & Assert
            foreach (var query in problematicQueries)
            {
                var isInvalid = WildcardValidator.IsInvalidWildcardQuery(query);
                var sanitized = WildcardValidator.SanitizeWildcardQuery(query);

                isInvalid.Should().BeTrue($"'{query}' should be detected as invalid");
                
                if (sanitized != null)
                {
                    var sanitizedIsInvalid = WildcardValidator.IsInvalidWildcardQuery(sanitized);
                    sanitizedIsInvalid.Should().BeFalse($"sanitized query '{sanitized}' should be valid");
                }
            }
        }

        #endregion
    }
}