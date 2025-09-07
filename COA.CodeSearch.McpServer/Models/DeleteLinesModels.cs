using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for deleting a range of lines from a file
/// </summary>
public class DeleteLinesParameters
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
    [Description("Starting line number (1-based, inclusive). This line and all lines up to EndLine will be deleted.")]
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based, inclusive). If not specified, only StartLine is deleted.
    /// </summary>
    [Range(1, int.MaxValue)]
    [Description("Ending line number (1-based, inclusive). If not specified, only StartLine is deleted. Must be >= StartLine.")]
    public int? EndLine { get; set; }

    /// <summary>
    /// Number of context lines to return before and after the deletion for verification
    /// </summary>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after deletion for verification (default: 3)")]
    public int ContextLines { get; set; } = 3;
}

/// <summary>
/// Result of deleting lines from a file
/// </summary>
public class DeleteLinesResult
{
    /// <summary>
    /// Whether the deletion was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if deletion failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// File path that was modified
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Starting line number that was deleted
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number that was deleted
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Number of lines that were deleted
    /// </summary>
    public int LinesDeleted { get; set; }

    /// <summary>
    /// Context lines showing the area around the deletion for verification
    /// </summary>
    public string[]? ContextLines { get; set; }

    /// <summary>
    /// The original content that was deleted (for undo purposes)
    /// </summary>
    public string? DeletedContent { get; set; }
}