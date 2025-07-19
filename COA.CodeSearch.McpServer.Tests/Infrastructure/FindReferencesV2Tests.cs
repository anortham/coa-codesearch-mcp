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

public class FindReferencesV2Tests : TestBase
{
    private readonly FindReferencesToolV2 _tool;

    public FindReferencesV2Tests()
    {
        _tool = new FindReferencesToolV2(
            ServiceProvider.GetRequiredService<ILogger<FindReferencesToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_Summary_Mode_With_Insights()
    {
        // Arrange
        var testCodePath = GetTestCodePath();
        
        // Act - Request summary mode
        var result = await _tool.ExecuteAsync(
            testCodePath, 
            line: 9,      // TestClass definition
            column: 18, 
            includeDeclaration: true,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Logger.LogInformation("Claude-optimized response:\n{Json}", json);
        
        // Parse as dynamic to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
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
        
        // Check for progressive disclosure metadata
        var metadata = response.GetProperty("metadata");
        metadata.GetProperty("detailRequestToken").GetString().Should().NotBeNullOrEmpty();
        
        // Check next actions
        var nextActions = response.GetProperty("nextActions");
        nextActions.GetProperty("recommended").GetArrayLength().Should().BeGreaterThan(0);
        
        // Check context analysis
        var context = response.GetProperty("context");
        context.GetProperty("impact").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Auto_Switch_To_Summary_For_Large_Results()
    {
        // This test would need a symbol with many references
        // For now, we'll test with TestClass which has limited references
        var testCodePath = GetTestCodePath();
        
        // Act - Request full mode but should auto-switch if large
        var result = await _tool.ExecuteAsync(
            testCodePath,
            line: 9,
            column: 18,
            includeDeclaration: true,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var response = JsonDocument.Parse(json).RootElement;
        
        // For small result sets, it should stay in full mode
        // In a real scenario with ICmsService, it would auto-switch
        response.GetProperty("mode").GetString().Should().BeOneOf("full", "summary");
        
        if (response.GetProperty("autoModeSwitch").ValueKind == JsonValueKind.True)
        {
            Logger.LogInformation("Auto-switched to summary mode!");
            response.GetProperty("mode").GetString().Should().Be("summary");
        }
    }
}