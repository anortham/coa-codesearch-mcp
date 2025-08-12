using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools.Parameters;

/// <summary>
/// Parameters for directory search operations
/// </summary>
public class DirectorySearchParameters
{
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    [Required]
    [Description("The search pattern (glob or regex)")]
    public string Pattern { get; set; } = string.Empty;
    
    [Description("Use regular expression instead of glob pattern")]
    public bool? UseRegex { get; set; }
    
    [Description("Include subdirectories in search")]
    public bool? IncludeSubdirectories { get; set; } = true;
    
    [Description("Include hidden directories (starting with .)")]
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