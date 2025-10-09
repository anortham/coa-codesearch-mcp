using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for directory search operations - locate directories by name patterns with intelligent filtering
/// </summary>
public class DirectorySearchParameters
{
    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path (default: current workspace)
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;

    /// <summary>
    /// The search pattern supporting glob patterns, wildcards, and regex for flexible directory matching
    /// </summary>
    /// <example>*cache*</example>
    /// <example>test*</example>
    /// <example>src/**</example>
    [Required]
    [Description("The search pattern supporting glob and regex. Examples: '*cache*', 'test*', 'src/**'")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Use regular expression instead of glob pattern for advanced directory matching capabilities (default: false)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Use regular expression instead of glob pattern for advanced matching (default: false).")]
    public bool UseRegex { get; set; } = false;

    /// <summary>
    /// Include subdirectories in search results for comprehensive directory discovery (default: true)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include subdirectories in search for comprehensive discovery (default: true).")]
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// Include hidden directories (starting with .) in search results, useful for finding configuration folders (default: false)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include hidden directories (starting with .) for finding config folders like .git, .vscode (default: false).")]
    public bool IncludeHidden { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return (default: 100)
    /// </summary>
    [Range(1, 500)]
    [Description("Maximum number of results to return (default: 100, max: 500)")]
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Range(100, 100000)]
    [Description("Maximum tokens for response (default: 8000)")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string ResponseMode { get; set; } = "adaptive";

    /// <summary>
    /// Disable caching for this request (default: false - caching enabled)
    /// </summary>
    [Description("Disable caching for this request (default: false)")]
    public bool NoCache { get; set; } = false;
}