using COA.CodeSearch.McpServer.Services;

// Test the exact scenario from the failing test
var lines = new string[]
{
    "Line 1",           // Index 0: no indent
    "\tTab indented",   // Index 1: tab indent
    "    Space indented", // Index 2: 4 spaces
    "No indent"         // Index 3: no indent
};

Console.WriteLine("=== DEBUGGING INDENTATION DETECTION ===");
Console.WriteLine("File content:");
for (int i = 0; i < lines.Length; i++)
{
    var indent = FileLineUtilities.ExtractIndentation(lines[i]);
    Console.WriteLine($"  Index {i}: '{lines[i]}' -> Indentation: '{indent}' (Length: {indent.Length})");
}

Console.WriteLine("\n=== TESTING INSERT AT LINE 2 (targetLineIndex = 1) ===");
var result = FileLineUtilities.DetectIndentationForInsertion(lines, 1);
Console.WriteLine($"Detected indentation: '{result}' (Length: {result.Length})");

if (result == "\t")
    Console.WriteLine("✅ SUCCESS: Detected tab indentation");
else if (result == "    ")
    Console.WriteLine("❌ FAILURE: Detected 4 spaces instead of tab");
else
    Console.WriteLine($"❓ UNEXPECTED: Got '{result}'");