using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class JsonNodePerformanceTest
{
    private readonly ITestOutputHelper _output;

    public JsonNodePerformanceTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompareJsonNodeVsAnonymousObjectPerformance()
    {
        const int iterations = 10000;
        
        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = BuildResponseWithAnonymousObject();
            _ = BuildResponseWithJsonNode();
        }

        // Measure anonymous object approach
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var result = BuildResponseWithAnonymousObject();
            var json = JsonSerializer.Serialize(result);
        }
        sw1.Stop();
        var anonymousTime = sw1.ElapsedMilliseconds;

        // Measure JsonNode approach
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var result = BuildResponseWithJsonNode();
            var json = result.ToJsonString();
        }
        sw2.Stop();
        var jsonNodeTime = sw2.ElapsedMilliseconds;

        // Calculate improvement
        var improvement = ((double)(anonymousTime - jsonNodeTime) / anonymousTime) * 100;
        
        _output.WriteLine($"Anonymous Object: {anonymousTime}ms for {iterations} iterations");
        _output.WriteLine($"JsonNode: {jsonNodeTime}ms for {iterations} iterations");
        _output.WriteLine($"Improvement: {improvement:F1}% ({anonymousTime - jsonNodeTime}ms faster)");
        
        // Note: Anonymous objects are actually faster for creating new data
        // This test documents the performance characteristics
        _output.WriteLine($"Performance ratio: {(double)jsonNodeTime / anonymousTime:F2}x");
        _output.WriteLine("Note: Anonymous objects are optimized by the compiler for data creation");
    }

    private object BuildResponseWithAnonymousObject()
    {
        return new
        {
            success = true,
            operation = "text_search",
            query = new
            {
                text = "search query",
                type = "standard",
                workspace = "C:\\workspace"
            },
            summary = new
            {
                totalHits = 100L,
                returnedResults = 50,
                filesMatched = 25,
                truncated = true
            },
            results = Enumerable.Range(0, 10).Select(i => new
            {
                file = $"file{i}.cs",
                path = $"src/file{i}.cs",
                score = 0.95 - (i * 0.05)
            }).ToList(),
            distribution = new
            {
                byExtension = new Dictionary<string, object>
                {
                    [".cs"] = new { count = 40, files = 20 },
                    [".js"] = new { count = 10, files = 5 }
                },
                byDirectory = new Dictionary<string, int>
                {
                    ["src"] = 35,
                    ["tests"] = 15
                }
            },
            hotspots = Enumerable.Range(0, 3).Select(i => new
            {
                file = $"hotspot{i}.cs",
                matches = 10 - i,
                lines = 5 - i
            }).ToList(),
            insights = new[] { "Insight 1", "Insight 2", "Insight 3" },
            actions = Enumerable.Range(0, 3).Select(i => new
            {
                id = $"action{i}",
                cmd = new { query = "refine", filter = $"*.{i}" },
                tokens = 100 + i * 50,
                priority = "high"
            }).ToList(),
            meta = new
            {
                mode = "summary",
                indexed = true,
                tokens = 1500,
                cached = "txt_12345678",
                safetyLimitApplied = false
            }
        };
    }

    private JsonNode BuildResponseWithJsonNode()
    {
        var response = new JsonObject
        {
            ["success"] = true,
            ["operation"] = "text_search"
        };

        response["query"] = new JsonObject
        {
            ["text"] = "search query",
            ["type"] = "standard",
            ["workspace"] = "C:\\workspace"
        };

        response["summary"] = new JsonObject
        {
            ["totalHits"] = 100L,
            ["returnedResults"] = 50,
            ["filesMatched"] = 25,
            ["truncated"] = true
        };

        var results = new JsonArray();
        for (int i = 0; i < 10; i++)
        {
            results.Add(new JsonObject
            {
                ["file"] = $"file{i}.cs",
                ["path"] = $"src/file{i}.cs",
                ["score"] = 0.95 - (i * 0.05)
            });
        }
        response["results"] = results;

        var distribution = new JsonObject();
        var byExtension = new JsonObject
        {
            [".cs"] = new JsonObject { ["count"] = 40, ["files"] = 20 },
            [".js"] = new JsonObject { ["count"] = 10, ["files"] = 5 }
        };
        distribution["byExtension"] = byExtension;
        
        var byDirectory = new JsonObject
        {
            ["src"] = 35,
            ["tests"] = 15
        };
        distribution["byDirectory"] = byDirectory;
        response["distribution"] = distribution;

        var hotspots = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            hotspots.Add(new JsonObject
            {
                ["file"] = $"hotspot{i}.cs",
                ["matches"] = 10 - i,
                ["lines"] = 5 - i
            });
        }
        response["hotspots"] = hotspots;

        var insights = new JsonArray { "Insight 1", "Insight 2", "Insight 3" };
        response["insights"] = insights;

        var actions = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            actions.Add(new JsonObject
            {
                ["id"] = $"action{i}",
                ["cmd"] = new JsonObject { ["query"] = "refine", ["filter"] = $"*.{i}" },
                ["tokens"] = 100 + i * 50,
                ["priority"] = "high"
            });
        }
        response["actions"] = actions;

        response["meta"] = new JsonObject
        {
            ["mode"] = "summary",
            ["indexed"] = true,
            ["tokens"] = 1500,
            ["cached"] = "txt_12345678",
            ["safetyLimitApplied"] = false
        };

        return response;
    }

    [Fact]
    public void MeasureMemoryAllocation()
    {
        const int iterations = 1000;
        
        // Measure anonymous object allocations
        var before1 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < iterations; i++)
        {
            var result = BuildResponseWithAnonymousObject();
            var json = JsonSerializer.Serialize(result);
        }
        var after1 = GC.GetTotalAllocatedBytes();
        var anonymousAllocations = after1 - before1;

        // Measure JsonNode allocations
        var before2 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < iterations; i++)
        {
            var result = BuildResponseWithJsonNode();
            var json = result.ToJsonString();
        }
        var after2 = GC.GetTotalAllocatedBytes();
        var jsonNodeAllocations = after2 - before2;

        // Calculate improvement
        var memoryImprovement = ((double)(anonymousAllocations - jsonNodeAllocations) / anonymousAllocations) * 100;
        
        _output.WriteLine($"Anonymous Object: {anonymousAllocations:N0} bytes for {iterations} iterations");
        _output.WriteLine($"JsonNode: {jsonNodeAllocations:N0} bytes for {iterations} iterations");
        _output.WriteLine($"Memory Improvement: {memoryImprovement:F1}% ({anonymousAllocations - jsonNodeAllocations:N0} bytes less)");
        
        // Note: Anonymous objects use less memory for creating new data
        // This test documents the memory characteristics
        _output.WriteLine($"Memory ratio: {(double)jsonNodeAllocations / anonymousAllocations:F2}x");
        _output.WriteLine("Note: JsonNode has overhead for mutable DOM structure");
    }
}