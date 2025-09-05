using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for search and replace operations
/// </summary>
public class SearchAndReplaceParams
{
    /// <summary>
    /// Pattern to search for
    /// </summary>
    [JsonPropertyName("searchPattern")]
    [Description("Pattern to search for")]
    [Required]
    public required string SearchPattern { get; set; }

    /// <summary>
    /// Replacement pattern
    /// </summary>
    [JsonPropertyName("replacePattern")]
    [Description("Replacement pattern")]
    [Required]
    public required string ReplacePattern { get; set; }

    /// <summary>
    /// Workspace path to search in
    /// </summary>
    [JsonPropertyName("workspacePath")]
    [Description("Workspace path to search in")]
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// File pattern filter (e.g., "*.cs", "src/**/*.ts")
    /// </summary>
    [JsonPropertyName("filePattern")]
    [Description("File pattern filter (e.g., \"*.cs\", \"src/**/*.ts\")")]
    public string? FilePattern { get; set; }

    /// <summary>
    /// Search type: standard, literal, regex, code
    /// </summary>
    [JsonPropertyName("searchType")]
    [Description("Search type: standard, literal, regex, code")]
    public string SearchType { get; set; } = "literal";

    /// <summary>
    /// Case sensitive search
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    [Description("Case sensitive search")]
    public bool CaseSensitive { get; set; } = true;

    /// <summary>
    /// Preview mode (default: true for safety)
    /// </summary>
    [JsonPropertyName("preview")]
    [Description("Preview mode - shows what would change without applying (default: true for safety)")]
    public bool Preview { get; set; } = true;

    /// <summary>
    /// Number of context lines around each match
    /// </summary>
    [JsonPropertyName("contextLines")]
    [Description("Number of context lines around each match")]
    [Range(0, 10)]
    public int ContextLines { get; set; } = 2;

    /// <summary>
    /// Maximum total matches to process
    /// </summary>
    [JsonPropertyName("maxMatches")]
    [Description("Maximum total matches to process")]
    [Range(1, 1000)]
    public int MaxMatches { get; set; } = 100;

    /// <summary>
    /// Response mode: summary, default, full
    /// </summary>
    [JsonPropertyName("responseMode")]
    [Description("Response mode: summary, default, full")]
    public string? ResponseMode { get; set; }

    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [JsonPropertyName("maxTokens")]
    [Description("Maximum tokens for response")]
    [Range(100, 100000)]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [JsonPropertyName("noCache")]
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; }
}

/// <summary>
/// Individual replacement operation
/// </summary>
public class ReplacementChange
{
    /// <summary>
    /// File path where the replacement occurs
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; set; }

    /// <summary>
    /// Line number of the change (1-based)
    /// </summary>
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    /// <summary>
    /// Column position in the line (0-based)
    /// </summary>
    [JsonPropertyName("columnStart")]
    public int ColumnStart { get; set; }

    /// <summary>
    /// Length of the original text
    /// </summary>
    [JsonPropertyName("originalLength")]
    public int OriginalLength { get; set; }

    /// <summary>
    /// Original text being replaced
    /// </summary>
    [JsonPropertyName("originalText")]
    public required string OriginalText { get; set; }

    /// <summary>
    /// New text to replace with
    /// </summary>
    [JsonPropertyName("replacementText")]
    public required string ReplacementText { get; set; }

    /// <summary>
    /// Context lines around the change
    /// </summary>
    [JsonPropertyName("contextBefore")]
    public string[]? ContextBefore { get; set; }

    /// <summary>
    /// Context lines after the change
    /// </summary>
    [JsonPropertyName("contextAfter")]
    public string[]? ContextAfter { get; set; }

    /// <summary>
    /// The complete line after replacement
    /// </summary>
    [JsonPropertyName("modifiedLine")]
    public string? ModifiedLine { get; set; }

    /// <summary>
    /// Whether this change was successfully applied (only set when preview=false)
    /// </summary>
    [JsonPropertyName("applied")]
    public bool? Applied { get; set; }

    /// <summary>
    /// Error message if application failed
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// File-level summary of changes
/// </summary>
public class FileChangeSummary
{
    /// <summary>
    /// File path
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; set; }

    /// <summary>
    /// Number of replacements in this file
    /// </summary>
    [JsonPropertyName("changeCount")]
    public int ChangeCount { get; set; }

    /// <summary>
    /// File last modified timestamp
    /// </summary>
    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; set; }

    /// <summary>
    /// Whether all changes in this file were successfully applied
    /// </summary>
    [JsonPropertyName("allApplied")]
    public bool? AllApplied { get; set; }
}

/// <summary>
/// Complete search and replace results
/// </summary>
public class SearchAndReplaceResult
{
    /// <summary>
    /// Operation summary
    /// </summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; set; }

    /// <summary>
    /// Whether this was a preview (no changes applied)
    /// </summary>
    [JsonPropertyName("preview")]
    public bool Preview { get; set; }

    /// <summary>
    /// Individual replacement changes
    /// </summary>
    [JsonPropertyName("changes")]
    public required List<ReplacementChange> Changes { get; set; }

    /// <summary>
    /// File-level summaries
    /// </summary>
    [JsonPropertyName("fileSummaries")]
    public required List<FileChangeSummary> FileSummaries { get; set; }

    /// <summary>
    /// Total files that would be modified
    /// </summary>
    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    /// <summary>
    /// Total number of replacements
    /// </summary>
    [JsonPropertyName("totalReplacements")]
    public int TotalReplacements { get; set; }

    /// <summary>
    /// Search execution time
    /// </summary>
    [JsonPropertyName("searchTime")]
    public TimeSpan SearchTime { get; set; }

    /// <summary>
    /// Apply time (only when preview=false)
    /// </summary>
    [JsonPropertyName("applyTime")]
    public TimeSpan? ApplyTime { get; set; }

    /// <summary>
    /// Original search pattern
    /// </summary>
    [JsonPropertyName("searchPattern")]
    public required string SearchPattern { get; set; }

    /// <summary>
    /// Original replace pattern
    /// </summary>
    [JsonPropertyName("replacePattern")]
    public required string ReplacePattern { get; set; }

    /// <summary>
    /// Whether results were truncated due to limits
    /// </summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    /// <summary>
    /// Processing insights and suggestions
    /// </summary>
    [JsonPropertyName("insights")]
    public List<string>? Insights { get; set; }
}