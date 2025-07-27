using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers all tools with the MCP server
/// </summary>
public static class AllToolRegistrations
{
    /// <summary>
    /// Register all available tools
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        // Text search tools
        RegisterFastTextSearchV2(registry, serviceProvider.GetRequiredService<FastTextSearchToolV2>());
        RegisterFastFileSearchV2(registry, serviceProvider.GetRequiredService<FastFileSearchToolV2>());
        RegisterFastRecentFiles(registry, serviceProvider.GetRequiredService<FastRecentFilesTool>());
        RegisterFastFileSizeAnalysis(registry, serviceProvider.GetRequiredService<FastFileSizeAnalysisTool>());
        RegisterFastSimilarFiles(registry, serviceProvider.GetRequiredService<FastSimilarFilesTool>());
        RegisterFastDirectorySearch(registry, serviceProvider.GetRequiredService<FastDirectorySearchTool>());
        RegisterIndexWorkspace(registry, serviceProvider.GetRequiredService<IndexWorkspaceTool>());
        
        // Claude Memory System tools - only essential tools that don't have flexible equivalents
        var memoryTools = serviceProvider.GetRequiredService<ClaudeMemoryTools>();
        RegisterRecallContext(registry, memoryTools);
        RegisterBackupRestore(registry, memoryTools);
        
        // Flexible Memory System tools
        FlexibleMemoryToolRegistrations.RegisterFlexibleMemoryTools(registry, serviceProvider);
        
        // Memory Linking tools
        MemoryLinkingToolRegistrations.RegisterAll(registry, serviceProvider.GetRequiredService<MemoryLinkingTools>());
        
        // Checklist tools
        ChecklistToolRegistrations.RegisterChecklistTools(registry, serviceProvider);
        
        // Batch operations (for multi-search efficiency)
        RegisterBatchOperationsV2(registry, serviceProvider.GetRequiredService<BatchOperationsToolV2>());
        
        // Logging control tool
        RegisterSetLogging(registry, serviceProvider.GetRequiredService<SetLoggingTool>());
        
        // Version information tool
        RegisterGetVersion(registry, serviceProvider.GetRequiredService<GetVersionTool>());
        
        // Index health check tool
        RegisterIndexHealthCheck(registry, serviceProvider.GetRequiredService<IndexHealthCheckTool>());
        
