using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Parameters for the unified search_files tool that consolidates file and directory search
/// </summary>
public class SearchFilesParameters
{
    /// <summary>
    /// The search pattern supporting glob patterns, wildcards, and regex for flexible matching.
    /// </summary>
    /// <example>*.cs</example>
    /// <example>**/*.test.js</example>
    /// <example>src/**/Config*</example>
    [Required]
    [Description("Search pattern supporting glob and regex. Examples: '*.cs' (files), '**/test/*' (directories), 'Config*' (either)")]
    public required string Pattern { get; set; }

    /// <summary>
    /// Path to the workspace directory to search. Can be absolute or relative path.
    /// </summary>
    /// <example>C:\source\MyProject</example>
    /// <example>./src</example>
    /// <example>../other-project</example>
    [Description("Workspace path to search. Default: current workspace")]
    public string? WorkspacePath { get; set; } = null;

    /// <summary>
    /// Resource type to search for: "file", "directory", or "both"
    /// </summary>
    /// <example>file</example>
    /// <example>directory</example>
    /// <example>both</example>
    [Description("Resource type to search. Default: 'file' - Options: 'file' (files only), 'directory' (directories only), 'both' (files and directories)")]
    public string ResourceType { get; set; } = "file";

    /// <summary>
    /// Use regular expression instead of glob pattern for advanced matching.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Use regex instead of glob patterns. Default: false - uses glob patterns")]
    public bool UseRegex { get; set; } = false;

    /// <summary>
    /// Comma-separated list of file extensions to filter results (only applies when ResourceType is "file" or "both").
    /// </summary>
    /// <example>.cs,.js</example>
    /// <example>.tsx,.ts</example>
    [Description("File extension filter (comma-separated). Default: null - no filter. Examples: '.cs,.js', '.tsx,.ts'")]
    public string? ExtensionFilter { get; set; } = null;

    /// <summary>
    /// Include hidden directories (starting with .) in search results.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include hidden directories/files (starting with .). Default: false - excludes hidden items")]
    public bool IncludeHidden { get; set; } = false;

    /// <summary>
    /// Include subdirectories in directory search results.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include subdirectories in results. Default: false - only top-level matches")]
    public bool IncludeSubdirectories { get; set; } = false;

    /// <summary>
    /// Include list of matching directories in file search results for better context.
    /// </summary>
    /// <example>true</example>
    /// <example>false</example>
    [Description("Include directory list in file search results. Default: false - files only")]
    public bool IncludeDirectories { get; set; } = false;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    /// <example>50</example>
    /// <example>200</example>
    [Range(1, 500)]
    [Description("Maximum results to return. Default: 100 (range: 1-500)")]
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive'
    /// </summary>
    [Description("Response mode. Default: 'adaptive' - Options: 'summary', 'full', 'adaptive'")]
    public string ResponseMode { get; set; } = "adaptive";

    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [Range(100, 100000)]
    [Description("Maximum tokens for response. Default: 8000")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching. Default: false - uses cache")]
    public bool NoCache { get; set; } = false;
}

/// <summary>
/// Result of a unified file/directory search operation
/// </summary>
public class SearchFilesResult
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
    /// The resource type that was searched
    /// </summary>
    public string ResourceType { get; set; } = "";

    /// <summary>
    /// The pattern that was used
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// The workspace path that was searched
    /// </summary>
    public string SearchPath { get; set; } = "";

    /// <summary>
    /// List of matching files (when ResourceType is "file" or "both")
    /// </summary>
    public List<FileMatch>? Files { get; set; }

    /// <summary>
    /// List of matching directories (when ResourceType is "directory" or "both")
    /// </summary>
    public List<DirectoryMatch>? Directories { get; set; }

    /// <summary>
    /// Total number of matches found
    /// </summary>
    public int TotalMatches { get; set; }
}

/// <summary>
/// Information about a matching file
/// </summary>
public class FileMatch
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Directory { get; set; } = "";
    public string Extension { get; set; } = "";
    public float Score { get; set; }
}

/// <summary>
/// Information about a matching directory
/// </summary>
public class DirectoryMatch
{
    public string DirectoryPath { get; set; } = "";
    public string DirectoryName { get; set; } = "";
    public string ParentPath { get; set; } = "";
    public float Score { get; set; }
}
