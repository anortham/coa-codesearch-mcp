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
    /// Detect async methods without ConfigureAwait(false).
    /// </summary>
    [JsonPropertyName("detectAsyncPatterns")]
    public bool DetectAsyncPatterns { get; set; } = true;

    /// <summary>
    /// Detect empty catch blocks that swallow exceptions.
    /// </summary>
    [JsonPropertyName("detectEmptyCatchBlocks")]
    public bool DetectEmptyCatchBlocks { get; set; } = true;

    /// <summary>
    /// Detect potentially unused using statements.
    /// </summary>
    [JsonPropertyName("detectUnusedUsings")]
    public bool DetectUnusedUsings { get; set; } = true;

    /// <summary>
    /// Detect magic numbers that should be constants.
    /// </summary>
    [JsonPropertyName("detectMagicNumbers")]
    public bool DetectMagicNumbers { get; set; } = true;

    /// <summary>
    /// Detect methods that are too large (high complexity).
    /// </summary>
    [JsonPropertyName("detectLargeMethods")]
    public bool DetectLargeMethods { get; set; } = true;

    /// <summary>
    /// Custom patterns to search for (regex patterns) - allows detection of project-specific anti-patterns.
    /// </summary>
    /// <example>["TODO.*urgent", "@deprecated"]</example>
    /// <example>["console\\.log", "debugger;"]</example>
    [JsonPropertyName("customPatterns")]
    public List<string> CustomPatterns { get; set; } = new();

    /// <summary>
    /// Severity levels to include in results - filter by impact level for focused analysis.
    /// </summary>
    /// <example>["Warning", "Error"]</example>
    /// <example>["Info"]</example>
    /// <example>["Error"]</example>
    [JsonPropertyName("severityLevels")]
    public List<string> SeverityLevels { get; set; } = new() { "Info", "Warning", "Error" };

    /// <summary>
    /// Maximum number of patterns to return.
    /// </summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 100;
}