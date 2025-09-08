using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Tools.Models;

/// <summary>
/// Result of pattern detection analysis.
/// </summary>
public class FindPatternsResult
{
    /// <summary>
    /// Path to the analyzed file.
    /// </summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Programming language detected.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// List of patterns found in the code.
    /// </summary>
    [JsonPropertyName("patternsFound")]
    public List<CodePattern> PatternsFound { get; set; } = new();

    /// <summary>
    /// Total number of patterns detected.
    /// </summary>
    [JsonPropertyName("totalPatterns")]
    public int TotalPatterns { get; set; }

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    [JsonPropertyName("analysisTime")]
    public DateTime AnalysisTime { get; set; }

    /// <summary>
    /// Summary of patterns by type.
    /// </summary>
    [JsonPropertyName("patternSummary")]
    public Dictionary<string, int> PatternSummary => 
        PatternsFound.GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Summary of patterns by severity.
    /// </summary>
    [JsonPropertyName("severitySummary")]
    public Dictionary<string, int> SeveritySummary => 
        PatternsFound.GroupBy(p => p.Severity).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Represents a detected code pattern or issue.
/// </summary>
public class CodePattern
{
    /// <summary>
    /// Type of pattern detected (e.g., "AsyncWithoutConfigureAwait", "EmptyCatchBlock").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Severity level: Info, Warning, Error.
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the pattern.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Line number where the pattern was found (1-based).
    /// </summary>
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    /// <summary>
    /// The actual line of code containing the pattern.
    /// </summary>
    [JsonPropertyName("lineContent")]
    public string LineContent { get; set; } = string.Empty;

    /// <summary>
    /// Suggested improvement or fix.
    /// </summary>
    [JsonPropertyName("suggestion")]
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>
    /// Additional context or metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}