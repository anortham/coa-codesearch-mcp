using COA.CodeSearch.McpServer.Events;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Simple in-memory event publisher for memory storage events
/// </summary>
public class MemoryEventPublisher : IMemoryEventPublisher
{
    private readonly List<Func<MemoryStorageEvent, Task>> _subscribers = new();
    private readonly ILogger<MemoryEventPublisher> _logger;

    public MemoryEventPublisher(ILogger<MemoryEventPublisher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to memory storage events
    /// </summary>
    public void Subscribe(Func<MemoryStorageEvent, Task> handler)
    {
        _subscribers.Add(handler);
        _logger.LogDebug("Added memory storage event subscriber (total: {Count})", _subscribers.Count);
    }

    /// <summary>
    /// Publishes a memory storage event to all subscribers
    /// </summary>
    public async Task PublishMemoryStorageEventAsync(MemoryStorageEvent eventData)
    {
        _logger.LogDebug("Publishing memory storage event: {Action} for memory {MemoryId}", 
            eventData.Action, eventData.Memory.Id);

        // Fire and forget - don't let subscriber failures block the main operation
        var tasks = _subscribers.Select(async subscriber =>
        {
            try
            {
                await subscriber(eventData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory storage event subscriber failed for memory {MemoryId}", 
                    eventData.Memory.Id);
            }
        });

        // Wait for all subscribers to complete (but failures don't propagate)
        await Task.WhenAll(tasks);
    }
}