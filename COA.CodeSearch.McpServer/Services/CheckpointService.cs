using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing checkpoint IDs and operations
/// </summary>
public class CheckpointService
{
    private readonly ILogger<CheckpointService> _logger;
    private readonly IPathResolutionService _pathResolution;
    private readonly IMemoryService _memoryService;
    private readonly object _idLock = new();
    
    public CheckpointService(
        ILogger<CheckpointService> logger,
        IPathResolutionService pathResolution,
        IMemoryService memoryService)
    {
        _logger = logger;
        _pathResolution = pathResolution;
        _memoryService = memoryService;
    }
    
    /// <summary>
    /// Get the next sequential checkpoint ID
    /// </summary>
    public Task<int> GetNextCheckpointIdAsync()
    {
        lock (_idLock)
        {
            var checkpointIdPath = _pathResolution.GetCheckpointIdPath();
            var directory = Path.GetDirectoryName(checkpointIdPath);
            
            // Ensure directory exists
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            if (File.Exists(checkpointIdPath))
            {
                var content = File.ReadAllText(checkpointIdPath);
                if (int.TryParse(content, out var lastId))
                {
                    var nextId = lastId + 1;
                    File.WriteAllText(checkpointIdPath, nextId.ToString());
                    _logger.LogInformation("Generated checkpoint ID: {CheckpointId}", nextId);
                    return Task.FromResult(nextId);
                }
            }
            
            // Initialize with ID 1
            File.WriteAllText(checkpointIdPath, "1");
            _logger.LogInformation("Initialized checkpoint system with ID: 1");
            return Task.FromResult(1);
        }
    }
    
    /// <summary>
    /// Get the current checkpoint ID without incrementing
    /// </summary>
    public async Task<int?> GetCurrentCheckpointIdAsync()
    {
        var checkpointIdPath = _pathResolution.GetCheckpointIdPath();
        
        if (!File.Exists(checkpointIdPath))
        {
            _logger.LogInformation("No checkpoint ID file found");
            return null;
        }
        
        var content = await File.ReadAllTextAsync(checkpointIdPath);
        if (int.TryParse(content, out var currentId))
        {
            return currentId;
        }
        
        _logger.LogWarning("Invalid checkpoint ID in file: {Content}", content);
        return null;
    }
    
    /// <summary>
    /// Store a checkpoint with the given content
    /// </summary>
    public async Task<StoreCheckpointResult> StoreCheckpointAsync(string content, string sessionId = "")
    {
        try
        {
            var checkpointId = await GetNextCheckpointIdAsync();
            var checkpointIdString = $"CHECKPOINT-{checkpointId:D5}";
            
            var checkpoint = new FlexibleMemoryEntry
            {
                Id = checkpointIdString,
                Type = "Checkpoint",
                Content = $"**{checkpointIdString}**\nCreated: {DateTime.UtcNow:O}\n\n{content}",
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                IsShared = false,
                SessionId = sessionId
            };
            
            // Store in memory system
            await _memoryService.StoreMemoryAsync(checkpoint);
            
            return new StoreCheckpointResult
            {
                Success = true,
                CheckpointId = checkpointIdString,
                SequentialId = checkpointId,
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
            var currentId = await GetCurrentCheckpointIdAsync();
            if (!currentId.HasValue)
            {
                return new GetCheckpointResult
                {
                    Success = false,
                    Message = "No checkpoints found"
                };
            }
            
            var checkpointId = $"CHECKPOINT-{currentId.Value:D5}";
            
            // Get the checkpoint directly by ID
            var checkpoint = await _memoryService.GetMemoryByIdAsync(checkpointId);
            if (checkpoint != null)
            {
                return new GetCheckpointResult
                {
                    Success = true,
                    Checkpoint = checkpoint
                };
            }
            
            return new GetCheckpointResult
            {
                Success = false,
                Message = $"Checkpoint {checkpointId} not found in memory system"
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