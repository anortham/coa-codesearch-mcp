using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for smart refactoring operations
/// </summary>
public class SmartRefactorParameters
{
    /// <summary>
    /// The refactoring operation to perform
    /// </summary>
    [Description("Refactoring operation: rename_symbol, extract_function, inline_variable, replace_symbol_body")]
    public required string Operation { get; set; }

    /// <summary>
    /// Operation-specific parameters as JSON
    /// For rename_symbol: {\"old_name\": \"UserService\", \"new_name\": \"AccountService\"}
    /// </summary>
    [Description("Operation-specific parameters as JSON string")]
    public string Params { get; set; } = "{}";

    /// <summary>
    /// Preview changes without applying them (default: true for safety)
    /// </summary>
    [Description("Preview changes without applying them")]
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Path to the workspace to refactor
    /// </summary>
    [Description("Path to the workspace to refactor")]
    public required string WorkspacePath { get; set; }

    /// <summary>
    /// Maximum number of files to modify (safety limit)
    /// </summary>
    [Description("Maximum number of files to modify (safety limit, default: 100)")]
    public int MaxFiles { get; set; } = 100;

    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = true; // Always fresh for refactoring

    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [Description("Maximum tokens for response")]
    public int MaxTokens { get; set; } = 8000;
}
