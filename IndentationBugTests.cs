using System;
using System.IO;
using COA.CodeSearch.McpServer.Services;

class Program
{
    static void Main(string[] args)
    {
        // Recreate the test scenario exactly
        var content = "Line 1\n\tTab indented\n    Space indented\nNo indent";
        var lines = FileLineUtilities.SplitLines(content);
        
        Console.WriteLine("File content:");
        for (int i = 0; i < lines.Length; i++)
        {
            Console.WriteLine($"Line {i}: '{lines[i]}'");
            if (!string.IsNullOrEmpty(lines[i]))
            {
                var indent = FileLineUtilities.ExtractIndentation(lines[i]);
                Console.WriteLine($"  Indentation: '{indent}' (Length: {indent.Length})");
            }
        }
        
        // Test insertion at line 2 (0-based index 1, which should be before "Tab indented")  
        var detectedIndent = FileLineUtilities.DetectIndentationForInsertion(lines, 1);
        Console.WriteLine($"\nDetected indentation for insertion at line 2: '{detectedIndent}'");
        Console.WriteLine($"Expected: TAB (\\t)");
        Console.WriteLine($"Actual: {(detectedIndent == "\t" ? "TAB" : "SPACES")}");
    }
}