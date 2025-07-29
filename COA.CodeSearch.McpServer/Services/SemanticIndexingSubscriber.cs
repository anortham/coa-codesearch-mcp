using COA.CodeSearch.McpServer.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Background service that subscribes to memory storage events and handles semantic indexing
/// This is clean architecture - event-driven without circular dependencies
/// </summary>
public class SemanticIndexingSubscriber : IHostedService
{
    private readonly SemanticMemoryIndex _semanticIndex;
    private readonly MemoryEventPublisher _eventPublisher;
    private readonly ILogger<SemanticIndexingSubscriber> _logger;

    public SemanticIndexingSubscriber(
        SemanticMemoryIndex semanticIndex,
        IMemoryEventPublisher eventPublisher,
        ILogger<SemanticIndexingSubscriber> logger)
    {
        _semanticIndex = semanticIndex;
        _eventPublisher = (MemoryEventPublisher)eventPublisher; // Cast to get Subscribe method
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting semantic indexing subscriber");
        
        // Subscribe to memory storage events
        _eventPublisher.Subscribe(OnMemoryStorageEvent);
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping semantic indexing subscriber");
        return Task.CompletedTask;
    }

    private async Task OnMemoryStorageEvent(MemoryStorageEvent eventData)
    {
        try
        {
            switch (eventData.Action)
            {
                case MemoryStorageAction.Created:
                case MemoryStorageAction.Updated:
                    await _semanticIndex.IndexMemoryAsync(eventData.Memory);
                    _logger.LogDebug("Successfully indexed memory {MemoryId} for semantic search", 
                        eventData.Memory.Id);
                    break;
                    
                case MemoryStorageAction.Deleted:
                    await _semanticIndex.RemoveMemoryFromIndexAsync(eventData.Memory.Id);
                    _logger.LogDebug("Successfully removed memory {MemoryId} from semantic index", 
                        eventData.Memory.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle semantic indexing for memory {MemoryId}", 
                eventData.Memory.Id);
        }
    }
}