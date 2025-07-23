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
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class BatchOperationsV2Test : TestBase
{
    private readonly BatchOperationsToolV2 _tool;
    private readonly Mock<IBatchOperationsTool> _mockBatchTool;

    public BatchOperationsV2Test()
    {
        _mockBatchTool = new Mock<IBatchOperationsTool>();
        
        _tool = new BatchOperationsToolV2(
            ServiceProvider.GetRequiredService<ILogger<BatchOperationsToolV2>>(),
            _mockBatchTool.Object,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>());
    }

    private void SetupMockBatchToolForSuccess()
    {
        var mockResults = new
        {
            success = true,
            totalOperations = 2,
            results = new object[]
            {
                new { operation = "search_symbols", type = "search_symbols", success = true, result = new { symbols = new[] { "TestService", "TestController" } } },
                new { operation = "text_search", type = "text_search", success = true, result = new { matches = new[] { "public class Program" } } }
            }
        };

        _mockBatchTool.Setup(x => x.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);
    }

    private void SetupMockBatchToolForFailures()
    {
        var mockResults = new
        {
            success = true,
            totalOperations = 2,
            results = new object[]
            {
                new { operation = "search_symbols", type = "search_symbols", success = true, result = new { symbols = new[] { "TestService" } } },
                new { operation = "find_references", type = "find_references", success = false, error = "File not found" }
            }
        };

        _mockBatchTool.Setup(x => x.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);
    }

    private void SetupMockBatchToolForPatterns()
    {
        var mockResults = new
        {
            success = true,
            totalOperations = 3,
            results = new object[]
            {
                new { operation = "get_hover_info", type = "get_hover_info", success = true, result = new { name = "TestMethod", type = "void" } },
                new { operation = "find_references", type = "find_references", success = true, result = new { references = new[] { "TestCode.cs:10", "TestCode.cs:20" } } },
                new { operation = "go_to_definition", type = "go_to_definition", success = true, result = new { location = "TestCode.cs:15" } }
            }
        };

        _mockBatchTool.Setup(x => x.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Batch_Results()
    {
        // Arrange
        SetupMockBatchToolForSuccess();
        
        var operations = JsonSerializer.Deserialize<JsonElement>(@"[
            {
                ""operation"": ""search_symbols"",
                ""searchPattern"": ""*Test*"",
                ""workspacePath"": ""C:\\test\\project""
            },
            {
                ""operation"": ""text_search"",
                ""query"": ""public"",
                ""workspacePath"": ""C:\\test\\project"",
                ""maxResults"": 5
            }
        ]");

        // Act
        var result = await _tool.ExecuteAsync(
            operations: operations,
            workspacePath: "C:\\test\\project",
            mode: ResponseMode.Summary);

        // Assert
        result.Should().NotBeNull();

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Console.WriteLine("=== AI-OPTIMIZED BATCH OPERATIONS ==");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");

        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;

        // Check AI-optimized response structure
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("batch_operations");

        // Check batch summary
        var batch = response.GetProperty("batch");
        batch.GetProperty("totalOperations").GetInt32().Should().Be(2);
        batch.GetProperty("successCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        batch.GetProperty("operationTypes").Should().NotBeNull();

        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("successRate").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("executionTime").GetString().Should().NotBeNullOrEmpty();
        summary.GetProperty("topOperations").Should().NotBeNull();

        // Check analysis
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("patterns").Should().NotBeNull();

        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        Console.WriteLine("\nInsights:");
        foreach (var insight in insights.EnumerateArray())
        {
            Console.WriteLine($"- {insight.GetString()}");
        }

        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThan(0);
        Console.WriteLine("\nActions:");
        foreach (var action in actions.EnumerateArray())
        {
            var id = action.GetProperty("id").GetString();
            var priority = action.GetProperty("priority").GetString();
            Console.WriteLine($"- [{priority}] {id}");
        }

        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("totalTokens").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Handle_Failed_Operations()
    {
        // Arrange - Setup mock to return mixed success/failure results
        SetupMockBatchToolForFailures();
        
        var operations = JsonSerializer.Deserialize<JsonElement>(@"[
            {
                ""operation"": ""search_symbols"",
                ""searchPattern"": ""*Test*"",
                ""workspacePath"": ""C:\\test\\project""
            },
            {
                ""operation"": ""find_references"",
                ""filePath"": ""NonExistent.cs"",
                ""line"": 1,
                ""column"": 1
            }
        ]");

        // Act
        var result = await _tool.ExecuteAsync(
            operations: operations,
            workspacePath: "C:\\test\\project");

        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = JsonDocument.Parse(json).RootElement;

        // Should still succeed overall
        response.GetProperty("success").GetBoolean().Should().BeTrue();

        // But should report failure
        var batch = response.GetProperty("batch");
        batch.GetProperty("failureCount").GetInt32().Should().BeGreaterThan(0);

        // Error summary should be present
        var summary = response.GetProperty("summary");
        if (summary.TryGetProperty("errorSummary", out var errorSummary))
        {
            errorSummary.Should().NotBeNull();
            Console.WriteLine("\nError Summary:");
            Console.WriteLine(JsonSerializer.Serialize(errorSummary, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Should have retry action
        var actions = response.GetProperty("actions");
        var hasRetryAction = false;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.GetProperty("id").GetString() == "retry_failures")
            {
                hasRetryAction = true;
                action.GetProperty("priority").GetString().Should().Be("recommended");
                break;
            }
        }
        hasRetryAction.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Detect_Patterns_In_Operations()
    {
        // Arrange - Setup mock for pattern detection
        SetupMockBatchToolForPatterns();
        
        var operations = JsonSerializer.Deserialize<JsonElement>(@"[
            {
                ""operation"": ""get_hover_info"",
                ""filePath"": ""C:\\test\\project\\TestCode.cs"",
                ""line"": 10,
                ""column"": 15
            },
            {
                ""operation"": ""find_references"",
                ""filePath"": ""C:\\test\\project\\TestCode.cs"",
                ""line"": 10,
                ""column"": 15
            },
            {
                ""operation"": ""go_to_definition"",
                ""filePath"": ""C:\\test\\project\\TestCode.cs"",
                ""line"": 10,
                ""column"": 15
            }
        ]");

        // Act
        var result = await _tool.ExecuteAsync(operations: operations);

        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = JsonDocument.Parse(json).RootElement;
        
        Console.WriteLine("=== PATTERN DETECTION TEST RESPONSE ===");
        Console.WriteLine(json);
        Console.WriteLine("=== END ===");
        
        // Check if the response was successful
        if (!response.GetProperty("success").GetBoolean())
        {
            var error = response.GetProperty("error").GetString();
            throw new InvalidOperationException($"BatchOperationsV2 failed: {error}");
        }
        
        var analysis = response.GetProperty("analysis");
        var patterns = analysis.GetProperty("patterns");

        // Should detect focused analysis pattern
        bool foundFocusedPattern = false;
        foreach (var pattern in patterns.EnumerateArray())
        {
            var patternText = pattern.GetString() ?? "";
            if (patternText.Contains("Focused analysis") || patternText.Contains("operations on"))
            {
                foundFocusedPattern = true;
                Console.WriteLine($"Found pattern: {patternText}");
                break;
            }
        }
        foundFocusedPattern.Should().BeTrue("Should detect multiple operations on same file");
    }

    [Fact]
    public async Task Should_Support_Full_Mode()
    {
        // Arrange
        SetupMockBatchToolForSuccess();
        
        var operations = JsonSerializer.Deserialize<JsonElement>(@"[
            {
                ""operation"": ""search_symbols"",
                ""searchPattern"": ""Test"",
                ""workspacePath"": ""C:\\test\\project"",
                ""maxResults"": 3
            }
        ]");

        // Act
        var result = await _tool.ExecuteAsync(
            operations: operations,
            workspacePath: "C:\\test\\project",
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

        // In full mode, results should contain actual data
        var results = response.GetProperty("results");
        results.GetArrayLength().Should().BeGreaterThan(0);

        var firstResult = results[0];
        firstResult.GetProperty("operation").GetString().Should().Be("search_symbols");
        firstResult.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Should have actual result data
        if (firstResult.TryGetProperty("result", out var resultData))
        {
            resultData.Should().NotBeNull();
            Console.WriteLine("\nFull result data present");
        }
    }
}