using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for search and replace operations - BULK updates across files with safety preview mode
/// </summary>
public class SearchAndReplaceParams
{
    /// <summary>
    /// Pattern to search for - supports multiple search types for flexible matching.
    /// </summary>
    /// <example>oldMethodName</example>
    /// <example>TODO.*urgent</example>
    /// <example>class\s+\w+Service</example>
    [JsonPropertyName("searchPattern")]
    [Description("Search pattern (e.g., oldMethodName, TODO.*urgent, class\\s+\\w+Service)")]
    [Required]
    public required string SearchPattern { get; set; }

    /// <summary>
    /// Replacement pattern - use empty string for deletion, supports regex capture groups.
    /// </summary>
    /// <example>newMethodName</example>
    /// <example>FIXED: $1</example>
    /// <example></example>
    [JsonPropertyName("replacePattern")]
    [Description("Replace pattern (e.g., newMethodName, FIXED: $1, '' to delete)")]
    public string ReplacePattern { get; set; } = string.Empty;

    /// <summary>
    /// Workspace path to search in. Can be absolute or relative path.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [JsonPropertyName("workspacePath")]
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;

    /// <summary>
    /// File pattern filter to limit scope using glob patterns.
    /// </summary>
    /// <example>*.cs</example>
    /// <example>src/**/*.ts</example>
    /// <example>**/*.{js,jsx}</example>
    [JsonPropertyName("filePattern")]
    [Description("File pattern filter (e.g., *.cs, src/**/*.ts, **/*.{js,jsx})")]
    public string? FilePattern { get; set; }

    /// <summary>
    /// Search type: standard, literal, regex, code
    /// </summary>
    [JsonPropertyName("searchType")]
    [Description("Search type: standard, literal, regex, code")]
    public string SearchType { get; set; } = "literal";
        /// <summary>
        /// Matching mode for search and replace operations
        /// </summary>
        [JsonPropertyName("matchMode")]
        [Description("Matching mode: exact, whitespace_insensitive, multiline, fuzzy (default: exact)")]
        public string MatchMode { get; set; } = "exact";

    /// <summary>
    /// Fuzzy match threshold (0.0-1.0) - only used when matchMode = "fuzzy"
    /// 0.0 = perfect match only, 0.5 = moderate tolerance, 0.8 = high tolerance (default), 1.0 = match anything
    /// </summary>
    [JsonPropertyName("fuzzyThreshold")]
    [Description("Fuzzy match threshold 0.0-1.0 (default: 0.8, only for fuzzy mode)")]
    [Range(0.0, 1.0)]
    public float FuzzyThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Fuzzy match distance - how far to search in characters (only used when matchMode = "fuzzy")
    /// Higher = slower but more comprehensive. Default: 1000 characters
    /// </summary>
    [JsonPropertyName("fuzzyDistance")]
    [Description("Fuzzy search distance in characters (default: 1000, only for fuzzy mode)")]
    [Range(100, 10000)]
    public int FuzzyDistance { get; set; } = 1000;

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
    [Description("Preview mode - shows changes without applying (default: true)")]
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