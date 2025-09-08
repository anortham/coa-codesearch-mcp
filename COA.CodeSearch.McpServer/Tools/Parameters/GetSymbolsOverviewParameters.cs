using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the get_symbols_overview tool - extracts all symbols from a file
/// </summary>
public class GetSymbolsOverviewParameters : VisualizableParameters
{
    /// <summary>
    /// Path to the file to analyze
    /// </summary>
    [Required]
    [Description("Path to the file to analyze")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory (defaults to current workspace)
    /// </summary>
    [Description("Path to the workspace directory (defaults to current workspace)")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Include method signatures and details
    /// </summary>
    [Description("Include method signatures and details")]
    public bool IncludeMethods { get; set; } = true;

    /// <summary>
    /// Include type inheritance and interface information
    /// </summary>
    [Description("Include type inheritance and interface information")]
    public bool IncludeInheritance { get; set; } = true;

    /// <summary>
    /// Include line numbers for each symbol
    /// </summary>
    [Description("Include line numbers for each symbol")]
    public bool IncludeLineNumbers { get; set; } = true;

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
}