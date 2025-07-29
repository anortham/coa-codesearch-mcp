using COA.CodeSearch.McpServer.Events;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for publishing memory-related events
/// </summary>
public interface IMemoryEventPublisher
{
    /// <summary>
    /// Publishes a memory storage event
    /// </summary>
    Task PublishMemoryStorageEventAsync(MemoryStorageEvent eventData);
}