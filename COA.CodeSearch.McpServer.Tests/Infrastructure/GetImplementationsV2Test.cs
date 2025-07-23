using System.Text.Json;
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

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class GetImplementationsV2Test : TestBase
{
    private readonly GetImplementationsToolV2 _tool;

    public GetImplementationsV2Test()
    {
        _tool = new GetImplementationsToolV2(
            ServiceProvider.GetRequiredService<ILogger<GetImplementationsToolV2>>(),
            ServiceProvider.GetRequiredService<CodeAnalysisService>(),
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Implementations()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - find implementations of an interface or virtual method
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 42,  // Line with IService interface
            column: 25,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Console.WriteLine("=== AI-OPTIMIZED IMPLEMENTATIONS ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("get_implementations");
        
        // Check symbol info
        var symbol = response.GetProperty("symbol");
        symbol.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        symbol.GetProperty("kind").GetString().Should().NotBeNullOrEmpty();
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalImplementations").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // If implementations exist, check distribution
        if (summary.GetProperty("totalImplementations").GetInt32() > 0)
        {
            summary.GetProperty("uniqueTypes").GetInt32().Should().BeGreaterThan(0);
            summary.GetProperty("distribution").Should().NotBeNull();
        }
        
        // Check analysis
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("patterns").Should().NotBeNull();
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        Console.WriteLine("\nInsights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThan(0);
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
        meta.GetProperty("tokens").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Handle_No_Implementations()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - try to find implementations of a concrete method
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 20,  // Line with a regular method (not virtual/abstract)
            column: 15);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        if (response.GetProperty("success").GetBoolean())
        {
            var summary = response.GetProperty("summary");
            var totalImplementations = summary.GetProperty("totalImplementations").GetInt32();
            
            if (totalImplementations == 0)
            {
                // Should have insights about no implementations
                var insights = response.GetProperty("insights");
                var hasNoImplInsight = false;
                foreach (var insight in insights.EnumerateArray())
                {
                    var insightText = insight.GetString() ?? "";
                    if (insightText.Contains("No implementations found"))
                    {
                        hasNoImplInsight = true;
                        Console.WriteLine($"Found insight: {insightText}");
                        break;
                    }
                }
                hasNoImplInsight.Should().BeTrue("Should have insight about no implementations");
            }
        }
    }

    [Fact]
    public async Task Should_Detect_Implementation_Patterns()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - analyze interface with multiple implementations
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 42,  // Line with IService interface
            column: 25);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        if (response.GetProperty("success").GetBoolean())
        {
            var analysis = response.GetProperty("analysis");
            var patterns = analysis.GetProperty("patterns");
            
            Console.WriteLine("\nDetected patterns:");
            foreach (var pattern in patterns.EnumerateArray())
            {
                Console.WriteLine($"- {pattern.GetString()}");
            }
            
            // Check for inheritance analysis
            if (analysis.TryGetProperty("inheritance", out var inheritance))
            {
                Console.WriteLine($"\nInheritance depth: {inheritance.GetProperty("depth").GetInt32()}");
            }
            
            // Check for hotspots
            if (analysis.TryGetProperty("hotspots", out var hotspots))
            {
                Console.WriteLine("\nHotspots detected");
            }
        }
    }

    [Fact]
    public async Task Should_Support_Full_Mode()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 42,  // Line with IService interface
            column: 25,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check meta mode
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("full");
        
        // In full mode, implementations should have detailed info
        if (response.TryGetProperty("implementations", out var implementations))
        {
            implementations.Should().NotBeNull();
            
            if (implementations.GetArrayLength() > 0)
            {
                var firstImpl = implementations[0];
                firstImpl.GetProperty("containingType").Should().NotBeNull();
                
                if (firstImpl.TryGetProperty("implementations", out var implList))
                {
                    implList.Should().NotBeNull();
                    Console.WriteLine("\nFull implementation details present");
                }
            }
        }
    }

    [Fact]
    public async Task Should_Handle_Invalid_Position()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - position with no symbol
        var result = await _tool.ExecuteAsync(
            filePath: testCodePath,
            line: 1,  // Likely a using statement or empty line
            column: 1);
        
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