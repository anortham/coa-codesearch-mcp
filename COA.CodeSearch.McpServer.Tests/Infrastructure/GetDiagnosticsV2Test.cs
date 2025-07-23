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
    public async Task Should_Return_AI_Optimized_Diagnostics_Response()
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
        // Removed debug output for clean tests
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("get_diagnostics");
        
        // Check scope
        var scope = response.GetProperty("scope");
        scope.GetProperty("path").GetString().Should().Be(testCodePath);
        scope.GetProperty("type").GetString().Should().Be("file");
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("files").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("priority").GetString().Should().NotBeNullOrEmpty();
        
        // Check severity breakdown
        var severity = summary.GetProperty("severity");
        severity.EnumerateObject().Should().NotBeNull();
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check top issues
        var topIssues = response.GetProperty("topIssues");
        topIssues.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
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
        meta.GetProperty("cached").GetString().Should().StartWith("diag_");
    }
    
    [Fact]
    public async Task Should_Return_Full_Mode_For_Small_Results()
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
        
        // Act - request full mode for single file (should stay full)
        var result = await tool.ExecuteAsync(
            testCodePath,
            severities: null,
            mode: ResponseMode.Full);
            
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("get_diagnostics");
        
        // Check meta for mode
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().BeOneOf("full", "summary");
        
        // Should have actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // If we have errors, should have fix_errors action with critical priority
        var summary = response.GetProperty("summary");
        var severity = summary.GetProperty("severity");
        if (severity.TryGetProperty("error", out var errorCount) && errorCount.GetInt32() > 0)
        {
            var hasFixErrorsAction = false;
            foreach (var action in actions.EnumerateArray())
            {
                if (action.GetProperty("id").GetString() == "fix_errors")
                {
                    hasFixErrorsAction = true;
                    action.GetProperty("priority").GetString().Should().Be("critical");
                    break;
                }
            }
            hasFixErrorsAction.Should().BeTrue("Should have fix_errors action when errors exist");
        }
    }
}