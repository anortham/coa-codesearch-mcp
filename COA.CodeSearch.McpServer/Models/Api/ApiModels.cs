using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models.Api;

/// <summary>
/// Represents a single search result with location and confidence information
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Absolute file path to the result
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Line number (1-based for Roslyn compatibility)
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Column number (1-based for Roslyn compatibility)  
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Confidence score from 0.0 to 1.0
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Code snippet showing the match context
    /// </summary>
    public required string Preview { get; set; }

    /// <summary>
    /// Type of symbol if applicable (class, interface, method, property, etc.)
    /// </summary>
    public string? SymbolType { get; set; }

    /// <summary>
    /// Additional metadata about the match
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Standard response format for search operations
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// List of search results ordered by confidence (highest first)
    /// </summary>
    public required List<SearchResult> Results { get; set; }

    /// <summary>
    /// Total number of results found (may be higher than returned results due to limits)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Time taken to execute the search in milliseconds
    /// </summary>
    public long SearchTimeMs { get; set; }

    /// <summary>
    /// The query that was executed
    /// </summary>
    public required string Query { get; set; }

    /// <summary>
    /// Workspace path that was searched
    /// </summary>
    public string? Workspace { get; set; }
}

/// <summary>
/// Response for existence check operations
/// </summary>
public class ExistsResponse
{
    /// <summary>
    /// Whether the symbol/text exists in the workspace
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Number of occurrences found
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Types of symbols found (class, interface, method, etc.)
    /// </summary>
    public List<string>? Types { get; set; }

    /// <summary>
    /// Time taken to check existence in milliseconds
    /// </summary>
    public long SearchTimeMs { get; set; }
}

/// <summary>
/// Request model for batch search operations
/// </summary>
public class BatchSearchRequest
{
    /// <summary>
    /// Workspace path to search in
    /// </summary>
    [Required]
    public required string Workspace { get; set; }

    /// <summary>
    /// List of searches to perform
    /// </summary>
    [Required]
    public required List<SearchItem> Searches { get; set; }
}

/// <summary>
/// Individual search item for batch operations
/// </summary>
public class SearchItem
{
    /// <summary>
    /// Type of search: symbol, text, pattern, definition
    /// </summary>
    [Required]
    public required string Type { get; set; }

    /// <summary>
    /// Search query/name/pattern
    /// </summary>
    [Required]
    public required string Query { get; set; }

    /// <summary>
    /// Additional options specific to the search type
    /// </summary>
    public Dictionary<string, string>? Options { get; set; }
}

/// <summary>
/// Response for batch search operations
/// </summary>
public class BatchSearchResponse
{
    /// <summary>
    /// Results for each search in the same order as requested
    /// </summary>
    public required List<SearchResponse> Results { get; set; }

    /// <summary>
    /// Total time taken for all searches in milliseconds
    /// </summary>
    public long TotalTimeMs { get; set; }

    /// <summary>
    /// Number of searches that were successful
    /// </summary>
    public int SuccessfulSearches { get; set; }

    /// <summary>
    /// Any errors that occurred during batch processing
    /// </summary>
    public List<string>? Errors { get; set; }
}

/// <summary>
/// Workspace information
/// </summary>
public class WorkspaceInfo
{
    /// <summary>
    /// Absolute path to the workspace
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Whether the workspace is currently indexed
    /// </summary>
    public bool IsIndexed { get; set; }

    /// <summary>
    /// Number of files in the index
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Last time the workspace was indexed
    /// </summary>
    public DateTime? LastIndexed { get; set; }

    /// <summary>
    /// Size of the index in bytes
    /// </summary>
    public long IndexSizeBytes { get; set; }
}

/// <summary>
/// Response for listing workspaces
/// </summary>
public class WorkspacesResponse
{
    /// <summary>
    /// List of indexed workspaces
    /// </summary>
    public required List<WorkspaceInfo> Workspaces { get; set; }

    /// <summary>
    /// Total number of indexed workspaces
    /// </summary>
    public int TotalCount { get; set; }
}