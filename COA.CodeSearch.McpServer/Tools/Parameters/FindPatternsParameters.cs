using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for the FindPatterns tool that detects semantic patterns and code quality issues using Tree-sitter analysis
/// </summary>
public class FindPatternsParameters
{
    /// <summary>
    /// Path to the file to analyze for patterns. Must be an existing code file.
    /// </summary>
    /// <example>C:\source\MyProject\UserService.cs</example>
    /// <example>./src/components/Button.tsx</example>
    /// <example>../utils/helpers.js</example>
    [Required]
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Detect async methods without ConfigureAwait(false) (default: true)
    /// </summary>
    [JsonPropertyName("detectAsyncPatterns")]
    [Description("Detect async methods without ConfigureAwait(false) (default: true)")]
    public bool DetectAsyncPatterns { get; set; } = true;

    /// <summary>
    /// Detect empty catch blocks that swallow exceptions (default: true)
    /// </summary>
    [JsonPropertyName("detectEmptyCatchBlocks")]
    [Description("Detect empty catch blocks (default: true)")]
    public bool DetectEmptyCatchBlocks { get; set; } = true;

    /// <summary>
    /// Detect potentially unused using statements (default: true)
    /// </summary>
    [JsonPropertyName("detectUnusedUsings")]
    [Description("Detect unused using statements (default: true)")]
    public bool DetectUnusedUsings { get; set; } = true;

    /// <summary>
    /// Detect magic numbers that should be constants (default: true)
    /// </summary>
    [JsonPropertyName("detectMagicNumbers")]
    [Description("Detect magic numbers (default: true)")]
    public bool DetectMagicNumbers { get; set; } = true;

    /// <summary>
    /// Detect methods that are too large - high complexity (default: true)
    /// </summary>
    [JsonPropertyName("detectLargeMethods")]
    [Description("Detect large methods (default: true)")]
    public bool DetectLargeMethods { get; set; } = true;

    /// <summary>
    /// Detect unused private methods and fields - dead code (default: false)
    /// </summary>
    [JsonPropertyName("detectDeadCode")]
    [Description("Detect unused private methods and fields (default: false)")]
    public bool DetectDeadCode { get; set; } = false;

    /// <summary>
    /// Custom patterns to search for (regex patterns) - allows detection of project-specific anti-patterns (default: none)
    /// </summary>
    /// <example>["TODO.*urgent", "@deprecated"]</example>
    /// <example>["console\\.log", "debugger;"]</example>
    [JsonPropertyName("customPatterns")]
    [Description("Custom regex patterns to detect (default: none)")]
    public List<string> CustomPatterns { get; set; } = new();

    /// <summary>
    /// Severity levels to include in results - filter by impact level for focused analysis (default: [Info, Warning, Error])
    /// </summary>
    /// <example>["Warning", "Error"]</example>
    /// <example>["Info"]</example>
    /// <example>["Error"]</example>
    [JsonPropertyName("severityLevels")]
    [Description("Severity levels to include (default: [Info, Warning, Error] - all levels)")]
    public List<string> SeverityLevels { get; set; } = new() { "Info", "Warning", "Error" };

    /// <summary>
    /// Maximum number of patterns to return (default: 100)
    /// </summary>
    [JsonPropertyName("maxResults")]
    [Description("Maximum number of patterns to return (default: 100)")]
    public int MaxResults { get; set; } = 100;
}