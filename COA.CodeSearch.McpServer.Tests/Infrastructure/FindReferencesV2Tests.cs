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
    public async Task Should_Return_AI_Optimized_Response()
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
        
        Logger.LogInformation("AI-optimized response:\n{Json}", json);
        
        // Parse as dynamic to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("find_references");
        
        // Check symbol info
        var symbol = response.GetProperty("symbol");
        symbol.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        symbol.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        
        // Check summary structure
        var summary = response.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("usages").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("files").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("impact").GetString().Should().NotBeNullOrEmpty();
        
        // Check insights array
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("cached").GetString().Should().StartWith("refs_");
    }

    [Fact]
    public async Task Should_Return_Full_Mode_For_Small_Results()
    {
        // This test would need a symbol with many references to test auto-switch
        // For now, we'll test with TestClass which has limited references
        var testCodePath = GetTestCodePath();
        
        // Act - Request full mode - should stay full for small results
        var result = await _tool.ExecuteAsync(
            testCodePath,
            line: 9,
            column: 18,
            includeDeclaration: true,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        Logger.LogInformation("Full mode test response:\n{Json}", json);
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("find_references");
        
        // Check meta to verify mode
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("full");
        
        // For full mode, we should have reference types breakdown
        var refTypes = response.GetProperty("refTypes");
        refTypes.EnumerateObject().Should().NotBeNull();
        
        // Check that we still have the core structure
        response.GetProperty("symbol").Should().NotBeNull();
        response.GetProperty("summary").Should().NotBeNull();
        response.GetProperty("insights").Should().NotBeNull();
    }
}