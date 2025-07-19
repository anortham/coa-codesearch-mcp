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
        
        // Basic assertions
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("mode").GetString().Should().Be("summary");
        
        // Check data structure
        var data = response.GetProperty("data");
        data.Should().NotBeNull();
        
        // Check overview
        var overview = data.GetProperty("overview");
        overview.GetProperty("totalItems").GetInt32().Should().BeGreaterThan(0);
        
        // Check preview
        if (data.TryGetProperty("preview", out var preview))
        {
            var topChanges = preview.GetProperty("topChanges");
            topChanges.GetArrayLength().Should().BeGreaterThan(0);
            
            // First change should show the rename
            var firstChange = topChanges.EnumerateArray().First();
            var context = firstChange.GetProperty("context").GetString();
            context.Should().Contain("TestClass");
            context.Should().Contain("RenamedClass");
        }
        
        // Check next actions - should recommend applying the rename
        var nextActions = response.GetProperty("nextActions");
        var recommended = nextActions.GetProperty("recommended").EnumerateArray();
        
        var applyAction = recommended.FirstOrDefault(a => 
            a.GetProperty("action").GetString() == "apply_rename");
            
        if (applyAction.ValueKind != JsonValueKind.Undefined)
        {
            Console.WriteLine("Found apply_rename action in recommendations");
            applyAction.GetProperty("description").GetString().Should().Contain("Apply");
        }
    }
}