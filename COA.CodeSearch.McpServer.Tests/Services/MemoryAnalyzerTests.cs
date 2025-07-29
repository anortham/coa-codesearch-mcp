using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

/// <summary>
/// Unit tests for MemoryAnalyzer - custom Lucene analyzer for memory search optimization
/// </summary>
public class MemoryAnalyzerTests
{
    private readonly Mock<ILogger<MemoryAnalyzer>> _loggerMock;
    private readonly MemoryAnalyzer _analyzer;

    public MemoryAnalyzerTests()
    {
        _loggerMock = new Mock<ILogger<MemoryAnalyzer>>();
        _analyzer = new MemoryAnalyzer(_loggerMock.Object);
    }

    [Theory]
    [InlineData("authentication", new[] { "authent", "auth" })]
    [InlineData("authorization", new[] { "author", "auth" })]
    [InlineData("auth", new[] { "auth", "authent", "author" })]
    [InlineData("bug", new[] { "bug", "issu", "defect", "problem", "error" })]
    [InlineData("performance", new[] { "perform", "perf", "speed", "optim" })]
    [InlineData("database", new[] { "databas", "db", "sql", "datastor" })]
    [InlineData("api", new[] { "api", "endpoint", "servic", "interfac" })]
    public void Analyze_WithSynonyms_ExpandsCorrectly(string input, string[] expectedTokens)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        // Don't check exact count since synonym expansion can create more tokens
        foreach (var expected in expectedTokens)
        {
            Assert.Contains(expected, tokens);
        }
    }

    [Theory]
    [InlineData("running", "run")]
    [InlineData("tested", "test")]
    [InlineData("testing", "test")]
    [InlineData("optimization", "optim")]
    [InlineData("optimized", "optim")]
    [InlineData("configured", "configur")]
    [InlineData("configuration", "configur")]
    public void Analyze_WithStemming_StemsCorrectly(string input, string expectedStem)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        Assert.Contains(expectedStem, tokens);
    }

    [Theory]
    [InlineData("the quick brown fox", new[] { "quick", "brown", "fox" })]
    [InlineData("this is a test", new[] { "test" })]
    [InlineData("for and or but", new string[] { })]
    [InlineData("with without within", new[] { "without", "within" })]
    public void Analyze_WithStopWords_RemovesCommonWords(string input, string[] expectedTokens)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        // Should contain expected tokens, but might have more due to synonyms
        foreach (var expected in expectedTokens)
        {
            Assert.Contains(expected, tokens);
        }
        // If expecting empty array, verify it's actually empty
        if (expectedTokens.Length == 0)
        {
            Assert.Empty(tokens);
        }
    }

    [Theory]
    [InlineData("UPPERCASE", "uppercas")]
    [InlineData("MixedCase", "mixedcas")]
    [InlineData("camelCase", "camelcas")]
    [InlineData("PascalCase", "pascalcas")]
    public void Analyze_WithCaseVariations_NormalizesToLowercase(string input, string expected)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        Assert.Contains(expected, tokens);
    }

    [Theory]
    [InlineData("user-friendly", new[] { "user", "friendli" })]
    [InlineData("high-performance", new[] { "high", "perform", "perf", "speed", "optim" })]
    [InlineData("re-index", new[] { "re", "index" })]
    [InlineData("UTF-8", new[] { "utf", "8" })]
    public void Analyze_WithHyphenatedWords_TokenizesCorrectly(string input, string[] expectedTokens)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        foreach (var expected in expectedTokens)
        {
            Assert.Contains(expected, tokens);
        }
    }

    [Theory]
    [InlineData("UserService.GetUser()", new[] { "userservice.getus" })]
    [InlineData("file.txt", new[] { "file.txt" })]
    [InlineData("hello@example.com", new[] { "hello", "example.com" })]
    [InlineData("192.168.1.1", new[] { "192.168.1.1" })]
    public void Analyze_WithSpecialCharacters_TokenizesCorrectly(string input, string[] expectedTokens)
    {
        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        foreach (var expected in expectedTokens)
        {
            Assert.Contains(expected, tokens);
        }
    }

    [Fact]
    public void Analyze_WithMemoryTypeField_AppliesKeywordAnalysis()
    {
        // Arrange
        var input = "TechnicalDebt";

        // Act
        var tokens = AnalyzeText(input, "memoryType");

        // Assert
        Assert.Single(tokens);
        Assert.Equal("technicaldebt", tokens[0]); // Keyword analyzer preserves as single token, lowercase
    }

    [Fact]
    public void Analyze_WithComplexText_HandlesMultipleFeaturesCorrectly()
    {
        // Arrange
        var input = "The authentication system has PERFORMANCE issues - needs optimization!";

        // Act
        var tokens = AnalyzeText(input, "content");

        // Assert
        // Should have expanded synonyms for auth, performance (with stemming)
        Assert.Contains("authent", tokens); // stemmed from authentication
        Assert.Contains("auth", tokens);
        Assert.Contains("system", tokens);
        Assert.Contains("perform", tokens); // stemmed from performance
        Assert.Contains("perf", tokens);
        Assert.Contains("issu", tokens); // stemmed from issues
        Assert.Contains("need", tokens); // stemmed from needs
        Assert.Contains("optim", tokens); // stemmed from optimization

        // Should NOT contain stop words
        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("has", tokens);
    }

    [Fact]
    public void Analyze_EmptyText_ReturnsNoTokens()
    {
        // Act
        var tokens = AnalyzeText("", "content");

        // Assert
        Assert.Empty(tokens);
    }

    [Fact]
    public void Analyze_NullField_UsesDefaultAnalysis()
    {
        // Act
        var tokens = AnalyzeText("test content", null!);

        // Assert
        Assert.Contains("test", tokens);
        Assert.Contains("content", tokens);
    }

    [Theory]
    [InlineData("memoryType")]
    [InlineData("id")]
    [InlineData("sessionId")]
    [InlineData("isShared")]
    [InlineData("status")]
    public void Analyze_KeywordFields_PreservesAsWholeToken(string fieldName)
    {
        // Arrange
        var input = "Complex Value With Spaces";

        // Act
        var tokens = AnalyzeText(input, fieldName);

        // Assert
        Assert.Single(tokens);
        Assert.Equal("complex value with spaces", tokens[0]); // Lowercase but preserved as single token
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var analyzer = new MemoryAnalyzer(_loggerMock.Object);

        // Act & Assert - should not throw
        analyzer.Dispose();
        analyzer.Dispose();
    }

    [Fact]
    public void Constructor_LogsInitialization()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MemoryAnalyzer>>();
        
        // Act
        var analyzer = new MemoryAnalyzer(mockLogger.Object);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MemoryAnalyzer initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private List<string> AnalyzeText(string text, string fieldName)
    {
        var tokens = new List<string>();
        
        using (var tokenStream = _analyzer.GetTokenStream(fieldName ?? "content", text))
        {
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
            tokenStream.Reset();
            
            while (tokenStream.IncrementToken())
            {
                tokens.Add(termAttr.ToString());
            }
            
            tokenStream.End();
        }
        
        return tokens;
    }
}