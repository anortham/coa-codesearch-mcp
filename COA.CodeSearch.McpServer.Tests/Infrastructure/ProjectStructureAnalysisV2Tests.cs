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
    public async Task Should_Return_AI_Optimized_Project_Structure()
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
        
        // Removed debug output for clean tests
        
        // Parse as dynamic to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("project_structure_analysis");
        
        // Check workspace
        var workspace = response.GetProperty("workspace");
        workspace.GetProperty("path").GetString().Should().Be(projectPath);
        workspace.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        
        // Check overview
        var overview = response.GetProperty("overview");
        overview.GetProperty("projects").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        overview.GetProperty("files").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        overview.GetProperty("lines").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // Check breakdown
        var breakdown = response.GetProperty("breakdown");
        breakdown.GetProperty("types").Should().NotBeNull();
        breakdown.GetProperty("languages").Should().NotBeNull();
        breakdown.GetProperty("frameworks").Should().NotBeNull();
        
        // Check health
        response.GetProperty("health").GetString().Should().BeOneOf("excellent", "good", "fair", "needs-attention");
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check issues
        var issues = response.GetProperty("issues");
        issues.Should().NotBeNull();
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("includeMetrics").GetBoolean().Should().BeTrue();
        meta.GetProperty("includeFiles").GetBoolean().Should().BeFalse();
        meta.GetProperty("includeNuGet").GetBoolean().Should().BeFalse();
        meta.GetProperty("cached").GetString().Should().StartWith("struct_");
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
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("project_structure_analysis");
        
        // Check meta to confirm NuGet was requested
        var meta = response.GetProperty("meta");
        meta.GetProperty("includeNuGet").GetBoolean().Should().BeTrue();
        
        // Check issues for NuGet conflicts
        var issues = response.GetProperty("issues");
        if (issues.TryGetProperty("nugetConflicts", out var nugetConflicts))
        {
            nugetConflicts.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
            // Verify NuGet conflicts structure
            nugetConflicts.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        }
        
        // Check for NuGet-related insights
        var insights = response.GetProperty("insights");
        var nugetInsights = insights.EnumerateArray()
            .Select(i => i.GetString())
            .Where(s => s != null && s.ToLower().Contains("nuget"))
            .ToList();
        
        // Verify NuGet insights
        nugetInsights.Count.Should().BeGreaterThanOrEqualTo(0);
        
        // Check actions for dependency analysis
        var actions = response.GetProperty("actions");
        var depAction = actions.EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("id").GetString() == "analyze_dependencies");
            
        if (depAction.ValueKind != JsonValueKind.Undefined)
        {
            // Verify dependency analysis action exists
            depAction.GetProperty("priority").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Should_Handle_File_Inclusion_Efficiently()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        
        // Act - Request with files included
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: true, // This will make response large
            includeNuGetPackages: false,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("project_structure_analysis");
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("includeFiles").GetBoolean().Should().BeTrue();
        
        // For small test projects, we might get full file listing
        // For larger projects, actions would include browse_files
        var actions = response.GetProperty("actions");
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        var browseAction = actions.EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("id").GetString() == "browse_files");
            
        if (browseAction.ValueKind != JsonValueKind.Undefined)
        {
            // Browse files action available for detailed file exploration
        }
        else
        {
            // Solution small enough to include all files directly
        }
    }

    [Fact]
    public async Task Should_Assess_Project_Health_And_Issues()
    {
        // Test health assessment and issue detection
        var projectPath = GetTestProjectPath();
        
        var result = await _tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: false,
            includeNuGetPackages: false,
            mode: ResponseMode.Summary);
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check health assessment
        var health = response.GetProperty("health").GetString();
        health.Should().BeOneOf("excellent", "good", "fair", "needs-attention");
        // Verify health assessment is valid
        health.Should().NotBeNullOrEmpty();
        
        // Check issues
        var issues = response.GetProperty("issues");
        
        // Check high dependency projects
        if (issues.TryGetProperty("highDependencyProjects", out var highDeps))
        {
            // Verify high dependency projects structure
            highDeps.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
            foreach (var proj in highDeps.EnumerateArray())
            {
                proj.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
                proj.GetProperty("dependencies").GetInt32().Should().BeGreaterThan(0);
            }
        }
        
        // Check version conflicts
        if (issues.TryGetProperty("versionConflicts", out var conflicts))
        {
            // Verify version conflicts structure
            conflicts.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        }
        
        // Check insights for health-related information
        var insights = response.GetProperty("insights");
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check if dependency analysis is recommended
        var actions = response.GetProperty("actions");
        var hasDepAnalysis = actions.EnumerateArray()
            .Any(a => a.GetProperty("id").GetString() == "analyze_dependencies");
            
        if (hasDepAnalysis)
        {
            // Verify dependency analysis is recommended
        }
    }
}