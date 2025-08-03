# Checkpoint/Resume System Redesign

## Current Problems

1. **Timestamp Confusion**
   - Checkpoints show "Local Time" in content but are stored with UTC timestamps
   - Claude may generate incorrect local times (e.g., claiming it's 21:24)
   - No way to verify which timezone Claude thinks it's in

2. **Wrong Checkpoint Retrieved**
   - Despite ordering by created descending, wrong checkpoints are returned
   - Multiple checkpoints with similar timestamps cause confusion
   - Search query "Session Checkpoint" could match unintended content

3. **Unreliable Time Handling**
   - Claude doesn't have reliable access to current time
   - UTC vs local time conversions are error-prone
   - No validation of generated timestamps

## Proposed Solution

### 1. Use Sequential IDs Instead of Time

```csharp
public class CheckpointService
{
    // Store last checkpoint ID in a file or memory
    private async Task<int> GetNextCheckpointId()
    {
        var checkpointFile = Path.Combine(".codesearch", "checkpoint.id");
        if (File.Exists(checkpointFile))
        {
            var lastId = int.Parse(await File.ReadAllTextAsync(checkpointFile));
            var nextId = lastId + 1;
            await File.WriteAllTextAsync(checkpointFile, nextId.ToString());
            return nextId;
        }
        await File.WriteAllTextAsync(checkpointFile, "1");
        return 1;
    }
}
```

### 2. Structured Checkpoint Format

```markdown
**CHECKPOINT-00042**
Created: 2025-08-03T18:45:00Z

## Session Summary
...
```

### 3. Dedicated Checkpoint Tools

#### store_checkpoint
```csharp
[McpServerTool(Name = "store_checkpoint")]
public async Task<object> StoreCheckpoint(CheckpointParams parameters)
{
    var checkpointId = await GetNextCheckpointId();
    var checkpoint = new CheckpointMemory
    {
        Id = $"CHECKPOINT-{checkpointId:D5}",
        Content = parameters.Content,
        Created = DateTime.UtcNow,
        Type = "Checkpoint" // New memory type
    };
    
    // Store with special handling
    await _memoryService.StoreCheckpointAsync(checkpoint);
    
    return new { checkpointId, created = checkpoint.Created };
}
```

#### get_latest_checkpoint
```csharp
[McpServerTool(Name = "get_latest_checkpoint")]
public async Task<object> GetLatestCheckpoint()
{
    // Read the checkpoint ID file
    var checkpointFile = Path.Combine(".codesearch", "checkpoint.id");
    if (!File.Exists(checkpointFile))
        return new { error = "No checkpoints found" };
    
    var lastId = int.Parse(await File.ReadAllTextAsync(checkpointFile));
    var checkpointId = $"CHECKPOINT-{lastId:D5}";
    
    // Retrieve by exact ID
    var checkpoint = await _memoryService.GetCheckpointByIdAsync(checkpointId);
    return checkpoint ?? new { error = "Checkpoint not found" };
}
```

### 4. Updated Slash Commands

#### /checkpoint
```yaml
---
allowed-tools: ["mcp__codesearch__store_checkpoint"]
description: "Create a checkpoint of current work session"
---

Create a checkpoint with the following information:

$ARGUMENTS

Use store_checkpoint to save:
- What was accomplished
- Current state
- Next steps
- Files modified

The system will automatically assign a sequential checkpoint ID.
```

#### /resume
```yaml
---
allowed-tools: ["mcp__codesearch__get_latest_checkpoint"]
description: "Resume from the most recent checkpoint"
---

Retrieve and display the latest checkpoint.

$ARGUMENTS

Use get_latest_checkpoint to fetch the most recent checkpoint.
Display the full content and end with: "Ready to continue from checkpoint. What would you like to work on?"
```

## Benefits

1. **No Time Confusion**: Sequential IDs eliminate timezone issues
2. **Guaranteed Ordering**: Latest checkpoint is always the highest ID
3. **Fast Retrieval**: Direct lookup by ID instead of searching
4. **Reliability**: No dependency on Claude's time perception
5. **Simplicity**: Much simpler implementation and usage

## Migration Path

1. Add new checkpoint tools to MCP server
2. Update slash commands to use new tools
3. Keep old WorkSession memories for backward compatibility
4. Gradually phase out time-based checkpoints

## Alternative: Enhanced Current System

If we can't add new tools, enhance the current system:

1. **Use epoch timestamp in title**: `**CHECKPOINT-1754252680**`
2. **Add validation**: Check that retrieved checkpoint has expected format
3. **Use more specific queries**: Search for `"CHECKPOINT-"` prefix
4. **Store checkpoint ID separately**: Use a special field in memory