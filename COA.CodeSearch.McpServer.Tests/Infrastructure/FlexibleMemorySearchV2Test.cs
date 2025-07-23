using System.Text.Json;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class FlexibleMemorySearchV2Test : TestBase
{
    private readonly FlexibleMemorySearchToolV2 _tool;
    private readonly FlexibleMemoryService _memoryService;

    public FlexibleMemorySearchV2Test()
    {
        _memoryService = ServiceProvider.GetRequiredService<FlexibleMemoryService>();
        _tool = new FlexibleMemorySearchToolV2(
            ServiceProvider.GetRequiredService<ILogger<FlexibleMemorySearchToolV2>>(),
            _memoryService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IQueryExpansionService>(),
            ServiceProvider.GetRequiredService<IContextAwarenessService>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Memory_Search()
    {
        // Arrange - Store some test memories
        await StoreTestMemories();

        // Act - Search all memories
        var result = await _tool.ExecuteAsync(
            query: "*",
            mode: ResponseMode.Summary);

        // Assert
        result.Should().NotBeNull();

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Removed debug output for clean tests

        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;

        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("memory_search");

        // Check query
        var query = response.GetProperty("query");
        query.GetProperty("text").GetString().Should().Be("*");

        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalFound").GetInt32().Should().BeGreaterThan(0);
        summary.GetProperty("returned").GetInt32().Should().BeGreaterThan(0);

        // Type distribution should be present if memories exist
        if (summary.GetProperty("totalFound").GetInt32() > 0)
        {
            summary.GetProperty("typeDistribution").Should().NotBeNull();
        }

        // Check analysis
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("hotspots").Should().NotBeNull();

        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);

        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThan(0);
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);

        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("tokens").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Analyze_Memory_Patterns()
    {
        // Arrange - Store memories with patterns
        var testFile = GetTestCodePath();
        
        // Technical debt items
        for (int i = 0; i < 6; i++)
        {
            await _memoryService.StoreMemoryAsync(new FlexibleMemoryEntry
            {
                Type = "TechnicalDebt",
                Content = $"Technical debt item {i}: Refactor needed",
                FilesInvolved = new[] { testFile },
                Fields = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("pending"),
                    ["priority"] = JsonSerializer.SerializeToElement("medium")
                }
            });
        }

        // Questions
        for (int i = 0; i < 3; i++)
        {
            await _memoryService.StoreMemoryAsync(new FlexibleMemoryEntry
            {
                Type = "Question",
                Content = $"Question {i}: How does this work?",
                Fields = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("pending")
                }
            });
        }

        // Act
        var result = await _tool.ExecuteAsync(query: "*");

        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = JsonDocument.Parse(json).RootElement;
        var analysis = response.GetProperty("analysis");
        var patterns = analysis.GetProperty("patterns");

        // Should detect technical debt pattern
        bool foundTechDebtPattern = false;
        foreach (var pattern in patterns.EnumerateArray())
        {
            var patternText = pattern.GetString() ?? "";
            if (patternText.Contains("technical debt"))
            {
                foundTechDebtPattern = true;
                // Found expected pattern
                break;
            }
        }
        foundTechDebtPattern.Should().BeTrue("Should detect high technical debt accumulation");
    }

    [Fact]
    public async Task Should_Handle_Type_Filtering()
    {
        // Arrange
        await StoreTestMemories();

        // Act - Search only for specific types
        var result = await _tool.ExecuteAsync(
            query: "*",
            types: new[] { "TechnicalDebt", "Question" });

        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = JsonDocument.Parse(json).RootElement;
        
        // Check query shows types
        var query = response.GetProperty("query");
        if (query.TryGetProperty("types", out var types))
        {
            types.GetArrayLength().Should().Be(2);
        }

        // Type distribution should only include requested types
        var summary = response.GetProperty("summary");
        if (summary.TryGetProperty("typeDistribution", out var typeDistribution))
        {
            foreach (var type in typeDistribution.EnumerateObject())
            {
                (new[] { "TechnicalDebt", "Question" }).Should().Contain(type.Name);
            }
        }
    }

    [Fact]
    public async Task Should_Support_Full_Mode()
    {
        // Arrange
        await StoreTestMemories();

        // Act
        var result = await _tool.ExecuteAsync(
            query: "*",
            maxResults: 5,
            mode: ResponseMode.Full);

        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = JsonDocument.Parse(json).RootElement;

        // Check meta mode
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("full");

        // In full mode, memories should be present
        if (response.TryGetProperty("memories", out var memories))
        {
            memories.Should().NotBeNull();
            memories.GetArrayLength().Should().BeGreaterThan(0);

            var firstMemory = memories[0];
            firstMemory.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
            firstMemory.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
            firstMemory.GetProperty("content").GetString().Should().NotBeNullOrEmpty();
            
            // Verify first memory structure
            firstMemory.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
            firstMemory.GetProperty("content").GetString().Should().NotBeNullOrEmpty();
        }
    }

    private async Task StoreTestMemories()
    {
        // Store various types of memories for testing
        var memories = new[]
        {
            new FlexibleMemoryEntry
            {
                Type = "ArchitecturalDecision",
                Content = "Use dependency injection for all services",
                Fields = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("approved"),
                    ["impact"] = JsonSerializer.SerializeToElement("high")
                }
            },
            new FlexibleMemoryEntry
            {
                Type = "TechnicalDebt",
                Content = "Refactor authentication system to use JWT",
                Fields = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("pending"),
                    ["priority"] = JsonSerializer.SerializeToElement("high")
                }
            },
            new FlexibleMemoryEntry
            {
                Type = "Question",
                Content = "How should we handle rate limiting?",
                Fields = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("pending")
                }
            },
            new FlexibleMemoryEntry
            {
                Type = "WorkingMemory",
                Content = "Currently working on memory search optimization",
                Fields = new Dictionary<string, JsonElement>
                {
                    ["expiresAt"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.AddHours(2))
                }
            }
        };

        foreach (var memory in memories)
        {
            await _memoryService.StoreMemoryAsync(memory);
        }
    }
}