using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tools for managing checkpoints with sequential IDs
/// </summary>
[McpServerToolType]
public class CheckpointTools
{
    private readonly ILogger<CheckpointTools> _logger;
    private readonly CheckpointService _checkpointService;
    
    public CheckpointTools(
        ILogger<CheckpointTools> logger,
        CheckpointService checkpointService)
    {
        _logger = logger;
        _checkpointService = checkpointService;
    }
    
    [McpServerTool(Name = "store_checkpoint")]
    [Description(@"Stores a checkpoint of current work session with sequential ID.
Returns: Checkpoint ID and creation details.
Prerequisites: None.
Use cases: Creating session checkpoints, saving work progress.")]
    public async Task<object> StoreCheckpointAsync(StoreCheckpointParams parameters)
    {
        if (parameters == null) throw new InvalidParametersException("Parameters are required");
        if (string.IsNullOrWhiteSpace(parameters.Content)) 
            throw new InvalidParametersException("Content is required");
        
        var result = await _checkpointService.StoreCheckpointAsync(
            parameters.Content,
            parameters.SessionId ?? "");
        
        if (result.Success)
        {
            _logger.LogInformation("Stored checkpoint {CheckpointId}", result.CheckpointId);
            return new
            {
                success = true,
                checkpointId = result.CheckpointId,
                sequentialId = result.SequentialId,
                created = result.Created,
                message = $"Checkpoint {result.CheckpointId} created successfully"
            };
        }
        
        _logger.LogError("Failed to store checkpoint: {Message}", result.Message);
        return new
        {
            success = false,
            error = result.Message
        };
    }
    
    [McpServerTool(Name = "get_latest_checkpoint")]
    [Description(@"Retrieves the most recent checkpoint by sequential ID.
Returns: Latest checkpoint content and metadata.
Prerequisites: At least one checkpoint must exist.
Use cases: Resuming work, viewing latest checkpoint.")]
    public async Task<object> GetLatestCheckpointAsync(GetLatestCheckpointParams? parameters = null)
    {
        var result = await _checkpointService.GetLatestCheckpointAsync();
        
        if (result.Success && result.Checkpoint != null)
        {
            _logger.LogInformation("Retrieved checkpoint {CheckpointId}", result.Checkpoint.Id);
            return new
            {
                success = true,
                checkpoint = new
                {
                    id = result.Checkpoint.Id,
                    content = result.Checkpoint.Content,
                    created = result.Checkpoint.Created,
                    modified = result.Checkpoint.Modified,
                    sessionId = result.Checkpoint.SessionId
                }
            };
        }
        
        _logger.LogWarning("Failed to get latest checkpoint: {Message}", result.Message);
        return new
        {
            success = false,
            error = result.Message ?? "No checkpoints found"
        };
    }
}

// Parameter classes
public class StoreCheckpointParams
{
    [Description("Content of the checkpoint to store")]
    public string? Content { get; set; }
    
    [Description("Optional session ID to associate with the checkpoint")]
    public string? SessionId { get; set; }
}

public class GetLatestCheckpointParams
{
    // No parameters needed for getting latest checkpoint
}