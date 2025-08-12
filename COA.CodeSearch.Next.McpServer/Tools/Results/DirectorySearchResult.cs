using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tools.Results;

/// <summary>
/// Result model for directory search operations
/// </summary>
public class DirectorySearchResult : ToolResultBase
{
    public override string Operation => "directory_search";
    
    /// <summary>
    /// List of matching directories
    /// </summary>
    public List<DirectoryMatch> Directories { get; set; } = new();
    
    /// <summary>
    /// Total number of directories found (before limiting)
    /// </summary>
    public int TotalMatches { get; set; }
    
    /// <summary>
    /// Search pattern used
    /// </summary>
    public string Pattern { get; set; } = string.Empty;
    
    /// <summary>
    /// Workspace path searched
    /// </summary>
    public string WorkspacePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether subdirectories were included
    /// </summary>
    public bool IncludedSubdirectories { get; set; }
    
    /// <summary>
    /// Time taken for the search
    /// </summary>
    public long SearchTimeMs { get; set; }
}

/// <summary>
/// Represents a matched directory
/// </summary>
public class DirectoryMatch
{
    /// <summary>
    /// Full path to the directory
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Parent directory path
    /// </summary>
    public string ParentPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Relative path from workspace root
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Depth level from workspace root
    /// </summary>
    public int Depth { get; set; }
    
    /// <summary>
    /// Number of files in this directory (direct children only)
    /// </summary>
    public int FileCount { get; set; }
    
    /// <summary>
    /// Number of subdirectories (direct children only)
    /// </summary>
    public int SubdirectoryCount { get; set; }
    
    /// <summary>
    /// Whether this is a hidden directory
    /// </summary>
    public bool IsHidden { get; set; }
}