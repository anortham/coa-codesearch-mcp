using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for smart refactoring operations
/// </summary>
public class SmartRefactorParameters
{
    /// <summary>
    /// The refactoring operation to perform.
    /// Valid operations: rename_symbol, extract_to_file, move_symbol_to_file, extract_interface
    /// </summary>
    [Description("The refactoring operation to perform: rename_symbol, extract_to_file, move_symbol_to_file, extract_interface")]
    public required string Operation { get; set; }

    /// <summary>
    /// Operation-specific parameters as JSON (default: {} - empty object)
    /// For rename_symbol: {\"old_name\": \"UserService\", \"new_name\": \"AccountService\"}
    /// </summary>
    [Description("Operation-specific parameters as JSON string (default: {} - empty object)")]
    public string Params { get; set; } = "{}";

    /// <summary>
    /// Preview changes without applying them (default: true - dry run for safety)
    /// </summary>
    [Description("Preview changes without applying them (default: true - dry run for safety)")]
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Path to the workspace to refactor
    /// </summary>
    [Description("Path to the workspace to refactor")]
    public required string WorkspacePath { get; set; }

    /// <summary>
    /// Maximum number of files to modify - safety limit (default: 100)
    /// </summary>
    [Description("Maximum number of files to modify (safety limit, default: 100)")]
    public int MaxFiles { get; set; } = 100;

    /// <summary>
    /// Disable caching for this request (default: true - always fresh for refactoring)
    /// </summary>
    [Description("Disable caching for this request (default: true - always fresh for refactoring)")]
    public bool NoCache { get; set; } = true; // Always fresh for refactoring

    /// <summary>
    /// Maximum tokens for response (default: 8000)
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    public int MaxTokens { get; set; } = 8000;
}
