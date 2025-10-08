using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for the unified edit_lines tool that consolidates insert, replace, and delete operations
/// </summary>
public class EditLinesParameters
{
    /// <summary>
    /// Absolute or relative path to the file to modify. Must be an existing file with write permissions.
    /// </summary>
    /// <example>C:\source\MyProject\UserService.cs</example>
    /// <example>./src/components/Button.tsx</example>
    /// <example>../config/settings.json</example>
    [Required]
    [Description("Absolute or relative path to the file to modify. Must be an existing file. Examples: 'C:\\source\\MyProject\\UserService.cs', './src/components/Button.tsx', '../config/settings.json'")]
    public required string FilePath { get; set; }

    /// <summary>
    /// The edit operation to perform: "insert", "replace", or "delete"
    /// </summary>
    /// <example>insert</example>
    /// <example>replace</example>
    /// <example>delete</example>
    [Required]
    [Description("Edit operation to perform. Options: 'insert' (add lines), 'replace' (update lines), 'delete' (remove lines)")]
    public required string Operation { get; set; }

    /// <summary>
    /// Starting line number (1-based). For insert: text inserted BEFORE this line. For replace/delete: first line in range.
    /// </summary>
    /// <example>15</example>
    /// <example>1</example>
    /// <example>100</example>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Starting line number (1-based). For insert: inserts BEFORE this line. For replace/delete: first line in range. Examples: 15, 1, 100")]
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based, inclusive). Only used for replace and delete operations.
    /// If not specified, operates on single line (StartLine only).
    /// </summary>
    /// <example>20</example>
    /// <example>15</example>
    [Range(1, int.MaxValue)]
    [Description("Ending line number (1-based, inclusive). Default: null (single line). For replace/delete ranges: specify end line. Must be >= StartLine. Examples: 20, 15, null")]
    public int? EndLine { get; set; } = null;

    /// <summary>
    /// Text content for insert or replace operations. Not used for delete.
    /// Can be single line or multi-line. Indentation auto-detected if PreserveIndentation is true.
    /// </summary>
    /// <example>public void NewMethod() { }</example>
    /// <example>// TODO: Implement this feature</example>
    [Description("Text content for insert/replace. Default: empty string. For delete: ignored. For insert/replace: required. Examples: 'public void NewMethod() { }', '// Updated comment'")]
    public string Content { get; set; } = "";

    /// <summary>
    /// Whether to auto-detect and preserve indentation from surrounding lines for consistent formatting.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Auto-detect and preserve indentation. Default: true - matches surrounding code indentation")]
    public bool PreserveIndentation { get; set; } = true;

    /// <summary>
    /// Number of context lines to show before and after the edit for verification.
    /// </summary>
    /// <example>5</example>
    /// <example>0</example>
    /// <example>10</example>
    [Range(0, 20)]
    [Description("Number of context lines shown before/after edit for verification. Default: 5 - provides good context")]
    public int ContextLines { get; set; } = 5;
}

/// <summary>
/// Result of a unified line editing operation
/// </summary>
public class EditLinesResult
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The operation that was performed
    /// </summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// The file path that was modified
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Starting line number of the operation
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number of the operation (for ranges)
    /// </summary>
    public int? EndLine { get; set; }

    /// <summary>
    /// Number of lines added to the file
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// Number of lines removed from the file
    /// </summary>
    public int LinesRemoved { get; set; }

    /// <summary>
    /// Context lines around the edit for verification
    /// </summary>
    public string[]? ContextLines { get; set; }

    /// <summary>
    /// The indentation that was detected and applied (for insert/replace)
    /// </summary>
    public string? DetectedIndentation { get; set; }

    /// <summary>
    /// Content that was deleted or replaced (for recovery/undo)
    /// </summary>
    public string? DeletedContent { get; set; }

    /// <summary>
    /// Total file line count after operation
    /// </summary>
    public int TotalFileLines { get; set; }
}
