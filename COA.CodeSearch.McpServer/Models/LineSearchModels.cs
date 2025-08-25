using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for line-level search operations
/// </summary>
public class LineSearchParams
{
    /// <summary>
    /// Search pattern/query
    /// </summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; set; }

    /// <summary>
    /// Workspace path to search in
    /// </summary>
    [JsonPropertyName("workspacePath")]
    public required string WorkspacePath { get; set; }

    /// <summary>
    /// File pattern filter (e.g., "*.cs", "src/**/*.ts")
    /// </summary>
    [JsonPropertyName("filePattern")]
    public string? FilePattern { get; set; }

    /// <summary>
    /// Number of context lines around each match (like grep -A/-B)
    /// </summary>
    [JsonPropertyName("contextLines")]
    public int ContextLines { get; set; } = 2;

    /// <summary>
    /// Case sensitive search
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Maximum results per file to prevent flooding
    /// </summary>
    [JsonPropertyName("maxResultsPerFile")]
    public int MaxResultsPerFile { get; set; } = 10;

    /// <summary>
    /// Maximum total results across all files
    /// </summary>
    [JsonPropertyName("maxTotalResults")]
    public int MaxTotalResults { get; set; } = 100;

    /// <summary>
    /// Search type: standard, regex, wildcard, literal
    /// </summary>
    [JsonPropertyName("searchType")]
    public string SearchType { get; set; } = "standard";

    /// <summary>
    /// Response mode: summary, default, full
    /// </summary>
    [JsonPropertyName("responseMode")]
    public string? ResponseMode { get; set; }

    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }
}

/// <summary>
/// Individual line match within a file
/// </summary>
public class LineMatch
{
    /// <summary>
    /// Line number where match occurs
    /// </summary>
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    /// <summary>
    /// The matching line content
    /// </summary>
    [JsonPropertyName("lineContent")]
    public required string LineContent { get; set; }

    /// <summary>
    /// Context lines around the match
    /// </summary>
    [JsonPropertyName("contextLines")]
    public string[]? ContextLines { get; set; }

    /// <summary>
    /// Start line of context (for navigation)
    /// </summary>
    [JsonPropertyName("startLine")]
    public int? StartLine { get; set; }

    /// <summary>
    /// End line of context (for navigation)
    /// </summary>
    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    /// <summary>
    /// Highlighted fragments within the line
    /// </summary>
    [JsonPropertyName("highlightedFragments")]
    public List<string>? HighlightedFragments { get; set; }
}

/// <summary>
/// File-level results containing all line matches
/// </summary>
public class LineSearchFileResult
{
    /// <summary>
    /// Full file path
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; set; }

    /// <summary>
    /// All line matches in this file
    /// </summary>
    [JsonPropertyName("matches")]
    public required List<LineMatch> Matches { get; set; }

    /// <summary>
    /// Total matches in file (may be more than returned due to limits)
    /// </summary>
    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

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
}

/// <summary>
/// Complete line search results
/// </summary>
public class LineSearchResult
{
    /// <summary>
    /// Search execution summary
    /// </summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; set; }

    /// <summary>
    /// File results with line matches
    /// </summary>
    [JsonPropertyName("files")]
    public required List<LineSearchFileResult> Files { get; set; }

    /// <summary>
    /// Total files searched
    /// </summary>
    [JsonPropertyName("totalFilesSearched")]
    public int TotalFilesSearched { get; set; }

    /// <summary>
    /// Total files with matches
    /// </summary>
    [JsonPropertyName("totalFilesWithMatches")]
    public int TotalFilesWithMatches { get; set; }

    /// <summary>
    /// Total line matches across all files
    /// </summary>
    [JsonPropertyName("totalLineMatches")]
    public int TotalLineMatches { get; set; }

    /// <summary>
    /// Search execution time
    /// </summary>
    [JsonPropertyName("searchTime")]
    public TimeSpan SearchTime { get; set; }

    /// <summary>
    /// Original search pattern
    /// </summary>
    [JsonPropertyName("query")]
    public required string Query { get; set; }

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