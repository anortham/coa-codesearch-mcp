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

public class DependencyAnalysisV2Test : TestBase
{
    [Fact]
    public async Task Should_Return_Summary_With_Dependency_Insights()
    {
        // Arrange
        var tool = new DependencyAnalysisToolV2(
            ServiceProvider.GetRequiredService<ILogger<DependencyAnalysisToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var testProjectPath = GetTestProjectPath();
        
        // Act - analyze dependencies for TestClass in summary mode
        var result = await tool.ExecuteAsync(
            symbol: "TestClass",
            workspacePath: testProjectPath,
            direction: "both",
            depth: 2,
            includeTests: false,
            includeExternalDependencies: false,
            mode: ResponseMode.Summary);
        
        // Serialize to see what we got
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Print it
        Console.WriteLine("=== DEPENDENCY ANALYSIS RESULT ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Basic assertions
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("mode").GetString().Should().Be("summary");
        
        // Check data structure
        var data = response.GetProperty("data");
        data.Should().NotBeNull();
        
        // Check overview
        var overview = data.GetProperty("overview");
        overview.Should().NotBeNull();
        
        // Check for insights
        if (overview.TryGetProperty("keyInsights", out var insights))
        {
            Console.WriteLine("\nKey Insights:");
            foreach (var insight in insights.EnumerateArray())
            {
                Console.WriteLine($"- {insight.GetString()}");
            }
        }
        
        // Check hotspots
        if (data.TryGetProperty("hotspots", out var hotspots))
        {
            Console.WriteLine("\nHotspots:");
            foreach (var hotspot in hotspots.EnumerateArray())
            {
                var file = hotspot.GetProperty("file").GetString();
                var occurrences = hotspot.GetProperty("occurrences").GetInt32();
                Console.WriteLine($"- {file}: {occurrences} connections");
            }
        }
        
        // Check next actions
        var nextActions = response.GetProperty("nextActions");
        var recommended = nextActions.GetProperty("recommended").EnumerateArray();
        
        Console.WriteLine("\nRecommended Actions:");
        foreach (var action in recommended)
        {
            var desc = action.GetProperty("description").GetString();
            var priority = action.GetProperty("priority").GetString();
            Console.WriteLine($"- [{priority}] {desc}");
        }
    }
    
    [Fact]
    public async Task Should_Analyze_Outgoing_Dependencies()
    {
        // Arrange
        var tool = new DependencyAnalysisToolV2(
            ServiceProvider.GetRequiredService<ILogger<DependencyAnalysisToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var testProjectPath = GetTestProjectPath();
        
        // Act - analyze only outgoing dependencies
        var result = await tool.ExecuteAsync(
            symbol: "TestClass",
            workspacePath: testProjectPath,
            direction: "outgoing",
            depth: 1,
            includeTests: false,
            includeExternalDependencies: false,
            mode: ResponseMode.Summary);
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Should succeed
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check context for impact analysis
        if (response.TryGetProperty("context", out var context))
        {
            Console.WriteLine("\nDependency Analysis Context:");
            
            if (context.TryGetProperty("impact", out var impact))
            {
                Console.WriteLine($"Impact: {impact.GetString()}");
            }
            
            if (context.TryGetProperty("riskFactors", out var riskFactors))
            {
                Console.WriteLine("Risk Factors:");
                foreach (var risk in riskFactors.EnumerateArray())
                {
                    Console.WriteLine($"- {risk.GetString()}");
                }
            }
            
            if (context.TryGetProperty("suggestions", out var suggestions))
            {
                Console.WriteLine("Suggestions:");
                foreach (var suggestion in suggestions.EnumerateArray())
                {
                    Console.WriteLine($"- {suggestion.GetString()}");
                }
            }
        }
    }
}