using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the text search tool
/// </summary>
public class TextSearchParameters
{
    /// <summary>
    /// The search query string - supports multiple search types including regex, wildcards, and intelligent code patterns.
    /// </summary>
    /// <example>class UserService</example>
    /// <example>*.findBy*</example>
    /// <example>TODO|FIXME</example>
    [Required]
    [Description("Search query - supports regex, wildcards, code patterns (e.g., class UserService, *.findBy*, TODO|FIXME)")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path (default: current workspace)
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;

    // MaxResults removed - controlled internally based on ResponseMode to prevent token blowouts
    // ResponseMode determines the number of results:
    // - "summary": 20 results (optimized for quick overview)
    // - "full": 100 results (comprehensive but still token-safe)
    // - "adaptive": 50 results (balanced approach)

    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string ResponseMode { get; set; } = "adaptive";

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Disable caching for this request (default: false - caching enabled)
    /// </summary>
    [Description("Disable caching for this request (default: false - caching enabled)")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Search mode controls how queries are matched (default: auto - smart detection).
    /// - 'auto': Automatically detect best approach (symbol/pattern/standard routing)
    /// - 'exact': Literal exact matching (no fuzzy, no wildcards)
    /// - 'fuzzy': Typo-tolerant search (handles spelling variations)
    /// - 'semantic': Vector similarity search using embeddings (cross-language concept matching)
    /// - 'regex': Regular expression pattern matching (full regex syntax)
    /// </summary>
    /// <example>auto</example>
    /// <example>exact</example>
    /// <example>fuzzy</example>
    /// <example>semantic</example>
    /// <example>regex</example>
    [Description("Search mode: 'auto' (default - smart detection), 'exact' (literal), 'fuzzy' (typo-tolerant), 'semantic' (embeddings), 'regex' (patterns)")]
    public string SearchMode { get; set; } = "auto";


    /// <summary>
    /// Case sensitive search (default: false - case insensitive)
    /// </summary>
    [Description("Case sensitive search (default: false - case insensitive)")]
    public bool CaseSensitive { get; set; } = false;
}
