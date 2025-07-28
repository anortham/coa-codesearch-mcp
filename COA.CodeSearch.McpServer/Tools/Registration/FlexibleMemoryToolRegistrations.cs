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
        var memorySearchV2 = serviceProvider.GetRequiredService<FlexibleMemorySearchToolV2>();
        var timelineTool = serviceProvider.GetRequiredService<TimelineTool>();
        
        // Core memory operations
        RegisterStoreMemory(registry, memoryTools);
        RegisterSearchMemoriesV2(registry, memorySearchV2);
        RegisterUpdateMemory(registry, memoryTools);
        RegisterGetMemoryById(registry, memoryTools);
        
        // Memory relationships - handled by MemoryLinkingToolRegistrations
        
        // Intelligent features
        RegisterStoreWorkingMemory(registry, memoryTools);
        RegisterFindSimilarMemories(registry, memoryTools);
        RegisterSummarizeMemories(registry, memoryTools);
        RegisterGetMemorySuggestions(registry, memoryTools);
        
        // Management
        RegisterArchiveMemories(registry, memoryTools);
        RegisterMemoryDashboard(registry, memoryTools);
        RegisterTimeline(registry, timelineTool);
        
        // Templates
        RegisterListTemplates(registry, memoryTools);
        RegisterCreateFromTemplate(registry, memoryTools);
        
        // Git integration
        RegisterStoreGitCommitMemory(registry, memoryTools);
        
        // File context
        RegisterGetMemoriesForFile(registry, memoryTools);
    }

    // Helper methods for dynamic parameter access
    private static bool? GetBooleanProperty(dynamic? obj, string propertyName)
    {
        if (obj == null) return null;
        try
        {
            // Try reflection first
            var property = obj.GetType().GetProperty(propertyName);
            if (property != null)
            {
                var value = property.GetValue(obj);
                if (value is bool boolValue) return boolValue;
            }
            
            // Try dynamic access as fallback
            var dynamicValue = ((dynamic)obj)[propertyName];
            if (dynamicValue is bool dynamicBoolValue) return dynamicBoolValue;
        }
        catch
        {
            // Ignore property access errors
        }
        return null;
    }

    private static int? GetIntegerProperty(dynamic? obj, string propertyName)
    {
        if (obj == null) return null;
        try
        {
            // Try reflection first
            var property = obj.GetType().GetProperty(propertyName);
            if (property != null)
            {
                var value = property.GetValue(obj);
                if (value is int intValue) return intValue;
            }
            
            // Try dynamic access as fallback
            var dynamicValue = ((dynamic)obj)[propertyName];
            if (dynamicValue is int dynamicIntValue) return dynamicIntValue;
        }
        catch
        {
            // Ignore property access errors
        }
        return null;
    }
    
    private static void RegisterStoreMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreMemoryParams>(
            name: ToolNames.StoreMemory,
            description: @"Stores knowledge permanently in searchable memory system.
Returns: Created memory with ID and metadata.
Prerequisites: None - memory system is always available.
Error handling: Returns VALIDATION_ERROR if memoryType is invalid or content is empty.
Use cases: Architectural decisions, technical debt, code patterns, project insights.
Not for: Temporary notes (use store_temporary_memory), file storage (use Write tool).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryType = new { type = "string", description = "Memory type (TechnicalDebt, Question, ArchitecturalDecision, CodePattern, etc.)" },
                    content = new { type = "string", description = "Main content of the memory" },
                    isShared = new { type = "boolean", description = "Whether to share with team (default: true)", @default = true },
                    sessionId = new { type = "string", description = "Optional session ID" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    fields = new { 
                        type = "object", 
                        additionalProperties = true, 
                        description = "Custom fields as JSON object (importance, urgency, category, etc.)",
                        examples = new object[]
                        {
                            new { priority = "high", category = "security", effort = "days" },
                            new { importance = "critical", impact = "high", urgency = "immediate" },
                            new { complexity = "medium", owner = "backend-team", deadline = "2024-02-15" }
                        }
                    }
                },
                required = new[] { "memoryType", "content" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.StoreMemoryAsync(
                    ValidateRequired(parameters.MemoryType, "memoryType"),
                    ValidateRequired(parameters.Content, "content"),
                    parameters.IsShared ?? true,
                    parameters.SessionId,
                    parameters.Files,
                    parameters.Fields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterStoreWorkingMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreWorkingMemoryParams>(
            name: ToolNames.StoreTemporaryMemory,
            description: "Store notes that AUTO-DELETE after session ends (or specified time). Only use for temporary reminders like 'check this file later' or 'user wants feature X'. For important knowledge that should persist (architectural decisions, technical debt), use store_memory instead.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    content = new { type = "string", description = "Content of the working memory (what you want to remember)" },
                    expiresIn = new { type = "string", description = "Expiration time: 'end-of-session' (default), '1h', '4h', '24h', '7d', etc." },
                    sessionId = new { type = "string", description = "Optional session ID (auto-generated if not provided)" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files" },
                    fields = new { 
                        type = "object", 
                        additionalProperties = true, 
                        description = "Custom fields as JSON object (importance, urgency, category, etc.)",
                        examples = new object[]
                        {
                            new { importance = "reminder", urgency = "low" },
                            new { context = "debugging", session = "investigation-2024-01" },
                            new { reminder = "check tomorrow", priority = "medium" }
                        }
                    }
                },
                required = new[] { "content" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.StoreWorkingMemoryAsync(
                    ValidateRequired(parameters.Content, "content"),
                    parameters.ExpiresIn,
                    parameters.SessionId,
                    parameters.Files,
                    parameters.Fields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterSearchMemoriesV2(ToolRegistry registry, FlexibleMemorySearchToolV2 tool)
    {
        registry.RegisterTool<SearchMemoriesV2Params>(
            name: ToolNames.SearchMemories,
            description: @"Searches stored memories with intelligent query expansion.
Returns: Matching memories with scores, metadata, and relationships.
Prerequisites: None - searches existing memory database.
Error handling: Returns empty results if no matches found.
Use cases: Finding past decisions, reviewing technical debt, discovering patterns.
Features: Query expansion, context awareness, faceted filtering, smart ranking.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query (* for all)" },
                    types = new { type = "array", items = new { type = "string" }, description = "Filter by memory types" },
                    dateRange = new { type = "string", description = "Relative time: 'last-week', 'last-month', 'last-7-days'" },
                    facets = new { type = "object", additionalProperties = true, description = "Field filters (e.g., {\"status\": \"pending\", \"priority\": \"high\"})" },
                    orderBy = new { type = "string", description = "Sort field: 'created', 'modified', 'type', 'score', or custom field" },
                    orderDescending = new { type = "boolean", description = "Sort order (default: true)", @default = true },
                    maxResults = new { type = "integer", description = "Maximum results (default: 50)", @default = 50 },
                    includeArchived = new { type = "boolean", description = "Include archived memories (default: false)", @default = false },
                    boostRecent = new { type = "boolean", description = "Boost recently created memories", @default = false },
                    boostFrequent = new { type = "boolean", description = "Boost frequently accessed memories", @default = false },
                    // New intelligent features
                    enableQueryExpansion = new { type = "boolean", description = "Enable automatic query expansion with synonyms and related terms", @default = true },
                    enableContextAwareness = new { type = "boolean", description = "Enable context-aware memory boosting based on current work", @default = true },
                    currentFile = new { type = "string", description = "Path to current file being worked on (for context awareness)" },
                    recentFiles = new { type = "array", items = new { type = "string" }, description = "Recently accessed files (for context awareness)" },
                    mode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'", @default = "summary" },
                    // Highlighting parameters
                    enableHighlighting = new { type = "boolean", description = "Enable highlighting for search results", @default = false },
                    maxFragments = new { type = "integer", description = "Maximum number of highlight fragments per field", @default = 3 },
                    fragmentSize = new { type = "integer", description = "Size of highlight fragments in characters", @default = 100 },
                    detailRequest = new 
                    { 
                        type = "object", 
                        description = "Optional detail request for cached data",
                        properties = new
                        {
                            detailLevel = new { type = "string" },
                            detailRequestToken = new { type = "string" }
                        }
                    }
                }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync(
                    parameters?.Query,
                    parameters?.Types,
                    parameters?.DateRange,
                    parameters?.Facets,
                    parameters?.OrderBy,
                    parameters?.OrderDescending ?? true,
                    parameters?.MaxResults ?? 50,
                    parameters?.IncludeArchived ?? false,
                    parameters?.BoostRecent ?? false,
                    parameters?.BoostFrequent ?? false,
                    // New intelligent features
                    parameters?.EnableQueryExpansion ?? true,
                    parameters?.EnableContextAwareness ?? true,
                    parameters?.CurrentFile,
                    parameters?.RecentFiles,
                    Enum.TryParse<ResponseMode>(parameters?.Mode, true, out var mode) ? mode : ResponseMode.Summary,
                    // Highlighting parameters
                    GetBooleanProperty(parameters, "enableHighlighting") ?? false,
                    GetIntegerProperty(parameters, "maxFragments") ?? 3,
                    GetIntegerProperty(parameters, "fragmentSize") ?? 100,
                    parameters?.DetailRequest,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterUpdateMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<UpdateMemoryParams>(
            name: ToolNames.UpdateMemory,
            description: "Update a memory's content, status, or custom fields. Supports partial updates - only the fields you specify are changed. Use to mark technical debt as resolved, update status, or add new information.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "Memory ID to update" },
                    content = new { type = "string", description = "New content (optional)" },
                    fieldUpdates = new { type = "object", additionalProperties = true, description = "Field updates (null values remove fields)" },
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
    
    private static void RegisterFindSimilarMemories(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<FindSimilarMemoriesParams>(
            name: ToolNames.FindSimilarMemories,
            description: "Find memories with similar content to a given memory. Uses semantic analysis to discover related architectural decisions, duplicate technical debt, or similar code patterns.",
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
            name: ToolNames.ArchiveMemories,
            description: "Archive old memories by type and age to reduce clutter. Archived memories are preserved but excluded from regular searches. Use to clean up resolved technical debt or old work sessions.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryType = new { type = "string", description = "Memory type to archive" },
                    daysOld = new { type = "integer", description = "Archive memories older than this many days" }
                },
                required = new[] { "memoryType", "daysOld" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ArchiveMemoriesAsync(
                    ValidateRequired(parameters.MemoryType, "memoryType"),
                    ValidatePositive(parameters.DaysOld, "daysOld"));
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterGetMemoryById(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<GetMemoryByIdParams>(
            name: ToolNames.GetMemory,
            description: "Retrieve a memory by its ID. Returns full content, metadata, related files, custom fields, and relationship information.",
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
    
    private static void RegisterSummarizeMemories(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<SummarizeMemoriesParams>(
            name: ToolNames.SummarizeMemories,
            description: "Create condensed summaries of old memories by type and time period. Extracts key themes, most referenced files, and type-specific insights (e.g., resolution rates for technical debt).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryType = new { type = "string", description = "Memory type to summarize" },
                    daysOld = new { type = "integer", description = "Summarize memories older than this many days" },
                    batchSize = new { type = "integer", description = "Number of memories to summarize together (default: 10)", @default = 10 },
                    preserveOriginals = new { type = "boolean", description = "Keep original memories after summarization (default: false)", @default = false }
                },
                required = new[] { "memoryType", "daysOld" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.SummarizeMemoriesAsync(
                    ValidateRequired(parameters.MemoryType, "memoryType"),
                    ValidatePositive(parameters.DaysOld, "daysOld"),
                    parameters.BatchSize ?? 10,
                    parameters.PreserveOriginals ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterMemoryDashboard(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<object>(
            name: ToolNames.MemoryDashboard,
            description: "Get memory system dashboard with statistics, health checks, and insights. Shows total memories, type distribution, recent activity, storage info, and health recommendations.",
            inputSchema: new
            {
                type = "object",
                properties = new { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.GetMemoryDashboardAsync();
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterListTemplates(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<ListTemplatesParams>(
            name: ToolNames.ListMemoryTemplates,
            description: "List available memory templates for common scenarios. Templates provide structured formats for code reviews, performance issues, security findings, and architectural decisions.",
            inputSchema: new
            {
                type = "object",
                properties = new { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ListTemplatesAsync();
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterCreateFromTemplate(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<CreateFromTemplateParams>(
            name: ToolNames.CreateMemoryFromTemplate,
            description: "Create a memory using a predefined template. Templates ensure consistent structure for common scenarios like security findings or architectural decisions. Use list_memory_templates to see available options.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    templateId = new { type = "string", description = "Template ID (use flexible_list_templates to see available templates)" },
                    placeholders = new { type = "object", additionalProperties = true, description = "Key-value pairs for template placeholders" },
                    files = new { type = "array", items = new { type = "string" }, description = "Related files (optional)" },
                    additionalFields = new { type = "object", additionalProperties = true, description = "Additional custom fields (optional)" }
                },
                required = new[] { "templateId", "placeholders" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.CreateFromTemplateAsync(
                    ValidateRequired(parameters.TemplateId, "templateId"),
                    parameters.Placeholders,
                    parameters.Files,
                    parameters.AdditionalFields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterGetMemorySuggestions(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<GetMemorySuggestionsParams>(
            name: ToolNames.GetMemorySuggestions,
            description: "Get contextual suggestions for the current work. Analyzes what you're working on and suggests relevant existing memories, appropriate templates, and recommended actions.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    currentContext = new { type = "string", description = "Description of what you're currently working on" },
                    currentFile = new { type = "string", description = "Path to the file you're currently working on (optional)" },
                    recentFiles = new { type = "array", items = new { type = "string" }, description = "List of recently accessed files (optional)" },
                    maxSuggestions = new { type = "integer", description = "Maximum number of suggestions to return (default: 5)", @default = 5 }
                },
                required = new[] { "currentContext" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.GetMemorySuggestionsAsync(
                    ValidateRequired(parameters.CurrentContext, "currentContext"),
                    parameters.CurrentFile,
                    parameters.RecentFiles,
                    parameters.MaxSuggestions ?? 5);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterStoreGitCommitMemory(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<StoreGitCommitMemoryParams>(
            name: ToolNames.StoreGitCommitMemory,
            description: "Store a memory linked to a specific Git commit SHA. Use to track architectural decisions, important bug fixes, or insights tied to specific code changes.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    sha = new { type = "string", description = "Git commit SHA" },
                    message = new { type = "string", description = "Git commit message" },
                    description = new { type = "string", description = "Additional description or insights about this commit" },
                    author = new { type = "string", description = "Commit author (optional)" },
                    commitDate = new { type = "string", description = "Commit date in ISO format (optional)" },
                    filesChanged = new { type = "array", items = new { type = "string" }, description = "Files changed in this commit (optional)" },
                    branch = new { type = "string", description = "Branch name (optional)" },
                    additionalFields = new { type = "object", additionalProperties = true, description = "Additional custom fields (optional)" }
                },
                required = new[] { "sha", "message", "description" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                DateTime? commitDate = null;
                if (!string.IsNullOrEmpty(parameters.CommitDate))
                {
                    if (DateTime.TryParse(parameters.CommitDate, out var parsedDate))
                    {
                        commitDate = parsedDate;
                    }
                }
                
                var result = await tool.StoreGitCommitMemoryAsync(
                    ValidateRequired(parameters.Sha, "sha"),
                    ValidateRequired(parameters.Message, "message"),
                    ValidateRequired(parameters.Description, "description"),
                    parameters.Author,
                    commitDate,
                    parameters.FilesChanged,
                    parameters.Branch,
                    parameters.AdditionalFields);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterTimeline(ToolRegistry registry, TimelineTool tool)
    {
        registry.RegisterTool<TimelineParams>(
            name: ToolNames.MemoryTimeline,
            description: "View memories in chronological timeline format. Groups by time periods (Today, Yesterday, This Week). Perfect for understanding recent work and project history with user-friendly visualization.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    days = new { type = "integer", description = "Number of days to include (default: 7)", @default = 7 },
                    types = new { type = "array", items = new { type = "string" }, description = "Filter by memory types" },
                    includeArchived = new { type = "boolean", description = "Include archived memories (default: false)", @default = false },
                    includeExpired = new { type = "boolean", description = "Include expired working memories (default: false)", @default = false },
                    maxPerGroup = new { type = "integer", description = "Maximum memories per time group (default: 10)", @default = 10 }
                }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.GetTimelineAsync(
                    parameters?.Days ?? 7,
                    parameters?.Types,
                    parameters?.IncludeArchived ?? false,
                    parameters?.IncludeExpired ?? false,
                    parameters?.MaxPerGroup ?? 10);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterGetMemoriesForFile(ToolRegistry registry, FlexibleMemoryTools tool)
    {
        registry.RegisterTool<GetMemoriesForFileParams>(
            name: ToolNames.GetMemoriesForFile,
            description: "Find all memories related to a specific file. Shows architectural decisions, technical debt, code patterns, and project insights associated with the file path.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the file (absolute or relative)" },
                    includeArchived = new { type = "boolean", description = "Include archived memories (default: false)", @default = false }
                },
                required = new[] { "filePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.GetMemoriesForFileAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.IncludeArchived ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
}

// Parameter classes

public class StoreMemoryParams
{
    public string MemoryType { get; set; } = "";
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

public class SearchMemoriesV2Params
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
    // New intelligent features
    public bool? EnableQueryExpansion { get; set; } = true;
    public bool? EnableContextAwareness { get; set; } = true;
    public string? CurrentFile { get; set; }
    public string[]? RecentFiles { get; set; }
    public string? Mode { get; set; } = "summary";
    public DetailRequest? DetailRequest { get; set; }
}

public class UpdateMemoryParams
{
    public string Id { get; set; } = "";
    public string? Content { get; set; }
    public Dictionary<string, JsonElement?>? FieldUpdates { get; set; }
    public string[]? AddFiles { get; set; }
    public string[]? RemoveFiles { get; set; }
}

public class FindSimilarMemoriesParams
{
    public string MemoryId { get; set; } = "";
    public int? MaxResults { get; set; }
}

public class ArchiveMemoriesParams
{
    public string MemoryType { get; set; } = "";
    public int DaysOld { get; set; }
}

public class GetMemoryByIdParams
{
    public string Id { get; set; } = "";
}

public class SummarizeMemoriesParams
{
    public string MemoryType { get; set; } = "";
    public int DaysOld { get; set; }
    public int? BatchSize { get; set; }
    public bool? PreserveOriginals { get; set; }
}

public class ListTemplatesParams
{
}

public class CreateFromTemplateParams
{
    public string TemplateId { get; set; } = "";
    public Dictionary<string, string> Placeholders { get; set; } = new();
    public string[]? Files { get; set; }
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public class GetMemorySuggestionsParams
{
    public string CurrentContext { get; set; } = "";
    public string? CurrentFile { get; set; }
    public string[]? RecentFiles { get; set; }
    public int? MaxSuggestions { get; set; }
}

public class StoreWorkingMemoryParams
{
    public string Content { get; set; } = "";
    public string? ExpiresIn { get; set; }
    public string? SessionId { get; set; }
    public string[]? Files { get; set; }
    public Dictionary<string, JsonElement>? Fields { get; set; }
}

public class StoreGitCommitMemoryParams
{
    public string Sha { get; set; } = "";
    public string Message { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Author { get; set; }
    public string? CommitDate { get; set; }
    public string[]? FilesChanged { get; set; }
    public string? Branch { get; set; }
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public class GetMemoriesForFileParams
{
    public string FilePath { get; set; } = "";
    public bool? IncludeArchived { get; set; }
}

public class TimelineParams
{
    public int? Days { get; set; }
    public string[]? Types { get; set; }
    public bool? IncludeArchived { get; set; }
    public bool? IncludeExpired { get; set; }
    public int? MaxPerGroup { get; set; }
}