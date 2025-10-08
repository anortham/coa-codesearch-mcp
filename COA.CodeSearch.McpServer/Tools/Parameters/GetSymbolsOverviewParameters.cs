using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the get_symbols_overview tool - extracts all symbols from files with complete type information and line numbers
/// </summary>
public class GetSymbolsOverviewParameters
{
    /// <summary>
    /// Path to the file to analyze for symbol extraction. Must be an existing code file.
    /// </summary>
    /// <example>C:\source\MyProject\UserService.cs</example>
    /// <example>./src/components/Button.tsx</example>
    /// <example>../models/User.py</example>
    [Required]
    [Description("File path to analyze (e.g., C:\\source\\MyProject\\UserService.cs, ./src/components/Button.tsx)")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path. Defaults to current workspace if not specified.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path (e.g., C:\\source\\MyProject, ./src, ../other-project)")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Include method signatures and details for comprehensive symbol understanding.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include method signatures and details")]
    public bool IncludeMethods { get; set; } = true;

    /// <summary>
    /// Include type inheritance and interface information for understanding class relationships.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include inheritance and interface information")]
    public bool IncludeInheritance { get; set; } = true;

    /// <summary>
    /// Include line numbers for each symbol for precise navigation and editing.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include line numbers for precise navigation")]
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