using System.Text;
using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tools for persistent checklists
/// </summary>
public class ChecklistTools : ITool
{
    public string ToolName => "checklist";
    public string Description => "Persistent checklist management";
    public ToolCategory Category => ToolCategory.Memory;
    private readonly ILogger<ChecklistTools> _logger;
    private readonly FlexibleMemoryService _memoryService;
    private readonly MemoryLinkingTools _linkingTools;
    
    public ChecklistTools(ILogger<ChecklistTools> logger, FlexibleMemoryService memoryService, MemoryLinkingTools linkingTools)
    {
        _logger = logger;
        _memoryService = memoryService;
        _linkingTools = linkingTools;
    }
    
    /// <summary>
    /// Create a new checklist
    /// </summary>
    public async Task<CreateChecklistResult> CreateChecklistAsync(
        string title,
        string? description = null,
        bool isShared = true,
        string? sessionId = null,
        Dictionary<string, JsonElement>? customFields = null)
    {
        try
        {
            var checklist = new FlexibleMemoryEntry
            {
                Type = MemoryTypes.Checklist,
                Content = title,
                IsShared = isShared,
                SessionId = sessionId ?? "",
                Fields = customFields ?? new Dictionary<string, JsonElement>()
            };
            
            // Add checklist-specific fields
            checklist.SetField(MemoryFields.ChecklistDescription, description ?? "");
            checklist.SetField(MemoryFields.ItemCount, 0);
            checklist.SetField(MemoryFields.CompletedCount, 0);
            checklist.SetField(MemoryFields.Status, MemoryStatus.InProgress);
            
            var success = await _memoryService.StoreMemoryAsync(checklist);
            
            return new CreateChecklistResult
            {
                Success = success,
                ChecklistId = success ? checklist.Id : null,
                Message = success 
                    ? $"Created checklist '{title}' with ID: {checklist.Id}"
                    : "Failed to create checklist"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating checklist");
            return new CreateChecklistResult
            {
                Success = false,
                Message = $"Error creating checklist: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Add one or more items to a checklist
    /// </summary>
    public async Task<AddChecklistItemsResult> AddChecklistItemsAsync(
        string checklistId,
        ChecklistItemInput[] items)
    {
        try
        {
            // First, get the checklist to verify it exists and get current counts
            var checklist = await _memoryService.GetMemoryByIdAsync(checklistId);
            if (checklist == null || checklist.Type != MemoryTypes.Checklist)
            {
                return new AddChecklistItemsResult
                {
                    Success = false,
                    Message = "Checklist not found"
                };
            }
            
            // Get current item count
            var itemCount = checklist.GetField<int>(MemoryFields.ItemCount);
            var addedItems = new List<AddedChecklistItem>();
            var failedItems = new List<string>();
            
            // Process each item
            for (int i = 0; i < items.Length; i++)
            {
                var itemInput = items[i];
                var nextOrder = itemCount + i + 1;
                
                try
                {
                    // Create the checklist item
                    var item = new FlexibleMemoryEntry
                    {
                        Type = MemoryTypes.ChecklistItem,
                        Content = itemInput.ItemText,
                        IsShared = checklist.IsShared,
                        SessionId = checklist.SessionId,
                        FilesInvolved = itemInput.RelatedFiles ?? Array.Empty<string>(),
                        Fields = itemInput.CustomFields ?? new Dictionary<string, JsonElement>()
                    };
                    
                    // Add item-specific fields
                    item.SetField(MemoryFields.ParentChecklistId, checklistId);
                    item.SetField(MemoryFields.IsCompleted, false);
                    item.SetField(MemoryFields.ItemOrder, nextOrder);
                    if (!string.IsNullOrEmpty(itemInput.Notes))
                    {
                        item.SetField("notes", itemInput.Notes);
                    }
                    
                    var success = await _memoryService.StoreMemoryAsync(item);
                    
                    if (success)
                    {
                        // Link the item to the checklist
                        await _linkingTools.LinkMemoriesAsync(
                            sourceId: checklistId,
                            targetId: item.Id,
                            relationshipType: MemoryRelationshipTypes.ParentOf,
                            bidirectional: true
                        );
                        
                        addedItems.Add(new AddedChecklistItem
                        {
                            ItemId = item.Id,
                            ItemText = itemInput.ItemText,
                            ItemOrder = nextOrder
                        });
                    }
                    else
                    {
                        failedItems.Add(itemInput.ItemText);
                    }
                }
                catch (Exception itemEx)
                {
                    _logger.LogError(itemEx, "Error adding item '{ItemText}'", itemInput.ItemText);
                    failedItems.Add(itemInput.ItemText);
                }
            }
            
            // Update checklist item count with the number of successfully added items
            if (addedItems.Count > 0)
            {
                var updateRequest = new MemoryUpdateRequest
                {
                    Id = checklistId,
                    FieldUpdates = new Dictionary<string, JsonElement?>
                    {
                        [MemoryFields.ItemCount] = JsonDocument.Parse(JsonSerializer.Serialize(itemCount + addedItems.Count)).RootElement
                    }
                };
                await _memoryService.UpdateMemoryAsync(updateRequest);
            }
            
            var overallSuccess = addedItems.Count > 0 && failedItems.Count == 0;
            var message = overallSuccess 
                ? $"Added {addedItems.Count} item{(addedItems.Count == 1 ? "" : "s")} to checklist"
                : failedItems.Count == 0 
                    ? "No items were added to the checklist"
                    : $"Added {addedItems.Count} item{(addedItems.Count == 1 ? "" : "s")}, {failedItems.Count} failed";
            
            return new AddChecklistItemsResult
            {
                Success = overallSuccess,
                AddedItems = addedItems,
                FailedItems = failedItems,
                TotalAdded = addedItems.Count,
                TotalFailed = failedItems.Count,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding checklist items");
            return new AddChecklistItemsResult
            {
                Success = false,
                Message = $"Error adding checklist items: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Toggle the completion status of a checklist item
    /// </summary>
    public async Task<ToggleChecklistItemResult> ToggleChecklistItemAsync(
        string itemId,
        bool? isCompleted = null,
        string? completedBy = null)
    {
        try
        {
            var item = await _memoryService.GetMemoryByIdAsync(itemId);
            if (item == null || item.Type != MemoryTypes.ChecklistItem)
            {
                return new ToggleChecklistItemResult
                {
                    Success = false,
                    Message = "Checklist item not found"
                };
            }
            
            var currentStatus = item.GetField<bool>(MemoryFields.IsCompleted);
            var newStatus = isCompleted ?? !currentStatus;
            
            var updateRequest = new MemoryUpdateRequest
            {
                Id = itemId,
                FieldUpdates = new Dictionary<string, JsonElement?>
                {
                    [MemoryFields.IsCompleted] = JsonDocument.Parse(JsonSerializer.Serialize(newStatus)).RootElement
                }
            };
            
            if (newStatus)
            {
                updateRequest.FieldUpdates[MemoryFields.CompletedAt] = 
                    JsonDocument.Parse(JsonSerializer.Serialize(DateTime.UtcNow)).RootElement;
                if (!string.IsNullOrEmpty(completedBy))
                {
                    updateRequest.FieldUpdates[MemoryFields.CompletedBy] = 
                        JsonDocument.Parse(JsonSerializer.Serialize(completedBy)).RootElement;
                }
            }
            else
            {
                // Clear completion fields when uncompleting
                updateRequest.FieldUpdates[MemoryFields.CompletedAt] = null;
                updateRequest.FieldUpdates[MemoryFields.CompletedBy] = null;
            }
            
            var success = await _memoryService.UpdateMemoryAsync(updateRequest);
            
            if (success)
            {
                // Update parent checklist completed count
                var parentChecklistId = item.GetField<string>(MemoryFields.ParentChecklistId);
                if (!string.IsNullOrEmpty(parentChecklistId))
                {
                    await UpdateChecklistCompletedCountAsync(parentChecklistId);
                }
            }
            
            return new ToggleChecklistItemResult
            {
                Success = success,
                IsCompleted = newStatus,
                Message = success 
                    ? $"Item marked as {(newStatus ? "completed" : "incomplete")}"
                    : "Failed to update item status"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling checklist item");
            return new ToggleChecklistItemResult
            {
                Success = false,
                Message = $"Error toggling checklist item: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Update a checklist item's text or properties
    /// </summary>
    public async Task<UpdateChecklistItemResult> UpdateChecklistItemAsync(
        string itemId,
        string? newText = null,
        string? notes = null,
        Dictionary<string, JsonElement>? customFields = null)
    {
        try
        {
            var item = await _memoryService.GetMemoryByIdAsync(itemId);
            if (item == null || item.Type != MemoryTypes.ChecklistItem)
            {
                return new UpdateChecklistItemResult
                {
                    Success = false,
                    Message = "Checklist item not found"
                };
            }
            
            var updateRequest = new MemoryUpdateRequest
            {
                Id = itemId,
                Content = newText,
                FieldUpdates = new Dictionary<string, JsonElement?>()
            };
            
            // Merge custom fields if provided
            if (customFields != null)
            {
                foreach (var kvp in customFields)
                {
                    updateRequest.FieldUpdates[kvp.Key] = kvp.Value;
                }
            }
            
            if (notes != null)
            {
                updateRequest.FieldUpdates["notes"] = 
                    JsonDocument.Parse(JsonSerializer.Serialize(notes)).RootElement;
            }
            
            var success = await _memoryService.UpdateMemoryAsync(updateRequest);
            
            return new UpdateChecklistItemResult
            {
                Success = success,
                Message = success 
                    ? "Checklist item updated successfully"
                    : "Failed to update checklist item"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating checklist item");
            return new UpdateChecklistItemResult
            {
                Success = false,
                Message = $"Error updating checklist item: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// View a checklist with all its items and progress
    /// </summary>
    public async Task<ViewChecklistResult> ViewChecklistAsync(
        string checklistId,
        bool includeCompleted = true,
        bool exportAsMarkdown = false)
    {
        try
        {
            var checklist = await _memoryService.GetMemoryByIdAsync(checklistId);
            if (checklist == null || checklist.Type != MemoryTypes.Checklist)
            {
                return new ViewChecklistResult
                {
                    Success = false,
                    Message = "Checklist not found"
                };
            }
            
            // Get all items for this checklist
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Types = new[] { MemoryTypes.ChecklistItem },
                Facets = new Dictionary<string, string>
                {
                    [MemoryFields.ParentChecklistId] = checklistId
                },
                MaxResults = 1000,
                OrderBy = MemoryFields.ItemOrder,
                OrderDescending = false
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            var items = searchResult.Memories;
            
            // Filter out completed items if requested
            if (!includeCompleted)
            {
                items = items.Where(i => !i.GetField<bool>(MemoryFields.IsCompleted)).ToList();
            }
            
            // Calculate progress
            var totalItems = items.Count;
            var completedItems = items.Count(i => i.GetField<bool>(MemoryFields.IsCompleted));
            var progressPercentage = totalItems > 0 ? (completedItems * 100.0 / totalItems) : 0;
            
            var result = new ViewChecklistResult
            {
                Success = true,
                Checklist = new ChecklistView
                {
                    Id = checklist.Id,
                    Title = checklist.Content,
                    Description = checklist.GetField<string>(MemoryFields.ChecklistDescription) ?? "",
                    Status = checklist.GetField<string>(MemoryFields.Status) ?? MemoryStatus.InProgress,
                    Created = checklist.Created,
                    Modified = checklist.Modified,
                    TotalItems = totalItems,
                    CompletedItems = completedItems,
                    ProgressPercentage = progressPercentage,
                    Items = items.Select(item => new ChecklistItemView
                    {
                        Id = item.Id,
                        Text = item.Content,
                        IsCompleted = item.GetField<bool>(MemoryFields.IsCompleted),
                        CompletedAt = item.GetField<DateTime?>(MemoryFields.CompletedAt),
                        CompletedBy = item.GetField<string>(MemoryFields.CompletedBy),
                        Order = item.GetField<int>(MemoryFields.ItemOrder),
                        Notes = item.GetField<string>("notes"),
                        Files = item.FilesInvolved
                    }).OrderBy(i => i.Order).ToList()
                }
            };
            
            if (exportAsMarkdown)
            {
                result.MarkdownExport = GenerateMarkdownExport(result.Checklist);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error viewing checklist");
            return new ViewChecklistResult
            {
                Success = false,
                Message = $"Error viewing checklist: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// List all checklists with optional filtering
    /// </summary>
    public async Task<ListChecklistsResult> ListChecklistsAsync(
        bool includeCompleted = true,
        bool onlyShared = false,
        int maxResults = 50)
    {
        try
        {
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Types = new[] { MemoryTypes.Checklist },
                MaxResults = maxResults,
                OrderBy = "modified",
                OrderDescending = true
            };
            
            if (!includeCompleted)
            {
                searchRequest.Facets = new Dictionary<string, string>
                {
                    [MemoryFields.Status] = MemoryStatus.InProgress
                };
            }
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            var checklists = searchResult.Memories
                .Where(c => !onlyShared || c.IsShared)
                .Select(c => new ChecklistSummary
                {
                    Id = c.Id,
                    Title = c.Content,
                    Description = c.GetField<string>(MemoryFields.ChecklistDescription) ?? "",
                    Status = c.GetField<string>(MemoryFields.Status) ?? MemoryStatus.InProgress,
                    Created = c.Created,
                    Modified = c.Modified,
                    TotalItems = c.GetField<int>(MemoryFields.ItemCount),
                    CompletedItems = c.GetField<int>(MemoryFields.CompletedCount),
                    IsShared = c.IsShared
                })
                .ToList();
            
            return new ListChecklistsResult
            {
                Success = true,
                Checklists = checklists,
                TotalFound = searchResult.TotalFound
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing checklists");
            return new ListChecklistsResult
            {
                Success = false,
                Message = $"Error listing checklists: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Update the completed count for a checklist
    /// </summary>
    private async Task UpdateChecklistCompletedCountAsync(string checklistId)
    {
        try
        {
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Types = new[] { MemoryTypes.ChecklistItem },
                Facets = new Dictionary<string, string>
                {
                    [MemoryFields.ParentChecklistId] = checklistId
                },
                MaxResults = 1000
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            var completedCount = searchResult.Memories.Count(i => i.GetField<bool>(MemoryFields.IsCompleted));
            
            var updateRequest = new MemoryUpdateRequest
            {
                Id = checklistId,
                FieldUpdates = new Dictionary<string, JsonElement?>
                {
                    [MemoryFields.CompletedCount] = JsonDocument.Parse(JsonSerializer.Serialize(completedCount)).RootElement
                }
            };
            
            // Update status if all items are completed
            if (completedCount == searchResult.TotalFound && searchResult.TotalFound > 0)
            {
                updateRequest.FieldUpdates[MemoryFields.Status] = 
                    JsonDocument.Parse(JsonSerializer.Serialize(MemoryStatus.Done)).RootElement;
            }
            else
            {
                updateRequest.FieldUpdates[MemoryFields.Status] = 
                    JsonDocument.Parse(JsonSerializer.Serialize(MemoryStatus.InProgress)).RootElement;
            }
            
            await _memoryService.UpdateMemoryAsync(updateRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating checklist completed count");
        }
    }
    
    /// <summary>
    /// Generate markdown export of a checklist
    /// </summary>
    private string GenerateMarkdownExport(ChecklistView checklist)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"# {checklist.Title}");
        sb.AppendLine();
        
        if (!string.IsNullOrEmpty(checklist.Description))
        {
            sb.AppendLine($"> {checklist.Description}");
            sb.AppendLine();
        }
        
        sb.AppendLine($"**Progress:** {checklist.CompletedItems}/{checklist.TotalItems} ({checklist.ProgressPercentage:F0}%)");
        sb.AppendLine($"**Status:** {checklist.Status}");
        sb.AppendLine($"**Created:** {checklist.Created:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"**Modified:** {checklist.Modified:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        
        if (checklist.Items.Any())
        {
            sb.AppendLine("## Items");
            sb.AppendLine();
            
            foreach (var item in checklist.Items)
            {
                var checkbox = item.IsCompleted ? "[x]" : "[ ]";
                sb.Append($"- {checkbox} {item.Text}");
                
                if (item.IsCompleted && item.CompletedAt.HasValue)
                {
                    sb.Append($" *(completed {item.CompletedAt.Value:yyyy-MM-dd}");
                    if (!string.IsNullOrEmpty(item.CompletedBy))
                    {
                        sb.Append($" by {item.CompletedBy}");
                    }
                    sb.Append(")*");
                }
                
                sb.AppendLine();
                
                if (!string.IsNullOrEmpty(item.Notes))
                {
                    sb.AppendLine($"  - Notes: {item.Notes}");
                }
                
                if (item.Files?.Any() == true)
                {
                    sb.AppendLine($"  - Files: {string.Join(", ", item.Files)}");
                }
            }
        }
        
        return sb.ToString();
    }
}

// Result models
public class CreateChecklistResult
{
    public bool Success { get; set; }
    public string? ChecklistId { get; set; }
    public string Message { get; set; } = "";
}

public class AddChecklistItemResult
{
    public bool Success { get; set; }
    public string? ItemId { get; set; }
    public int ItemOrder { get; set; }
    public string Message { get; set; } = "";
}

public class AddChecklistItemsResult
{
    public bool Success { get; set; }
    public List<AddedChecklistItem> AddedItems { get; set; } = new();
    public List<string> FailedItems { get; set; } = new();
    public int TotalAdded { get; set; }
    public int TotalFailed { get; set; }
    public string Message { get; set; } = "";
}

public class AddedChecklistItem
{
    public string ItemId { get; set; } = "";
    public string ItemText { get; set; } = "";
    public int ItemOrder { get; set; }
}

public class ChecklistItemInput
{
    public string ItemText { get; set; } = "";
    public string? Notes { get; set; }
    public string[]? RelatedFiles { get; set; }
    public Dictionary<string, JsonElement>? CustomFields { get; set; }
}

public class ToggleChecklistItemResult
{
    public bool Success { get; set; }
    public bool IsCompleted { get; set; }
    public string Message { get; set; } = "";
}

public class UpdateChecklistItemResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class ViewChecklistResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public ChecklistView? Checklist { get; set; }
    public string? MarkdownExport { get; set; }
}

public class ListChecklistsResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<ChecklistSummary> Checklists { get; set; } = new();
    public int TotalFound { get; set; }
}

// View models
public class ChecklistView
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public double ProgressPercentage { get; set; }
    public List<ChecklistItemView> Items { get; set; } = new();
}

public class ChecklistItemView
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public int Order { get; set; }
    public string? Notes { get; set; }
    public string[]? Files { get; set; }
}

public class ChecklistSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public bool IsShared { get; set; }
}