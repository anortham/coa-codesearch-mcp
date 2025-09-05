using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the symbol search tool - finds type and method definitions using Tree-sitter data
/// </summary>
public class SymbolSearchParameters : VisualizableParameters
{
    /// <summary>
    /// The symbol name to search for (class, interface, method, etc.)
    /// </summary>
    [Required]
    [Description("The symbol name to search for (class, interface, method, etc.)")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search (defaults to current workspace)
    /// </summary>
    [Description("Path to the workspace directory to search (defaults to current workspace)")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Filter by symbol type (class, interface, method, function, etc.)
    /// </summary>
    [Description("Optional: Filter by symbol type (class, interface, method, function, etc.)")]
    public string? SymbolType { get; set; }

    /// <summary>
    /// Include usage count for the symbol (shows how many references exist)
    /// </summary>
    [Description("Include usage count for the symbol (shows how many references exist)")]
    public bool IncludeReferences { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return (default: 20)
    /// </summary>
    [Description("Maximum number of results to return")]
    [Range(1, 100)]
    public int MaxResults { get; set; } = 20;

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
    /// Case sensitive search
    /// </summary>
    [Description("Case sensitive search")]
    public bool CaseSensitive { get; set; } = false;
}