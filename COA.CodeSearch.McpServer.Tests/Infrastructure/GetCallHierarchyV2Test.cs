using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class GetCallHierarchyV2Test : TestBase
{
    private readonly GetCallHierarchyToolV2 _tool;

    public GetCallHierarchyV2Test()
    {
        _tool = new GetCallHierarchyToolV2(
            ServiceProvider.GetRequiredService<ILogger<GetCallHierarchyToolV2>>(),
            ServiceProvider.GetRequiredService<CodeAnalysisService>(),
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Call_Hierarchy()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - get call hierarchy for a method
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 15,  // Line with a method
            column: 20,
            direction: "both",
            maxDepth: 2,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Console.WriteLine("=== AI-OPTIMIZED CALL HIERARCHY ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("get_call_hierarchy");
        
        // Check symbol info
        var symbol = response.GetProperty("symbol");
        symbol.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        symbol.GetProperty("kind").GetString().Should().NotBeNullOrEmpty();
        
        // Check query
        var query = response.GetProperty("query");
        query.GetProperty("direction").GetString().Should().Be("both");
        query.GetProperty("maxDepth").GetInt32().Should().Be(2);
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalCalls").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("uniqueMethods").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("circularDependencies").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // Check analysis
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("callPaths").Should().NotBeNull();
        analysis.GetProperty("criticalPaths").Should().NotBeNull();
        analysis.GetProperty("recursivePatterns").Should().NotBeNull();
        
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
        meta.GetProperty("cached").GetString().Should().StartWith("call_");
    }

    [Fact]
    public async Task Should_Analyze_Incoming_Calls()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - get incoming calls only
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 15,
            column: 20,
            direction: "incoming",
            maxDepth: 2);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Console.WriteLine("=== INCOMING CALLS ANALYSIS ===");
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check query direction
        var query = response.GetProperty("query");
        query.GetProperty("direction").GetString().Should().Be("incoming");
        
        // Check call paths
        var analysis = response.GetProperty("analysis");
        if (analysis.TryGetProperty("callPaths", out var callPaths))
        {
            callPaths.GetProperty("incoming").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            Console.WriteLine($"\nIncoming call paths: {callPaths.GetProperty("incoming").GetInt32()}");
        }
    }

    [Fact]
    public async Task Should_Detect_Circular_Dependencies()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - analyze with deeper depth to potentially find circular dependencies
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 15,
            column: 20,
            direction: "both",
            maxDepth: 3);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check issues
        var issues = response.GetProperty("issues");
        if (issues.TryGetProperty("circular", out var circular))
        {
            Console.WriteLine("\nCircular dependencies:");
            foreach (var dep in circular.EnumerateArray())
            {
                Console.WriteLine($"- {dep.GetString()}");
            }
        }
        
        if (issues.TryGetProperty("deepNesting", out var deepNesting))
        {
            Console.WriteLine("\nDeep nesting points:");
            foreach (var point in deepNesting.EnumerateArray())
            {
                Console.WriteLine($"- {point.GetString()}");
            }
        }
    }

    [Fact]
    public async Task Should_Handle_No_Symbol_Found()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - try to get call hierarchy at a position with no method
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 1,  // Likely a namespace or using statement
            column: 1,
            direction: "both");
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Should return error
        response.GetProperty("success").GetBoolean().Should().BeFalse();
        response.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        
        Console.WriteLine($"\nError handling: {response.GetProperty("error").GetString()}");
    }
}