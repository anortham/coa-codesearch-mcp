using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for inserting text at a specific line in a file
/// </summary>
public class InsertAtLineParameters
{
    /// <summary>
    /// Absolute or relative path to the file
    /// </summary>
    [Required]
    [Description("Absolute or relative path to the file to modify")]
    public required string FilePath { get; set; }

    /// <summary>
    /// Line number where to insert the text (1-based)
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Line number where to insert the text (1-based). Text will be inserted BEFORE this line.")]
    public int LineNumber { get; set; }

    /// <summary>
    /// Text content to insert
    /// </summary>
    [Required]
    [Description("Text content to insert. Will preserve the indentation of the target line.")]
    public required string Content { get; set; }

    /// <summary>
    /// Whether to auto-detect and preserve indentation from the target line
    /// </summary>
    [Description("Whether to auto-detect and preserve indentation from the target line (default: true)")]
    public bool PreserveIndentation { get; set; } = true;

    /// <summary>
    /// Number of context lines to return before and after the insertion point for verification
    /// </summary>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after insertion for verification (default: 3)")]
    public int ContextLines { get; set; } = 3;
}

/// <summary>
/// Result of inserting text at a specific line
/// </summary>
public class InsertAtLineResult
{
    /// <summary>
    /// Whether the insertion was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if insertion failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The file path that was modified
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The line number where text was inserted
    /// </summary>
    public int InsertedAtLine { get; set; }

    /// <summary>
    /// Number of lines inserted
    /// </summary>
    public int LinesInserted { get; set; }

    /// <summary>
    /// Context lines around the insertion point for verification
    /// </summary>
    public string[]? ContextLines { get; set; }

    /// <summary>
    /// The indentation that was detected and applied
    /// </summary>
    public string? DetectedIndentation { get; set; }

    /// <summary>
    /// Total file line count after insertion
    /// </summary>
    public int TotalFileLines { get; set; }
}