        // System health check tool
        RegisterSystemHealthCheck(registry, serviceProvider.GetRequiredService<SystemHealthCheckTool>());
    }

    private static void RegisterBatchOperationsV2(ToolRegistry registry, BatchOperationsToolV2 tool)
    {
        registry.RegisterTool<BatchOperationsV2Params>(
            name: ToolNames.BatchOperations,
            description: "Execute multiple code analysis operations in parallel for comprehensive insights. Combines results across different analysis types, identifies patterns, and suggests next steps. Faster than running operations sequentially. Supported: text_search, file_search, recent_files, similar_files, directory_search.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    operations = new 
                    { 
                        type = "array", 
                        items = new { type = "object" },
                        description = "Array of operations to execute. Format: [{\"operation\": \"text_search\", \"query\": \"TODO\"}, {\"operation\": \"file_search\", \"query\": \"*.cs\"}]. Each operation must have 'operation' field plus operation-specific parameters."
                    },
                    workspacePath = new { type = "string", description = "Default workspace path for operations" },
                    mode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'", @default = "summary" },
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
                },
                required = new[] { "operations" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                if (parameters.Operations.ValueKind == JsonValueKind.Undefined || 
                    (parameters.Operations.ValueKind == JsonValueKind.Array && parameters.Operations.GetArrayLength() == 0))
                    throw new InvalidParametersException("operations are required and cannot be empty");
                
                try
                {
                    var result = await tool.ExecuteAsync(
                        parameters.Operations, 
                        parameters.WorkspacePath,
                        Enum.TryParse<ResponseMode>(parameters.Mode, true, out var mode) ? mode : ResponseMode.Summary,
                        parameters.DetailRequest,
                        ct);
                        
                    return CreateSuccessResult(result);
                }
                catch (JsonException ex)
                {
                    throw new InvalidParametersException($"Invalid JSON in operations: {ex.Message}");
                }
            }
        );
    }

    // Parameter classes
    private class BatchOperationsV2Params
    {
        public JsonElement Operations { get; set; }
        public string? WorkspacePath { get; set; }
        public string? Mode { get; set; } = "summary";
        public DetailRequest? DetailRequest { get; set; }
    }
    
    private static void RegisterFastTextSearchV2(ToolRegistry registry, FastTextSearchToolV2 tool)
    {
        registry.RegisterTool<FastTextSearchV2Params>(
            name: ToolNames.TextSearch,
            description: "Search for text content within files across the codebase. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Use when looking for specific strings, error messages, configuration values, or any text that appears in code, comments, or documentation.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")" },
                    workspacePath = new { type = "string", description = "Directory path to search in (e.g., C:\\MyProject). Always use the project root directory. To search in specific folders, use the filePattern parameter instead of passing subdirectories." },
                    filePattern = new { type = "string", description = "Optional: Filter by file pattern (e.g., '*.cs' for C# files, 'src/**/*.js' for JavaScript in src)" },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Limit to specific file types (e.g., ['.cs', '.js', '.json'])" },
                    contextLines = new { type = "integer", description = "Optional: Show N lines before/after each match for context (default: 0)" },
                    maxResults = new { type = "integer", description = "Maximum number of results", @default = 50 },
                    caseSensitive = new { type = "boolean", description = "Case sensitive search", @default = false },
                    searchType = new { type = "string", description = "Optional: Search mode - 'standard' (default), 'wildcard' (with *), 'fuzzy' (approximate), 'phrase' (exact)", @default = "standard" },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "query", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = ResponseMode.Summary;  // Default to summary for AI optimization
                if (!string.IsNullOrWhiteSpace(parameters.ResponseMode))
                {
                    mode = parameters.ResponseMode.ToLowerInvariant() switch
                    {
                        "full" => ResponseMode.Full,
                        "summary" => ResponseMode.Summary,
                        _ => ResponseMode.Summary
                    };
                }
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Query, "query"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.FilePattern,
                    parameters.Extensions,
                    parameters.ContextLines,
                    parameters.MaxResults ?? 50,
                    parameters.CaseSensitive ?? false,
                    parameters.SearchType ?? "standard",
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastTextSearchV2Params
    {
        public string? Query { get; set; }
        public string? WorkspacePath { get; set; }
        public string? FilePattern { get; set; }
        public string[]? Extensions { get; set; }
        public int? ContextLines { get; set; }
        public int? MaxResults { get; set; }
        public bool? CaseSensitive { get; set; }
        public string? SearchType { get; set; }
        public string? ResponseMode { get; set; }
    }
    

    private static void RegisterFastFileSearchV2(ToolRegistry registry, FastFileSearchToolV2 tool)
    {
        registry.RegisterTool<FastFileSearchV2Params>(
            name: ToolNames.FileSearch,
            description: "Find files by name when you know the filename but not the exact location. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Use when looking for specific files, especially with typos or partial names (e.g., find 'UserService.cs' by searching 'UserServ').",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "File name to search for - examples: 'UserService' (contains), 'UserSrvc~' (fuzzy), 'User*.cs' (wildcard), '^User' (regex start)" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    searchType = new { type = "string", description = "Search mode: 'standard' (default), 'fuzzy' (UserSrvc finds UserService), 'wildcard' (User*), 'exact' (exact match), 'regex' (/pattern/)", @default = "standard" },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 50 },
                    includeDirectories = new { type = "boolean", description = "Include directory names in search", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "query", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    _ => ResponseMode.Summary
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Query, "query"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.SearchType ?? "standard",
                    parameters.MaxResults ?? 50,
                    parameters.IncludeDirectories ?? false,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    

    private class FastFileSearchV2Params
    {
        public string? Query { get; set; }
        public string? WorkspacePath { get; set; }
        public string? SearchType { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeDirectories { get; set; }
        public string? ResponseMode { get; set; }
    }
    
    private static void RegisterFastRecentFiles(ToolRegistry registry, FastRecentFilesTool tool)
    {
        registry.RegisterTool<FastRecentFilesParams>(
            name: ToolNames.RecentFiles,
            description: "Find files that were recently changed within a time period. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Use when resuming work after a break, reviewing recent changes, or finding files that were worked on today/this week.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    timeFrame = new { type = "string", description = "Time frame: '30m', '24h' (default), '7d', '4w' for minutes, hours, days, weeks", @default = "24h" },
                    filePattern = new { type = "string", description = "Optional: Filter by file pattern (e.g., '*.cs', 'src/**/*.ts')" },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Filter by file extensions" },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 50 },
                    includeSize = new { type = "boolean", description = "Include file size information", @default = true }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.TimeFrame ?? "24h",
                    parameters.FilePattern,
                    parameters.Extensions,
                    parameters.MaxResults ?? 50,
                    parameters.IncludeSize ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastRecentFilesParams
    {
        public string? WorkspacePath { get; set; }
        public string? TimeFrame { get; set; }
        public string? FilePattern { get; set; }
        public string[]? Extensions { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeSize { get; set; }
    }
    
    private static void RegisterFastFileSizeAnalysis(ToolRegistry registry, FastFileSizeAnalysisTool tool)
    {
        registry.RegisterTool<FastFileSizeAnalysisParams>(
            name: ToolNames.FileSizeAnalysis,
            description: "Analyze files by size - find large files, empty files, or analyze size distributions. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Uses indexed data for instant results across entire codebase.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to analyze" },
                    mode = new { type = "string", description = "Analysis mode: 'largest' (default), 'smallest', 'range', 'zero', 'distribution'", @default = "largest" },
                    minSize = new { type = "integer", description = "Minimum file size in bytes (for 'range' mode)" },
                    maxSize = new { type = "integer", description = "Maximum file size in bytes (for 'range' mode)" },
                    filePattern = new { type = "string", description = "Optional: Filter by file pattern" },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Filter by extensions" },
                    maxResults = new { type = "integer", description = "Maximum results", @default = 50 },
                    includeAnalysis = new { type = "boolean", description = "Include size distribution analysis", @default = true }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.Mode ?? "largest",
                    parameters.MinSize,
                    parameters.MaxSize,
                    parameters.FilePattern,
                    parameters.Extensions,
                    parameters.MaxResults ?? 50,
                    parameters.IncludeAnalysis ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastFileSizeAnalysisParams
    {
        public string? WorkspacePath { get; set; }
        public string? Mode { get; set; }
        public long? MinSize { get; set; }
        public long? MaxSize { get; set; }
        public string? FilePattern { get; set; }
        public string[]? Extensions { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeAnalysis { get; set; }
    }
    
    private static void RegisterFastSimilarFiles(ToolRegistry registry, FastSimilarFilesTool tool)
    {
        registry.RegisterTool<FastSimilarFilesParams>(
            name: ToolNames.SimilarFiles,
            description: "Find files with similar content using 'More Like This' algorithm - ideal for discovering duplicate code, related implementations, or similar patterns. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Shows similarity scores and matching terms.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    sourceFilePath = new { type = "string", description = "Path to the source file to find similar files for" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    maxResults = new { type = "integer", description = "Maximum similar files to return", @default = 10 },
                    minTermFreq = new { type = "integer", description = "Min times a term must appear in source", @default = 2 },
                    minDocFreq = new { type = "integer", description = "Min docs a term must appear in", @default = 2 },
                    minWordLength = new { type = "integer", description = "Minimum word length to consider", @default = 4 },
                    maxWordLength = new { type = "integer", description = "Maximum word length to consider", @default = 30 },
                    excludeExtensions = new { type = "array", items = new { type = "string" }, description = "File extensions to exclude" },
                    includeScore = new { type = "boolean", description = "Include similarity scores", @default = true }
                },
                required = new[] { "sourceFilePath", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.SourceFilePath, "sourceFilePath"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.MaxResults ?? 10,
                    parameters.MinTermFreq ?? 2,
                    parameters.MinDocFreq ?? 2,
                    parameters.MinWordLength ?? 4,
                    parameters.MaxWordLength ?? 30,
                    parameters.ExcludeExtensions,
                    parameters.IncludeScore ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastSimilarFilesParams
    {
        public string? SourceFilePath { get; set; }
        public string? WorkspacePath { get; set; }
        public int? MaxResults { get; set; }
        public int? MinTermFreq { get; set; }
        public int? MinDocFreq { get; set; }
        public int? MinWordLength { get; set; }
        public int? MaxWordLength { get; set; }
        public string[]? ExcludeExtensions { get; set; }
        public bool? IncludeScore { get; set; }
    }
    
    private static void RegisterFastDirectorySearch(ToolRegistry registry, FastDirectorySearchTool tool)
    {
        registry.RegisterTool<FastDirectorySearchParams>(
            name: ToolNames.DirectorySearch,
            description: "Search for directories/folders with fuzzy matching - locate project folders, discover structure, find namespaces. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Shows file counts and supports typo correction.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Directory name to search for - examples: 'Services' (contains), 'Servces~' (fuzzy match), 'User*' (wildcard), 'src/*/models' (pattern)" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    searchType = new { type = "string", description = "Search mode: 'standard' (default), 'fuzzy' (UserSrvc finds UserService), 'wildcard' (User*), 'exact' (exact match), 'regex' (/pattern/)", @default = "standard" },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 30 },
                    includeFileCount = new { type = "boolean", description = "Include file count per directory", @default = true },
                    groupByDirectory = new { type = "boolean", description = "Group results by unique directories", @default = true }
                },
                required = new[] { "query", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Query, "query"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.SearchType ?? "standard",
                    parameters.MaxResults ?? 30,
                    parameters.IncludeFileCount ?? true,
                    parameters.GroupByDirectory ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastDirectorySearchParams
    {
        public string? Query { get; set; }
        public string? WorkspacePath { get; set; }
        public string? SearchType { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeFileCount { get; set; }
        public bool? GroupByDirectory { get; set; }
    }
    
    private static void RegisterIndexWorkspace(ToolRegistry registry, IndexWorkspaceTool tool)
    {
        registry.RegisterTool<IndexWorkspaceParams>(
            name: ToolNames.IndexWorkspace,
            description: "REQUIRED FIRST STEP: Build search index to enable text searches. You MUST run this before using text_search, file_search, recent_files, and other indexed search tools - they will fail without it. One-time setup per workspace, then searches are instant.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Directory path to index (e.g., C:\\MyProject or C:\\source\\MyRepo). Must be a directory, not a file path." },
                    forceRebuild = new { type = "boolean", description = "Force rebuild even if index exists (default: false)" }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync(
                    parameters?.WorkspacePath ?? "",
                    parameters?.ForceRebuild ?? false,
                    ct);
                
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class IndexWorkspaceParams
    {
        public string? WorkspacePath { get; set; }
        public bool? ForceRebuild { get; set; }
    }
    
    private static void RegisterSetLogging(ToolRegistry registry, SetLoggingTool tool)
    {
        registry.RegisterTool<SetLoggingParams>(
            name: ToolNames.LogDiagnostics,
            description: "View and manage log files. Logs are written to .codesearch/logs directory. Actions: status, list, cleanup",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    action = new { 
                        type = "string", 
                        description = "Action to perform: 'status', 'list', 'cleanup'",
                        @enum = new[] { "status", "list", "cleanup" }
                    },
                    cleanup = new { 
                        type = "boolean", 
                        description = "For 'cleanup' action: set to true to confirm deletion of old log files" 
                    }
                },
                required = new[] { "action" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Action, "action"),
                    null, // level parameter removed - configuration-driven now
                    parameters.Cleanup,
                    ct);
                
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class SetLoggingParams
    {
        public string? Action { get; set; }
        public bool? Cleanup { get; set; }
    }
    
    private static void RegisterGetVersion(ToolRegistry registry, GetVersionTool tool)
    {
        registry.RegisterTool<object>(
            name: ToolNames.GetVersion,
            description: "Get the version and build information of the running MCP server. Shows version number, build date, runtime info, and helps identify if running code matches edited code.",
            inputSchema: new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync();
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRecallContext(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RecallContextParams>(
            name: ToolNames.RecallContext,
            description: "Load relevant project knowledge from previous sessions including architectural decisions, code patterns, and insights. Searches stored memories based on your current work context. Recommended at session start to restore context from past work.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "What you're currently working on or want to learn about" },
                    scopeFilter = new { type = "string", description = "Filter by type: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight, WorkSession, LocalInsight" },
                    maxResults = new { type = "integer", description = "Maximum number of results to return (default: 10)", @default = 10 }
                },
                required = new[] { "query" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                MemoryScope? scopeFilter = null;
                if (!string.IsNullOrEmpty(parameters.ScopeFilter))
                {
                    if (System.Enum.TryParse<MemoryScope>(parameters.ScopeFilter, out var scope))
                    {
                        scopeFilter = scope;
                    }
                }
                
                var result = await tool.RecallContext(
                    ValidateRequired(parameters.Query, "query"),
                    scopeFilter,
                    parameters.MaxResults ?? 10);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBackupRestore(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<BackupMemoriesParams>(
            name: ToolNames.BackupMemories,
            description: "Export memories to JSON file for version control and team sharing. Creates timestamped, human-readable backups. Use cases: commit to git for team collaboration, backup before major changes, transfer knowledge to new machines. By default backs up only project memories (ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight). Use includeLocal=true to include personal memories.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    scopes = new { type = "array", items = new { type = "string" }, description = "Memory types to backup. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight" },
                    includeLocal = new { type = "boolean", description = "Include local developer memories (WorkSession, LocalInsight). Default: false", @default = false }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.BackupMemories(
                    parameters?.Scopes,
                    parameters?.IncludeLocal ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
        
        registry.RegisterTool<RestoreMemoriesParams>(
            name: ToolNames.RestoreMemories,
            description: "Restore memories from JSON backup file. Automatically finds most recent backup if no file specified. Useful when setting up on a new machine or after losing the Lucene index. By default restores only project-level memories.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    scopes = new { type = "array", items = new { type = "string" }, description = "Memory types to restore. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight" },
                    includeLocal = new { type = "boolean", description = "Include local developer memories (WorkSession, LocalInsight). Default: false", @default = false }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.RestoreMemories(
                    parameters?.Scopes,
                    parameters?.IncludeLocal ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class RecallContextParams
    {
        public string? Query { get; set; }
        public string? ScopeFilter { get; set; }
        public int? MaxResults { get; set; }
    }
    
    private class BackupMemoriesParams
    {
        public string[]? Scopes { get; set; }
        public bool? IncludeLocal { get; set; }
    }
    
    private class RestoreMemoriesParams
    {
        public string[]? Scopes { get; set; }
        public bool? IncludeLocal { get; set; }
    }
    
    private static void RegisterIndexHealthCheck(ToolRegistry registry, IndexHealthCheckTool tool)
    {
        registry.RegisterTool<IndexHealthCheckParams>(
            name: ToolNames.IndexHealthCheck,
            description: "Perform comprehensive health check of Lucene indexes with detailed metrics, circuit breaker status, and recommendations. Returns status, diagnostics, performance metrics, and actionable recommendations.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    includeMetrics = new 
                    { 
                        type = "boolean", 
                        description = "Include performance metrics in the response", 
                        @default = true 
                    },
                    includeCircuitBreakerStatus = new 
                    { 
                        type = "boolean", 
                        description = "Include circuit breaker status for all operations", 
                        @default = true 
                    },
                    includeAutoRepair = new
                    {
                        type = "boolean",
                        description = "Automatically attempt to repair corrupted indexes when detected",
                        @default = false
                    }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync(
                    parameters?.IncludeMetrics ?? true,
                    parameters?.IncludeCircuitBreakerStatus ?? true,
                    parameters?.IncludeAutoRepair ?? false,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class IndexHealthCheckParams
    {
        public bool? IncludeMetrics { get; set; }
        public bool? IncludeCircuitBreakerStatus { get; set; }
        public bool? IncludeAutoRepair { get; set; }
    }

    private static void RegisterSystemHealthCheck(ToolRegistry registry, SystemHealthCheckTool tool)
    {
        registry.RegisterTool<SystemHealthCheckParams>(
            name: ToolNames.SystemHealthCheck,
            description: "Perform comprehensive system health check covering all major services and components. Includes memory pressure, index health, circuit breakers, system metrics, and configuration validation with overall assessment and recommendations.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    includeIndexHealth = new 
                    { 
                        type = "boolean", 
                        description = "Include index health status in the response", 
                        @default = true 
                    },
                    includeMemoryPressure = new 
                    { 
                        type = "boolean", 
                        description = "Include memory pressure monitoring data", 
                        @default = true 
                    },
                    includeSystemMetrics = new 
                    { 
                        type = "boolean", 
                        description = "Include system performance metrics", 
                        @default = true 
                    },
                    includeConfiguration = new
                    {
                        type = "boolean",
                        description = "Include configuration status and validation",
                        @default = false
                    }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync(
                    parameters?.IncludeIndexHealth ?? true,
                    parameters?.IncludeMemoryPressure ?? true,
                    parameters?.IncludeSystemMetrics ?? true,
                    parameters?.IncludeConfiguration ?? false,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class SystemHealthCheckParams
    {
        public bool? IncludeIndexHealth { get; set; }
        public bool? IncludeMemoryPressure { get; set; }
        public bool? IncludeSystemMetrics { get; set; }
        public bool? IncludeConfiguration { get; set; }
    }
}