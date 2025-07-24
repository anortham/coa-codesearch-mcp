using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tools for managing relationships between memories
/// </summary>
public class MemoryLinkingTools : ITool
{
    public string ToolName => "memory_linking";
    public string Description => "Memory relationship management";
    public ToolCategory Category => ToolCategory.Memory;
    private readonly ILogger<MemoryLinkingTools> _logger;
    private readonly FlexibleMemoryService _memoryService;
    
    public MemoryLinkingTools(ILogger<MemoryLinkingTools> logger, FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }
    
    /// <summary>
    /// Link two memories together with a specified relationship type
    /// </summary>
    public async Task<LinkMemoriesResult> LinkMemoriesAsync(
        string sourceId, 
        string targetId, 
        string relationshipType = "relatedTo",
        bool bidirectional = false)
    {
        try
        {
            // Get source memory
            var sourceMemory = await _memoryService.GetMemoryByIdAsync(sourceId);
            if (sourceMemory == null)
            {
                return new LinkMemoriesResult 
                { 
                    Success = false, 
                    Message = $"Source memory {sourceId} not found" 
                };
            }
            
            // Get target memory
            var targetMemory = await _memoryService.GetMemoryByIdAsync(targetId);
            if (targetMemory == null)
            {
                return new LinkMemoriesResult 
                { 
                    Success = false, 
                    Message = $"Target memory {targetId} not found" 
                };
            }
            
            // Add the relationship
            var currentRelations = sourceMemory.GetField<Dictionary<string, string[]>>($"{relationshipType}Links") 
                ?? new Dictionary<string, string[]>();
            
            // Add target to the relationship type
            if (!currentRelations.ContainsKey(relationshipType))
            {
                currentRelations[relationshipType] = new string[] { targetId };
            }
            else
            {
                var existing = currentRelations[relationshipType].ToList();
                if (!existing.Contains(targetId))
                {
                    existing.Add(targetId);
                    currentRelations[relationshipType] = existing.ToArray();
                }
            }
            
            sourceMemory.SetField($"{relationshipType}Links", currentRelations);
            
            // Also update the legacy relatedTo field for backwards compatibility
            if (relationshipType == "relatedTo")
            {
                var relatedTo = sourceMemory.GetField<string[]>("relatedTo") ?? Array.Empty<string>();
                if (!relatedTo.Contains(targetId))
                {
                    var newRelatedTo = relatedTo.Append(targetId).ToArray();
                    sourceMemory.SetField("relatedTo", newRelatedTo);
                }
            }
            
            // Update the source memory
            var updateRequest = new MemoryUpdateRequest
            {
                Id = sourceId,
                FieldUpdates = sourceMemory.Fields.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (JsonElement?)kvp.Value
                )
            };
            
            await _memoryService.UpdateMemoryAsync(updateRequest);
            
            // If bidirectional, also link target back to source
            if (bidirectional)
            {
                var reverseResult = await LinkMemoriesAsync(targetId, sourceId, relationshipType, false);
                if (!reverseResult.Success)
                {
                    _logger.LogWarning("Failed to create bidirectional link: {Message}", reverseResult.Message);
                }
            }
            
            return new LinkMemoriesResult
            {
                Success = true,
                Message = $"Successfully linked {sourceId} to {targetId} with relationship '{relationshipType}'",
                SourceId = sourceId,
                TargetId = targetId,
                RelationshipType = relationshipType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking memories");
            return new LinkMemoriesResult
            {
                Success = false,
                Message = $"Error linking memories: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Get all memories related to a given memory, traversing relationships up to specified depth
    /// </summary>
    public async Task<GetRelatedMemoriesResult> GetRelatedMemoriesAsync(
        string memoryId, 
        int maxDepth = 2,
        string[]? relationshipTypes = null)
    {
        try
        {
            var visited = new HashSet<string>();
            var results = new List<RelatedMemoryInfo>();
            var rootMemory = await _memoryService.GetMemoryByIdAsync(memoryId);
            
            if (rootMemory == null)
            {
                return new GetRelatedMemoriesResult
                {
                    Success = false,
                    Message = $"Memory {memoryId} not found"
                };
            }
            
            // Start traversal
            await TraverseRelationshipsAsync(
                rootMemory, 
                0, 
                maxDepth, 
                visited, 
                results, 
                relationshipTypes,
                null
            );
            
            return new GetRelatedMemoriesResult
            {
                Success = true,
                RootMemory = rootMemory,
                RelatedMemories = results,
                TotalFound = results.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting related memories");
            return new GetRelatedMemoriesResult
            {
                Success = false,
                Message = $"Error getting related memories: {ex.Message}"
            };
        }
    }
    
    private async Task TraverseRelationshipsAsync(
        FlexibleMemoryEntry memory,
        int currentDepth,
        int maxDepth,
        HashSet<string> visited,
        List<RelatedMemoryInfo> results,
        string[]? relationshipTypes,
        string? relationshipFromParent)
    {
        if (currentDepth > maxDepth || visited.Contains(memory.Id))
            return;
            
        visited.Add(memory.Id);
        
        if (currentDepth > 0) // Don't include root in results
        {
            results.Add(new RelatedMemoryInfo
            {
                Memory = memory,
                Depth = currentDepth,
                RelationshipType = relationshipFromParent ?? "unknown",
                Path = GeneratePath(memory, currentDepth)
            });
        }
        
        // Get all relationship links
        var allLinks = new Dictionary<string, string[]>();
        
        // Check modern relationship storage
        foreach (var field in memory.Fields)
        {
            if (field.Key.EndsWith("Links"))
            {
                try
                {
                    var links = memory.GetField<Dictionary<string, string[]>>(field.Key);
                    if (links != null)
                    {
                        foreach (var kvp in links)
                        {
                            allLinks[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch
                {
                    // Field might not be in expected format
                }
            }
        }
        
        // Also check legacy relatedTo field
        var relatedTo = memory.GetField<string[]>("relatedTo");
        if (relatedTo != null && relatedTo.Any())
        {
            allLinks["relatedTo"] = relatedTo;
        }
        
        // Traverse each relationship type
        foreach (var relationship in allLinks)
        {
            // Skip if we're filtering by relationship types and this isn't included
            if (relationshipTypes != null && 
                relationshipTypes.Any() && 
                !relationshipTypes.Contains(relationship.Key))
                continue;
                
            foreach (var relatedId in relationship.Value)
            {
                if (!visited.Contains(relatedId))
                {
                    var relatedMemory = await _memoryService.GetMemoryByIdAsync(relatedId);
                    if (relatedMemory != null)
                    {
                        await TraverseRelationshipsAsync(
                            relatedMemory,
                            currentDepth + 1,
                            maxDepth,
                            visited,
                            results,
                            relationshipTypes,
                            relationship.Key
                        );
                    }
                }
            }
        }
    }
    
    private string GeneratePath(FlexibleMemoryEntry memory, int depth)
    {
        // Simple path representation - could be enhanced to show full traversal path
        return $"→ [{depth}] {memory.Type}: {memory.Content.Substring(0, Math.Min(50, memory.Content.Length))}...";
    }
    
    /// <summary>
    /// Remove a relationship between two memories
    /// </summary>
    public async Task<UnlinkMemoriesResult> UnlinkMemoriesAsync(
        string sourceId,
        string targetId,
        string relationshipType = "relatedTo",
        bool bidirectional = false)
    {
        try
        {
            var sourceMemory = await _memoryService.GetMemoryByIdAsync(sourceId);
            if (sourceMemory == null)
            {
                return new UnlinkMemoriesResult
                {
                    Success = false,
                    Message = $"Source memory {sourceId} not found"
                };
            }
            
            // Remove from modern relationship storage
            var currentRelations = sourceMemory.GetField<Dictionary<string, string[]>>($"{relationshipType}Links");
            if (currentRelations != null && currentRelations.ContainsKey(relationshipType))
            {
                var updated = currentRelations[relationshipType]
                    .Where(id => id != targetId)
                    .ToArray();
                    
                if (updated.Length == 0)
                {
                    currentRelations.Remove(relationshipType);
                }
                else
                {
                    currentRelations[relationshipType] = updated;
                }
                
                sourceMemory.SetField($"{relationshipType}Links", currentRelations);
            }
            
            // Also update legacy relatedTo field
            if (relationshipType == "relatedTo")
            {
                var relatedTo = sourceMemory.GetField<string[]>("relatedTo");
                if (relatedTo != null)
                {
                    var updated = relatedTo.Where(id => id != targetId).ToArray();
                    sourceMemory.SetField("relatedTo", updated);
                }
            }
            
            // Update the memory
            var updateRequest = new MemoryUpdateRequest
            {
                Id = sourceId,
                FieldUpdates = sourceMemory.Fields.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (JsonElement?)kvp.Value
                )
            };
            
            await _memoryService.UpdateMemoryAsync(updateRequest);
            
            // If bidirectional, also unlink target from source
            if (bidirectional)
            {
                await UnlinkMemoriesAsync(targetId, sourceId, relationshipType, false);
            }
            
            return new UnlinkMemoriesResult
            {
                Success = true,
                Message = $"Successfully unlinked {sourceId} from {targetId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking memories");
            return new UnlinkMemoriesResult
            {
                Success = false,
                Message = $"Error unlinking memories: {ex.Message}"
            };
        }
    }
}

// Result classes
public class LinkMemoriesResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public string? RelationshipType { get; set; }
}

public class GetRelatedMemoriesResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public FlexibleMemoryEntry? RootMemory { get; set; }
    public List<RelatedMemoryInfo> RelatedMemories { get; set; } = new();
    public int TotalFound { get; set; }
}

public class RelatedMemoryInfo
{
    public FlexibleMemoryEntry Memory { get; set; } = null!;
    public int Depth { get; set; }
    public string RelationshipType { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class UnlinkMemoriesResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}