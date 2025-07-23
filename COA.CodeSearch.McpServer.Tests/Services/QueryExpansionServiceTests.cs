using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class QueryExpansionServiceTests
{
    private readonly QueryExpansionService _service;
    private readonly Mock<ILogger<QueryExpansionService>> _mockLogger;
    
    public QueryExpansionServiceTests()
    {
        _mockLogger = new Mock<ILogger<QueryExpansionService>>();
        _service = new QueryExpansionService(_mockLogger.Object);
    }
    
    [Fact]
    public async Task ExpandQueryAsync_SimpleAuthTerm_ReturnsExpectedSynonyms()
    {
        // Arrange
        var query = "auth";
        var options = new QueryExpansionOptions();
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        Assert.Equal("auth", result.OriginalQuery);
        Assert.Contains("auth", result.WeightedTerms.Keys);
        Assert.Contains("authentication", result.WeightedTerms.Keys);
        Assert.Contains("login", result.WeightedTerms.Keys);
        Assert.Contains("jwt", result.WeightedTerms.Keys);
        
        // Original term should have highest weight
        Assert.Equal(1.0f, result.WeightedTerms["auth"]);
        
        // Synonyms should have lower weights
        Assert.True(result.WeightedTerms["authentication"] < 1.0f);
    }
    
    [Theory]
    [InlineData("UserService", new[] { "User", "Service", "user", "service", "userservice", "user-service", "user_service" })]
    [InlineData("getUserName", new[] { "get", "User", "Name", "getusername", "get-user-name", "get_user_name" })]
    [InlineData("API_KEY", new[] { "API", "KEY", "api", "key", "apikey", "api-key" })]
    [InlineData("file-name", new[] { "file", "name", "filename", "file_name" })]
    public void ExtractCodeTerms_VariousNamingConventions_ReturnsExpectedTerms(string identifier, string[] expectedTerms)
    {
        // Act
        var result = _service.ExtractCodeTerms(identifier);
        
        // Assert
        foreach (var expectedTerm in expectedTerms)
        {
            Assert.Contains(expectedTerm, result, StringComparer.OrdinalIgnoreCase);
        }
    }
    
    [Fact]
    public async Task ExpandQueryAsync_CodeIdentifier_ExtractsAndExpandsTerms()
    {
        // Arrange
        var query = "AuthenticationService";
        var options = new QueryExpansionOptions
        {
            EnableCodeTermExtraction = true,
            EnableSynonymExpansion = true
        };
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        // Should extract "Authentication" and "Service"
        Assert.Contains("authentication", result.ExtractedCodeTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("service", result.ExtractedCodeTerms, StringComparer.OrdinalIgnoreCase);
        
        // Should have weighted terms that include expansions
        Assert.True(result.WeightedTerms.Count > 3); // Original + extracted + some expansions
        
        // Original term should be included
        Assert.Contains("authenticationservice", result.WeightedTerms.Keys);
    }
    
    [Theory]
    [InlineData("auth", new[] { "authentication", "login", "signin", "jwt", "oauth", "security" })]
    [InlineData("database", new[] { "db", "sql", "entity", "repository", "data" })]
    [InlineData("api", new[] { "endpoint", "controller", "service", "http", "rest" })]
    [InlineData("nonexistent", new string[0])]
    public void GetSynonyms_VariousTerms_ReturnsExpectedSynonyms(string term, string[] expectedSynonyms)
    {
        // Act
        var result = _service.GetSynonyms(term);
        
        // Assert
        foreach (var expectedSynonym in expectedSynonyms)
        {
            Assert.Contains(expectedSynonym, result);
        }
    }
    
    [Fact]
    public async Task ExpandQueryAsync_WithMaxTermsLimit_RespectsLimit()
    {
        // Arrange
        var query = "auth";
        var options = new QueryExpansionOptions
        {
            MaxExpansionTerms = 3
        };
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        Assert.True(result.WeightedTerms.Count <= 3);
        
        // Should keep highest weighted terms
        Assert.Contains("auth", result.WeightedTerms.Keys); // Original should always be included
    }
    
    [Fact]
    public async Task ExpandQueryAsync_WithMinWeightFilter_FiltersLowWeightTerms()
    {
        // Arrange
        var query = "auth";
        var options = new QueryExpansionOptions
        {
            MinTermWeight = 0.7f
        };
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        // All terms should have weight >= 0.7
        Assert.All(result.WeightedTerms.Values, weight => Assert.True(weight >= 0.7f));
        
        // Original term should definitely be included
        Assert.Contains("auth", result.WeightedTerms.Keys);
    }
    
    [Fact]
    public async Task ExpandQueryAsync_DisabledExpansion_ReturnsOnlyOriginalTerm()
    {
        // Arrange
        var query = "auth";
        var options = new QueryExpansionOptions
        {
            EnableCodeTermExtraction = false,
            EnableSynonymExpansion = false
        };
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        Assert.Single(result.WeightedTerms);
        Assert.Contains("auth", result.WeightedTerms.Keys);
        Assert.Equal(1.0f, result.WeightedTerms["auth"]);
        Assert.Empty(result.ExtractedCodeTerms);
        Assert.Empty(result.SynonymTerms);
    }
    
    [Fact]
    public async Task ExpandQueryAsync_EmptyQuery_HandlesGracefully()
    {
        // Arrange
        var query = "";
        var options = new QueryExpansionOptions();
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        Assert.Equal("", result.OriginalQuery);
        Assert.Single(result.WeightedTerms);
        Assert.Contains("", result.WeightedTerms.Keys);
    }
    
    [Fact]
    public async Task ExpandQueryAsync_GeneratesValidLuceneQuery()
    {
        // Arrange
        var query = "auth";
        var options = new QueryExpansionOptions();
        
        // Act
        var result = await _service.ExpandQueryAsync(query, options);
        
        // Assert
        Assert.NotEmpty(result.ExpandedLuceneQuery);
        
        // Should contain OR operators for multiple terms
        if (result.WeightedTerms.Count > 1)
        {
            Assert.Contains(" OR ", result.ExpandedLuceneQuery);
        }
        
        // Should contain boost notations for weighted terms
        Assert.Contains("^", result.ExpandedLuceneQuery);
    }
    
    [Fact]
    public void ExtractCodeTerms_EmptyOrWhitespace_ReturnsEmpty()
    {
        // Act & Assert
        Assert.Empty(_service.ExtractCodeTerms(""));
        Assert.Empty(_service.ExtractCodeTerms("   "));
        Assert.Empty(_service.ExtractCodeTerms(null!));
    }
    
    [Fact]
    public void ExtractCodeTerms_SingleCharacter_ReturnsEmpty()
    {
        // Act
        var result = _service.ExtractCodeTerms("A");
        
        // Assert
        Assert.Empty(result);
    }
}