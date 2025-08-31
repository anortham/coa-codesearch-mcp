using System;
using System.IO;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestApp;

class Program
{
    static void Main(string[] args)
    {
        var logger = NullLogger<TypeExtractionService>.Instance;
        var typeExtractionService = new TypeExtractionService(logger);
        
        Console.WriteLine("Testing Vue type extraction...");
        TestVueFile(typeExtractionService);
        
        Console.WriteLine("\nTesting Razor type extraction...");
        TestRazorFile(typeExtractionService);
    }
    
    static void TestVueFile(TypeExtractionService service)
    {
        var vueFilePath = "TestComponent.vue";
        
        if (!File.Exists(vueFilePath))
        {
            Console.WriteLine("Vue test file not found!");
            return;
        }
        
        var vueContent = File.ReadAllText(vueFilePath);
        var result = service.ExtractTypes(vueContent, vueFilePath);
        
        Console.WriteLine($"Vue extraction successful: {result.Success}");
        Console.WriteLine($"Language: {result.Language}");
        Console.WriteLine($"Types found: {result.Types.Count}");
        Console.WriteLine($"Methods found: {result.Methods.Count}");
        
        foreach (var type in result.Types)
        {
            Console.WriteLine($"  Type: {type.Name} ({type.Kind}) at line {type.Line}");
        }
        
        foreach (var method in result.Methods)
        {
            Console.WriteLine($"  Method: {method.Name} -> {method.ReturnType} at line {method.Line}");
        }
    }
    
    static void TestRazorFile(TypeExtractionService service)
    {
        var razorFilePath = "UserManagement.cshtml";
        
        if (!File.Exists(razorFilePath))
        {
            Console.WriteLine("Razor test file not found!");
            return;
        }
        
        var razorContent = File.ReadAllText(razorFilePath);
        var result = service.ExtractTypes(razorContent, razorFilePath);
        
        Console.WriteLine($"Razor extraction successful: {result.Success}");
        Console.WriteLine($"Language: {result.Language}");
        Console.WriteLine($"Types found: {result.Types.Count}");
        Console.WriteLine($"Methods found: {result.Methods.Count}");
        
        foreach (var type in result.Types)
        {
            Console.WriteLine($"  Type: {type.Name} ({type.Kind}) at line {type.Line}");
        }
        
        foreach (var method in result.Methods)
        {
            Console.WriteLine($"  Method: {method.Name} -> {method.ReturnType} at line {method.Line}");
        }
    }
}