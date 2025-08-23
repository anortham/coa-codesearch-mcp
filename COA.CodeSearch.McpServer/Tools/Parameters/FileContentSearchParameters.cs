using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for searching within a specific file's content
/// </summary>
public class FileContentSearchParameters : VisualizableParameters
{
    /// <summary>
    /// The specific file path to search within
    /// </summary>
    [Required]
    [Description("The specific file path to search within")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// The search pattern to find in the file
    /// </summary>
    [Required]
    [Description("The search pattern to find in the file")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory containing the file
    /// </summary>
    [Required]
    [Description("Path to the workspace directory containing the file")]
    public string WorkspacePath { get; set; } = string.Empty;

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
    /// Number of lines of context to show before and after each match (default: 3)
    /// </summary>
    [Description("Number of lines of context to show before and after each match (default: 3)")]
    [Range(0, 20)]
    public int ContextLines { get; set; } = 3;

    /// <summary>
    /// Maximum number of results to return (default: 50)
    /// </summary>
    [Description("Maximum number of results to return (default: 50)")]
    [Range(1, 500)]
    public int MaxResults { get; set; } = 50;

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
    /// Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string ResponseMode { get; set; } = "adaptive";
}