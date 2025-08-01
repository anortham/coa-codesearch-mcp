using COA.CodeSearch.Contracts.Responses.BatchOperations;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services.ResponseBuilders;

public class BatchOperationsResponseBuilderTests
{
    [Fact]
    public void BatchSlowestOperation_SerializesWithExactPropertyNames()
    {
        // Arrange
        var operation = new BatchSlowestOperation
        {
            operation = "text_search",
            duration = "125.3ms"
        };

        // Act
        var json = JsonSerializer.Serialize(operation);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("text_search", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("duration", out var durProp));
        Assert.Equal("125.3ms", durProp.GetString());
    }

    [Fact]
    public void BatchOperationSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new BatchOperationSummary
        {
            index = 2,
            operation = "file_search",
            parameters = new { query = "UserService" },
            success = true,
            timing = "45.2ms"
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("index", out var indexProp));
        Assert.Equal(2, indexProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("file_search", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("parameters", out var paramsProp));
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("timing", out var timingProp));
        Assert.Equal("45.2ms", timingProp.GetString());
    }

    [Fact]
    public void BatchDetailData_SerializesWithExactPropertyNames()
    {
        // Arrange
        var data = new BatchDetailData
        {
            operations = new List<object> { new { operation = "text_search" } },
            results = new List<object> { new { totalHits = 42 } },
            operationSummaries = new List<object> { new { index = 0 } },
            resultAnalysis = new { totalMatches = 42 }
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("operations", out var opsProp));
        Assert.Equal(1, opsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("results", out var resultsProp));
        Assert.Equal(1, resultsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("operationSummaries", out var summariesProp));
        Assert.Equal(1, summariesProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("resultAnalysis", out var analysisProp));
        Assert.True(analysisProp.TryGetProperty("totalMatches", out var matchesProp));
        Assert.Equal(42, matchesProp.GetInt32());
    }

    [Fact]
    public void BatchQuery_SerializesWithExactPropertyNames()
    {
        // Arrange
        var query = new BatchQuery
        {
            operationCount = 3,
            operationTypes = new Dictionary<string, int> { { "text_search", 2 }, { "file_search", 1 } },
            workspace = "MyProject"
        };

        // Act
        var json = JsonSerializer.Serialize(query);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("operationCount", out var countProp));
        Assert.Equal(3, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("operationTypes", out var typesProp));
        Assert.True(typesProp.TryGetProperty("text_search", out var textSearchProp));
        Assert.Equal(2, textSearchProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("workspace", out var workspaceProp));
        Assert.Equal("MyProject", workspaceProp.GetString());
    }

    [Fact]
    public void BatchSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new BatchSummary
        {
            totalOperations = 5,
            completedOperations = 5,
            totalMatches = 127,
            totalTime = "450.3ms",
            avgTimePerOperation = "90.1ms"
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("totalOperations", out var totalProp));
        Assert.Equal(5, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("completedOperations", out var completedProp));
        Assert.Equal(5, completedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalMatches", out var matchesProp));
        Assert.Equal(127, matchesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalTime", out var timeProp));
        Assert.Equal("450.3ms", timeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("avgTimePerOperation", out var avgProp));
        Assert.Equal("90.1ms", avgProp.GetString());
    }

    [Fact]
    public void BatchResultEntry_SerializesWithExactPropertyNames()
    {
        // Arrange
        var entry = new BatchResultEntry
        {
            index = 1,
            operation = "text_search",
            query = "TODO",
            matches = 25,
            summary = new { totalHits = 25 },
            success = true,
            error = null,
            result = new { operation = "text_search", totalHits = 25 }
        };

        // Act
        var json = JsonSerializer.Serialize(entry);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("index", out var indexProp));
        Assert.Equal(1, indexProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("text_search", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("query", out var queryProp));
        Assert.Equal("TODO", queryProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("matches", out var matchesProp));
        Assert.Equal(25, matchesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Equal(JsonValueKind.Null, errorProp.ValueKind);
        Assert.True(parsed.RootElement.TryGetProperty("result", out var resultProp));
        Assert.True(resultProp.TryGetProperty("totalHits", out var hitsProp));
        Assert.Equal(25, hitsProp.GetInt32());
    }

    [Fact]
    public void BatchFallbackResult_SerializesWithExactPropertyNames()
    {
        // Arrange
        var fallback = new BatchFallbackResult
        {
            index = 0,
            operation = "file_search",
            query = "*.cs",
            matches = 42,
            summary = new { totalFound = 42 },
            result = new { files = new[] { "Program.cs", "Startup.cs" } }
        };

        // Act
        var json = JsonSerializer.Serialize(fallback);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("index", out var indexProp));
        Assert.Equal(0, indexProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("file_search", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("query", out var queryProp));
        Assert.Equal("*.cs", queryProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("matches", out var matchesProp));
        Assert.Equal(42, matchesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.TryGetProperty("totalFound", out var foundProp));
        Assert.Equal(42, foundProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("result", out var resultProp));
        Assert.True(resultProp.TryGetProperty("files", out var filesProp));
        Assert.Equal(2, filesProp.GetArrayLength());
    }

    [Fact]
    public void BatchResultsSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new BatchResultsSummary
        {
            included = 3,
            total = 5,
            hasMore = true
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("included", out var includedProp));
        Assert.Equal(3, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("total", out var totalProp));
        Assert.Equal(5, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("hasMore", out var hasMoreProp));
        Assert.True(hasMoreProp.GetBoolean());
    }

    [Fact]
    public void BatchDistribution_SerializesWithExactPropertyNames()
    {
        // Arrange
        var distribution = new BatchDistribution
        {
            byOperation = new Dictionary<string, int> { { "text_search", 3 }, { "file_search", 2 } },
            commonFiles = new List<BatchCommonFile> { new BatchCommonFile { file = "Program.cs", count = 2 } }
        };

        // Act
        var json = JsonSerializer.Serialize(distribution);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("byOperation", out var byOpProp));
        Assert.True(byOpProp.TryGetProperty("text_search", out var textSearchProp));
        Assert.Equal(3, textSearchProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("commonFiles", out var commonProp));
        Assert.Equal(1, commonProp.GetArrayLength());
    }

    [Fact]
    public void BatchAction_SerializesWithExactPropertyNames()
    {
        // Arrange
        var action = new BatchAction
        {
            id = "analyze_common_file",
            cmd = new Dictionary<string, object> { { "file_path", "/src/Program.cs" } },
            tokens = 1000,
            priority = "recommended"
        };

        // Act
        var json = JsonSerializer.Serialize(action);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("id", out var idProp));
        Assert.Equal("analyze_common_file", idProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("cmd", out var cmdProp));
        Assert.True(cmdProp.TryGetProperty("file_path", out var pathProp));
        Assert.Equal("/src/Program.cs", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("tokens", out var tokensProp));
        Assert.Equal(1000, tokensProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("priority", out var priorityProp));
        Assert.Equal("recommended", priorityProp.GetString());
    }

    [Fact]
    public void BatchCommonFile_SerializesWithExactPropertyNames()
    {
        // Arrange
        var commonFile = new BatchCommonFile
        {
            file = "src/Services/UserService.cs",
            count = 3
        };

        // Act
        var json = JsonSerializer.Serialize(commonFile);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("file", out var fileProp));
        Assert.Equal("src/Services/UserService.cs", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("count", out var countProp));
        Assert.Equal(3, countProp.GetInt32());
    }

    [Fact]
    public void BatchResultAnalysis_SerializesWithExactPropertyNames()
    {
        // Arrange
        var analysis = new BatchResultAnalysis
        {
            totalMatches = 127,
            highMatchOperations = 2,
            avgMatchesPerOperation = 25,
            commonFiles = new List<BatchCommonFile>
            {
                new() { file = "Program.cs", count = 3 },
                new() { file = "Startup.cs", count = 2 }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(analysis);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("totalMatches", out var totalProp));
        Assert.Equal(127, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("highMatchOperations", out var highProp));
        Assert.Equal(2, highProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("avgMatchesPerOperation", out var avgProp));
        Assert.Equal(25, avgProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("commonFiles", out var filesProp));
        Assert.Equal(2, filesProp.GetArrayLength());
    }

    [Fact]
    public void BatchGetResultSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var resultSummary = new BatchGetResultSummary
        {
            type = "text_search",
            matches = 42,
            summary = "42 results found"
        };

        // Act
        var json = JsonSerializer.Serialize(resultSummary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("type", out var typeProp));
        Assert.Equal("text_search", typeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("matches", out var matchesProp));
        Assert.Equal(42, matchesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.Equal("42 results found", summaryProp.GetString());
    }

    [Fact]
    public void BatchOperationsResponse_IntegrationTest_SerializesWithExactStructure()
    {
        // Arrange
        var response = new BatchOperationsResponse
        {
            success = true,
            operation = "batch_operations",
            query = new BatchQuery
            {
                operationCount = 3,
                operationTypes = new Dictionary<string, int> { { "text_search", 2 }, { "file_search", 1 } },
                workspace = "MyProject"
            },
            summary = new BatchSummary
            {
                totalOperations = 3,
                completedOperations = 3,
                totalMatches = 89,
                totalTime = "267.5ms",
                avgTimePerOperation = "89.2ms"
            },
            results = new List<object>
            {
                new BatchResultEntry
                {
                    index = 0,
                    operation = "text_search",
                    query = "TODO",
                    matches = 25,
                    summary = new { totalHits = 25 },
                    success = true,
                    error = null,
                    result = new { operation = "text_search", totalHits = 25 }
                }
            },
            resultsSummary = new BatchResultsSummary
            {
                included = 1,
                total = 3,
                hasMore = true
            },
            distribution = new BatchDistribution
            {
                byOperation = new Dictionary<string, int> { { "text_search", 2 }, { "file_search", 1 } },
                commonFiles = new List<BatchCommonFile> { new BatchCommonFile { file = "Program.cs", count = 2 } }
            },
            insights = new List<string> { "Executed 3 operations in 267.5ms", "Found 89 total matches" },
            actions = new List<object>
            {
                new BatchAction
                {
                    id = "analyze_common_file",
                    cmd = new Dictionary<string, object> { { "file_path", "/src/Program.cs" } },
                    tokens = 1000,
                    priority = "recommended"
                }
            },
            meta = new BatchMeta
            {
                mode = "summary",
                truncated = true,
                tokens = 1250,
                detailRequestToken = "cache_abc123",
                performance = new BatchPerformance
                {
                    parallel = true,
                    speedup = "2.1x",
                    slowestOperations = new List<BatchSlowestOperation>
                    {
                        new() { operation = "text_search", duration = "125.3ms" }
                    }
                },
                analysis = new BatchAnalysis
                {
                    effectiveness = "effective",
                    highMatchOperations = 2,
                    avgMatchesPerOperation = 29
                }
            },
            resourceUri = "codesearch-batch://batch_12345678_1234567890"
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Assert - verify complete structure with exact property names
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("batch_operations", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("query", out var queryProp));
        Assert.True(queryProp.TryGetProperty("operationCount", out var countProp));
        Assert.Equal(3, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.TryGetProperty("totalMatches", out var matchesProp));
        Assert.Equal(89, matchesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("results", out var resultsProp));
        Assert.Equal(1, resultsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("meta", out var metaProp));
        Assert.True(metaProp.TryGetProperty("performance", out var perfProp));
        Assert.True(perfProp.TryGetProperty("parallel", out var parallelProp));
        Assert.True(parallelProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("resourceUri", out var uriProp));
        Assert.Equal("codesearch-batch://batch_12345678_1234567890", uriProp.GetString());
    }
}