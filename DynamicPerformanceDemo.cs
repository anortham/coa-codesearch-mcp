using System;
using System.Diagnostics;
using System.Text.Json;

class DynamicPerformanceDemo
{
    static void Main()
    {
        const int iterations = 10000;
        
        // Create a sample response object
        var response = new
        {
            success = true,
            operation = "text_search",
            query = new { text = "search query", type = "standard" },
            summary = new { totalHits = 100L, filesMatched = 25 },
            results = new[] { 
                new { file = "test1.cs", score = 0.95 },
                new { file = "test2.cs", score = 0.90 },
                new { file = "test3.cs", score = 0.85 }
            }
        };
        
        var resourceUri = "codesearch://resource_12345";
        
        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = AddResourceUriWithDynamic(response, resourceUri);
            _ = AddResourceUriWithJsonSerialization(response, resourceUri);
        }
        
        // Test 1: Dynamic approach (current code)
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddResourceUriWithDynamic(response, resourceUri);
        }
        sw1.Stop();
        
        // Test 2: JSON serialization approach (ResponseEnhancer)
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddResourceUriWithJsonSerialization(response, resourceUri);
        }
        sw2.Stop();
        
        Console.WriteLine($"Performance comparison for {iterations} iterations:");
        Console.WriteLine($"Dynamic approach: {sw1.ElapsedMilliseconds}ms");
        Console.WriteLine($"JSON serialization: {sw2.ElapsedMilliseconds}ms");
        Console.WriteLine($"JSON is {(double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x slower");
        
        // Memory test
        var before1 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 1000; i++)
        {
            _ = AddResourceUriWithDynamic(response, resourceUri);
        }
        var after1 = GC.GetTotalAllocatedBytes();
        
        var before2 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 1000; i++)
        {
            _ = AddResourceUriWithJsonSerialization(response, resourceUri);
        }
        var after2 = GC.GetTotalAllocatedBytes();
        
        Console.WriteLine($"\nMemory allocation for 1000 iterations:");
        Console.WriteLine($"Dynamic approach: {(after1 - before1):N0} bytes");
        Console.WriteLine($"JSON serialization: {(after2 - before2):N0} bytes");
        Console.WriteLine($"JSON uses {(double)(after2 - before2) / (after1 - before1):F1}x more memory");
    }
    
    static object AddResourceUriWithDynamic(object response, string resourceUri)
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
    
    static object AddResourceUriWithJsonSerialization(object response, string resourceUri)
    {
        // This is what ResponseEnhancer.AddProperty does
        var json = JsonSerializer.Serialize(response);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        dict["resourceUri"] = resourceUri;
        return dict;
    }
}