using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the goto definition tool - locates the exact definition of symbols with precision and context
/// </summary>
public class GoToDefinitionParameters
{
    /// <summary>
    /// The symbol name to find the exact definition for - VERIFY BEFORE CODING to understand types and signatures.
    /// </summary>
    /// <example>UserService</example>
    /// <example>FindByEmailAsync</example>
    /// <example>IUserRepository</example>
    [Required]
    [Description("The symbol name to find the exact definition for. Examples: 'UserService', 'FindByEmailAsync', 'IUserRepository'")]
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
    /// Include full file content around definition for comprehensive understanding of the symbol's context and usage (default: false)
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include full file content around definition (default: false - snippet only)")]
    public bool IncludeFullContext { get; set; } = false;

    /// <summary>
    /// Number of context lines to show around the definition for better understanding of implementation details (default: 10)
    /// </summary>
    /// <example>5</example>
    /// <example>15</example>
    /// <example>0</example>
    [Description("Number of context lines around the definition (default: 10, range: 0-50)")]
    [Range(0, 50)]
    public int ContextLines { get; set; } = 10;

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