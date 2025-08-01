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
        // RegisterFastTextSearchV2(registry, serviceProvider.GetRequiredService<FastTextSearchToolV2>()); // COMMENTED OUT - Migrated to attribute-based registration
        // RegisterFastFileSearchV2(registry, serviceProvider.GetRequiredService<FastFileSearchToolV2>()); // COMMENTED OUT - Migrated to attribute-based registration
        // RegisterFastRecentFiles(registry, serviceProvider.GetRequiredService<FastRecentFilesTool>()); // COMMENTED OUT - Migrated to attribute-based registration
        // RegisterFastFileSizeAnalysis(registry, serviceProvider.GetRequiredService<FastFileSizeAnalysisTool>()); // COMMENTED OUT - Migrated to attribute-based registration
        // RegisterFastSimilarFiles(registry, serviceProvider.GetRequiredService<FastSimilarFilesTool>()); // COMMENTED OUT - Migrated to attribute-based registration
        RegisterFastDirectorySearch(registry, serviceProvider.GetRequiredService<FastDirectorySearchTool>());
        // RegisterIndexWorkspace(registry, serviceProvider.GetRequiredService<IndexWorkspaceTool>()); // COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        
        // Claude Memory System tools - only essential tools that don't have flexible equivalents
        var memoryTools = serviceProvider.GetRequiredService<ClaudeMemoryTools>();
        RegisterRecallContext(registry, memoryTools);
        RegisterBackupRestore(registry, memoryTools);
        
        // Essential Memory Tools (only 6 tools exposed as per design)
        RegisterEssentialMemoryTools(registry, serviceProvider);
        
        // Batch operations (for multi-search efficiency)
        RegisterBatchOperationsV2(registry, serviceProvider.GetRequiredService<BatchOperationsToolV2>());
        
        // Logging control tool - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        // RegisterSetLogging(registry, serviceProvider.GetRequiredService<SetLoggingTool>());
        
        // Version information tool - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        // RegisterGetVersion(registry, serviceProvider.GetRequiredService<GetVersionTool>());
        
        // Index health check tool - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION  
        // RegisterIndexHealthCheck(registry, serviceProvider.GetRequiredService<IndexHealthCheckTool>());
        
        // System health check tool - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        // RegisterSystemHealthCheck(registry, serviceProvider.GetRequiredService<SystemHealthCheckTool>());
        
        // AI-optimized search tools
        RegisterSearchAssistant(registry, serviceProvider.GetRequiredService<SearchAssistantTool>());
        RegisterPatternDetector(registry, serviceProvider.GetRequiredService<PatternDetectorTool>());
        RegisterMemoryGraphNavigator(registry, serviceProvider.GetRequiredService<MemoryGraphNavigatorTool>());
        
        // Tool usage analytics
        RegisterToolUsageAnalytics(registry, serviceProvider.GetRequiredService<ToolUsageAnalyticsTool>());
        
        // Workflow discovery - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        // RegisterWorkflowDiscovery(registry, serviceProvider.GetRequiredService<WorkflowDiscoveryTool>());
        
        // Phase 3: Advanced tools now handled by RegisterEssentialMemoryTools
        
        
        // AI Context loading - COMMENTED OUT TO TEST ATTRIBUTE REGISTRATION
        // RegisterLoadContext(registry, serviceProvider.GetRequiredService<LoadContextTool>());
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
                        items = new { type = "object", additionalProperties = true },
                        description = "Array of operations to execute. Format: [{\"operation\": \"text_search\", \"query\": \"TODO\"}, {\"operation\": \"file_search\", \"query\": \"*.cs\"}]. Each operation must have 'operation' field plus operation-specific parameters."
                    },
                    workspacePath = new { type = "string", description = "Default workspace path for operations" },
                    mode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'", @default = "summary" },
                    detailRequest = new 
                    { 
                        type = "object", 
                        description = @"Request more details from a previous summary response.
Example: After getting a summary with 150 results, use the provided 
detailRequestToken to get full results.

Usage:
1. First call returns summary with metadata.detailRequestToken
2. Second call with detailRequest gets additional data",
                        
                        properties = new
                        {
                            detailLevel = new { 
                                type = "string", 
                                @enum = new[] { "full", "next50", "hotspots" },
                                description = "Level of detail: full (all results), next50 (next batch), hotspots (high-concentration files)"
                            },
                            detailRequestToken = new { 
                                type = "string",
                                description = "Token from metadata.detailRequestToken in previous response" 
                            }
                        },
                        
                        examples = new[] {
                            new {
                                detailLevel = "full",
                                detailRequestToken = "cache_123abc_1706332800"
                            }
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
            description: @"Searches file contents for text patterns (literals, wildcards, regex).
Returns: File paths with line numbers and optional context.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Finding code patterns, error messages, TODOs, configuration values.
Not for: File name searches (use file_search), directory searches (use directory_search).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")" },
                    searchQuery = new { type = "string", description = "[DEPRECATED] Use 'query' instead. Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")" },
                    workspacePath = new { type = "string", description = "Directory path to search in (e.g., C:\\MyProject). Always use the project root directory. To search in specific folders, use the filePattern parameter instead of passing subdirectories." },
                    filePattern = new { 
                        type = "string", 
                        description = @"Glob pattern to filter files. 
Syntax:
- '*.cs' = all C# files
- 'src/**/*.js' = all JS files under src/
- '*Test.cs' = files ending with Test.cs
- '!*.min.js' = exclude minified JS files
Uses minimatch patterns: * (any chars), ** (any dirs), ? (single char), [abc] (char set)",
                        examples = new[] { "*.cs", "src/**/*.ts", "*Test.*", "!node_modules/**" }
                    },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Limit to specific file types (e.g., ['.cs', '.js', '.json'])" },
                    contextLines = new { 
                        type = "integer", 
                        description = @"Lines of context before/after matches. 
Token impact: ~100 tokens per result with context=3.
Example: 50 results with context=3 ≈ 5,000 tokens",
                        @default = 0
                    },
                    maxResults = new { type = "integer", description = "Maximum number of results", @default = 50 },
                    caseSensitive = new { type = "boolean", description = "Case sensitive search", @default = false },
                    searchType = new { 
                        type = "string",
                        @enum = new[] { "standard", "fuzzy", "wildcard", "phrase", "regex" },
                        description = @"Search algorithm:
- standard: Exact substring match (case-insensitive by default)
- fuzzy: Approximate match allowing typos (append ~ to terms)  
- wildcard: Pattern matching with * and ?
- phrase: Exact phrase in quotes
- regex: Full regex support with capturing groups",
                        examples = new[] { "getUserName", "getUserNam~", "get*Name", "\"get user name\"", "get\\w+Name" },
                        @default = "standard" 
                    },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Validate that at least one query parameter is provided
                var query = parameters.GetQuery();
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new InvalidParametersException("Either 'query' or 'searchQuery' parameter is required");
                }
                
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
                    query,
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
        public string? SearchQuery { get; set; } // Backward compatibility
        public string? WorkspacePath { get; set; }
        public string? FilePattern { get; set; }
        public string[]? Extensions { get; set; }
        public int? ContextLines { get; set; }
        public int? MaxResults { get; set; }
        public bool? CaseSensitive { get; set; }
        public string? SearchType { get; set; }
        public string? ResponseMode { get; set; }
        
        public string? GetQuery() => Query ?? SearchQuery;
    }

    private static void RegisterFastFileSearchV2(ToolRegistry registry, FastFileSearchToolV2 tool)
    {
        registry.RegisterTool<FastFileSearchV2Params>(
            name: ToolNames.FileSearch,
            description: @"Finds files by name patterns with fuzzy matching support.
Returns: File paths sorted by relevance score.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Locating specific files, finding files with typos, discovering file patterns.
Not for: Text content searches (use text_search), directory searches (use directory_search).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "File name to search for - examples: 'UserService' (contains), 'UserSrvc~' (fuzzy), 'User*.cs' (wildcard), '.*Test.*.cs' (regex)" },
                    nameQuery = new { type = "string", description = "[DEPRECATED] Use 'query' instead. File name to search for - examples: 'UserService' (contains), 'UserSrvc~' (fuzzy), 'User*.cs' (wildcard), '.*Test.*.cs' (regex)" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    searchType = new { 
                        type = "string",
                        @enum = new[] { "standard", "fuzzy", "wildcard", "exact", "regex" },
                        description = @"Search algorithm for file names:
- standard: Contains match (UserService matches UserService.cs)
- fuzzy: Typo-tolerant (UserSrvc~ finds UserService.cs)
- wildcard: Pattern matching (User*.cs finds UserService.cs)
- exact: Exact filename match
- regex: Regular expressions on relative paths - examples:
  * '.*Test.*\.cs' - files with 'Test' in name
  * '.*Service\.cs' - files ending with 'Service.cs'
  * 'Tests\\.*' - files in Tests folders
  * '.*\\Services\\.*' - files in Services folders",
                        examples = new[] { "UserService", "UserSrvc~", "User*.cs", "UserService.cs", "^User.*Service\\.cs$" },
                        @default = "standard" 
                    },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 50 },
                    includeDirectories = new { type = "boolean", description = "Include directory names in search", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Validate that at least one query parameter is provided
                var query = parameters.GetQuery();
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new InvalidParametersException("Either 'query' or 'nameQuery' parameter is required");
                }
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    _ => ResponseMode.Summary
                };
                
                var result = await tool.ExecuteAsync(
                    query,
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
        public string? NameQuery { get; set; } // Backward compatibility
        public string? WorkspacePath { get; set; }
        public string? SearchType { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeDirectories { get; set; }
        public string? ResponseMode { get; set; }
        
        public string? GetQuery() => Query ?? NameQuery;
    }
    
    private static void RegisterFastRecentFiles(ToolRegistry registry, FastRecentFilesTool tool)
    {
        registry.RegisterTool<FastRecentFilesParams>(
            name: ToolNames.RecentFiles,
            description: @"Finds files modified within specified time periods.
Returns: File paths sorted by modification time with size information.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Resuming work after breaks, reviewing recent changes, tracking daily progress.
Not for: Content searches (use text_search), finding specific files (use file_search).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    timeFrame = new { 
                        type = "string", 
                        description = @"Time period for recent changes.
Format: number + unit (m=minutes, h=hours, d=days, w=weeks)
Examples: '30m' = 30 minutes, '24h' = 24 hours, '7d' = 7 days, '4w' = 4 weeks",
                        examples = new[] { "30m", "1h", "24h", "3d", "7d", "2w", "4w" },
                        @default = "24h" 
                    },
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
            description: @"Analyzes files by size with distribution insights.
Returns: File paths with sizes, grouped by analysis mode.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Finding large files, identifying empty files, understanding codebase distribution.
Not for: Content analysis (use text_search), recent changes (use recent_files).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to analyze" },
                    mode = new { 
                        type = "string",
                        @enum = new[] { "largest", "smallest", "range", "zero", "distribution" },
                        description = @"Analysis mode:
- largest: Find biggest files (default)
- smallest: Find smallest non-empty files
- range: Files within size bounds (requires minSize/maxSize)
- zero: Find empty files
- distribution: Size distribution statistics",
                        @default = "largest" 
                    },
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
            description: @"Finds files with similar content using 'More Like This' algorithm.
Returns: File paths with similarity scores and matching terms.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Finding duplicate code, discovering related implementations, identifying patterns.
Not for: Exact text matches (use text_search), file name searches (use file_search).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    sourcePath = new { type = "string", description = "Path to the source file to find similar files for" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    maxResults = new { type = "integer", description = "Maximum similar files to return", @default = 10 },
                    minTermFreq = new { type = "integer", description = "Min times a term must appear in source", @default = 2 },
                    minDocFreq = new { type = "integer", description = "Min docs a term must appear in", @default = 2 },
                    minWordLength = new { type = "integer", description = "Minimum word length to consider", @default = 4 },
                    maxWordLength = new { type = "integer", description = "Maximum word length to consider", @default = 30 },
                    excludeExtensions = new { type = "array", items = new { type = "string" }, description = "File extensions to exclude" },
                    includeScore = new { type = "boolean", description = "Include similarity scores", @default = true }
                },
                required = new[] { "sourcePath", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.SourcePath, "sourcePath"),
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
        public string? SourcePath { get; set; }
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
            description: @"Searches for directories/folders with fuzzy matching support.
Returns: Directory paths with file counts and structure information.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Exploring project structure, finding namespaces, locating module folders.
Not for: File searches (use file_search), text content searches (use text_search).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Directory name to search for - examples: 'Services' (contains), 'Servces~' (fuzzy match), 'User*' (wildcard), 'src/*/models' (pattern)" },
                    directoryQuery = new { type = "string", description = "[DEPRECATED] Use 'query' instead. Directory name to search for - examples: 'Services' (contains), 'Servces~' (fuzzy match), 'User*' (wildcard), 'src/*/models' (pattern)" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    searchType = new { 
                        type = "string",
                        @enum = new[] { "standard", "fuzzy", "wildcard", "exact", "regex" },
                        description = @"Search algorithm for directory names:
- standard: Contains match (Service matches Services/)
- fuzzy: Typo-tolerant (Servces~ finds Services/)
- wildcard: Pattern matching (User* finds UserService/)
- exact: Exact directory name match
- regex: Regular expressions (^src/.*/models$)",
                        examples = new[] { "Services", "Servces~", "User*", "Services", "^src/.*/models$" },
                        @default = "standard" 
                    },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 30 },
                    includeFileCount = new { type = "boolean", description = "Include file count per directory", @default = true },
                    groupByDirectory = new { type = "boolean", description = "Group results by unique directories", @default = true }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Validate that at least one query parameter is provided
                var query = parameters.GetQuery();
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new InvalidParametersException("Either 'query' or 'directoryQuery' parameter is required");
                }
                
                var result = await tool.ExecuteAsync(
                    query,
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
        public string? DirectoryQuery { get; set; } // Backward compatibility
        public string? WorkspacePath { get; set; }
        public string? SearchType { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeFileCount { get; set; }
        public bool? GroupByDirectory { get; set; }
        
        public string? GetQuery() => Query ?? DirectoryQuery;
    }
    
    private static void RegisterIndexWorkspace(ToolRegistry registry, IndexWorkspaceTool tool)
    {
        registry.RegisterTool<IndexWorkspaceParams>(
            name: ToolNames.IndexWorkspace,
            description: @"Creates search index for a workspace directory.
Returns: Index statistics including file count, size, and build time.
Prerequisites: None - this is the first step for all search operations.
Error handling: Returns DIRECTORY_NOT_FOUND if path doesn't exist.
Use cases: Initial workspace setup, re-indexing after major changes.
Important: One-time operation per workspace - subsequent searches use the index.",
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
                properties = new { }
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
                }
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
                }
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
                }
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
                }
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

    private static void RegisterSearchAssistant(ToolRegistry registry, SearchAssistantTool tool)
    {
        registry.RegisterTool<SearchAssistantParams>(
            name: ToolNames.SearchAssistant,
            description: @"Orchestrates multi-step search operations while maintaining context.
Analyzes search goals, creates search strategies, executes multiple search operations, discovers patterns and insights, finds related content, and provides actionable next steps.
Returns: Structured findings with strategy, insights, and resource URI for continued exploration.
Use cases: Complex code discovery, pattern analysis, architecture understanding, debugging workflows.
AI-optimized: Provides guided search assistance with context preservation.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    goal = new 
                    { 
                        type = "string", 
                        description = "The goal or objective of the search operation (e.g., 'Find all error handling patterns', 'Locate authentication code', 'Understand data flow')" 
                    },
                    workspacePath = new 
                    { 
                        type = "string", 
                        description = "Directory path to search in (e.g., C:\\MyProject). Use the project root directory." 
                    },
                    constraints = new
                    {
                        type = "object",
                        description = "Optional constraints to limit search scope",
                        properties = new
                        {
                            fileTypes = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "File types to include (e.g., ['cs', 'ts', 'js'])"
                            },
                            excludePaths = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Paths to exclude from search"
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of results per operation",
                                @default = 50
                            }
                        }
                    },
                    previousContext = new
                    {
                        type = "string",
                        description = "Resource URI from a previous search to build upon (enables context preservation)"
                    },
                    responseMode = new 
                    { 
                        type = "string", 
                        description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary for large results.", 
                        @default = "summary" 
                    }
                },
                required = new[] { "goal", "workspacePath" }
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

                var constraints = parameters.Constraints != null ? new SearchConstraints
                {
                    FileTypes = parameters.Constraints.FileTypes?.ToList(),
                    ExcludePaths = parameters.Constraints.ExcludePaths?.ToList(),
                    MaxResults = parameters.Constraints.MaxResults
                } : null;
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Goal, "goal"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    constraints,
                    parameters.PreviousContext,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class SearchAssistantParams
    {
        public string? Goal { get; set; }
        public string? WorkspacePath { get; set; }
        public SearchConstraintsParams? Constraints { get; set; }
        public string? PreviousContext { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class SearchConstraintsParams
    {
        public string[]? FileTypes { get; set; }
        public string[]? ExcludePaths { get; set; }
        public int? MaxResults { get; set; }
    }

    private static void RegisterPatternDetector(ToolRegistry registry, PatternDetectorTool tool)
    {
        registry.RegisterTool<PatternDetectorParams>(
            name: ToolNames.PatternDetector,
            description: @"Analyzes files by size with distribution insights.
Detects architectural patterns, security vulnerabilities, performance issues, and testing patterns.
Returns: Detailed analysis of patterns and anti-patterns with severity levels and remediation guidance.
Use cases: Code quality assessment, security audits, architecture reviews, technical debt identification.
AI-optimized: Provides actionable insights with confidence scores and prioritized recommendations.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new 
                    { 
                        type = "string", 
                        description = "Directory path to analyze (e.g., C:\\MyProject). Use the project root directory." 
                    },
                    patternTypes = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "string",
                            @enum = new[] { "architecture", "security", "performance", "testing" }
                        },
                        description = "Types of patterns to detect (e.g., ['architecture', 'security'])"
                    },
                    depth = new
                    {
                        type = "string",
                        @enum = new[] { "shallow", "deep" },
                        description = "Analysis depth: 'shallow' for quick scan, 'deep' for comprehensive analysis",
                        @default = "shallow"
                    },
                    createMemories = new
                    {
                        type = "boolean",
                        description = "Automatically create memories for significant findings",
                        @default = false
                    },
                    responseMode = new 
                    { 
                        type = "string", 
                        description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary for large results.", 
                        @default = "summary" 
                    }
                },
                required = new[] { "workspacePath", "patternTypes" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = ResponseMode.Summary;
                if (!string.IsNullOrWhiteSpace(parameters.ResponseMode))
                {
                    mode = parameters.ResponseMode.ToLowerInvariant() switch
                    {
                        "full" => ResponseMode.Full,
                        "summary" => ResponseMode.Summary,
                        _ => ResponseMode.Summary
                    };
                }

                var patternTypes = parameters.PatternTypes?.Select(pt => Enum.Parse<PatternType>(pt, true)).ToArray() ?? new PatternType[0];
                var depth = Enum.TryParse<PatternDepth>(parameters.Depth, true, out var parsedDepth) ? parsedDepth : PatternDepth.Shallow;
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    patternTypes,
                    depth,
                    parameters.CreateMemories ?? false,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class PatternDetectorParams
    {
        public string? WorkspacePath { get; set; }
        public string[]? PatternTypes { get; set; }
        public string? Depth { get; set; }
        public bool? CreateMemories { get; set; }
        public string? ResponseMode { get; set; }
    }

    private static void RegisterMemoryGraphNavigator(ToolRegistry registry, MemoryGraphNavigatorTool tool)
    {
        registry.RegisterTool<MemoryGraphNavigatorParams>(
            name: ToolNames.MemoryGraphNavigator,
            description: @"Explores memory relationships and dependencies with graph visualization.
Maps connections between memories to understand knowledge structure and dependencies.
Returns: Interactive graph showing memory nodes, relationships, clusters, and insights.
Use cases: Understanding project knowledge structure, finding related memories, identifying knowledge gaps.
AI-optimized: Provides relationship strength, clusters themes, and suggests exploration paths.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    startPoint = new 
                    { 
                        type = "string", 
                        description = "Memory ID or search query to start graph exploration from (e.g., memory ID or 'authentication patterns')" 
                    },
                    depth = new
                    {
                        type = "integer",
                        description = "Maximum depth of relationship traversal (1-5)",
                        @default = 2,
                        minimum = 1,
                        maximum = 5
                    },
                    filterTypes = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Filter by specific memory types (e.g., ['TechnicalDebt', 'ArchitecturalDecision'])"
                    },
                    includeOrphans = new
                    {
                        type = "boolean",
                        description = "Include memories with no relationships in the graph",
                        @default = false
                    },
                    responseMode = new 
                    { 
                        type = "string", 
                        description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary for large graphs.", 
                        @default = "summary" 
                    }
                },
                required = new[] { "startPoint" }
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
                    ValidateRequired(parameters.StartPoint, "startPoint"),
                    parameters.Depth ?? 2,
                    parameters.FilterTypes,
                    parameters.IncludeOrphans ?? false,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class MemoryGraphNavigatorParams
    {
        public string? StartPoint { get; set; }
        public int? Depth { get; set; }
        public string[]? FilterTypes { get; set; }
        public bool? IncludeOrphans { get; set; }
        public string? ResponseMode { get; set; }
    }

    private static void RegisterToolUsageAnalytics(ToolRegistry registry, ToolUsageAnalyticsTool tool)
    {
        registry.RegisterTool<ToolUsageAnalyticsParams>(
            name: ToolNames.ToolUsageAnalytics,
            description: @"View tool usage analytics, performance metrics, and workflow patterns.
Provides insights into tool effectiveness, usage patterns, error analysis, and optimization recommendations.
Returns: Analytics data including usage statistics, performance metrics, and actionable insights.
Use cases: Understanding tool performance, optimizing workflows, identifying issues, tracking usage patterns.
AI-optimized: Provides intelligent recommendations and workflow optimization suggestions.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    action = new
                    {
                        type = "string",
                        @enum = new[] { "summary", "detailed", "tool_specific", "export", "reset" },
                        description = @"Analytics action to perform:
- summary: High-level analytics overview
- detailed: Complete analytics data with all metrics
- tool_specific: Analytics for a specific tool (requires toolName)
- export: Export all analytics data as JSON
- reset: Clear all analytics data",
                        @default = "summary"
                    },
                    toolName = new
                    {
                        type = "string",
                        description = "Name of specific tool to analyze (required for tool_specific action)"
                    },
                    responseMode = new
                    {
                        type = "string",
                        description = "Response mode: 'summary' (default) or 'full'",
                        @default = "summary"
                    }
                }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = ResponseMode.Summary;
                if (!string.IsNullOrWhiteSpace(parameters.ResponseMode))
                {
                    mode = parameters.ResponseMode.ToLowerInvariant() switch
                    {
                        "full" => ResponseMode.Full,
                        "summary" => ResponseMode.Summary,
                        _ => ResponseMode.Summary
                    };
                }

                var action = AnalyticsAction.Summary;
                if (!string.IsNullOrWhiteSpace(parameters.Action))
                {
                    action = parameters.Action.ToLowerInvariant() switch
                    {
                        "summary" => AnalyticsAction.Summary,
                        "detailed" => AnalyticsAction.Detailed,
                        "tool_specific" => AnalyticsAction.ToolSpecific,
                        "export" => AnalyticsAction.Export,
                        "reset" => AnalyticsAction.Reset,
                        _ => AnalyticsAction.Summary
                    };
                }
                
                var result = await tool.ExecuteAsync(
                    action,
                    parameters.ToolName,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private class ToolUsageAnalyticsParams
    {
        public string? Action { get; set; }
        public string? ToolName { get; set; }
        public string? ResponseMode { get; set; }
    }

    private static void RegisterWorkflowDiscovery(ToolRegistry registry, WorkflowDiscoveryTool tool)
    {
        registry.RegisterTool<WorkflowDiscoveryParams>(
            name: ToolNames.WorkflowDiscovery,
            description: @"Discover workflow dependencies and suggested tool chains.
Provides AI agents with proactive understanding of tool prerequisites and common workflows.
Returns: Workflow information with dependencies, steps, and use cases.
Use cases: Understanding tool dependencies, discovering workflow patterns, getting guidance on tool chains.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    toolName = new { type = "string", description = "Get workflow information for a specific tool (optional)" },
                    goal = new { type = "string", description = "Get workflows for a specific goal like 'search code' or 'analyze patterns' (optional)" }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.GetWorkflowsAsync(parameters?.ToolName, parameters?.Goal);
                return CreateSuccessResult(result);
            }
        );
    }

    private class WorkflowDiscoveryParams
    {
        public string? ToolName { get; set; }
        public string? Goal { get; set; }
    }

    private static void RegisterLoadContext(ToolRegistry registry, LoadContextTool tool)
    {
        registry.RegisterTool<LoadContextParams>(
            name: ToolNames.LoadContext,
            description: @"Load AI working context with relevant memories for directory or session.
Automatically loads and ranks memories by relevance for the current working environment.
Returns: Organized memories (primary/secondary/available), insights, and suggested actions.
Use cases: Starting work sessions, resuming projects, getting contextual memory overview.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workingDirectory = new { type = "string", description = "Directory to load context for (defaults to current directory)" },
                    sessionId = new { type = "string", description = "Session ID to load session-specific memories (optional)" },
                    refreshCache = new { type = "boolean", description = "Force refresh cached context (default: false)" }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.ExecuteAsync(
                    parameters?.WorkingDirectory,
                    parameters?.SessionId,
                    parameters?.RefreshCache ?? false,
                    ct);
                return CreateSuccessResult(result);
            }
        );
    }

    private class LoadContextParams
    {
        public string? WorkingDirectory { get; set; }
        public string? SessionId { get; set; }
        public bool? RefreshCache { get; set; }
    }

    /// <summary>
    /// Phase 3: Register unified memory tool - natural language interface to all memory operations
    /// </summary>
    private static void RegisterUnifiedMemory(ToolRegistry registry, UnifiedMemoryTool tool)
    {
        registry.RegisterTool<UnifiedMemoryInputParams>(
            name: ToolNames.UnifiedMemory,
            description: @"Unified memory interface that uses natural language to perform memory operations.
Replaces the need for multiple memory tools with intelligent intent detection.
Automatically routes commands to appropriate tools based on detected intent.

Supported intents:
- SAVE: Store memories, create checklists (""remember that UserService has performance issues"")
- FIND: Search memories, files, content (""find all authentication bugs"")  
- CONNECT: Link related memories (""connect auth bug to security audit"")
- EXPLORE: Navigate relationships (""explore authentication system connections"")
- SUGGEST: Get recommendations (""suggest improvements for authentication"")
- MANAGE: Update/delete memories (""update technical debt status to resolved"")

Examples:
- ""remember that database query in UserService.GetActiveUsers() takes 5 seconds""
- ""find all technical debt related to authentication system""
- ""create checklist for database migration project""
- ""explore relationships around user management architecture""
- ""suggest next steps for performance optimization""

Use cases: Natural language memory operations, AI agent workflows, context-aware suggestions.
AI-optimized: Provides intent detection, action suggestions, and usage guidance.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    command = new 
                    { 
                        type = "string", 
                        description = "The natural language command to execute" 
                    },
                    intent = new
                    {
                        type = "string",
                        @enum = new[] { "save", "find", "connect", "explore", "suggest", "manage", "auto" },
                        description = "Optional: Force a specific intent instead of auto-detection",
                        @default = "auto"
                    },
                    workingDirectory = new 
                    { 
                        type = "string", 
                        description = "Optional: Working directory for file operations" 
                    },
                    sessionId = new 
                    { 
                        type = "string", 
                        description = "Optional: Session ID for tracking" 
                    },
                    relatedFiles = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Optional: Files currently being worked on"
                    },
                    currentFocus = new
                    {
                        type = "string",
                        description = "Optional: Current focus or task description"
                    }
                },
                required = new[] { "command" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Convert string intent to MemoryIntent enum
                MemoryIntent? intent = null;
                if (!string.IsNullOrEmpty(parameters.Intent) && parameters.Intent != "auto")
                {
                    intent = parameters.Intent.ToLowerInvariant() switch
                    {
                        "save" => MemoryIntent.Save,
                        "find" => MemoryIntent.Find,
                        "connect" => MemoryIntent.Connect,
                        "explore" => MemoryIntent.Explore,
                        "suggest" => MemoryIntent.Suggest,
                        "manage" => MemoryIntent.Manage,
                        _ => null
                    };
                }

                var toolParams = new UnifiedMemoryParams
                {
                    Command = ValidateRequired(parameters.Command, "command"),
                    Intent = intent,
                    WorkingDirectory = parameters.WorkingDirectory,
                    SessionId = parameters.SessionId,
                    RelatedFiles = parameters.RelatedFiles?.ToList() ?? new List<string>(),
                    CurrentFocus = parameters.CurrentFocus
                };

                var result = await tool.ExecuteAsync(toolParams, ct);
                return CreateSuccessResult(result);
            }
        );
    }

    private class UnifiedMemoryToolParams
    {
        public string? Command { get; set; }
        public string? Intent { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? SessionId { get; set; }
        public string[]? RelatedFiles { get; set; }
        public string? CurrentFocus { get; set; }
    }

    private static void RegisterSemanticSearch(ToolRegistry registry, SemanticSearchTool tool)
    {
        registry.RegisterTool<SemanticSearchParams>(
            name: "semantic_search",
            description: "Perform semantic search to find conceptually similar memories using embeddings. Finds memories based on concepts and meaning, not just exact keyword matches. Ideal for discovering related architectural decisions, similar problems, or concept-based exploration.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query (concepts, not just keywords). Examples: 'authentication issues', 'performance problems', 'database design patterns'" },
                    maxResults = new { type = "integer", description = "Maximum number of results to return", @default = 20, minimum = 1, maximum = 100 },
                    threshold = new { type = "number", description = "Minimum similarity threshold (0.0 to 1.0). Lower values find more results.", @default = 0.2f, minimum = 0.0f, maximum = 1.0f },
                    memoryType = new { type = "string", description = "Filter by memory type (TechnicalDebt, ArchitecturalDecision, etc.)" },
                    isShared = new { type = "boolean", description = "Filter by shared status" },
                    customFilters = new { 
                        type = "object", 
                        description = "Custom field filters as key-value pairs",
                        additionalProperties = true
                    }
                },
                required = new[] { "query" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                if (string.IsNullOrWhiteSpace(parameters.Query))
                    throw new InvalidParametersException("query is required and cannot be empty");

                var result = await tool.ExecuteAsync(parameters);
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterHybridSearch(ToolRegistry registry, HybridSearchTool tool)
    {
        registry.RegisterTool<HybridSearchParams>(
            name: "hybrid_search",
            description: "Perform hybrid search combining Lucene text search with semantic search for comprehensive results. Merges exact keyword matches with conceptual understanding. Best for thorough exploration when you need both precision and recall.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query for both text and semantic search" },
                    maxResults = new { type = "integer", description = "Maximum number of results to return", @default = 20, minimum = 1, maximum = 100 },
                    luceneWeight = new { type = "number", description = "Weight for text search results (0.0 to 1.0)", @default = 0.6f, minimum = 0.0f, maximum = 1.0f },
                    semanticWeight = new { type = "number", description = "Weight for semantic search results (0.0 to 1.0)", @default = 0.4f, minimum = 0.0f, maximum = 1.0f },
                    mergeStrategy = new { 
                        type = "string", 
                        description = "Strategy for merging results: Linear, Reciprocal, Multiplicative", 
                        @enum = new[] { "Linear", "Reciprocal", "Multiplicative" },
                        @default = "Linear"
                    },
                    semanticThreshold = new { type = "number", description = "Minimum similarity threshold for semantic results", @default = 0.2f, minimum = 0.0f, maximum = 1.0f },
                    bothFoundBoost = new { type = "number", description = "Boost factor when result found by both methods", @default = 1.2f, minimum = 1.0f, maximum = 3.0f },
                    luceneFilters = new { 
                        type = "object", 
                        description = "Filters for Lucene search (faceted search)",
                        additionalProperties = new { type = "string" }
                    },
                    semanticFilters = new { 
                        type = "object", 
                        description = "Filters for semantic search",
                        additionalProperties = true
                    }
                },
                required = new[] { "query" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                if (string.IsNullOrWhiteSpace(parameters.Query))
                    throw new InvalidParametersException("query is required and cannot be empty");

                var result = await tool.ExecuteAsync(parameters);
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterMemoryQualityAssessment(ToolRegistry registry, MemoryQualityAssessmentTool tool)
    {
        registry.RegisterTool<MemoryQualityAssessmentParams>(
            name: "memory_quality_assessment",
            description: "Assess and improve the quality of stored memories with detailed scoring and suggestions. Evaluates completeness, relevance, consistency, and provides actionable improvement recommendations.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryId = new { type = "string", description = "Memory ID to assess quality for" },
                    memoryIds = new { 
                        type = "array", 
                        items = new { type = "string" },
                        description = "Multiple memory IDs to assess (batch operation)" 
                    },
                    memoryType = new { type = "string", description = "Memory type to filter by (for bulk assessment)" },
                    qualityThreshold = new { type = "number", description = "Quality threshold (0.0-1.0, default: 0.7)", @default = 0.7, minimum = 0.0, maximum = 1.0 },
                    enabledValidators = new { 
                        type = "array", 
                        items = new { type = "string" },
                        description = "Specific validators to use (if not specified, all validators are used)" 
                    },
                    includeSuggestions = new { type = "boolean", description = "Include improvement suggestions in results", @default = true },
                    allowAutoImprovements = new { type = "boolean", description = "Allow automatic improvements to be applied", @default = false },
                    contextWorkspace = new { type = "string", description = "Current workspace context for relevance assessment" },
                    recentFiles = new { 
                        type = "array", 
                        items = new { type = "string" },
                        description = "Recently accessed files for context assessment" 
                    },
                    maxResults = new { type = "integer", description = "Maximum number of memories to assess (for bulk operations)", @default = 50, minimum = 1, maximum = 200 },
                    showDetails = new { type = "boolean", description = "Show detailed validation results", @default = false },
                    mode = new { 
                        type = "string", 
                        description = "Assessment mode: 'single', 'batch', 'bulk', or 'report'",
                        @enum = new[] { "single", "batch", "bulk", "report" },
                        @default = "single"
                    }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(parameters);
                return CreateSuccessResult(result);
            }
        );
    }

    /// <summary>
    /// Register only the essential memory tools as per the design document
    /// Exposes 6 tools instead of 40+ individual tools for simplified AI interface
    /// </summary>
    private static void RegisterEssentialMemoryTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        // Get required services
        var unifiedMemoryTool = serviceProvider.GetRequiredService<UnifiedMemoryTool>();
        var memorySearchV2 = serviceProvider.GetRequiredService<FlexibleMemorySearchToolV2>();
        var memoryTools = serviceProvider.GetRequiredService<FlexibleMemoryTools>();
        var semanticSearchTool = serviceProvider.GetRequiredService<SemanticSearchTool>();
        var hybridSearchTool = serviceProvider.GetRequiredService<HybridSearchTool>();
        var qualityAssessmentTool = serviceProvider.GetRequiredService<MemoryQualityAssessmentTool>();

        // Register the 6 essential tools only
        RegisterUnifiedMemory(registry, unifiedMemoryTool);
        RegisterSearchMemoriesV2(registry, memorySearchV2);
        RegisterStoreMemoryOnly(registry, memoryTools);
        RegisterSemanticSearch(registry, semanticSearchTool);
        RegisterHybridSearch(registry, hybridSearchTool);
        RegisterMemoryQualityAssessment(registry, qualityAssessmentTool);
    }

    /// <summary>
    /// Register only the store_memory tool from FlexibleMemoryTools
    /// </summary>
    private static void RegisterStoreMemoryOnly(ToolRegistry registry, FlexibleMemoryTools tool)
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

    /// <summary>
    /// Register the search_memories tool with V2 features
    /// </summary>
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
                    enableQueryExpansion = new { type = "boolean", description = "Enable automatic query expansion with synonyms and related terms", @default = true },
                    enableContextAwareness = new { type = "boolean", description = "Enable context-aware memory boosting based on current work", @default = true },
                    currentFile = new { type = "string", description = "Path to current file being worked on (for context awareness)" },
                    recentFiles = new { type = "array", items = new { type = "string" }, description = "Recently accessed files (for context awareness)" },
                    mode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'", @default = "summary" }
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
                    parameters?.EnableQueryExpansion ?? true, // Query expansion parameter
                    parameters?.EnableContextAwareness ?? true,
                    parameters?.CurrentFile,
                    parameters?.RecentFiles,
                    Enum.TryParse<ResponseMode>(parameters?.Mode, true, out var mode) ? mode : ResponseMode.Summary,
                    false, // enableHighlighting
                    3, // maxFragments 
                    100, // fragmentSize
                    parameters?.DetailRequest,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }



}