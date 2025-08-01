using COA.CodeSearch.Contracts.Responses.DirectorySearch;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services.ResponseBuilders;

public class DirectorySearchResponseBuilderTests
{
    [Fact]
    public void DirectoryFileItem_SerializesWithExactPropertyNames()
    {
        // Arrange
        var item = new DirectoryFileItem
        {
            path = "src/Services",
            fileCount = 42
        };

        // Act
        var json = JsonSerializer.Serialize(item);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Services", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("fileCount", out var countProp));
        Assert.Equal(42, countProp.GetInt32());
    }

    [Fact]
    public void DirectorySearchQuery_SerializesWithExactPropertyNames()
    {
        // Arrange
        var query = new DirectorySearchQuery
        {
            text = "Services",
            type = "fuzzy",
            workspace = "MyProject"
        };

        // Act
        var json = JsonSerializer.Serialize(query);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("text", out var textProp));
        Assert.Equal("Services", textProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("type", out var typeProp));
        Assert.Equal("fuzzy", typeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("workspace", out var workspaceProp));
        Assert.Equal("MyProject", workspaceProp.GetString());
    }

    [Fact]
    public void DirectorySearchSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new DirectorySearchSummary
        {
            totalFound = 15,
            searchTime = "2.5ms",
            performance = "excellent",
            avgDepth = 2.3
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("totalFound", out var totalProp));
        Assert.Equal(15, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("searchTime", out var timeProp));
        Assert.Equal("2.5ms", timeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("performance", out var perfProp));
        Assert.Equal("excellent", perfProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("avgDepth", out var depthProp));
        Assert.Equal(2.3, depthProp.GetDouble());
    }

    [Fact]
    public void DirectorySearchHotspots_SerializesWithExactPropertyNames()
    {
        // Arrange
        var hotspots = new DirectorySearchHotspots
        {
            byParent = new Dictionary<string, int> { { "src", 5 }, { "tests", 3 } },
            byFileCount = new List<DirectoryFileItem>
            {
                new() { path = "src/Services", fileCount = 42 }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(hotspots);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("byParent", out var parentProp));
        Assert.True(parentProp.TryGetProperty("src", out var srcProp));
        Assert.Equal(5, srcProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("byFileCount", out var fileCountProp));
        Assert.True(fileCountProp.EnumerateArray().MoveNext());
    }

    [Fact]
    public void DirectorySearchAnalysis_SerializesWithExactPropertyNames()
    {
        // Arrange
        var analysis = new DirectorySearchAnalysis
        {
            patterns = new List<string> { "Found 15 directories", "Excellent performance" },
            depthDistribution = new Dictionary<int, int> { { 0, 2 }, { 1, 5 }, { 2, 8 } },
            hotspots = new DirectorySearchHotspots
            {
                byParent = new Dictionary<string, int> { { "src", 5 } },
                byFileCount = new List<DirectoryFileItem>()
            }
        };

        // Act
        var json = JsonSerializer.Serialize(analysis);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("patterns", out var patternsProp));
        Assert.Equal(2, patternsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("depthDistribution", out var depthProp));
        Assert.True(depthProp.TryGetProperty("0", out var depth0Prop));
        Assert.Equal(2, depth0Prop.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("hotspots", out var hotspotsProp));
        Assert.True(hotspotsProp.TryGetProperty("byParent", out _));
    }

    [Fact]
    public void DirectorySearchResultItem_SerializesWithExactPropertyNames()
    {
        // Arrange
        var resultItem = new DirectorySearchResultItem
        {
            name = "Services",
            path = "src/Services",
            fileCount = 15,
            depth = 1,
            score = 0.95
        };

        // Act
        var json = JsonSerializer.Serialize(resultItem);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("name", out var nameProp));
        Assert.Equal("Services", nameProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Services", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("fileCount", out var countProp));
        Assert.Equal(15, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("depth", out var depthProp));
        Assert.Equal(1, depthProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("score", out var scoreProp));
        Assert.Equal(0.95, scoreProp.GetDouble());
    }

    [Fact]
    public void DirectorySearchResultsSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new DirectorySearchResultsSummary
        {
            included = 10,
            total = 15,
            hasMore = true
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("included", out var includedProp));
        Assert.Equal(10, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("total", out var totalProp));
        Assert.Equal(15, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("hasMore", out var hasMoreProp));
        Assert.True(hasMoreProp.GetBoolean());
    }

    [Fact]
    public void DirectorySearchAction_SerializesWithExactPropertyNames()
    {
        // Arrange
        var action = new DirectorySearchAction
        {
            id = "explore_directory",
            description = "Explore top matching directory",
            command = "ls",
            parameters = new Dictionary<string, object> { { "path", "/src/Services" } },
            priority = "recommended"
        };

        // Act
        var json = JsonSerializer.Serialize(action);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("id", out var idProp));
        Assert.Equal("explore_directory", idProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("description", out var descProp));
        Assert.Equal("Explore top matching directory", descProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("command", out var cmdProp));
        Assert.Equal("ls", cmdProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("parameters", out var paramsProp));
        Assert.True(paramsProp.TryGetProperty("path", out var pathProp));
        Assert.True(parsed.RootElement.TryGetProperty("priority", out var priorityProp));
        Assert.Equal("recommended", priorityProp.GetString());
    }

    [Fact]
    public void DirectorySearchMeta_SerializesWithExactPropertyNames()
    {
        // Arrange
        var meta = new DirectorySearchMeta
        {
            mode = "summary",
            truncated = true,
            tokens = 450,
            format = "ai-optimized",
            cached = "dirsearch_abc123_def456"
        };

        // Act
        var json = JsonSerializer.Serialize(meta);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("mode", out var modeProp));
        Assert.Equal("summary", modeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("truncated", out var truncProp));
        Assert.True(truncProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("tokens", out var tokensProp));
        Assert.Equal(450, tokensProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("format", out var formatProp));
        Assert.Equal("ai-optimized", formatProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("cached", out var cachedProp));
        Assert.Equal("dirsearch_abc123_def456", cachedProp.GetString());
    }

    [Fact]
    public void DirectorySearchResponse_IntegrationTest_SerializesWithExactStructure()
    {
        // Arrange
        var response = new DirectorySearchResponse
        {
            success = true,
            operation = "directory_search",
            query = new DirectorySearchQuery
            {
                text = "Services",
                type = "fuzzy",
                workspace = "MyProject"
            },
            summary = new DirectorySearchSummary
            {
                totalFound = 15,
                searchTime = "2.5ms",
                performance = "excellent",
                avgDepth = 2.3
            },
            analysis = new DirectorySearchAnalysis
            {
                patterns = new List<string> { "Found 15 directories" },
                depthDistribution = new Dictionary<int, int> { { 1, 15 } },
                hotspots = new DirectorySearchHotspots
                {
                    byParent = new Dictionary<string, int> { { "src", 5 } },
                    byFileCount = new List<DirectoryFileItem>
                    {
                        new() { path = "src/Services", fileCount = 42 }
                    }
                }
            },
            results = new List<DirectorySearchResultItem>
            {
                new()
                {
                    name = "Services",
                    path = "src/Services",
                    fileCount = 42,
                    depth = 1,
                    score = 0.95
                }
            },
            resultsSummary = new DirectorySearchResultsSummary
            {
                included = 1,
                total = 15,
                hasMore = true
            },
            insights = new List<string> { "Found 15 directories in 2.5ms" },
            actions = new List<object>
            {
                new DirectorySearchAction
                {
                    id = "explore_directory",
                    description = "Explore top matching directory",
                    command = "ls",
                    parameters = new Dictionary<string, object> { { "path", "/src/Services" } },
                    priority = "recommended"
                }
            },
            meta = new DirectorySearchMeta
            {
                mode = "summary",
                truncated = true,
                tokens = 450,
                format = "ai-optimized",
                cached = "dirsearch_abc123_def456"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Assert - verify complete structure with exact property names
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("directory_search", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("query", out var queryProp));
        Assert.True(queryProp.TryGetProperty("text", out var textProp));
        Assert.Equal("Services", textProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.TryGetProperty("totalFound", out var totalProp));
        Assert.Equal(15, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("analysis", out var analysisProp));
        Assert.True(analysisProp.TryGetProperty("patterns", out var patternsProp));
        Assert.Equal(1, patternsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("results", out var resultsProp));
        Assert.Equal(1, resultsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("resultsSummary", out var resSummaryProp));
        Assert.True(resSummaryProp.TryGetProperty("included", out var includedProp));
        Assert.Equal(1, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("insights", out var insightsProp));
        Assert.Equal(1, insightsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("actions", out var actionsProp));
        Assert.Equal(1, actionsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("meta", out var metaProp));
        Assert.True(metaProp.TryGetProperty("mode", out var modeProp));
        Assert.Equal("summary", modeProp.GetString());
    }
}