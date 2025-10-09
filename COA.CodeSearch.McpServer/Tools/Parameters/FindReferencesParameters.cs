using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the find references tool - finds all usages of a symbol
/// </summary>
public class FindReferencesParameters
{
    /// <summary>
    /// The symbol name to find all references for - CRITICAL for understanding impact before refactoring.
    /// </summary>
    /// <example>UpdateUser</example>
    /// <example>IUserService</example>
    /// <example>UserController</example>
    [Required]
    [Description("Symbol to find all references for (e.g., UpdateUser, IUserService, UserController)")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path (default: current workspace)
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;

    /// <summary>
    /// Include potential references using less strict matching. May include false positives but catches more usage patterns (default: false)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include potential matches (default: false - strict matching only)")]
    public bool IncludePotential { get; set; } = false;

    /// <summary>
    /// Group results by file for better organization and readability. Recommended for most use cases (default: true)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Group results by file (default: true - organized by file)")]
    public bool GroupByFile { get; set; } = true;

    /// <summary>
    /// Maximum number of references to return (default: 100)
    /// </summary>
    [Description("Maximum number of references to return (default: 100)")]
    [Range(1, 500)]
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Number of context lines to show around each reference for better understanding of usage (default: 2)
    /// </summary>
    /// <example>2</example>
    /// <example>5</example>
    /// <example>0</example>
    [Description("Context lines around each reference (default: 2, range: 0-10)")]
    [Range(0, 10)]
    public int ContextLines { get; set; } = 2;

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Disable caching for this request (default: false - caching enabled)
    /// </summary>
    [Description("Disable caching for this request (default: false - caching enabled)")]
    public bool NoCache { get; set; } = false;

    /// <summary>
    /// Case sensitive search (default: false - case insensitive)
    /// </summary>
    [Description("Case sensitive search (default: false - case insensitive)")]
    public bool CaseSensitive { get; set; } = false;
}