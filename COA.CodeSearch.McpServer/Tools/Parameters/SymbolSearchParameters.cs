using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the symbol search tool - finds type and method definitions using Tree-sitter data
/// </summary>
public class SymbolSearchParameters
{
    /// <summary>
    /// The symbol name to search for - supports partial matching and intelligent type detection.
    /// </summary>
    /// <example>UserService</example>
    /// <example>FindByEmail</example>
    /// <example>IRepository</example>
    [Required]
    [Description("Symbol to search for (e.g., UserService, FindByEmail, IRepository)")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path (e.g., C:\\source\\MyProject, ./src, ../other-project)")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Filter results by specific symbol type to narrow down search results.
    /// </summary>
    /// <example>class</example>
    /// <example>interface</example>
    /// <example>method</example>
    [Description("Filter by symbol type (e.g., class, interface, method, function, property)")]
    public string? SymbolType { get; set; }

    /// <summary>
    /// Include usage count for each symbol showing how many references exist across the codebase. Useful for understanding symbol popularity and impact.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include usage count (shows reference count for refactoring impact)")]
    public bool IncludeReferences { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return (default: 20)
    /// </summary>
    [Description("Maximum number of results to return")]
    [Range(1, 100)]
    public int MaxResults { get; set; } = 20;

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
    [Description("Case sensitive")]
    public bool CaseSensitive { get; set; } = false;
}