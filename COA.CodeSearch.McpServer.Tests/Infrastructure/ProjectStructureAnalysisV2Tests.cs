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
        
        Console.WriteLine("=== AI-OPTIMIZED PROJECT STRUCTURE ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
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
        Console.WriteLine("\nInsights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check issues
        var issues = response.GetProperty("issues");
        issues.Should().NotBeNull();
        
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
        
        Console.WriteLine("=== NuGet Analysis Response ===");
        Console.WriteLine(json);
        
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
            Console.WriteLine($"\nNuGet conflicts: {nugetConflicts.GetArrayLength()}");
        }
        
        // Check for NuGet-related insights
        var insights = response.GetProperty("insights");
        var nugetInsights = insights.EnumerateArray()
            .Select(i => i.GetString())
            .Where(s => s != null && s.ToLower().Contains("nuget"))
            .ToList();
        
        Console.WriteLine($"\nNuGet insights found: {nugetInsights.Count}");
        foreach (var insight in nugetInsights)
        {
            Console.WriteLine($"- {insight}");
        }
        
        // Check actions for dependency analysis
        var actions = response.GetProperty("actions");
        var depAction = actions.EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("id").GetString() == "analyze_dependencies");
            
        if (depAction.ValueKind != JsonValueKind.Undefined)
        {
            Console.WriteLine("\nDependency analysis action found");
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
        
        Console.WriteLine("=== Project Structure with Files ===");
        // Only print first 1000 chars if large
        Console.WriteLine(json.Length > 1000 ? json.Substring(0, 1000) + "..." : json);
        
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
        Console.WriteLine($"\nTotal actions: {actions.GetArrayLength()}");
        
        var browseAction = actions.EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("id").GetString() == "browse_files");
            
        if (browseAction.ValueKind != JsonValueKind.Undefined)
        {
            Console.WriteLine("Browse files action available for detailed file exploration");
        }
        else
        {
            Console.WriteLine("Solution small enough to include all files directly");
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
        
        Console.WriteLine("=== Project Health Assessment ===");
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check health assessment
        var health = response.GetProperty("health").GetString();
        health.Should().BeOneOf("excellent", "good", "fair", "needs-attention");
        Console.WriteLine($"\nProject health: {health}");
        
        // Check issues
        var issues = response.GetProperty("issues");
        
        // Check high dependency projects
        if (issues.TryGetProperty("highDependencyProjects", out var highDeps))
        {
            Console.WriteLine($"\nHigh dependency projects: {highDeps.GetArrayLength()}");
            foreach (var proj in highDeps.EnumerateArray())
            {
                var name = proj.GetProperty("name").GetString();
                var deps = proj.GetProperty("dependencies").GetInt32();
                Console.WriteLine($"- {name}: {deps} dependencies");
            }
        }
        
        // Check version conflicts
        if (issues.TryGetProperty("versionConflicts", out var conflicts))
        {
            Console.WriteLine($"\nVersion conflicts: {conflicts.GetArrayLength()}");
        }
        
        // Check insights for health-related information
        var insights = response.GetProperty("insights");
        Console.WriteLine($"\nTotal insights: {insights.GetArrayLength()}");
        
        // Check if dependency analysis is recommended
        var actions = response.GetProperty("actions");
        var hasDepAnalysis = actions.EnumerateArray()
            .Any(a => a.GetProperty("id").GetString() == "analyze_dependencies");
            
        if (hasDepAnalysis)
        {
            Console.WriteLine("\nDependency analysis recommended based on project structure");
        }
    }
}