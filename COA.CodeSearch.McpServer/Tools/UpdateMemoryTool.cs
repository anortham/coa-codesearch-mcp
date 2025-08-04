using System.Text.Json;
using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for updating existing memories with field changes and ID migration
/// </summary>
[McpServerToolType]
public class UpdateMemoryTool
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<UpdateMemoryTool> _logger;
    
    public UpdateMemoryTool(IMemoryService memoryService, ILogger<UpdateMemoryTool> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }
    
    [McpServerTool(Name = "update_memory")]
    [Description(@"Update an existing memory's fields or migrate its ID to timestamp format.
Returns: Updated memory with changes applied.
Prerequisites: Memory must exist.
Use cases: Updating memory content, adding/removing fields, migrating GUID to timestamp ID.")]
    public async Task<object> UpdateMemoryAsync(UpdateMemoryParams parameters)
    {
        if (parameters == null)
            throw new InvalidParametersException("Parameters are required");
            
        var memoryId = ValidateRequired(parameters.MemoryId, "memoryId");
        
        try
        {
            // Get the existing memory
            var existingMemory = await _memoryService.GetMemoryByIdAsync(memoryId);
            if (existingMemory == null)
            {
                return new
                {
                    success = false,
                    error = $"Memory with ID '{memoryId}' not found"
                };
            }
            
            // Handle ID migration if requested
            if (parameters.MigrateIdToTimestamp == true)
            {
                var result = await MigrateMemoryIdAsync(existingMemory);
                if (result.Success)
                {
                    // Update the memory ID for further operations
                    memoryId = result.NewId!;
                    existingMemory = result.UpdatedMemory!;
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = result.Error
                    };
                }
            }
            
            // Build field updates
            var fieldUpdates = new Dictionary<string, JsonElement?>();
            
            // Update content if provided
            if (parameters.Content != null)
            {
                fieldUpdates["content"] = JsonDocument.Parse(JsonSerializer.Serialize(parameters.Content)).RootElement;
            }
            
            // Update type if provided
            if (parameters.Type != null)
            {
                fieldUpdates["type"] = JsonDocument.Parse(JsonSerializer.Serialize(parameters.Type)).RootElement;
            }
            
            // Update custom fields
            if (parameters.Fields != null)
            {
                foreach (var field in parameters.Fields)
                {
                    if (field.Value == null)
                    {
                        // null means remove the field
                        fieldUpdates[field.Key] = null;
                    }
                    else
                    {
                        // Convert value to JsonElement
                        var jsonValue = JsonSerializer.Serialize(field.Value);
                        fieldUpdates[field.Key] = JsonDocument.Parse(jsonValue).RootElement;
                    }
                }
            }
            
            // Apply the updates if there are any
            if (fieldUpdates.Any())
            {
                var updateRequest = new MemoryUpdateRequest
                {
                    Id = memoryId,
                    FieldUpdates = fieldUpdates
                };
                
                var updateSuccess = await _memoryService.UpdateMemoryAsync(updateRequest);
                if (!updateSuccess)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to update memory"
                    };
                }
            }
            
            // Get the updated memory
            var updatedMemory = await _memoryService.GetMemoryByIdAsync(memoryId);
            
            return new
            {
                success = true,
                memory = new
                {
                    id = updatedMemory!.Id,
                    type = updatedMemory.Type,
                    content = updatedMemory.Content,
                    created = updatedMemory.Created,
                    modified = updatedMemory.Modified,
                    isShared = updatedMemory.IsShared,
                    fields = updatedMemory.Fields.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.ValueKind == JsonValueKind.String 
                            ? kvp.Value.GetString() 
                            : kvp.Value.ToString()
                    ),
                    idMigrated = parameters.MigrateIdToTimestamp == true && !TimestampIdGenerator.IsTimestampId(parameters.MemoryId ?? "")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating memory {MemoryId}", memoryId);
            return new
            {
                success = false,
                error = $"Error updating memory: {ex.Message}"
            };
        }
    }
    
    private async Task<MigrationResult> MigrateMemoryIdAsync(FlexibleMemoryEntry memory)
    {
        // Check if already a timestamp ID
        if (TimestampIdGenerator.IsTimestampId(memory.Id))
        {
            return new MigrationResult 
            { 
                Success = true, 
                NewId = memory.Id,
                UpdatedMemory = memory,
                Error = "Memory already has a timestamp-based ID"
            };
        }
        
        // Generate new ID based on creation timestamp
        var newId = TimestampIdGenerator.GenerateIdForTimestamp(memory.Created);
        
        _logger.LogInformation("Migrating memory ID from {OldId} to {NewId}", memory.Id, newId);
        
        // Create a new memory with the new ID
        var migratedMemory = new FlexibleMemoryEntry
        {
            Id = newId,
            Type = memory.Type,
            Content = memory.Content,
            Created = memory.Created,
            Modified = DateTime.UtcNow, // Update modified time
            Fields = memory.Fields,
            FilesInvolved = memory.FilesInvolved,
            SessionId = memory.SessionId,
            IsShared = memory.IsShared,
            AccessCount = memory.AccessCount,
            LastAccessed = memory.LastAccessed
        };
        
        // Store the new memory
        var storeSuccess = await _memoryService.StoreMemoryAsync(migratedMemory);
        if (!storeSuccess)
        {
            return new MigrationResult
            {
                Success = false,
                Error = "Failed to store memory with new ID"
            };
        }
        
        // Delete the old memory
        var deleteSuccess = await _memoryService.DeleteMemoryAsync(memory.Id);
        if (!deleteSuccess)
        {
            // Try to clean up the new memory since we couldn't delete the old one
            await _memoryService.DeleteMemoryAsync(newId);
            return new MigrationResult
            {
                Success = false,
                Error = "Failed to delete old memory after creating new one"
            };
        }
        
        return new MigrationResult
        {
            Success = true,
            NewId = newId,
            UpdatedMemory = migratedMemory
        };
    }
    
    private static string ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidParametersException($"{parameterName} is required");
        return value;
    }
    
    private class MigrationResult
    {
        public bool Success { get; set; }
        public string? NewId { get; set; }
        public FlexibleMemoryEntry? UpdatedMemory { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>
/// Parameters for updating a memory
/// </summary>
public class UpdateMemoryParams
{
    [Description("The ID of the memory to update")]
    public string? MemoryId { get; set; }
    
    [Description("New content for the memory (optional)")]
    public string? Content { get; set; }
    
    [Description("New type for the memory (optional)")]
    public string? Type { get; set; }
    
    [Description("Field updates as key-value pairs. Set value to null to remove a field. Example: {\"status\": \"completed\", \"priority\": null}")]
    public Dictionary<string, object?>? Fields { get; set; }
    
    [Description("Migrate GUID-based ID to timestamp-based ID for chronological sorting (default: false)")]
    public bool? MigrateIdToTimestamp { get; set; }
}