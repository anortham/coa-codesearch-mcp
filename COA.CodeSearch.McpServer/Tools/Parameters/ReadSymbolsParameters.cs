using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the read_symbols tool - extracts specific symbol implementations from files using byte-offset precision for token-efficient reading
/// </summary>
public class ReadSymbolsParameters
{
    /// <summary>
    /// Path to the file to read symbols from. Must be an existing code file.
    /// Use AFTER get_symbols_overview to see file structure, THEN use this for specific implementations.
    /// </summary>
    /// <example>C:\source\MyProject\UserService.cs</example>
    /// <example>./src/components/Button.tsx</example>
    /// <example>../models/User.py</example>
    [Required]
    [Description("Path to the file to read symbols from. Examples: 'C:\\source\\MyProject\\UserService.cs', './src/components/Button.tsx', '../models/User.py'")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// List of symbol names to extract (e.g., ["Circle", "Draw", "UserService"]).
    /// Extract only what you need for 80-95% token savings vs reading entire file.
    /// </summary>
    /// <example>["Circle"]</example>
    /// <example>["UserService", "Authenticate", "ValidateToken"]</example>
    [Required]
    [MinLength(1)]
    [Description("List of symbol names to extract for token-efficient reading. Examples: [\"Circle\"], [\"UserService\", \"Authenticate\"]")]
    public List<string> SymbolNames { get; set; } = new();

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path (default: current workspace)
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path. Default: current workspace - Examples: 'C:\\source\\MyProject', './src', '../other-project'")]
    public string? WorkspacePath { get; set; } = null;

    /// <summary>
    /// Level of detail to include: "signature" (declaration only for quick reference), "implementation" (full code for understanding/refactoring), "full" (code + dependencies + callers + inheritance for complete context).
    /// Default: "implementation" for balanced detail (default: implementation)
    /// </summary>
    /// <example>signature</example>
    /// <example>implementation</example>
    /// <example>full</example>
    [Description("Detail level (default: 'implementation' - full code). Options: 'signature' (declaration only), 'implementation' (full code), 'full' (code + all analysis)")]
    public string DetailLevel { get; set; } = "implementation";

    /// <summary>
    /// Include what these symbols call (outbound dependencies) - useful for understanding symbol behavior and impact analysis (default: false).
    /// Shows method calls, function invocations from the symbol and its children.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include what these symbols call - outbound dependencies (default: false)")]
    public bool IncludeDependencies { get; set; } = false;

    /// <summary>
    /// Include what calls these symbols (inbound callers) - critical for refactoring impact analysis (default: false).
    /// Shows all references to this symbol across the workspace.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include what calls these symbols - inbound callers (default: false)")]
    public bool IncludeCallers { get; set; } = false;

    /// <summary>
    /// Include inheritance information (base classes, interfaces) for understanding type hierarchies (default: false).
    /// Shows extends and implements relationships for classes and interfaces.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include inheritance information - base classes and interfaces (default: false)")]
    public bool IncludeInheritance { get; set; } = false;

    /// <summary>
    /// Number of context lines to show before and after symbol code for better understanding (default: 3, range: 0-20).
    /// Note: Byte-offset extraction is surgical - context lines not currently supported with byte offsets.
    /// </summary>
    /// <example>3</example>
    /// <example>5</example>
    /// <example>0</example>
    [Description("Context lines before/after symbol (default: 3, range: 0-20)")]
    [Range(0, 20)]
    public int ContextLines { get; set; } = 3;

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
}
