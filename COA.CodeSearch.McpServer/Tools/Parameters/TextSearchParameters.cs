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
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path (e.g., C:\\source\\MyProject, ./src, ../other-project)")]
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
    /// Search type controls how the query is interpreted and matched against indexed content.
    /// </summary>
    /// <example>standard</example>
    /// <example>regex</example>
    /// <example>wildcard</example>
    [Description("Search type: standard (intelligent), literal (exact), code (code-aware), wildcard (*?), fuzzy (typos), phrase, regex")]
    public string SearchType { get; set; } = "standard";

    /// <summary>
    /// Search mode determines the intelligence level and preprocessing applied to queries.
    /// </summary>
    /// <example>auto</example>
    /// <example>code</example>
    /// <example>symbol</example>
    [Description("Search mode: auto (smart routing), literal (no preprocessing), code (camelCase), symbol (classes/methods), standard, fuzzy")]
    public string SearchMode { get; set; } = "auto";


    /// <summary>
    /// Case sensitive search
    /// </summary>
    [Description("Case sensitive")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Automatically document findings in ProjectKnowledge MCP when patterns are detected
    /// </summary>
    [Description("Auto-document findings in ProjectKnowledge MCP")]
    public bool DocumentFindings { get; set; } = false;

    /// <summary>
    /// Override the auto-detected knowledge type (TechnicalDebt, ProjectInsight, WorkNote)
    /// </summary>
    [Description("Override the auto-detected knowledge type (TechnicalDebt, ProjectInsight, WorkNote)")]
    public string? FindingType { get; set; }

    /// <summary>
    /// Enable intelligent pattern detection for auto-documentation
    /// </summary>
    [Description("Enable intelligent pattern detection")]
    public bool AutoDetectPatterns { get; set; } = true;
}
