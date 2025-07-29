using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Orchestrates memory storage operations including semantic indexing
/// This is the proper architectural pattern - separate orchestration from core services
/// </summary>
public class MemoryStorageOrchestrator
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly SemanticMemoryIndex _semanticIndex;
    private readonly ILogger<MemoryStorageOrchestrator> _logger;

    public MemoryStorageOrchestrator(
        FlexibleMemoryService memoryService,
        SemanticMemoryIndex semanticIndex,
        ILogger<MemoryStorageOrchestrator> logger)
    {
        _memoryService = memoryService;
        _semanticIndex = semanticIndex;
        _logger = logger;
    }

    /// <summary>
    /// Stores a memory and handles semantic indexing orchestration
    /// </summary>
    public async Task<bool> StoreMemoryWithSemanticIndexingAsync(FlexibleMemoryEntry memory)
    {
        try
        {
            // 1. Store the memory first
            var stored = await _memoryService.StoreMemoryAsync(memory);
            
            if (!stored)
            {
                _logger.LogWarning("Failed to store memory {MemoryId}, skipping semantic indexing", memory.Id);
                return false;
            }

            // 2. Add semantic indexing (non-blocking)
            try
            {
                await _semanticIndex.IndexMemoryAsync(memory);
                _logger.LogDebug("Successfully indexed memory {MemoryId} for semantic search", memory.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to semantically index memory {MemoryId} (non-blocking)", memory.Id);
                // Don't fail the entire operation for semantic indexing failures
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store memory {MemoryId}", memory.Id);
            return false;
        }
    }

    /// <summary>
    /// Updates a memory and refreshes semantic indexing
    /// </summary>
    public async Task<bool> UpdateMemoryWithSemanticIndexingAsync(FlexibleMemoryEntry memory)
    {
        try
        {
            // 1. Update the memory
            var updated = await _memoryService.StoreMemoryAsync(memory); // StoreMemoryAsync handles updates
            
            if (!updated)
            {
                _logger.LogWarning("Failed to update memory {MemoryId}, skipping semantic re-indexing", memory.Id);
                return false;
            }

            // 2. Update semantic indexing
            try
            {
                await _semanticIndex.UpdateMemoryIndexAsync(memory);
                _logger.LogDebug("Successfully re-indexed memory {MemoryId} for semantic search", memory.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to semantically re-index memory {MemoryId} (non-blocking)", memory.Id);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memory {MemoryId}", memory.Id);
            return false;
        }
    }

    // Note: Delete functionality not implemented as FlexibleMemoryService doesn't expose DeleteMemoryAsync
}