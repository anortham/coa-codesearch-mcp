using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the goto definition tool - jumps to where a symbol is defined
/// </summary>
public class GoToDefinitionParameters : VisualizableParameters
{
    /// <summary>
    /// The symbol name to find the definition for
    /// </summary>
    [Required]
    [Description("The symbol name to find the definition for")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search (defaults to current workspace)
    /// </summary>
    [Description("Path to the workspace directory to search (defaults to current workspace)")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Include full file content around definition
    /// </summary>
    [Description("Include full file content around definition")]
    public bool IncludeFullContext { get; set; } = false;

    /// <summary>
    /// Number of context lines around the definition
    /// </summary>
    [Description("Number of context lines around the definition")]
    [Range(0, 50)]
    public int ContextLines { get; set; } = 10;

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