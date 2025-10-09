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
    /// Direction to trace: 'up' (callers), 'down' (callees), or 'both' (full hierarchy) for comprehensive analysis (default: up)
    /// </summary>
    /// <example>up</example>
    /// <example>down</example>
    /// <example>both</example>
    [Description("Trace direction (default: up - who calls this): up, down (what this calls), both (full tree)")]
    public string Direction { get; set; } = "up";

    /// <summary>
    /// Maximum depth to trace - prevents infinite recursion (default: 3)
    /// </summary>
    [Description("Maximum depth to trace (default: 3 - prevents infinite recursion)")]
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Group results by file for better organization (default: true)
    /// </summary>
    [Description("Group results by file (default: true - organized by file)")]
    public bool GroupByFile { get; set; } = true;

    /// <summary>
    /// Number of context lines to show around each call site (default: 2)
    /// </summary>
    [Description("Number of context lines around each call site (default: 2)")]
    public int ContextLines { get; set; } = 2;

    /// <summary>
    /// Include potential matches - less strict matching (default: false)
    /// </summary>
    [Description("Include potential matches (default: false - strict matching only)")]
    public bool IncludePotential { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return per level (default: 50)
    /// </summary>
    [Description("Maximum number of results to return per level (default: 50)")]
    public int MaxResults { get; set; } = 50;

    /// <summary>
    /// Case sensitive search (default: false - case insensitive)
    /// </summary>
    [Description("Case sensitive search (default: false - case insensitive)")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Show detailed method signatures in results (default: true)
    /// </summary>
    [Description("Show detailed method signatures (default: true - includes signatures)")]
    public bool ShowSignatures { get; set; } = true;

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Disable caching for this request (default: false - caching enabled)
    /// </summary>
    [Description("Disable caching for this request (default: false - caching enabled)")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path (default: current workspace)
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;
}