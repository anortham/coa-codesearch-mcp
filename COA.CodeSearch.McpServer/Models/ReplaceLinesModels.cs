using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for replacing a range of lines in a file
/// </summary>
public class ReplaceLinesParameters
{
    /// <summary>
    /// Absolute or relative path to the file
    /// </summary>
    [Required]
    [Description("Absolute or relative path to the file to modify")]
    public required string FilePath { get; set; }

    /// <summary>
    /// Starting line number (1-based, inclusive)
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Starting line number (1-based, inclusive). This line and all lines up to EndLine will be replaced.")]
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based, inclusive). If not specified, only StartLine is replaced.
    /// </summary>
    [Range(1, int.MaxValue)]
    [Description("Ending line number (1-based, inclusive). If not specified, only StartLine is replaced. Must be >= StartLine.")]
    public int? EndLine { get; set; }

    /// <summary>
    /// New content to replace the specified lines
    /// </summary>
    [Required]
    [Description("New content to replace the specified lines. Can be empty to delete lines.")]
    public required string Content { get; set; }

    /// <summary>
    /// Whether to auto-detect and preserve indentation from the surrounding lines
    /// </summary>
    [Description("Whether to auto-detect and preserve indentation from the surrounding lines (default: true)")]
    public bool PreserveIndentation { get; set; } = true;

    /// <summary>
    /// Number of context lines to return before and after the replacement for verification
    /// </summary>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after replacement for verification (default: 3)")]
    public int ContextLines { get; set; } = 3;
}

/// <summary>
/// Result of replacing lines in a file
/// </summary>
public class ReplaceLinesResult
{
    /// <summary>
    /// Whether the replacement was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if replacement failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// File path that was modified
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Starting line number that was replaced
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number that was replaced
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Number of lines that were removed
    /// </summary>
    public int LinesRemoved { get; set; }

    /// <summary>
    /// Number of lines that were added
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// Context lines showing the area around the replacement for verification
    /// </summary>
    public string[]? ContextLines { get; set; }

    /// <summary>
    /// The original content that was replaced (for undo purposes)
    /// </summary>
    public string? OriginalContent { get; set; }
}