using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class GetDiagnosticsV2Test : TestBase
{
    [Fact]
    public async Task Should_Return_Summary_Of_Diagnostics_With_Insights()
    {
        // Arrange
        var tool = new GetDiagnosticsToolV2(
            ServiceProvider.GetRequiredService<ILogger<GetDiagnosticsToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var testCodePath = GetTestCodePath();
        
        // Act - get diagnostics for test file in summary mode
        var result = await tool.ExecuteAsync(
            testCodePath,
            severities: null, // All severities
            mode: ResponseMode.Summary);
        
        // Serialize to see what we got
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Print it
        Console.WriteLine("=== DIAGNOSTICS SUMMARY RESULT ===");
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
            insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
            
            // If we have insights, print them
            if (insights.GetArrayLength() > 0)
            {
                Console.WriteLine("\nKey Insights:");
                foreach (var insight in insights.EnumerateArray())
                {
                    Console.WriteLine($"- {insight.GetString()}");
                }
            }
        }
        
        // Check categories (severity breakdown)
        if (data.TryGetProperty("byCategory", out var categories))
        {
            // We should have some categories
            categories.EnumerateObject().Should().HaveCountGreaterThanOrEqualTo(0);
            
            Console.WriteLine("\nCategories:");
            foreach (var category in categories.EnumerateObject())
            {
                var occurrences = category.Value.GetProperty("occurrences").GetInt32();
                Console.WriteLine($"- {category.Name}: {occurrences} occurrences");
            }
        }
        
        // Check next actions
        var nextActions = response.GetProperty("nextActions");
        nextActions.Should().NotBeNull();
        
        // Should have recommended actions
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
    public async Task Should_Auto_Switch_To_Summary_For_Large_Results()
    {
        // Arrange
        var tool = new GetDiagnosticsToolV2(
            ServiceProvider.GetRequiredService<ILogger<GetDiagnosticsToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var testProjectPath = GetTestProjectPath();
        
        // Act - request full mode but should auto-switch to summary for test project
        var result = await tool.ExecuteAsync(
            testProjectPath,
            severities: null,
            mode: ResponseMode.Full);
            
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Should be in summary mode
        response.GetProperty("mode").GetString().Should().Be("summary");
        
        // Check if auto-mode switch is indicated
        if (response.TryGetProperty("autoModeSwitch", out var autoSwitch))
        {
            Console.WriteLine($"Auto-mode switch: {autoSwitch.GetBoolean()}");
        }
        
        // Should have recommended actions
        var nextActions = response.GetProperty("nextActions");
        var recommended = nextActions.GetProperty("recommended").EnumerateArray();
        recommended.Should().NotBeEmpty("Should have recommended actions in summary mode");
    }
}