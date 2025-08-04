using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing checkpoint IDs and operations
/// </summary>
public class CheckpointService
{
    private readonly ILogger<CheckpointService> _logger;
    private readonly IMemoryService _memoryService;
    
    public CheckpointService(
        ILogger<CheckpointService> logger,
        IMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }
    
    
    /// <summary>
    /// Store a checkpoint with the given content
    /// </summary>
    public async Task<StoreCheckpointResult> StoreCheckpointAsync(string content, string sessionId = "")
    {
        try
        {
            var checkpointId = CheckpointIdGenerator.GenerateId();
            var timestamp = CheckpointIdGenerator.ExtractTimestamp(checkpointId) ?? DateTime.UtcNow;
            
            var checkpoint = new FlexibleMemoryEntry
            {
                Id = checkpointId,
                Type = "WorkSession",
                Content = $"**{checkpointId}**\nCreated: {timestamp:O}\n\n{content}",
                Created = timestamp,
                Modified = timestamp,
                IsShared = false,
                SessionId = sessionId
            };
            
            // Store in memory system
            await _memoryService.StoreMemoryAsync(checkpoint);
            
            return new StoreCheckpointResult
            {
                Success = true,
                CheckpointId = checkpointId,
                SequentialId = 0, // No longer using sequential IDs
                Created = checkpoint.Created
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing checkpoint");
            return new StoreCheckpointResult
            {
                Success = false,
                Message = $"Failed to store checkpoint: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Get the latest checkpoint
    /// </summary>
    public async Task<GetCheckpointResult> GetLatestCheckpointAsync()
    {
        try
        {
            // Since our checkpoint IDs are time-based and sortable (CHECKPOINT-{timestamp}-{counter}),
            // we can use ID sorting to get the latest checkpoint efficiently
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "CHECKPOINT-", // Search for checkpoint content
                Types = new[] { "WorkSession" },
                MaxResults = 1, // We only need the latest one
                OrderBy = "id", // Sort by ID which is time-based and sortable
                OrderDescending = true, // Latest first (highest ID = most recent)
                IncludeArchived = false // Don't include archived memories
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            _logger.LogInformation("Search returned {Count} memories for checkpoint query", searchResult.Memories?.Count ?? 0);
            
            if (searchResult.Memories != null && searchResult.Memories.Any())
            {
                _logger.LogInformation("Found memories: {Ids}", 
                    string.Join(", ", searchResult.Memories.Select(m => m.Id)));
            }
            
            // Find the first valid checkpoint (they should already be sorted by ID desc)
            var latestCheckpoint = searchResult.Memories?
                .Where(m => m.Id.StartsWith("CHECKPOINT-") && m.Id.Count(c => c == '-') == 2)
                .FirstOrDefault();
            
            if (latestCheckpoint != null)
            {
                return new GetCheckpointResult
                {
                    Success = true,
                    Checkpoint = latestCheckpoint
                };
            }
            
            return new GetCheckpointResult
            {
                Success = false,
                Message = "No checkpoints found"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest checkpoint");
            return new GetCheckpointResult
            {
                Success = false,
                Message = $"Failed to get latest checkpoint: {ex.Message}"
            };
        }
    }
}

// Result models
public class StoreCheckpointResult
{
    public bool Success { get; set; }
    public string? CheckpointId { get; set; }
    public int SequentialId { get; set; }
    public DateTime Created { get; set; }
    public string? Message { get; set; }
}

public class GetCheckpointResult
{
    public bool Success { get; set; }
    public FlexibleMemoryEntry? Checkpoint { get; set; }
    public string? Message { get; set; }
}