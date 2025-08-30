using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Global registry containing all workspace metadata and orphaned indexes
/// </summary>
public class WorkspaceRegistry
{
    /// <summary>
    /// Registry format version for future migrations
    /// </summary>
    public string Version { get; set; } = "1.0";
    
    /// <summary>
    /// When this registry was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// All registered workspaces, keyed by hash
    /// </summary>
    public Dictionary<string, WorkspaceEntry> Workspaces { get; set; } = new();
    
    /// <summary>
    /// Orphaned indexes that need cleanup, keyed by hash or directory name
    /// </summary>
    public Dictionary<string, OrphanedIndex> OrphanedIndexes { get; set; } = new();
    
    
    /// <summary>
    /// Statistics about the registry
    /// </summary>
    public RegistryStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Entry for a registered workspace
/// </summary>
public class WorkspaceEntry
{
    /// <summary>
    /// Computed hash of the original path (used as key)
    /// </summary>
    public string Hash { get; set; } = string.Empty;
    
    /// <summary>
    /// Original workspace path
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory name in indexes folder (workspacename_hash)
    /// </summary>
    public string DirectoryName { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for UI purposes
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of this workspace
    /// </summary>
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;
    
    /// <summary>
    /// When this workspace was first indexed
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time this workspace was accessed
    /// </summary>
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    
    
    /// <summary>
    /// Number of documents in the index
    /// </summary>
    public int DocumentCount { get; set; }
    
    /// <summary>
    /// Size of index in bytes
    /// </summary>
    public long IndexSizeBytes { get; set; }
    
    /// <summary>
    /// Process ID that currently has a lock on this workspace
    /// </summary>
    public string? LockedBy { get; set; }
    
    
    /// <summary>
    /// Check if this workspace path still exists
    /// </summary>
    [JsonIgnore]
    public bool PathExists => Directory.Exists(OriginalPath);
}

/// <summary>
/// Information about an orphaned index that needs cleanup
/// </summary>
public class OrphanedIndex
{
    /// <summary>
    /// Directory name in indexes folder
    /// </summary>
    public string DirectoryName { get; set; } = string.Empty;
    
    /// <summary>
    /// When this orphan was first discovered
    /// </summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last modified date of the index directory
    /// </summary>
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// Reason why this index is considered orphaned
    /// </summary>
    public OrphanReason Reason { get; set; }
    
    /// <summary>
    /// When this index is scheduled for automatic deletion
    /// </summary>
    public DateTime ScheduledForDeletion { get; set; }
    
    /// <summary>
    /// Size of the orphaned index in bytes
    /// </summary>
    public long SizeBytes { get; set; }
    
    /// <summary>
    /// Original path that was attempted to be resolved (if known)
    /// </summary>
    public string? AttemptedPath { get; set; }
}

/// <summary>
/// Statistics about the registry
/// </summary>
public class RegistryStatistics
{
    /// <summary>
    /// Total number of registered workspaces
    /// </summary>
    public int TotalWorkspaces { get; set; }
    
    /// <summary>
    /// Total number of orphaned indexes
    /// </summary>
    public int TotalOrphans { get; set; }
    
    /// <summary>
    /// Total size of all indexes in bytes
    /// </summary>
    public long TotalIndexSizeBytes { get; set; }
    
    /// <summary>
    /// Total number of documents across all indexes
    /// </summary>
    public int TotalDocuments { get; set; }
}

/// <summary>
/// Status of a workspace
/// </summary>
public enum WorkspaceStatus
{
    /// <summary>
    /// Workspace is active and being used
    /// </summary>
    Active,
    
    /// <summary>
    /// Workspace path no longer exists
    /// </summary>
    Missing,
    
    
    /// <summary>
    /// Workspace has errors
    /// </summary>
    Error,
    
    /// <summary>
    /// Workspace is archived/inactive
    /// </summary>
    Archived
}

/// <summary>
/// Reason why an index is considered orphaned
/// </summary>
public enum OrphanReason
{
    /// <summary>
    /// No metadata file found in index directory
    /// </summary>
    NoMetadataFile,
    
    /// <summary>
    /// Metadata file is corrupted
    /// </summary>
    CorruptedMetadata,
    
    /// <summary>
    /// Original path in metadata no longer exists
    /// </summary>
    PathNotFound,
    
    /// <summary>
    /// Cannot resolve path from directory name
    /// </summary>
    UnresolvablePath,
    
    /// <summary>
    /// Manually marked as orphaned
    /// </summary>
    ManuallyMarked
}