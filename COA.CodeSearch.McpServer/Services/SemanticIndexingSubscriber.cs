using COA.CodeSearch.McpServer.Events;
using COA.CodeSearch.McpServer.Models;
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
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<SemanticIndexingSubscriber> _logger;

    public SemanticIndexingSubscriber(
        SemanticMemoryIndex semanticIndex,
        IMemoryEventPublisher eventPublisher,
        FlexibleMemoryService memoryService,
        ILogger<SemanticIndexingSubscriber> logger)
    {
        _semanticIndex = semanticIndex;
        _eventPublisher = (MemoryEventPublisher)eventPublisher; // Cast to get Subscribe method
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting semantic indexing subscriber");
        
        // Subscribe to memory storage events
        _eventPublisher.Subscribe(OnMemoryStorageEvent);
        
        _logger.LogInformation("Semantic indexing subscriber started and subscribed to events");
        
        // Index existing memories on startup
        await IndexExistingMemoriesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping semantic indexing subscriber");
        return Task.CompletedTask;
    }

    private async Task OnMemoryStorageEvent(MemoryStorageEvent eventData)
    {
        _logger.LogInformation("Received memory storage event: {Action} for memory {MemoryId} of type {MemoryType}", 
            eventData.Action, eventData.Memory.Id, eventData.Memory.Type);
        
        try
        {
            switch (eventData.Action)
            {
                case MemoryStorageAction.Created:
                case MemoryStorageAction.Updated:
                    _logger.LogInformation("Indexing memory {MemoryId} for semantic search", eventData.Memory.Id);
                    await _semanticIndex.IndexMemoryAsync(eventData.Memory);
                    _logger.LogInformation("Successfully indexed memory {MemoryId} for semantic search", 
                        eventData.Memory.Id);
                    break;
                    
                case MemoryStorageAction.Deleted:
                    _logger.LogInformation("Removing memory {MemoryId} from semantic index", eventData.Memory.Id);
                    await _semanticIndex.RemoveMemoryFromIndexAsync(eventData.Memory.Id);
                    _logger.LogInformation("Successfully removed memory {MemoryId} from semantic index", 
                        eventData.Memory.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle semantic indexing for memory {MemoryId}", 
                eventData.Memory.Id);
        }
    }
    
    private async Task IndexExistingMemoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting to index existing memories for semantic search");
            
            // Get current vector index stats
            var initialStats = await _semanticIndex.GetIndexStatsAsync();
            _logger.LogInformation("Initial vector index count: {Count}", initialStats.TotalVectors);
            
            // Fetch all existing memories
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                MaxResults = 1000 // Index up to 1000 memories on startup
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            _logger.LogInformation("Found {Count} existing memories to index", searchResult.TotalFound);
            
            if (searchResult.Memories.Count > 0)
            {
                // Bulk index the memories
                await _semanticIndex.BulkIndexMemoriesAsync(searchResult.Memories);
                
                // Get updated stats
                var finalStats = await _semanticIndex.GetIndexStatsAsync();
                _logger.LogInformation("Completed indexing existing memories. Vector index now contains {Count} vectors", 
                    finalStats.TotalVectors);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index existing memories on startup");
        }
    }
}