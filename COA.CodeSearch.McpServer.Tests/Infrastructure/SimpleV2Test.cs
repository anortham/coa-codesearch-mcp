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

public class SimpleV2Test : TestBase
{
    [Fact]
    public async Task Simple_ProjectStructure_Test()
    {
        // Arrange
        var tool = new ProjectStructureAnalysisToolV2(
            ServiceProvider.GetRequiredService<ILogger<ProjectStructureAnalysisToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var projectPath = GetTestProjectPath();
        
        // Act
        var result = await tool.ExecuteAsync(
            projectPath,
            includeMetrics: true,
            includeFiles: false,
            includeNuGetPackages: false,
            mode: ResponseMode.Summary);
        
        // Serialize to see what we got
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        // Parse to check basics
        var response = JsonDocument.Parse(json).RootElement;
        
        // Basic assertions
        response.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}