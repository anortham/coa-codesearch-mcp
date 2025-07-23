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
    public async Task Should_Return_AI_Optimized_Dependency_Analysis()
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
        // Removed debug output for clean tests
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("dependency_analysis");
        
        // Check target
        var target = response.GetProperty("target");
        target.GetProperty("symbol").GetString().Should().Be("TestClass");
        target.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        
        // Check analysis settings
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("direction").GetString().Should().Be("both");
        analysis.GetProperty("depth").GetInt32().Should().Be(2);
        
        // Check metrics
        var metrics = response.GetProperty("metrics");
        metrics.GetProperty("incoming").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        metrics.GetProperty("outgoing").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        metrics.GetProperty("instability").GetDouble().Should().BeInRange(0, 1);
        
        // Check health assessment
        response.GetProperty("health").GetString().Should().BeOneOf("healthy", "moderate", "poor", "critical");
        
        // Check circular dependencies
        var circular = response.GetProperty("circular");
        // Just verify we can read it as boolean - it's either true or false
        var found = circular.GetProperty("found").GetBoolean();
        circular.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("cached").GetString().Should().StartWith("dep_");
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
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("dependency_analysis");
        
        // Check analysis direction
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("direction").GetString().Should().Be("outgoing");
        analysis.GetProperty("depth").GetInt32().Should().Be(1);
        
        // Check metrics - should have 0 incoming when analyzing only outgoing
        var metrics = response.GetProperty("metrics");
        metrics.GetProperty("incoming").GetInt32().Should().Be(0);
        metrics.GetProperty("outgoing").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // Check health and insights
        // Verify outgoing dependencies analysis
        response.GetProperty("health").GetString().Should().NotBeNullOrEmpty();
        metrics.GetProperty("outgoing").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        metrics.GetProperty("instability").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        
        // If there are circular dependencies, they should be reported
        var circular = response.GetProperty("circular");
        if (circular.GetProperty("found").GetBoolean())
        {
            // Verify circular dependencies structure
            circular.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        }
    }
}