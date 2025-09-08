using System.Text;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Manages a curated collection of real-world files from the COA CodeSearch MCP codebase
/// for comprehensive line editing tool testing. Uses the "dogfooding" approach to test
/// against the actual files the tools will encounter in practice.
/// </summary>
public static class TestFileCollection
{
    /// <summary>
    /// Small files (34-42 lines) - Basic functionality testing with minimal complexity
    /// </summary>
    public static readonly string[] SmallFiles = new[]
    {
        "./COA.CodeSearch.McpServer/Models/SearchMode.cs",              // 42 lines, 1207 bytes - enum definitions
        "./COA.CodeSearch.McpServer/Models/FileChangeEvent.cs",         // 35 lines, 917 bytes - simple class
        "./COA.CodeSearch.McpServer/Models/SearchAndReplaceMode.cs"     // 34 lines, 1160 bytes - enum definitions
    };

    /// <summary>
    /// Medium files (159-293 lines) - Real-world complexity testing with varied content types
    /// </summary>
    public static readonly string[] MediumFiles = new[]
    {
        "./COA.CodeSearch.McpServer/appsettings.json",                  // 159 lines, 5268 bytes - JSON configuration
        "./COA.CodeSearch.McpServer/Models/LineSearchModels.cs",        // 213 lines, 5852 bytes - model classes
        "./COA.CodeSearch.McpServer/Models/Api/ApiModels.cs",           // 215 lines, 5544 bytes - API models
        "./COA.CodeSearch.McpServer/Services/AdvancedPatternMatcher.cs", // 255 lines, 9640 bytes - service logic
        "./COA.CodeSearch.McpServer/Models/SearchAndReplaceModels.cs",  // 293 lines, 8551 bytes - complex models
        "./CLAUDE.md"                                                   // 203 lines, 7005 bytes - markdown documentation
    };

    /// <summary>
    /// Large files (500+ lines) - Performance and stress testing
    /// </summary>
    public static readonly string[] LargeFiles = new[]
    {
        "./README.md"  // 589 lines, 20613 bytes - comprehensive documentation with mixed content
    };

    /// <summary>
    /// Files with special characteristics for edge case testing
    /// </summary>
    public static readonly TestFileCharacteristics[] SpecialFiles = new[]
    {
        new TestFileCharacteristics
        {
            FilePath = "./COA.CodeSearch.McpServer/appsettings.json",
            Description = "JSON configuration file",
            ExpectedEncoding = Encoding.UTF8,
            ExpectedLineEnding = "\r\n",
            HasTrailingNewline = true,
            ContainsUnicode = false
        },
        new TestFileCharacteristics  
        {
            FilePath = "./README.md",
            Description = "Markdown documentation with mixed content",
            ExpectedEncoding = Encoding.UTF8,
            ExpectedLineEnding = "\r\n",
            HasTrailingNewline = true,
            ContainsUnicode = true // May contain emoji or special chars
        },
        new TestFileCharacteristics
        {
            FilePath = "./COA.CodeSearch.McpServer/Models/Api/ApiModels.cs", 
            Description = "C# model classes with complex indentation",
            ExpectedEncoding = Encoding.UTF8,
            ExpectedLineEnding = "\r\n",
            HasTrailingNewline = true,
            ContainsUnicode = false
        }
    };

    /// <summary>
    /// Gets all files categorized by size for comprehensive testing
    /// </summary>
    public static IEnumerable<string> AllTestFiles => 
        SmallFiles.Concat(MediumFiles).Concat(LargeFiles).Distinct();

    /// <summary>
    /// Gets a representative sample of files covering all categories and file types
    /// </summary>
    public static string[] RepresentativeSample => new[]
    {
        SmallFiles[0],    // Small C# enum file (42 lines)
        MediumFiles[0],   // Medium JSON configuration (159 lines)
        MediumFiles[3],   // Medium C# service logic (255 lines)
        MediumFiles[5],   // Medium Markdown documentation (203 lines)
        LargeFiles[0]     // Large comprehensive documentation (589 lines)
    };

