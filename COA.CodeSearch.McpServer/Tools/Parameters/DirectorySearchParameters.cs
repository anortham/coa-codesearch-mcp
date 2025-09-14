using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for directory search operations - locate directories by name patterns with intelligent filtering
/// </summary>
public class DirectorySearchParameters
{
    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Path to the workspace directory to search. Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    /// <summary>
    /// The search pattern supporting glob patterns, wildcards, and regex for flexible directory matching.
    /// </summary>
    /// <example>*cache*</example>
    /// <example>test*</example>
    /// <example>src/**</example>
    [Required]
    [Description("The search pattern supporting glob and regex. Examples: '*cache*', 'test*', 'src/**'")]
    public string Pattern { get; set; } = string.Empty;
    
    /// <summary>
    /// Use regular expression instead of glob pattern for advanced directory matching capabilities.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Use regular expression instead of glob pattern for advanced matching.")]
    public bool? UseRegex { get; set; }
    
    /// <summary>
    /// Include subdirectories in search results for comprehensive directory discovery.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include subdirectories in search for comprehensive discovery (default: true).")]
    public bool? IncludeSubdirectories { get; set; } = true;
    
    /// <summary>
    /// Include hidden directories (starting with .) in search results, useful for finding configuration folders.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include hidden directories (starting with .) for finding config folders like .git, .vscode.")]
    public bool? IncludeHidden { get; set; } = false;
    
    [Range(1, 500)]
    [Description("Maximum number of results to return (default: 100, max: 500)")]
    public int? MaxResults { get; set; }
    
    [Range(100, 100000)]
    [Description("Maximum tokens for response (default: 8000)")]
    public int? MaxTokens { get; set; }
    
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string? ResponseMode { get; set; }
    
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; }
}