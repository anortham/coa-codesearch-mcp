using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class DynamicDispatchDetailedTest
{
    private readonly ITestOutputHelper _output;

    public DynamicDispatchDetailedTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompareAllApproachesForAddingProperty()
    {
        const int iterations = 10000;
        
        var sampleResponse = new
        {
            success = true,
            operation = "text_search",
            query = new { text = "search", type = "standard" },
            summary = new { totalHits = 100L, filesMatched = 25 },
            results = new[] { new { file = "test.cs", score = 0.95 } }
        };
        
        var resourceUri = "codesearch://resource_12345";
        
        // Warm up all approaches
        for (int i = 0; i < 100; i++)
        {
            _ = Approach1_DynamicReconstruction(sampleResponse, resourceUri);
            _ = Approach2_JsonSerialization(sampleResponse, resourceUri);
            _ = Approach3_Reflection(sampleResponse, resourceUri);
            _ = Approach4_JsonNode(sampleResponse, resourceUri);
            _ = Approach5_DirectConstruction(sampleResponse, resourceUri);
        }

        // Test 1: Dynamic reconstruction (current code)
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = Approach1_DynamicReconstruction(sampleResponse, resourceUri);
        }
        sw1.Stop();
        
        // Test 2: JSON serialization (ResponseEnhancer)
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = Approach2_JsonSerialization(sampleResponse, resourceUri);
        }
        sw2.Stop();
        
        // Test 3: Reflection approach
        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = Approach3_Reflection(sampleResponse, resourceUri);
        }
        sw3.Stop();
        
        // Test 4: JsonNode approach
        var sw4 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = Approach4_JsonNode(sampleResponse, resourceUri);
        }
        sw4.Stop();
        
        // Test 5: Direct construction (if we knew the type)
        var sw5 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = Approach5_DirectConstruction(sampleResponse, resourceUri);
        }
        sw5.Stop();
        
        _output.WriteLine($"Performance comparison for {iterations} iterations:");
        _output.WriteLine($"1. Dynamic reconstruction: {sw1.ElapsedMilliseconds}ms");
        _output.WriteLine($"2. JSON serialization: {sw2.ElapsedMilliseconds}ms ({(double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x slower)");
        _output.WriteLine($"3. Reflection: {sw3.ElapsedMilliseconds}ms ({(double)sw3.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x slower)");
        _output.WriteLine($"4. JsonNode: {sw4.ElapsedMilliseconds}ms ({(double)sw4.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x slower)");
        _output.WriteLine($"5. Direct construction: {sw5.ElapsedMilliseconds}ms ({(double)sw5.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
    }

    private object Approach1_DynamicReconstruction(object response, string resourceUri)
    {
        dynamic d = response;
        return new
        {
            success = d.success,
            operation = d.operation,
            query = d.query,
            summary = d.summary,
            results = d.results,
            resourceUri = resourceUri
        };
    }

    private object Approach2_JsonSerialization(object response, string resourceUri)
    {
        var json = JsonSerializer.Serialize(response);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        dict["resourceUri"] = resourceUri;
        return dict;
    }

    private object Approach3_Reflection(object response, string resourceUri)
    {
        var properties = response.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var dict = new Dictionary<string, object>();
        
        foreach (var prop in properties)
        {
            dict[prop.Name] = prop.GetValue(response)!;
        }
        dict["resourceUri"] = resourceUri;
        
        return dict;
    }

    private object Approach4_JsonNode(object response, string resourceUri)
    {
        var json = JsonSerializer.Serialize(response);
        var node = JsonNode.Parse(json)!;
        node["resourceUri"] = resourceUri;
        return node;
    }

    private object Approach5_DirectConstruction(object response, string resourceUri)
    {
        // This simulates if we had a strongly typed model
        var r = response as dynamic;
        return new
        {
            success = true,
            operation = "text_search",
            query = new { text = "search", type = "standard" },
            summary = new { totalHits = 100L, filesMatched = 25 },
            results = new[] { new { file = "test.cs", score = 0.95 } },
            resourceUri = resourceUri
        };
    }

    [Fact]
    public void TestDynamicPerformanceByPropertyCount()
    {
        const int iterations = 1000;
        
        _output.WriteLine("Testing dynamic performance with different property counts:");
        
        // Test with different numbers of properties
        for (int propCount = 5; propCount <= 50; propCount += 5)
        {
            var obj = CreateObjectWithProperties(propCount);
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = AccessAllPropertiesWithDynamic(obj, propCount);
            }
            sw.Stop();
            
            _output.WriteLine($"{propCount} properties: {sw.ElapsedMilliseconds}ms for {iterations} iterations ({sw.ElapsedMilliseconds / (double)iterations:F3}ms per operation)");
        }
    }

    private object CreateObjectWithProperties(int count)
    {
        var dict = new Dictionary<string, object>();
        for (int i = 0; i < count; i++)
        {
            dict[$"property{i}"] = $"value{i}";
        }
        
        // Convert to anonymous object equivalent
        return JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(dict))!;
    }

    private Dictionary<string, object> AccessAllPropertiesWithDynamic(object obj, int expectedCount)
    {
        dynamic d = obj;
        var result = new Dictionary<string, object>();
        
        // We can't iterate dynamic properties easily, so we'll simulate accessing them
        // In real code, we'd access specific known properties
        var json = JsonSerializer.Serialize(obj);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        
        foreach (var kvp in dict)
        {
            result[kvp.Key] = kvp.Value;
        }
        
        return result;
    }

    [Fact]
    public void MeasureJsonSerializationOverhead()
    {
        const int iterations = 10000;
        
        var sizes = new[] { 10, 100, 1000, 10000, 100000 };
        
        _output.WriteLine("JSON serialization overhead by object size:");
        
        foreach (var size in sizes)
        {
            var obj = new
            {
                data = Enumerable.Range(0, size).Select(i => new { id = i, value = $"item{i}" }).ToArray()
            };
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var json = JsonSerializer.Serialize(obj);
                _ = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }
            sw.Stop();
            
            _output.WriteLine($"Array size {size}: {sw.ElapsedMilliseconds}ms for {iterations} iterations ({sw.ElapsedMilliseconds / (double)iterations:F3}ms per operation)");
        }
    }
}