    /// <summary>
    /// Validates that all test files exist and are accessible
    /// </summary>
    public static async Task<TestFileValidationResult> ValidateTestFiles()
    {
        var result = new TestFileValidationResult();
        var basePath = GetBasePath();

        foreach (var file in AllTestFiles)
        {
            var fullPath = Path.Combine(basePath, file.TrimStart('.', '/', '\\'));
            
            if (!File.Exists(fullPath))
            {
                result.MissingFiles.Add(file);
                continue;
            }

            try
            {
                var info = new FileInfo(fullPath);
                var content = await File.ReadAllTextAsync(fullPath);
                
                result.ValidFiles.Add(new TestFileInfo
                {
                    RelativePath = file,
                    FullPath = fullPath,
                    SizeBytes = info.Length,
                    LineCount = content.Split('\n').Length,
                    Encoding = DetectEncoding(fullPath),
                    LineEnding = DetectLineEnding(content)
                });
            }
            catch (Exception ex)
            {
                result.ErrorFiles.Add(file, ex.Message);
            }
        }

        return result;
    }

    /// <summary>
    /// Creates test files with specific characteristics for edge case testing
    /// </summary>
    public static async Task<string[]> CreateEdgeCaseTestFiles(string testDirectory)
    {
        Directory.CreateDirectory(testDirectory);
        var createdFiles = new List<string>();

        // File without trailing newline
        var noTrailingNewlinePath = Path.Combine(testDirectory, "no_trailing_newline.cs");
        await File.WriteAllTextAsync(noTrailingNewlinePath, "class Test\n{\n    // No trailing newline", Encoding.UTF8);
        createdFiles.Add(noTrailingNewlinePath);

        // File with Unicode characters
        var unicodePath = Path.Combine(testDirectory, "unicode_test.cs");
        await File.WriteAllTextAsync(unicodePath, "// Unicode test: 🚀 ñáéíóú\nclass Test { }\n", Encoding.UTF8);
        createdFiles.Add(unicodePath);

        // File with mixed line endings (CRLF + LF)
        var mixedLineEndingsPath = Path.Combine(testDirectory, "mixed_endings.txt");
        var mixedContent = "Line 1\r\nLine 2\nLine 3\r\nLine 4\n";
        await File.WriteAllBytesAsync(mixedLineEndingsPath, Encoding.UTF8.GetBytes(mixedContent));
        createdFiles.Add(mixedLineEndingsPath);

        // Very long line file
        var longLinePath = Path.Combine(testDirectory, "long_line.cs");
        var longLineContent = "// " + new string('A', 2000) + "\nclass Test { }\n";
        await File.WriteAllTextAsync(longLinePath, longLineContent, Encoding.UTF8);
        createdFiles.Add(longLinePath);

        return createdFiles.ToArray();
    }

    private static string GetBasePath()
    {
        // Get the solution root directory
        var currentDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(currentDir, "COA.CodeSearch.McpServer.sln")) && 
               Directory.GetParent(currentDir) != null)
        {
            currentDir = Directory.GetParent(currentDir)!.FullName;
        }
        return currentDir;
    }

    private static Encoding DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath).Take(4).ToArray();
        
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
            
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        return Encoding.UTF8; // Default assumption
    }

    private static string DetectLineEnding(string content)
    {
        if (content.Contains("\r\n")) return "\r\n";
        if (content.Contains("\n")) return "\n";
        if (content.Contains("\r")) return "\r";
        return Environment.NewLine; // Default
    }
}

/// <summary>
/// Characteristics of a test file for validation purposes
/// </summary>
public class TestFileCharacteristics
{
    public required string FilePath { get; set; }
    public required string Description { get; set; }
    public required Encoding ExpectedEncoding { get; set; }
    public required string ExpectedLineEnding { get; set; }
    public required bool HasTrailingNewline { get; set; }
    public required bool ContainsUnicode { get; set; }
}

/// <summary>
/// Information about a validated test file
/// </summary>
public class TestFileInfo
{
    public required string RelativePath { get; set; }
    public required string FullPath { get; set; }
    public required long SizeBytes { get; set; }
    public required int LineCount { get; set; }
    public required Encoding Encoding { get; set; }
    public required string LineEnding { get; set; }
}

/// <summary>
/// Result of test file validation
/// </summary>
public class TestFileValidationResult
{
    public List<TestFileInfo> ValidFiles { get; } = new();
    public List<string> MissingFiles { get; } = new();
    public Dictionary<string, string> ErrorFiles { get; } = new();
    
    public bool IsValid => MissingFiles.Count == 0 && ErrorFiles.Count == 0;
    public int TotalFiles => ValidFiles.Count + MissingFiles.Count + ErrorFiles.Count;
}