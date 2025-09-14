using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for inserting text at a specific line in a file with precision positioning and automatic indentation
/// </summary>
public class InsertAtLineParameters
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
    /// Line number where to insert the text (1-based). Text will be inserted BEFORE this line, shifting existing content down.
    /// </summary>
    /// <example>15</example>
    /// <example>1</example>
    /// <example>100</example>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Line number where to insert the text (1-based). Examples: '15' (insert before line 15), '1' (insert at top)")]
    public int LineNumber { get; set; }

    /// <summary>
    /// Text content to insert. Can be single line or multi-line content. Indentation will be automatically detected and applied.
    /// </summary>
    /// <example>public void NewMethod() { }</example>
    /// <example>// TODO: Implement this feature</example>
    /// <example>using System.Collections.Generic;</example>
    [Required]
    [Description("Text content to insert. Examples: 'public void NewMethod() { }', '// TODO: Implement this feature'")]
    public required string Content { get; set; }

    /// <summary>
    /// Whether to auto-detect and preserve indentation from the surrounding lines for consistent code formatting.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Whether to auto-detect and preserve indentation from surrounding lines (default: true)")]
    public bool PreserveIndentation { get; set; } = true;

    /// <summary>
    /// Number of context lines to show before and after the insertion point for verification and confidence.
    /// </summary>
    /// <example>5</example>
    /// <example>0</example>
    /// <example>10</example>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after insertion for verification. Examples: '5' (more context), '0' (no context)")]
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