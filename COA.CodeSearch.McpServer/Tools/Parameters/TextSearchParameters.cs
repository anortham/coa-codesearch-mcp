using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the text search tool
/// </summary>
public class TextSearchParameters
{
    /// <summary>
    /// The search query string
    /// </summary>
    [Required]
    [Description("The search query string")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

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
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Search type: 'standard', 'literal', 'code', 'wildcard', 'fuzzy', 'phrase', 'regex' (default: standard)
    /// </summary>
    [Description("Search type: 'standard', 'literal', 'code', 'wildcard', 'fuzzy', 'phrase', 'regex' (default: standard)")]
    public string SearchType { get; set; } = "standard";

    /// <summary>
    /// Case sensitive search
    /// </summary>
    [Description("Case sensitive search")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Automatically document findings in ProjectKnowledge MCP when patterns are detected
    /// </summary>
    [Description("Automatically document findings in ProjectKnowledge MCP when patterns are detected")]
    public bool DocumentFindings { get; set; } = false;

    /// <summary>
    /// Override the auto-detected knowledge type (TechnicalDebt, ProjectInsight, WorkNote)
    /// </summary>
    [Description("Override the auto-detected knowledge type (TechnicalDebt, ProjectInsight, WorkNote)")]
    public string? FindingType { get; set; }

    /// <summary>
    /// Enable intelligent pattern detection for auto-documentation
    /// </summary>
    [Description("Enable intelligent pattern detection for auto-documentation")]
    public bool AutoDetectPatterns { get; set; } = true;
}