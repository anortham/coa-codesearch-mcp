using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the goto definition tool - locates the exact definition of symbols with precision and context
/// </summary>
public class GoToDefinitionParameters : VisualizableParameters
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
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Path to the workspace directory to search. Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Include full file content around definition for comprehensive understanding of the symbol's context and usage.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include full file content around definition for comprehensive context understanding.")]
    public bool IncludeFullContext { get; set; } = false;

    /// <summary>
    /// Number of context lines to show around the definition for better understanding of implementation details.
    /// </summary>
    /// <example>5</example>
    /// <example>15</example>
    /// <example>0</example>
    [Description("Number of context lines around the definition (0-50). More context helps understand implementation details.")]
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