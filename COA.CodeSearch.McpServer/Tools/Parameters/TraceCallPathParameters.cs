using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for the TraceCallPath tool - builds hierarchical call chains to understand code execution flow and dependencies
/// </summary>
public class TraceCallPathParameters
{
    /// <summary>
    /// The symbol name to trace (method, class, or function name) for building call hierarchies.
    /// </summary>
    /// <example>ProcessPayment</example>
    /// <example>UserService</example>
    /// <example>validateInput</example>
    [Description("Symbol to trace for call hierarchies (e.g., ProcessPayment, UserService, validateInput)")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Direction to trace: 'up' (callers), 'down' (callees), or 'both' (full hierarchy) for comprehensive analysis.
    /// </summary>
    /// <example>up</example>
    /// <example>down</example>
    /// <example>both</example>
    [Description("Trace direction: up (who calls), down (what calls), both (full tree)")]
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
    [Description("Case sensitive")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Show detailed method signatures in results
    /// </summary>
    [Description("Show detailed method signatures")]
    public bool ShowSignatures { get; set; } = true;

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path (e.g., C:\\source\\MyProject, ./src, ../other-project)")]
    public string WorkspacePath { get; set; } = string.Empty;
}