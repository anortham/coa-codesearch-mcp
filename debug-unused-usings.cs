using System;
using System.Text.RegularExpressions;

{
        var testCode = @"
using System;
using System.Collections.Generic;
using System.Unused.Namespace; // This should be detected
using UnusedCustom.Something; // This should be detected

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""Hello""); // Uses System
        var list = new List<int>(); // Uses System.Collections.Generic
    }
}";

        // Extract using statements
        var usingMatches = Regex.Matches(testCode, @"using\s+([^;]+);");
        foreach (Match match in usingMatches)
        {
            var namespaceName = match.Groups[1].Value.Trim();
            Console.WriteLine($"Found using: {namespaceName}");
        }

        // Remove using statements
        var codeWithoutUsings = Regex.Replace(testCode, @"using\s+[^;]+;\s*", "", RegexOptions.Multiline);
        Console.WriteLine("Code without usings:");
        Console.WriteLine(codeWithoutUsings);

        // Remove comments
        var codeWithoutComments = Regex.Replace(codeWithoutUsings, @"//.*$", "", RegexOptions.Multiline);
        Console.WriteLine("Code without comments:");
        Console.WriteLine(codeWithoutComments);

        // Test logic similar to FindPatternsTool
        var usingNamespaces = new[] { "System", "System.Collections.Generic", "System.Unused.Namespace", "UnusedCustom.Something" };
        var exactCommonNamespaces = new[] { 
            "System", "System.Collections.Generic", "System.Linq", "System.Threading",
            "Microsoft.Extensions.Logging", "Microsoft.Extensions.DependencyInjection" 
        };
        
        foreach (var namespaceName in usingNamespaces)
        {
            Console.WriteLine($"\n--- Checking namespace: {namespaceName} ---");
            
            // Check if it's in exact common namespaces
            var isCommon = exactCommonNamespaces.Contains(namespaceName);
            Console.WriteLine($"Is common namespace: {isCommon}");
            
            if (isCommon)
            {
                Console.WriteLine("Skipping because it's a common namespace");
                continue;
            }
            
            var parts = namespaceName.Split('.');
            var lastPart = parts.LastOrDefault();
            Console.WriteLine($"Last part: '{lastPart}'");
            
            if (!string.IsNullOrEmpty(lastPart))
            {
                var lastPartPattern = @"\b" + Regex.Escape(lastPart) + @"\b";
                var found = Regex.IsMatch(codeWithoutComments, lastPartPattern);
                Console.WriteLine($"Last part '{lastPart}' found in code: {found}");
                
                if (!found)
                {
                    Console.WriteLine($"*** SHOULD BE DETECTED AS UNUSED: {namespaceName} ***");
                }
            }
        }
}