using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tools for the flexible memory system
/// </summary>
public class FlexibleMemoryTools
{
    private readonly ILogger<FlexibleMemoryTools> _logger;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryTools(ILogger<FlexibleMemoryTools> logger, FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }
    
    /// <summary>
    /// Store a new memory with flexible schema
    /// </summary>
    public async Task<StoreMemoryResult> StoreMemoryAsync(
        string type,
        string content,
        bool isShared = true,
        string? sessionId = null,
        string[]? files = null,
        Dictionary<string, JsonElement>? fields = null)
    {
        try
        {
            var memory = new FlexibleMemoryEntry
            {
                Type = type,
                Content = content,
                IsShared = isShared,
                SessionId = sessionId ?? "",
                FilesInvolved = files ?? Array.Empty<string>(),
                Fields = fields ?? new Dictionary<string, JsonElement>()
            };
            
            var success = await _memoryService.StoreMemoryAsync(memory);
            
            return new StoreMemoryResult
            {
                Success = success,
                MemoryId = success ? memory.Id : null,
                Message = success ? 
                    $"Successfully stored {type} memory" : 
                    "Failed to store memory"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing memory");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Store a working memory (temporary, session-scoped)
    /// </summary>
    public async Task<StoreMemoryResult> StoreWorkingMemoryAsync(
        string content,
        string? expiresIn = "end-of-session",
        string? sessionId = null,
        string[]? files = null,
        Dictionary<string, JsonElement>? fields = null)
    {
        try
        {
            // Calculate expiration time
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(expiresIn) && expiresIn != "end-of-session")
            {
                if (expiresIn.EndsWith("h"))
                {
                    var hours = int.Parse(expiresIn.TrimEnd('h'));
                    expiresAt = DateTime.UtcNow.AddHours(hours);
                }
                else if (expiresIn.EndsWith("m"))
                {
                    var minutes = int.Parse(expiresIn.TrimEnd('m'));
                    expiresAt = DateTime.UtcNow.AddMinutes(minutes);
                }
                else if (expiresIn.EndsWith("d"))
                {
                    var days = int.Parse(expiresIn.TrimEnd('d'));
                    expiresAt = DateTime.UtcNow.AddDays(days);
                }
            }
            
            var workingFields = fields ?? new Dictionary<string, JsonElement>();
            
            // Add expiration field if specified
            if (expiresAt.HasValue)
            {
                workingFields[MemoryFields.ExpiresAt] = JsonSerializer.SerializeToElement(expiresAt.Value);
            }
            
            // Add working memory marker
            workingFields["isWorkingMemory"] = JsonSerializer.SerializeToElement(true);
            workingFields["sessionExpiry"] = JsonSerializer.SerializeToElement(expiresIn ?? "end-of-session");
            
            var memory = new FlexibleMemoryEntry
            {
                Type = MemoryTypes.WorkingMemory,
                Content = content,
                IsShared = false, // Working memories are always local
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                FilesInvolved = files ?? Array.Empty<string>(),
                Fields = workingFields
            };
            
            var success = await _memoryService.StoreMemoryAsync(memory);
            
            return new StoreMemoryResult
            {
                Success = success,
                MemoryId = success ? memory.Id : null,
                Message = success ? 
                    $"Working memory stored (expires: {expiresIn})" : 
                    "Failed to store working memory"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing working memory");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Search memories with advanced filtering
    /// </summary>
    public async Task<FlexibleMemorySearchResult> SearchMemoriesAsync(
        string? query = null,
        string[]? types = null,
        string? dateRange = null,
        Dictionary<string, string>? facets = null,
        string? orderBy = null,
        bool orderDescending = true,
        int maxResults = 50,
        bool includeArchived = false,
        bool boostRecent = false,
        bool boostFrequent = false)
    {
        try
        {
            var request = new FlexibleMemorySearchRequest
            {
                Query = query ?? "*",
                Types = types,
                Facets = facets,
                OrderBy = orderBy,
                OrderDescending = orderDescending,
                MaxResults = maxResults,
                IncludeArchived = includeArchived,
                BoostRecent = boostRecent,
                BoostFrequent = boostFrequent
            };
            
            if (!string.IsNullOrEmpty(dateRange))
            {
                request.DateRange = new DateRangeFilter { RelativeTime = dateRange };
            }
            
            return await _memoryService.SearchMemoriesAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching memories");
            return new FlexibleMemorySearchResult
            {
                Memories = new List<FlexibleMemoryEntry>(),
                TotalFound = 0,
                FacetCounts = new Dictionary<string, Dictionary<string, int>>()
            };
        }
    }
    
    /// <summary>
    /// Update an existing memory
    /// </summary>
    public async Task<UpdateMemoryResult> UpdateMemoryAsync(
        string id,
        string? content = null,
        Dictionary<string, JsonElement?>? fieldUpdates = null,
        string[]? addFiles = null,
        string[]? removeFiles = null)
    {
        try
        {
            var request = new MemoryUpdateRequest
            {
                Id = id,
                Content = content,
                FieldUpdates = fieldUpdates ?? new Dictionary<string, JsonElement?>(),
                AddFiles = addFiles,
                RemoveFiles = removeFiles
            };
            
            var success = await _memoryService.UpdateMemoryAsync(request);
            
            return new UpdateMemoryResult
            {
                Success = success,
                Message = success ? 
                    "Memory updated successfully" : 
                    "Memory not found or update failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating memory");
            return new UpdateMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Store a technical debt memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreTechnicalDebtAsync(
        string description,
        string? status = null,
        string? priority = null,
        string? category = null,
        int? estimatedHours = null,
        string[]? files = null,
        string[]? tags = null)
    {
        var fields = new Dictionary<string, JsonElement>();
        
        if (!string.IsNullOrEmpty(status))
            fields[MemoryFields.Status] = JsonDocument.Parse($"\"{status}\"").RootElement;
        
        if (!string.IsNullOrEmpty(priority))
            fields[MemoryFields.Priority] = JsonDocument.Parse($"\"{priority}\"").RootElement;
        
        if (!string.IsNullOrEmpty(category))
            fields[MemoryFields.Category] = JsonDocument.Parse($"\"{category}\"").RootElement;
        
        if (estimatedHours.HasValue)
            fields["estimatedHours"] = JsonDocument.Parse(estimatedHours.Value.ToString()).RootElement;
        
        if (tags != null && tags.Length > 0)
            fields[MemoryFields.Tags] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tags));
        
        return await StoreMemoryAsync(
            MemoryTypes.TechnicalDebt,
            description,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Store a question memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreQuestionAsync(
        string question,
        string? context = null,
        string? status = null,
        string[]? files = null,
        string[]? tags = null)
    {
        var fields = new Dictionary<string, JsonElement>();
        
        if (!string.IsNullOrEmpty(status))
            fields[MemoryFields.Status] = JsonDocument.Parse($"\"{status}\"").RootElement;
        
        if (!string.IsNullOrEmpty(context))
            fields["context"] = JsonDocument.Parse($"\"{context}\"").RootElement;
        
        if (tags != null && tags.Length > 0)
            fields[MemoryFields.Tags] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tags));
        
        return await StoreMemoryAsync(
            MemoryTypes.Question,
            question,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Store a deferred task memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreDeferredTaskAsync(
        string task,
        string reason,
        DateTime? deferredUntil = null,
        string? priority = null,
        string[]? files = null,
        string[]? blockedBy = null)
    {
        var fields = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonDocument.Parse($"\"{reason}\"").RootElement,
            [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.Deferred}\"").RootElement
        };
        
        if (deferredUntil.HasValue)
            fields["deferredUntil"] = JsonDocument.Parse($"\"{deferredUntil.Value:O}\"").RootElement;
        
        if (!string.IsNullOrEmpty(priority))
            fields[MemoryFields.Priority] = JsonDocument.Parse($"\"{priority}\"").RootElement;
        
        if (blockedBy != null && blockedBy.Length > 0)
            fields["blockedBy"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(blockedBy));
        
        return await StoreMemoryAsync(
            MemoryTypes.DeferredTask,
            task,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Find memories similar to a given memory
    /// </summary>
    public async Task<SimilarMemoriesResult> FindSimilarMemoriesAsync(string memoryId, int maxResults = 10)
    {
        try
        {
            var similar = await _memoryService.FindSimilarMemoriesAsync(memoryId, maxResults);
            
            return new SimilarMemoriesResult
            {
                Success = true,
                SimilarMemories = similar,
                Message = $"Found {similar.Count} similar memories"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar memories");
            return new SimilarMemoriesResult
            {
                Success = false,
                SimilarMemories = new List<FlexibleMemoryEntry>(),
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Mark a memory as resolved/completed
    /// </summary>
    public async Task<UpdateMemoryResult> MarkMemoryResolvedAsync(
        string id,
        string? resolutionNote = null)
    {
        var fieldUpdates = new Dictionary<string, JsonElement?>
        {
            [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.Resolved}\"").RootElement,
            ["resolvedAt"] = JsonDocument.Parse($"\"{DateTime.UtcNow:O}\"").RootElement
        };
        
        if (!string.IsNullOrEmpty(resolutionNote))
        {
            fieldUpdates["resolutionNote"] = JsonDocument.Parse($"\"{resolutionNote}\"").RootElement;
        }
        
        return await UpdateMemoryAsync(id, fieldUpdates: fieldUpdates);
    }
    
    /// <summary>
    /// Archive old memories of a specific type
    /// </summary>
    public async Task<ArchiveMemoriesResult> ArchiveMemoriesAsync(string type, int daysOld)
    {
        try
        {
            var archived = await _memoryService.ArchiveMemoriesAsync(type, TimeSpan.FromDays(daysOld));
            
            return new ArchiveMemoriesResult
            {
                Success = true,
                ArchivedCount = archived,
                Message = $"Archived {archived} {type} memories older than {daysOld} days"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving memories");
            return new ArchiveMemoriesResult
            {
                Success = false,
                ArchivedCount = 0,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Get memory by ID
    /// </summary>
    public async Task<GetMemoryResult> GetMemoryByIdAsync(string id)
    {
        try
        {
            var memory = await _memoryService.GetMemoryByIdAsync(id);
            
            if (memory != null)
            {
                return new GetMemoryResult
                {
                    Success = true,
                    Memory = memory,
                    Message = "Memory found"
                };
            }
            else
            {
                return new GetMemoryResult
                {
                    Success = false,
                    Message = "Memory not found"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memory by ID");
            return new GetMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
}

// Result classes

public class StoreMemoryResult
{
    public bool Success { get; set; }
    public string? MemoryId { get; set; }
    public string Message { get; set; } = "";
}

public class UpdateMemoryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class SimilarMemoriesResult
{
    public bool Success { get; set; }
    public List<FlexibleMemoryEntry> SimilarMemories { get; set; } = new();
    public string Message { get; set; } = "";
}

public class ArchiveMemoriesResult
{
    public bool Success { get; set; }
    public int ArchivedCount { get; set; }
    public string Message { get; set; } = "";
}

public class GetMemoryResult
{
    public bool Success { get; set; }
    public FlexibleMemoryEntry? Memory { get; set; }
    public string Message { get; set; } = "";
}