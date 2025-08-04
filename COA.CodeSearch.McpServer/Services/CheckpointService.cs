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
            // Search for checkpoints and get the most recent one
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "CHECKPOINT-*",
                Types = new[] { "WorkSession" },
                MaxResults = 10,
                OrderBy = "created",
                OrderDescending = true
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            // Find the most recent checkpoint by ID pattern
            var latestCheckpoint = searchResult.Memories
                .Where(m => m.Id.StartsWith("CHECKPOINT-") && m.Id.Contains("-"))
                .OrderByDescending(m => m.Id) // Time-based IDs sort naturally
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