namespace COA.CodeSearch.McpServer.Tools.Models;

/// <summary>
/// Result of a smart refactoring operation
/// </summary>
public class SmartRefactorResult
{
    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The operation that was performed
    /// </summary>
    public required string Operation { get; set; }

    /// <summary>
    /// Whether this was a dry run (preview only)
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// List of files that were (or would be) modified
    /// </summary>
    public List<string> FilesModified { get; set; } = new();

    /// <summary>
    /// Total number of changes made (or previewed)
    /// </summary>
    public int ChangesCount { get; set; }

    /// <summary>
    /// Detailed changes per file
    /// </summary>
    public List<FileRefactorChange> Changes { get; set; } = new();

    /// <summary>
    /// Any errors or warnings
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Next steps or suggestions
    /// </summary>
    public List<string> NextActions { get; set; } = new();

    /// <summary>
    /// Time taken to perform the operation
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Represents changes made to a single file
/// </summary>
public class FileRefactorChange
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Number of replacements made in this file
    /// </summary>
    public int ReplacementCount { get; set; }

    /// <summary>
    /// Preview of changes (for dry run)
    /// </summary>
    public string? ChangePreview { get; set; }

    /// <summary>
    /// Locations that were changed (line numbers)
    /// </summary>
    public List<int> Lines { get; set; } = new();
}
