namespace COA.CodeSearch.Next.McpServer.Models;

/// <summary>
/// Metadata about indexed workspaces for centralized tracking
/// </summary>
public class WorkspaceMetadata
{
    public Dictionary<string, WorkspaceIndexInfo> Indexes { get; set; } = new();
}

/// <summary>
/// Information about a specific workspace index
/// </summary>
public class WorkspaceIndexInfo
{
    public string OriginalPath { get; set; } = string.Empty;
    public string HashPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public DateTime LastModified { get; set; }
    public long DocumentCount { get; set; }
    public long IndexSizeBytes { get; set; }
}

/// <summary>
/// Index entry for backward compatibility
/// </summary>
public class IndexEntry
{
    public string OriginalPath { get; set; } = string.Empty;
    public string HashPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
}