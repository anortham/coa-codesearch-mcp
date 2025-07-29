using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Text.Json;

class ExpandoObjectTest
{
    static void Main()
    {
        const int iterations = 10000;
        
        // Create test objects
        var anonymousObject = new
        {
            success = true,
            operation = "text_search",
            query = new { text = "search query", type = "standard" },
            summary = new { totalHits = 100L, filesMatched = 25 },
            results = new[] { 
                new { file = "test1.cs", score = 0.95 },
                new { file = "test2.cs", score = 0.90 }
            }
        };
        
        var resourceUri = "codesearch://resource_12345";
        
        // Warm up all approaches
        for (int i = 0; i < 100; i++)
        {
            _ = AddPropertyWithDynamic(anonymousObject, resourceUri);
            _ = AddPropertyWithExpando(anonymousObject, resourceUri);
            _ = AddPropertyWithExpandoFromJson(anonymousObject, resourceUri);
            _ = AddPropertyWithDictionary(anonymousObject, resourceUri);
        }
        
        Console.WriteLine($"Performance comparison for {iterations} iterations:\n");
        
        // Test 1: Dynamic reconstruction (current best)
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddPropertyWithDynamic(anonymousObject, resourceUri);
        }
        sw1.Stop();
        Console.WriteLine($"1. Dynamic reconstruction: {sw1.ElapsedMilliseconds}ms");
        
        // Test 2: ExpandoObject with manual copying
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddPropertyWithExpando(anonymousObject, resourceUri);
        }
        sw2.Stop();
        Console.WriteLine($"2. ExpandoObject (manual): {sw2.ElapsedMilliseconds}ms ({(double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
        
        // Test 3: ExpandoObject via JSON
        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddPropertyWithExpandoFromJson(anonymousObject, resourceUri);
        }
        sw3.Stop();
        Console.WriteLine($"3. ExpandoObject (JSON): {sw3.ElapsedMilliseconds}ms ({(double)sw3.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
        
        // Test 4: Dictionary approach
        var sw4 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AddPropertyWithDictionary(anonymousObject, resourceUri);
        }
        sw4.Stop();
        Console.WriteLine($"4. Dictionary<string,object>: {sw4.ElapsedMilliseconds}ms ({(double)sw4.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
        
        // Memory test
        Console.WriteLine($"\nMemory allocation for 1000 iterations:");
        
        var before1 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 1000; i++)
        {
            _ = AddPropertyWithDynamic(anonymousObject, resourceUri);
        }
        var after1 = GC.GetTotalAllocatedBytes();
        Console.WriteLine($"1. Dynamic: {(after1 - before1):N0} bytes");
        
        var before2 = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 1000; i++)
        {
            _ = AddPropertyWithExpando(anonymousObject, resourceUri);
        }
        var after2 = GC.GetTotalAllocatedBytes();
        Console.WriteLine($"2. ExpandoObject: {(after2 - before2):N0} bytes ({(double)(after2 - before2) / (after1 - before1):F1}x)");
        
        // Test property access performance
        Console.WriteLine($"\nProperty access performance (100,000 iterations):");
        TestPropertyAccess();
    }
    
    static object AddPropertyWithDynamic(object response, string resourceUri)
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
    
    static dynamic AddPropertyWithExpando(object response, string resourceUri)
    {
        dynamic source = response;
        dynamic expando = new ExpandoObject();
        
        expando.success = source.success;
        expando.operation = source.operation;
        expando.query = source.query;
        expando.summary = source.summary;
        expando.results = source.results;
        expando.resourceUri = resourceUri;
        
        return expando;
    }
    
    static dynamic AddPropertyWithExpandoFromJson(object response, string resourceUri)
    {
        // Convert to JSON then to ExpandoObject
        var json = JsonSerializer.Serialize(response);
        dynamic expando = JsonSerializer.Deserialize<ExpandoObject>(json);
        expando.resourceUri = resourceUri;
        return expando;
    }
    
    static Dictionary<string, object> AddPropertyWithDictionary(object response, string resourceUri)
    {
        dynamic d = response;
        return new Dictionary<string, object>
        {
            ["success"] = d.success,
            ["operation"] = d.operation,
            ["query"] = d.query,
            ["summary"] = d.summary,
            ["results"] = d.results,
            ["resourceUri"] = resourceUri
        };
    }
    
    static void TestPropertyAccess()
    {
        // Create test objects
        var anonymous = new { value = "test", number = 42 };
        
        dynamic expando = new ExpandoObject();
        expando.value = "test";
        expando.number = 42;
        
        var dict = new Dictionary<string, object> { ["value"] = "test", ["number"] = 42 };
        
        const int accessIterations = 100000;
        
        // Test anonymous object access via dynamic
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < accessIterations; i++)
        {
            dynamic d = anonymous;
            var v = d.value;
            var n = d.number;
        }
        sw1.Stop();
        Console.WriteLine($"Anonymous (dynamic): {sw1.ElapsedMilliseconds}ms");
        
        // Test ExpandoObject access
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < accessIterations; i++)
        {
            var v = expando.value;
            var n = expando.number;
        }
        sw2.Stop();
        Console.WriteLine($"ExpandoObject: {sw2.ElapsedMilliseconds}ms ({(double)sw2.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
        
        // Test Dictionary access
        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < accessIterations; i++)
        {
            var v = dict["value"];
            var n = dict["number"];
        }
        sw3.Stop();
        Console.WriteLine($"Dictionary: {sw3.ElapsedMilliseconds}ms ({(double)sw3.ElapsedMilliseconds / sw1.ElapsedMilliseconds:F1}x)");
    }
}