namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Represents a file change event
/// </summary>
public class FileChangeEvent
{
    public string FilePath { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Type of file change
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}

/// <summary>
/// Tracks a pending delete operation
/// </summary>
public class PendingDelete
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime FirstSeenTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
    public bool Cancelled { get; set; }
    public int RetryCount { get; set; }
}