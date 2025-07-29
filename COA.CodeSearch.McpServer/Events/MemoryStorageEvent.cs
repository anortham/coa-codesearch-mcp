using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Events;

/// <summary>
/// Event published when a memory is stored or updated
/// </summary>
public class MemoryStorageEvent
{
    public FlexibleMemoryEntry Memory { get; init; } = null!;
    public MemoryStorageAction Action { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum MemoryStorageAction
{
    Created,
    Updated,
    Deleted
}