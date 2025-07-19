using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class TokenLimitTests : TestBase
{
    private readonly IResponseSizeEstimator _sizeEstimator;
    private readonly IResultTruncator _truncator;
    private readonly IOptions<ResponseLimitOptions> _options;

    public TokenLimitTests()
    {
        _sizeEstimator = ServiceProvider.GetRequiredService<IResponseSizeEstimator>();
        _truncator = ServiceProvider.GetRequiredService<IResultTruncator>();
        _options = ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>();
    }

    [Fact]
    public void ResponseSizeEstimator_Should_Estimate_Token_Count()
    {
        // Arrange
        var testObject = new
        {
            name = "Test",
            description = "This is a test object with some content to estimate token size",
            items = Enumerable.Range(1, 10).Select(i => new { id = i, value = $"Item {i}" })
        };

        // Act
        var tokens = _sizeEstimator.EstimateTokens(testObject);

        // Assert
        tokens.Should().BeGreaterThan(0);
        tokens.Should().BeLessThan(1000); // Reasonable upper bound for this small object
    }

    [Fact]
    public void ResultTruncator_Should_Truncate_Large_Result_Sets()
    {
        // Arrange
        var largeResults = Enumerable.Range(1, 1000)
            .Select(i => new
            {
                id = i,
                name = $"Item {i}",
                description = $"This is a detailed description for item {i} with enough text to consume tokens",
                metadata = new
                {
                    created = DateTime.Now,
                    modified = DateTime.Now,
                    tags = new[] { "tag1", "tag2", "tag3" }
                }
            })
            .ToList();

        // Act
        var truncated = _truncator.TruncateResults(largeResults, maxTokens: 5000);

        // Assert
        truncated.IsTruncated.Should().BeTrue();
        truncated.ReturnedCount.Should().BeLessThan(truncated.TotalCount);
        truncated.TruncationReason.Should().Contain("token");
        truncated.Results.Should().NotBeEmpty();
    }

    [Fact]
    public void ResultTruncator_Should_Not_Truncate_Small_Result_Sets()
    {
        // Arrange
        var smallResults = Enumerable.Range(1, 5)
            .Select(i => new { id = i, name = $"Item {i}" })
            .ToList();

        // Act
        var truncated = _truncator.TruncateResults(smallResults, maxTokens: 5000);

        // Assert
        truncated.IsTruncated.Should().BeFalse();
        truncated.ReturnedCount.Should().Be(truncated.TotalCount);
        truncated.Results.Count.Should().Be(5);
    }

    [Fact]
    public void ResponseLimitOptions_Should_Return_Tool_Specific_Limits()
    {
        // Arrange
        var options = new ResponseLimitOptions
        {
            MaxTokens = 20000,
            ToolSpecificLimits = new Dictionary<string, int>
            {
                ["RenameSymbolTool"] = 15000,
                ["ProjectStructureAnalysisTool"] = 10000
            }
        };

        // Act & Assert
        options.GetTokenLimitForTool("RenameSymbolTool").Should().Be(15000);
        options.GetTokenLimitForTool("UnknownTool").Should().Be(20000); // Falls back to default
    }

    [Fact]
    public void McpToolResponse_Should_Include_Metadata()
    {
        // Arrange & Act
        var response = new McpToolResponse<string>
        {
            Success = true,
            Data = "Test data",
            Metadata = new ResponseMetadata
            {
                TotalResults = 100,
                ReturnedResults = 50,
                IsTruncated = true,
                TruncationReason = "Response size limit reached",
                EstimatedTokens = 15000
            }
        };

        // Assert
        response.Metadata.IsTruncated.Should().BeTrue();
        response.Metadata.TotalResults.Should().Be(100);
        response.Metadata.ReturnedResults.Should().Be(50);
        response.Metadata.EstimatedTokens.Should().Be(15000);
    }
}