using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for the TraceCallPath tool that provides hierarchical call chain analysis
/// </summary>
public class TraceCallPathParameters
{
    /// <summary>
    /// The symbol name to trace (method, class, or function name)
    /// </summary>
    [Description("The symbol name to trace (method, class, or function name)")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Direction to trace: 'up' (callers), 'down' (callees), or 'both' (full hierarchy)
    /// </summary>
    [Description("Direction to trace: 'up' (callers), 'down' (callees), or 'both' (full hierarchy)")]
    public string Direction { get; set; } = "up";

    /// <summary>
    /// Maximum depth to trace (prevents infinite recursion)
    /// </summary>
    [Description("Maximum depth to trace (prevents infinite recursion)")]
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Group results by file for better organization
    /// </summary>
    [Description("Group results by file for better organization")]
    public bool GroupByFile { get; set; } = true;

    /// <summary>
    /// Number of context lines to show around each call site
    /// </summary>
    [Description("Number of context lines to show around each call site")]
    public int ContextLines { get; set; } = 2;

    /// <summary>
    /// Include potential matches (less strict matching)
    /// </summary>
    [Description("Include potential matches (less strict matching)")]
    public bool IncludePotential { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return per level
    /// </summary>
    [Description("Maximum number of results to return per level")]
    public int MaxResults { get; set; } = 50;

    /// <summary>
    /// Case sensitive search
    /// </summary>
    [Description("Case sensitive search")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Show detailed method signatures in results
    /// </summary>
    [Description("Show detailed method signatures in results")]
    public bool ShowSignatures { get; set; } = true;

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Navigate to first result automatically when showing in VS Code
    /// </summary>
    [Description("Navigate to first result automatically when showing in VS Code")]
    public bool NavigateToFirstResult { get; set; } = false;

    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Override tool default: whether to show in VS Code (null = use tool default)
    /// </summary>
    [Description("Override tool default: whether to show in VS Code (null = use tool default)")]
    public bool? ShowInVSCode { get; set; }

    /// <summary>
    /// Override tool default: preferred view type (auto, grid, chart, markdown, tree, timeline)
    /// </summary>
    [Description("Override tool default: preferred view type (auto, grid, chart, markdown, tree, timeline)")]
    public string? VSCodeView { get; set; }

    /// <summary>
    /// Path to the workspace directory to search (defaults to current workspace)
    /// </summary>
    [Description("Path to the workspace directory to search (defaults to current workspace)")]
    public string WorkspacePath { get; set; } = string.Empty;
}