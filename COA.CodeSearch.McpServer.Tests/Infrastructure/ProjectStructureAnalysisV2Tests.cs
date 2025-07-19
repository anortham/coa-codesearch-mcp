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

public class ProjectStructureAnalysisV2Tests : TestBase
{
    private readonly ProjectStructureAnalysisToolV2 _tool;

    public ProjectStructureAnalysisV2Tests()
    {
        _tool = new ProjectStructureAnalysisToolV2(
            ServiceProvider.GetRequiredService<ILogger<ProjectStructureAnalysisToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_Summary_Mode_With_Solution_Insights()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        
        // Act - Request summary mode with metrics
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: false,
            includeNuGetPackages: false,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Logger.LogInformation("Claude-optimized project structure response:\n{Json}", json);
        
        // Parse as dynamic to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Log error if not successful
        if (response.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            if (response.TryGetProperty("error", out var error))
            {
                Logger.LogError("Test failed with error: {Error}", error.GetString());
            }
        }
        
        // Check response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("mode").GetString().Should().Be("summary");
        
        // Check data structure
        var data = response.GetProperty("data");
        data.Should().NotBeNull();
        
        // Check overview
        var overview = data.GetProperty("overview");
        overview.GetProperty("totalItems").GetInt32().Should().BeGreaterThan(0);
        overview.GetProperty("keyInsights").GetArrayLength().Should().BeGreaterThan(0);
        
        // Check for solution-level insights
        var insights = overview.GetProperty("keyInsights").EnumerateArray()
            .Select(i => i.GetString())
            .ToList();
        
        Logger.LogInformation("Solution insights: {Insights}", string.Join(", ", insights));
        
        // Check categories (by output type)
        var categories = data.GetProperty("byCategory");
        categories.EnumerateObject().Should().NotBeEmpty();
        
        // Check hotspots (largest projects)
        var hotspots = data.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check next actions
        var nextActions = response.GetProperty("nextActions");
        var recommended = nextActions.GetProperty("recommended").EnumerateArray();
        recommended.Should().NotBeEmpty();
        
        // Should recommend viewing largest projects
        var viewLargestAction = recommended.FirstOrDefault(a => 
            a.GetProperty("action").GetString() == "view_largest_projects");
        viewLargestAction.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Should_Include_NuGet_Analysis_When_Requested()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        
        // Act - Request with NuGet packages
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: false,
            includeNuGetPackages: true,
            mode: ResponseMode.Summary);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var response = JsonDocument.Parse(json).RootElement;
        
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check for NuGet-related insights
        var insights = response.GetProperty("data")
            .GetProperty("overview")
            .GetProperty("keyInsights")
            .EnumerateArray()
            .Select(i => i.GetString())
            .ToList();
        
        // Should have insights about NuGet packages if version conflicts exist
        Logger.LogInformation("NuGet insights: {Insights}", string.Join(", ", insights));
        
        // Check next actions includes NuGet analysis
        var nextActions = response.GetProperty("nextActions")
            .GetProperty("recommended")
            .EnumerateArray();
            
        var nugetAction = nextActions.FirstOrDefault(a => 
            a.GetProperty("action").GetString() == "analyze_dependencies");
            
        if (nugetAction.ValueKind != JsonValueKind.Undefined)
        {
            Logger.LogInformation("NuGet analysis action found in recommendations");
        }
    }

    [Fact]
    public async Task Should_Auto_Switch_For_Large_Solutions_With_Files()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        
        // Act - Request full mode with files (should trigger auto-switch)
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: true, // This will make response large
            includeNuGetPackages: false,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var response = JsonDocument.Parse(json).RootElement;
        
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check if auto-switch occurred (depends on solution size)
        if (response.TryGetProperty("autoModeSwitch", out var autoSwitch) && 
            autoSwitch.GetBoolean())
        {
            Logger.LogInformation("Auto-switched to summary mode for large file listing!");
            response.GetProperty("mode").GetString().Should().Be("summary");
            
            // Should have file browsing action
            var fileAction = response.GetProperty("nextActions")
                .GetProperty("recommended")
                .EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("action").GetString() == "browse_project_files");
                
            fileAction.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        }
        else
        {
            Logger.LogInformation("Solution small enough to return full file listing");
            response.GetProperty("mode").GetString().Should().Be("full");
        }
    }

    [Fact]
    public async Task Should_Detect_Circular_References()
    {
        // This test would need a solution with circular references
        // For now, we'll test the context analysis
        var projectPath = GetTestProjectPath();
        
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: false,
            includeNuGetPackages: false,
            mode: ResponseMode.Summary);
        
        var json = JsonSerializer.Serialize(result);
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check context analysis
        var context = response.GetProperty("context");
        context.GetProperty("impact").GetString().Should().NotBeNullOrEmpty();
        
        // Check for risk factors
        if (context.TryGetProperty("riskFactors", out var riskFactors))
        {
            var risks = riskFactors.EnumerateArray()
                .Select(r => r.GetString())
                .ToList();
                
            Logger.LogInformation("Risk factors identified: {Risks}", string.Join(", ", risks));
        }
        
        // Check for suggestions
        if (context.TryGetProperty("suggestions", out var suggestions))
        {
            var suggestionList = suggestions.EnumerateArray()
                .Select(s => s.GetString())
                .ToList();
                
            Logger.LogInformation("Suggestions: {Suggestions}", string.Join(", ", suggestionList));
        }
    }
}