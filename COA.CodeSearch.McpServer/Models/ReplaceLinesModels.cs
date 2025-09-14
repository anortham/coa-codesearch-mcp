using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for replacing a range of lines in a file with precise line positioning and automatic indentation
/// </summary>
public class ReplaceLinesParameters
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
    /// Starting line number (1-based, inclusive). This line and all lines up to EndLine will be replaced with new content.
    /// </summary>
    /// <example>15</example>
    /// <example>1</example>
    /// <example>100</example>
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Starting line number (1-based, inclusive). Examples: '15' (replace from line 15), '1' (replace from top)")]
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based, inclusive). If not specified, only StartLine is replaced. Must be >= StartLine for range replacements.
    /// </summary>
    /// <example>20</example>
    /// <example>15</example>
    /// <example>null</example>
    [Range(1, int.MaxValue)]
    [Description("Ending line number (1-based, inclusive). Examples: '20' (replace lines 15-20), null (replace only StartLine)")]
    public int? EndLine { get; set; }

    /// <summary>
    /// New content to replace the specified lines. Can be single line, multi-line, or empty string to delete the lines.
    /// </summary>
    /// <example>public void UpdatedMethod() { return true; }</example>
    /// <example>// Updated comment\n// with multiple lines</example>
    /// <example></example>
    [Required]
    [Description("New content to replace the specified lines. Examples: 'public void UpdatedMethod() { return true; }', '' (empty to delete)")]
    public required string Content { get; set; }

    /// <summary>
    /// Whether to auto-detect and preserve indentation from the surrounding lines for consistent code formatting.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Whether to auto-detect and preserve indentation from surrounding lines (default: true)")]
    public bool PreserveIndentation { get; set; } = true;

    /// <summary>
    /// Number of context lines to show before and after the replacement for verification and confidence in the changes.
    /// </summary>
    /// <example>5</example>
    /// <example>0</example>
    /// <example>10</example>
    [Range(0, 20)]
    [Description("Number of context lines to show before and after replacement for verification. Examples: '5' (more context), '0' (no context)")]
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