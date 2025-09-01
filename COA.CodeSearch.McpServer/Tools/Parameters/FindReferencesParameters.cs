using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the find references tool - finds all usages of a symbol
/// </summary>
public class FindReferencesParameters : VisualizableParameters
{
    /// <summary>
    /// The symbol name to find references for
    /// </summary>
    [Required]
    [Description("The symbol name to find references for")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Include potential references (less strict matching)
    /// </summary>
    [Description("Include potential references (less strict matching)")]
    public bool IncludePotential { get; set; } = false;

    /// <summary>
    /// Group results by file
    /// </summary>
    [Description("Group results by file")]
    public bool GroupByFile { get; set; } = true;

    /// <summary>
    /// Maximum number of references to return (default: 100)
    /// </summary>
    [Description("Maximum number of references to return")]
    [Range(1, 500)]
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Number of context lines around each reference
    /// </summary>
    [Description("Number of context lines around each reference")]
    [Range(0, 10)]
    public int ContextLines { get; set; } = 2;

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