using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for deleting a range of lines from a file with precision positioning and verification context
/// </summary>
public class DeleteLinesParameters
{
    /// <summary>
    /// Absolute or relative path to the file to modify. Must be an existing file with write permissions.
    /// </summary>
    /// <example>C:\source\MyProject\UserService.cs</example>
    /// <example>./src/components/Button.tsx</example>
    /// <example>../config/settings.json</example>
    [Required]
    [Description("Absolute or relative path to the file to modify. Examples: 'C:\\source\\MyProject\\UserService.cs', './src/components/Button.tsx'")]
    public required string FilePath { get; set; }

    /// <summary>
    /// Starting line number (1-based, inclusive). This line and all lines up to EndLine will be permanently removed.
    /// </summary>
    /// <example>15</example>
    /// <example>1</example>
    /// <example>100</example>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Starting line number (1-based, inclusive). Examples: '15' (delete from line 15), '1' (delete from top)")]
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based, inclusive). If not specified, only StartLine is deleted. Must be >= StartLine for range deletions.
    /// </summary>
    /// <example>20</example>
    /// <example>15</example>
    /// <example>null</example>
    [Range(1, int.MaxValue)]
    [Description("Ending line number (1-based, inclusive). Examples: '20' (delete lines 15-20), null (delete only StartLine)")]
    public int? EndLine { get; set; }

    /// <summary>
    /// Number of context lines to show before and after the deletion for verification and confidence in the changes.
    /// </summary>
    /// <example>5</example>
    /// <example>0</example>
    /// <example>10</example>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after deletion for verification. Examples: '5' (more context), '0' (no context)")]
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