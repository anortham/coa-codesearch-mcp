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

public class RenameSymbolV2Test : TestBase
{
    [Fact]
    public async Task Should_Return_Preview_Of_Rename_In_Summary_Mode()
    {
        // Arrange
        var tool = new RenameSymbolToolV2(
            ServiceProvider.GetRequiredService<ILogger<RenameSymbolToolV2>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
            
        var testCodePath = GetTestCodePath();
        
        // Act - rename TestClass to RenamedClass in summary mode
        var result = await tool.ExecuteAsync(
            testCodePath,
            line: 9,      // TestClass definition
            column: 18,   // Class name position
            newName: "RenamedClass",
            preview: true,
            mode: ResponseMode.Summary);
        
        // Serialize to see what we got
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Print it
        Console.WriteLine("=== RENAME PREVIEW RESULT ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Basic assertions - AI-optimized format
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("rename_symbol");
        
        // Check symbol structure (note: JSON is camelCase)
        var symbol = response.GetProperty("symbol");
        symbol.GetProperty("old").GetString().Should().Be("TestClass");
        symbol.GetProperty("new").GetString().Should().Be("RenamedClass");
        
        // Check impact structure
        var impact = response.GetProperty("impact");
        impact.GetProperty("refs").GetInt32().Should().BeGreaterThan(0);
        impact.GetProperty("files").GetInt32().Should().BeGreaterThan(0);
        impact.GetProperty("risk").GetString().Should().NotBeEmpty();
        
        // Check we have actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check preview flag
        response.GetProperty("preview").GetBoolean().Should().BeTrue();
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check metadata
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("tokens").GetInt32().Should().BeGreaterThan(0);
        
        // Check actions - should have an apply action
        var applyAction = actions.EnumerateArray().FirstOrDefault(a => 
            a.GetProperty("id").GetString() == "apply");
            
        if (applyAction.ValueKind != JsonValueKind.Undefined)
        {
            Console.WriteLine("Found apply action in actions array");
            applyAction.GetProperty("priority").GetString().Should().NotBeEmpty();
        }
    }
}