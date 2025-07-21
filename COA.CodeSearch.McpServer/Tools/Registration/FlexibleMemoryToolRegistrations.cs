using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Directus.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers all flexible memory system tools
/// </summary>
public static class FlexibleMemoryToolRegistrations
{
    /// <summary>
    /// Register all flexible memory tools
    /// </summary>
    public static void RegisterFlexibleMemoryTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        var memoryTools = serviceProvider.GetRequiredService<FlexibleMemoryTools>();
        
        RegisterStoreMemory(registry, memoryTools);
        RegisterSearchMemories(registry, memoryTools);
        RegisterUpdateMemory(registry, memoryTools);
        RegisterStoreTechnicalDebt(registry, memoryTools);
        RegisterStoreQuestion(registry, memoryTools);
        RegisterStoreDeferredTask(registry, memoryTools);
        RegisterFindSimilarMemories(registry, memoryTools);
        RegisterArchiveMemories(registry, memoryTools);
        RegisterGetMemoryById(registry, memoryTools);
    }
    
    private static void RegisterStoreMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreMemoryParams>(
            name: "flexible_store_memory",
            description: "Store a memory with flexible schema supporting custom fields and types. Enhanced replacement for remember_* tools with full JSON field support.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string", description = "Memory type (TechnicalDebt, Question, ArchitecturalDecision, CodePattern, etc.)" },
                    content = new { type = "string", description = "Main content of the memory" },
                    isShared = new { type = "boolean", description = "Whether to share with team (default: true)", @default = true },
                    sessionId = new { type = "string", description = "Optional session ID" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    fields = new { type = "object", description = "Custom fields as JSON object (status, priority, tags, etc.)" }
                },
                required = new[] { "type", "content" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.StoreMemoryAsync(
                    ValidateRequired(parameters.Type, "type"),
                    ValidateRequired(parameters.Content, "content"),
                    parameters.IsShared ?? true,
                    parameters.SessionId,
                    parameters.Files,
                    parameters.Fields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterSearchMemories(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<SearchMemoriesParams>(
            name: "flexible_search_memories",
            description: "Advanced memory search with faceted filtering, temporal queries, and smart insights. Supports full-text search, type filtering, date ranges, custom field filters, and intelligent ranking with recent/frequent boosting.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query (* for all)" },
                    types = new { type = "array", items = new { type = "string" }, description = "Filter by memory types" },
                    dateRange = new { type = "string", description = "Relative time: 'last-week', 'last-month', 'last-7-days'" },
                    facets = new { type = "object", description = "Field filters (e.g., {\"status\": \"pending\", \"priority\": \"high\"})" },
                    orderBy = new { type = "string", description = "Sort field: 'created', 'modified', 'type', 'score', or custom field" },
                    orderDescending = new { type = "boolean", description = "Sort order (default: true)", @default = true },
                    maxResults = new { type = "integer", description = "Maximum results (default: 50)", @default = 50 },
                    includeArchived = new { type = "boolean", description = "Include archived memories (default: false)", @default = false },
                    boostRecent = new { type = "boolean", description = "Boost recently created memories", @default = false },
                    boostFrequent = new { type = "boolean", description = "Boost frequently accessed memories", @default = false }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.SearchMemoriesAsync(
                    parameters?.Query,
                    parameters?.Types,
                    parameters?.DateRange,
                    parameters?.Facets,
                    parameters?.OrderBy,
                    parameters?.OrderDescending ?? true,
                    parameters?.MaxResults ?? 50,
                    parameters?.IncludeArchived ?? false,
                    parameters?.BoostRecent ?? false,
                    parameters?.BoostFrequent ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterUpdateMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<UpdateMemoryParams>(
            name: "flexible_update_memory",
            description: "Update an existing memory's content and fields. Supports partial updates - only specified fields are modified.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "Memory ID to update" },
                    content = new { type = "string", description = "New content (optional)" },
                    fieldUpdates = new { type = "object", description = "Field updates (null values remove fields)" },
                    addFiles = new { type = "array", items = new { type = "string" }, description = "Files to add" },
                    removeFiles = new { type = "array", items = new { type = "string" }, description = "Files to remove" }
                },
                required = new[] { "id" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.UpdateMemoryAsync(
                    ValidateRequired(parameters.Id, "id"),
                    parameters.Content,
                    parameters.FieldUpdates,
                    parameters.AddFiles,
                    parameters.RemoveFiles);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterStoreTechnicalDebt(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreTechnicalDebtParams>(
            name: "flexible_store_technical_debt",
            description: "Store a technical debt item with predefined fields optimized for debt tracking and prioritization.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    description = new { type = "string", description = "Description of the technical debt" },
                    status = new { type = "string", description = "Status: pending, in-progress, done, deferred" },
                    priority = new { type = "string", description = "Priority: low, medium, high, critical" },
                    category = new { type = "string", description = "Category: performance, security, maintainability, etc." },
                    estimatedHours = new { type = "integer", description = "Estimated hours to resolve" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Tags for categorization" }
                },
                required = new[] { "description" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.StoreTechnicalDebtAsync(
                    ValidateRequired(parameters.Description, "description"),
                    parameters.Status,
                    parameters.Priority,
                    parameters.Category,
                    parameters.EstimatedHours,
                    parameters.Files,
                    parameters.Tags);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterStoreQuestion(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreQuestionParams>(
            name: "flexible_store_question",
            description: "Store a question with context and tracking status for later follow-up and resolution.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    question = new { type = "string", description = "The question to store" },
                    context = new { type = "string", description = "Additional context about the question" },
                    status = new { type = "string", description = "Status: open, answered, investigating" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Tags for categorization" }
                },
                required = new[] { "question" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.StoreQuestionAsync(
                    ValidateRequired(parameters.Question, "question"),
                    parameters.Context,
                    parameters.Status,
                    parameters.Files,
                    parameters.Tags);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterStoreDeferredTask(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreDeferredTaskParams>(
            name: "flexible_store_deferred_task",
            description: "Store a deferred task with reason and optional defer-until date for future processing.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    task = new { type = "string", description = "The deferred task" },
                    reason = new { type = "string", description = "Reason for deferring" },
                    deferredUntil = new { type = "string", description = "ISO date when task should be reconsidered" },
                    priority = new { type = "string", description = "Priority: low, medium, high, critical" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    blockedBy = new { type = "array", items = new { type = "string" }, description = "Dependencies blocking this task" }
                },
                required = new[] { "task", "reason" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                DateTime? deferredUntil = null;
                if (!string.IsNullOrEmpty(parameters.DeferredUntil))
                {
                    if (DateTime.TryParse(parameters.DeferredUntil, out var parsed))
                    {
                        deferredUntil = parsed;
                    }
                }
                
                var result = await tool.StoreDeferredTaskAsync(
                    ValidateRequired(parameters.Task, "task"),
                    ValidateRequired(parameters.Reason, "reason"),
                    deferredUntil,
                    parameters.Priority,
                    parameters.Files,
                    parameters.BlockedBy);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterFindSimilarMemories(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<FindSimilarMemoriesParams>(
            name: "flexible_find_similar_memories",
            description: "Find memories with similar content using 'More Like This' algorithm - ideal for discovering related memories, duplicate content, or similar patterns.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryId = new { type = "string", description = "ID of the source memory to find similar to" },
                    maxResults = new { type = "integer", description = "Maximum similar memories to return (default: 10)", @default = 10 }
                },
                required = new[] { "memoryId" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.FindSimilarMemoriesAsync(
                    ValidateRequired(parameters.MemoryId, "memoryId"),
                    parameters.MaxResults ?? 10);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterArchiveMemories(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<ArchiveMemoriesParams>(
            name: "flexible_archive_memories",
            description: "Archive old memories of a specific type to reduce clutter while preserving them for future reference.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string", description = "Memory type to archive" },
                    daysOld = new { type = "integer", description = "Archive memories older than this many days" }
                },
                required = new[] { "type", "daysOld" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ArchiveMemoriesAsync(
                    ValidateRequired(parameters.Type, "type"),
                    ValidatePositive(parameters.DaysOld, "daysOld"));
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterGetMemoryById(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<GetMemoryByIdParams>(
            name: "flexible_get_memory",
            description: "Retrieve a specific memory by its ID with all associated fields and metadata.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "Memory ID to retrieve" }
                },
                required = new[] { "id" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.GetMemoryByIdAsync(
                    ValidateRequired(parameters.Id, "id"));
                    
                return CreateSuccessResult(result);
            }
        );
    }
}

// Parameter classes

public class StoreMemoryParams
{
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public bool? IsShared { get; set; }
    public string? SessionId { get; set; }
    public string[]? Files { get; set; }
    public Dictionary<string, JsonElement>? Fields { get; set; }
}

public class SearchMemoriesParams
{
    public string? Query { get; set; }
    public string[]? Types { get; set; }
    public string? DateRange { get; set; }
    public Dictionary<string, string>? Facets { get; set; }
    public string? OrderBy { get; set; }
    public bool? OrderDescending { get; set; }
    public int? MaxResults { get; set; }
    public bool? IncludeArchived { get; set; }
    public bool? BoostRecent { get; set; }
    public bool? BoostFrequent { get; set; }
}

public class UpdateMemoryParams
{
    public string Id { get; set; } = "";
    public string? Content { get; set; }
    public Dictionary<string, JsonElement?>? FieldUpdates { get; set; }
    public string[]? AddFiles { get; set; }
    public string[]? RemoveFiles { get; set; }
}

public class StoreTechnicalDebtParams
{
    public string Description { get; set; } = "";
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Category { get; set; }
    public int? EstimatedHours { get; set; }
    public string[]? Files { get; set; }
    public string[]? Tags { get; set; }
}

public class StoreQuestionParams
{
    public string Question { get; set; } = "";
    public string? Context { get; set; }
    public string? Status { get; set; }
    public string[]? Files { get; set; }
    public string[]? Tags { get; set; }
}

public class StoreDeferredTaskParams
{
    public string Task { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? DeferredUntil { get; set; }
    public string? Priority { get; set; }
    public string[]? Files { get; set; }
    public string[]? BlockedBy { get; set; }
}

public class FindSimilarMemoriesParams
{
    public string MemoryId { get; set; } = "";
    public int? MaxResults { get; set; }
}

public class ArchiveMemoriesParams
{
    public string Type { get; set; } = "";
    public int DaysOld { get; set; }
}

public class GetMemoryByIdParams
{
    public string Id { get; set; } = "";
}