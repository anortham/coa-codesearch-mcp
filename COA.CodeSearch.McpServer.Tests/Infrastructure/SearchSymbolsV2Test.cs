using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class SearchSymbolsV2Test : TestBase
{
    private readonly SearchSymbolsToolV2 _tool;

    public SearchSymbolsV2Test()
    {
        _tool = new SearchSymbolsToolV2(
            ServiceProvider.GetRequiredService<ILogger<SearchSymbolsToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Symbol_Search()
    {
        // Arrange
        var testProjectPath = GetTestProjectPath();
        
        // Act - search for Test pattern
        var result = await _tool.ExecuteAsync(
            pattern: "*Test*",
            workspacePath: testProjectPath,
            kinds: null,
            fuzzy: false,
            maxResults: 100,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Console.WriteLine("=== AI-OPTIMIZED SYMBOL SEARCH ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("search_symbols");
        
        // Check query
        var query = response.GetProperty("query");
        query.GetProperty("pattern").GetString().Should().Be("*Test*");
        query.GetProperty("fuzzy").GetBoolean().Should().BeFalse();
        query.GetProperty("mode").GetString().Should().Be("wildcard");
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("projects").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("types").Should().NotBeNull();
        
        // Check distribution
        response.GetProperty("distribution").Should().NotBeNull();
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        Console.WriteLine("\nInsights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        Console.WriteLine("\nActions:");
        foreach (var action in actions.EnumerateArray())
        {
            var id = action.GetProperty("id").GetString();
            var priority = action.GetProperty("priority").GetString();
            Console.WriteLine($"- [{priority}] {id}");
        }
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("cached").GetString().Should().StartWith("sym_");
    }

    [Fact]
    public async Task Should_Use_Fuzzy_Matching()
    {
        // Arrange
        var testProjectPath = GetTestProjectPath();
        
        // Act - fuzzy search for "Tst" should find "Test"
        var result = await _tool.ExecuteAsync(
            pattern: "Tst",
            workspacePath: testProjectPath,
            kinds: null,
            fuzzy: true,
            maxResults: 50);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Console.WriteLine("=== FUZZY SEARCH RESULTS ===");
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("search_symbols");
        
        // Check query indicates fuzzy mode
        var query = response.GetProperty("query");
        query.GetProperty("fuzzy").GetBoolean().Should().BeTrue();
        query.GetProperty("mode").GetString().Should().Be("fuzzy");
        
        // Should find symbols with "Test" in the name
        var summary = response.GetProperty("summary");
        var total = summary.GetProperty("total").GetInt32();
        Console.WriteLine($"\nFound {total} symbols with fuzzy match 'Tst'");
        
        // Check if we have type distribution
        if (summary.TryGetProperty("types", out var types))
        {
            Console.WriteLine("\nType distribution:");
            foreach (var type in types.EnumerateObject())
            {
                var typeInfo = type.Value;
                var count = typeInfo.GetProperty("count").GetInt32();
                Console.WriteLine($"- {type.Name}: {count}");
            }
        }
    }

    [Fact]
    public async Task Should_Filter_By_Symbol_Type()
    {
        // Arrange
        var testProjectPath = GetTestProjectPath();
        
        // Act - search only for classes and interfaces
        var result = await _tool.ExecuteAsync(
            pattern: "*",
            workspacePath: testProjectPath,
            kinds: new[] { "class", "interface" },
            fuzzy: false,
            maxResults: 50);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check type distribution - should only have NamedType
        var summary = response.GetProperty("summary");
        if (summary.TryGetProperty("types", out var types))
        {
            Console.WriteLine("\nFiltered symbol types:");
            foreach (var type in types.EnumerateObject())
            {
                Console.WriteLine($"- {type.Name}");
                // Should only be NamedType (which includes class, interface, enum, struct)
                type.Name.Should().Be("namedtype");
            }
        }
        
        // Check for filter-related insights
        var insights = response.GetProperty("insights");
        Console.WriteLine("\nFilter insights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }
    }

    [Fact]
    public async Task Should_Handle_No_Results()
    {
        // Arrange
        var testProjectPath = GetTestProjectPath();
        
        // Act - search for non-existent pattern
        var result = await _tool.ExecuteAsync(
            pattern: "XyzNonExistentSymbol",
            workspacePath: testProjectPath,
            kinds: null,
            fuzzy: false,
            maxResults: 100);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Should have 0 results
        var summary = response.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(0);
        
        // Should have helpful insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        Console.WriteLine("\nNo results insights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }
        
        // First insight should mention no symbols found
        insights[0].GetString().Should().Contain("No symbols found");
    }
}