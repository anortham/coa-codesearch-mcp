using System.Text.Json;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers all checklist tools
/// </summary>
public static class ChecklistToolRegistrations
{
    /// <summary>
    /// Register all checklist tools
    /// </summary>
    public static void RegisterChecklistTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        var checklistTools = serviceProvider.GetRequiredService<ChecklistTools>();
        
        RegisterCreateChecklist(registry, checklistTools);
        RegisterAddChecklistItems(registry, checklistTools);
        RegisterToggleChecklistItem(registry, checklistTools);
        RegisterUpdateChecklistItem(registry, checklistTools);
        RegisterViewChecklist(registry, checklistTools);
        RegisterListChecklists(registry, checklistTools);
    }
    
    private static void RegisterCreateChecklist(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<CreateChecklistParams>(
            name: ToolNames.CreateChecklist,
            description: "Create a new persistent checklist that tracks tasks across sessions. Checklists can be personal or shared with the team.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "Title of the checklist" },
                    description = new { type = "string", description = "Optional description of what this checklist is for" },
                    isShared = new { type = "boolean", description = "Whether to share with team (default: true)", @default = true },
                    sessionId = new { type = "string", description = "Optional session ID for tracking" },
                    customFields = new { type = "object", description = "Optional custom fields as JSON object" }
                },
                required = new[] { "title" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.CreateChecklistAsync(
                    ValidateRequired(parameters.Title, "title"),
                    parameters.Description,
                    parameters.IsShared ?? true,
                    parameters.SessionId,
                    parameters.CustomFields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterAddChecklistItems(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<AddChecklistItemsParams>(
            name: ToolNames.AddChecklistItems,
            description: "Add one or more items to an existing checklist. Items are automatically ordered and linked to their parent checklist. Pass a single item in the array to add just one item.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    checklistId = new { type = "string", description = "ID of the checklist to add items to" },
                    items = new { 
                        type = "array", 
                        description = "Array of items to add (can be a single item)",
                        items = new {
                            type = "object",
                            properties = new
                            {
                                itemText = new { type = "string", description = "Text/description of the checklist item" },
                                notes = new { type = "string", description = "Optional notes or additional details" },
                                relatedFiles = new { type = "array", items = new { type = "string" }, description = "Files related to this item" },
                                customFields = new { type = "object", description = "Optional custom fields as JSON object" }
                            },
                            required = new[] { "itemText" }
                        }
                    }
                },
                required = new[] { "checklistId", "items" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                if (parameters.Items == null || parameters.Items.Length == 0) 
                    throw new InvalidParametersException("At least one item is required");
                
                var result = await tool.AddChecklistItemsAsync(
                    ValidateRequired(parameters.ChecklistId, "checklistId"),
                    parameters.Items);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterToggleChecklistItem(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<ToggleChecklistItemParams>(
            name: ToolNames.ToggleChecklistItem,
            description: "Toggle the completion status of a checklist item. Automatically updates parent checklist progress.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    itemId = new { type = "string", description = "ID of the checklist item to toggle" },
                    isCompleted = new { type = "boolean", description = "Optional: explicitly set completion status (if not provided, toggles current state)" },
                    completedBy = new { type = "string", description = "Optional: who completed the item" }
                },
                required = new[] { "itemId" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ToggleChecklistItemAsync(
                    ValidateRequired(parameters.ItemId, "itemId"),
                    parameters.IsCompleted,
                    parameters.CompletedBy);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterUpdateChecklistItem(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<UpdateChecklistItemParams>(
            name: ToolNames.UpdateChecklistItem,
            description: "Update the text, notes, or custom fields of a checklist item.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    itemId = new { type = "string", description = "ID of the checklist item to update" },
                    newText = new { type = "string", description = "New text for the item (optional)" },
                    notes = new { type = "string", description = "Updated notes (optional)" },
                    customFields = new { type = "object", description = "Custom fields to update as JSON object" }
                },
                required = new[] { "itemId" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.UpdateChecklistItemAsync(
                    ValidateRequired(parameters.ItemId, "itemId"),
                    parameters.NewText,
                    parameters.Notes,
                    parameters.CustomFields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterViewChecklist(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<ViewChecklistParams>(
            name: ToolNames.ViewChecklist,
            description: "View a checklist with all its items, progress tracking, and optional markdown export. Shows completion status, dates, and related files.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    checklistId = new { type = "string", description = "ID of the checklist to view" },
                    includeCompleted = new { type = "boolean", description = "Include completed items (default: true)", @default = true },
                    exportAsMarkdown = new { type = "boolean", description = "Export checklist as markdown (default: false)", @default = false }
                },
                required = new[] { "checklistId" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ViewChecklistAsync(
                    ValidateRequired(parameters.ChecklistId, "checklistId"),
                    parameters.IncludeCompleted ?? true,
                    parameters.ExportAsMarkdown ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterListChecklists(ToolRegistry registry, ChecklistTools tool)
    {
        registry.RegisterTool<ListChecklistsParams>(
            name: ToolNames.ListChecklists,
            description: "List all available checklists with summary information including progress and status.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    includeCompleted = new { type = "boolean", description = "Include completed checklists (default: true)", @default = true },
                    onlyShared = new { type = "boolean", description = "Only show shared/team checklists (default: false)", @default = false },
                    maxResults = new { type = "integer", description = "Maximum number of checklists to return (default: 50)", @default = 50 }
                },
                required = Array.Empty<string>()
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ListChecklistsAsync(
                    parameters?.IncludeCompleted ?? true,
                    parameters?.OnlyShared ?? false,
                    parameters?.MaxResults ?? 50);
                    
                return CreateSuccessResult(result);
            }
        );
    }
}

// Parameter models
public class CreateChecklistParams
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool? IsShared { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, JsonElement>? CustomFields { get; set; }
}

public class AddChecklistItemsParams
{
    public required string ChecklistId { get; set; }
    public required ChecklistItemInput[] Items { get; set; }
}

public class ToggleChecklistItemParams
{
    public required string ItemId { get; set; }
    public bool? IsCompleted { get; set; }
    public string? CompletedBy { get; set; }
}

public class UpdateChecklistItemParams
{
    public required string ItemId { get; set; }
    public string? NewText { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, JsonElement>? CustomFields { get; set; }
}

public class ViewChecklistParams
{
    public required string ChecklistId { get; set; }
    public bool? IncludeCompleted { get; set; }
    public bool? ExportAsMarkdown { get; set; }
}

public class ListChecklistsParams
{
    public bool? IncludeCompleted { get; set; }
    public bool? OnlyShared { get; set; }
    public int? MaxResults { get; set; }
}