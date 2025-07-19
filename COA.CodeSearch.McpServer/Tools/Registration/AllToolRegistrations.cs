using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Directus.Mcp.Protocol;
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
        // Core navigation tools
        RegisterGoToDefinition(registry, serviceProvider.GetRequiredService<GoToDefinitionTool>());
        RegisterFindReferences(registry, serviceProvider.GetRequiredService<FindReferencesToolV2>());
        RegisterSearchSymbols(registry, serviceProvider.GetRequiredService<SearchSymbolsTool>());
        RegisterGetImplementations(registry, serviceProvider.GetRequiredService<GetImplementationsTool>());
        
        // Code information tools
        RegisterGetHoverInfo(registry, serviceProvider.GetRequiredService<GetHoverInfoTool>());
        RegisterGetDocumentSymbols(registry, serviceProvider.GetRequiredService<GetDocumentSymbolsTool>());
        RegisterGetDiagnostics(registry, serviceProvider.GetRequiredService<GetDiagnosticsToolV2>());
        
        // Advanced analysis tools
        RegisterGetCallHierarchy(registry, serviceProvider.GetRequiredService<GetCallHierarchyTool>());
        RegisterRenameSymbol(registry, serviceProvider.GetRequiredService<RenameSymbolToolV2>());
        RegisterBatchOperations(registry, serviceProvider.GetRequiredService<BatchOperationsTool>());
        RegisterAdvancedSymbolSearch(registry, serviceProvider.GetRequiredService<AdvancedSymbolSearchTool>());
        RegisterDependencyAnalysis(registry, serviceProvider.GetRequiredService<DependencyAnalysisToolV2>());
        RegisterProjectStructureAnalysis(registry, serviceProvider.GetRequiredService<ProjectStructureAnalysisToolV2>());
        
        // Text search tools
        RegisterFastTextSearch(registry, serviceProvider.GetRequiredService<FastTextSearchTool>());
        RegisterIndexWorkspace(registry, serviceProvider.GetRequiredService<IndexWorkspaceTool>());
        
        // Claude Memory System tools
        MemoryToolRegistrations.RegisterMemoryTools(registry, serviceProvider);
        
        // TypeScript tools
        TypeScriptToolRegistrations.RegisterTypeScriptTools(registry, serviceProvider);
    }

    private static void RegisterGoToDefinition(ToolRegistry registry, GoToDefinitionTool tool)
    {
        registry.RegisterTool<GoToDefinitionParams>(
            name: "go_to_definition",
            description: "Navigate instantly to where any symbol (class, method, property) is defined - works across entire solutions",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterFindReferences(ToolRegistry registry, FindReferencesToolV2 tool)
    {
        registry.RegisterTool<FindReferencesParams>(
            name: "find_references",
            description: "Find every place a symbol is used throughout your codebase. Automatically switches to summary mode for large results. Supports progressive disclosure for efficient navigation.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    includeDeclaration = new { type = "boolean", description = "Include the declaration", @default = true },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary for large results.", @default = "full" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "summary" => ResponseMode.Summary,
                    "compact" => ResponseMode.Compact,
                    _ => ResponseMode.Full
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    parameters.IncludeDeclaration ?? true,
                    mode,
                    null, // DetailRequest - not used in initial call
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterSearchSymbols(ToolRegistry registry, SearchSymbolsTool tool)
    {
        registry.RegisterTool<SearchSymbolsParams>(
            name: "search_symbols",
            description: "Lightning-fast semantic search for C# classes, methods, properties by name using Roslyn - supports wildcards and fuzzy matching",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution or project" },
                    searchPattern = new { type = "string", description = "Search pattern (supports wildcards)" },
                    caseSensitive = new { type = "boolean", description = "Case sensitive search", @default = false },
                    searchType = new { type = "string", description = "Type of search: 'exact', 'contains', 'startsWith', 'wildcard', 'fuzzy'", @default = "contains" },
                    symbolTypes = new { type = "array", items = new { type = "string" }, description = "Filter by symbol types" },
                    maxResults = new { type = "integer", description = "Maximum results", @default = 100 }
                },
                required = new[] { "workspacePath", "searchPattern" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.SearchPattern, "searchPattern"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.SymbolTypes,
                    parameters.SearchType == "fuzzy",
                    parameters.MaxResults ?? 100,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetImplementations(ToolRegistry registry, GetImplementationsTool tool)
    {
        registry.RegisterTool<GetImplementationsParams>(
            name: "get_implementations",
            description: "Discover all concrete implementations of interfaces or abstract classes - essential for navigating inheritance hierarchies",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetHoverInfo(ToolRegistry registry, GetHoverInfoTool tool)
    {
        registry.RegisterTool<GetHoverInfoParams>(
            name: "get_hover_info",
            description: "Get detailed type information, signatures, and documentation for any symbol - like IDE hover tooltips",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetDocumentSymbols(ToolRegistry registry, GetDocumentSymbolsTool tool)
    {
        registry.RegisterTool<GetDocumentSymbolsParams>(
            name: "get_document_symbols",
            description: "Get a complete outline of all classes, methods, and properties in a file - perfect for understanding file structure",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    includeMembers = new { type = "boolean", description = "Include class members", @default = true }
                },
                required = new[] { "filePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.IncludeMembers ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetDiagnostics(ToolRegistry registry, GetDiagnosticsToolV2 tool)
    {
        registry.RegisterTool<GetDiagnosticsParams>(
            name: "get_diagnostics",
            description: "Instantly check for compilation errors and warnings. Automatically switches to summary mode for large results. Supports progressive disclosure for efficient debugging.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to file, project, or solution" },
                    severities = new { type = "array", items = new { type = "string" }, description = "Filter by severity: Error, Warning, Info, Hidden" },
                    includeSuppressions = new { type = "boolean", description = "Include suppressed diagnostics", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary for large results.", @default = "full" }
                },
                required = new[] { "path" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "summary" => ResponseMode.Summary,
                    _ => ResponseMode.Full
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Path, "path"),
                    parameters.Severities,
                    mode,
                    null, // DetailRequest - not used in initial call
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetCallHierarchy(ToolRegistry registry, GetCallHierarchyTool tool)
    {
        registry.RegisterTool<GetCallHierarchyParams>(
            name: "get_call_hierarchy",
            description: "Trace method call chains to understand execution flow - see what calls a method and what it calls",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both'", @default = "incoming" },
                    maxDepth = new { type = "integer", description = "Maximum depth to traverse", @default = 3 }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    parameters.Direction ?? "incoming",
                    parameters.MaxDepth ?? 3,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterRenameSymbol(ToolRegistry registry, RenameSymbolToolV2 tool)
    {
        registry.RegisterTool<RenameSymbolParams>(
            name: "rename_symbol",
            description: "Safely rename any symbol across your entire codebase - all references updated automatically. Automatically switches to summary mode for large renames. Supports progressive disclosure for efficient review.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    newName = new { type = "string", description = "New name for the symbol" },
                    preview = new { type = "boolean", description = "Preview changes without applying them", @default = true },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary' for large operations", @default = "full" }
                },
                required = new[] { "filePath", "line", "column", "newName" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "summary" => ResponseMode.Summary,
                    _ => ResponseMode.Full
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ValidateRequired(parameters.NewName, "newName"),
                    parameters.Preview ?? true,
                    mode,
                    null, // DetailRequest - not used in initial call
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterBatchOperations(ToolRegistry registry, BatchOperationsTool tool)
    {
        registry.RegisterTool<BatchOperationsParams>(
            name: "batch_operations",
            description: "Perform multiple code analysis operations in one request - dramatically faster for complex workflows. Supports: text_search, search_symbols, find_references, go_to_definition, get_hover_info, get_implementations, get_document_symbols, get_diagnostics, get_call_hierarchy, analyze_dependencies",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to workspace" },
                    operations = new 
                    { 
                        type = "array", 
                        items = new { type = "object" },
                        description = "Array of operations to execute"
                    }
                },
                required = new[] { "workspacePath", "operations" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                if (parameters.Operations == null || parameters.Operations.Count == 0)
                    throw new InvalidParametersException("operations are required and cannot be empty");
                
                try
                {
                    // Convert operations list to JsonElement
                    var operationsJson = JsonSerializer.Serialize(parameters.Operations);
                    var operationsElement = JsonSerializer.Deserialize<JsonElement>(operationsJson);
                    
                    var result = await tool.ExecuteAsync(operationsElement, parameters.WorkspacePath, ct);
                        
                    return CreateSuccessResult(result);
                }
                catch (JsonException ex)
                {
                    throw new InvalidParametersException($"Invalid JSON in operations: {ex.Message}");
                }
            }
        );
    }

    private static void RegisterAdvancedSymbolSearch(ToolRegistry registry, AdvancedSymbolSearchTool tool)
    {
        registry.RegisterTool<AdvancedSymbolSearchParams>(
            name: "advanced_symbol_search",
            description: "Power-user symbol search with semantic filters - find by accessibility, modifiers, return types, and more",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to workspace" },
                    query = new { type = "string", description = "Search query" },
                    filters = new { type = "object", description = "Advanced filters" }
                },
                required = new[] { "workspacePath", "query" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // For now, just use basic search - advanced filters would need to be parsed
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Query, "query"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    null, // kinds
                    null, // accessibility
                    null, // isStatic
                    null, // isAbstract
                    null, // isVirtual
                    null, // isOverride
                    null, // returnType
                    null, // containingType
                    null, // containingNamespace
                    false, // fuzzy
                    100, // maxResults
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterDependencyAnalysis(ToolRegistry registry, DependencyAnalysisToolV2 tool)
    {
        registry.RegisterTool<DependencyAnalysisParams>(
            name: "dependency_analysis",
            description: "Analyze code dependencies with smart insights about coupling, circular dependencies, and architecture patterns. Automatically switches to summary mode for complex dependency graphs.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string", description = "Symbol to analyze" },
                    workspacePath = new { type = "string", description = "Path to workspace" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both'", @default = "both" },
                    depth = new { type = "integer", description = "Analysis depth", @default = 3 },
                    includeTests = new { type = "boolean", description = "Include test projects", @default = false },
                    includeExternalDependencies = new { type = "boolean", description = "Include external dependencies", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary for large graphs.", @default = "full" }
                },
                required = new[] { "symbol", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "summary" => ResponseMode.Summary,
                    _ => ResponseMode.Full
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Symbol, "symbol"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.Direction ?? "both",
                    parameters.Depth ?? 3,
                    parameters.IncludeTests ?? false,
                    parameters.IncludeExternalDependencies ?? false,
                    mode,
                    null, // DetailRequest - not used in initial call
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterProjectStructureAnalysis(ToolRegistry registry, ProjectStructureAnalysisToolV2 tool)
    {
        registry.RegisterTool<ProjectStructureAnalysisParams>(
            name: "project_structure_analysis",
            description: "Get comprehensive metrics and structure analysis - lines of code, complexity, dependencies, and project organization. Automatically switches to summary mode for large solutions.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution or project" },
                    includeMetrics = new { type = "boolean", description = "Include code metrics", @default = true },
                    includeFiles = new { type = "boolean", description = "Include file listings", @default = false },
                    includeNuGetPackages = new { type = "boolean", description = "Include NuGet packages", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary for large results.", @default = "full" }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "summary" => ResponseMode.Summary,
                    "compact" => ResponseMode.Compact,
                    _ => ResponseMode.Full
                };
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.IncludeMetrics ?? true,
                    parameters.IncludeFiles ?? false,
                    parameters.IncludeNuGetPackages ?? false,
                    mode,
                    null, // DetailRequest - not used in initial call
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    // Parameter classes
    private class GoToDefinitionParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    private class FindReferencesParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public bool? IncludeDeclaration { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class SearchSymbolsParams
    {
        public string? WorkspacePath { get; set; }
        public string? SearchPattern { get; set; }
        public bool? CaseSensitive { get; set; }
        public string? SearchType { get; set; }
        public string[]? SymbolTypes { get; set; }
        public int? MaxResults { get; set; }
    }

    private class GetImplementationsParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    private class GetHoverInfoParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    private class GetDocumentSymbolsParams
    {
        public string? FilePath { get; set; }
        public bool? IncludeMembers { get; set; }
    }

    private class GetDiagnosticsParams
    {
        public string? Path { get; set; }
        public string[]? Severities { get; set; }
        public bool? IncludeSuppressions { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class GetCallHierarchyParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string? Direction { get; set; }
        public int? MaxDepth { get; set; }
    }

    private class RenameSymbolParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string? NewName { get; set; }
        public bool? Preview { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class BatchOperationsParams
    {
        public string? WorkspacePath { get; set; }
        public List<object>? Operations { get; set; }
    }

    private class AdvancedSymbolSearchParams
    {
        public string? WorkspacePath { get; set; }
        public string? Query { get; set; }
        public object? Filters { get; set; }
    }

    private class DependencyAnalysisParams
    {
        public string? Symbol { get; set; }
        public string? WorkspacePath { get; set; }
        public string? Direction { get; set; }
        public int? Depth { get; set; }
        public bool? IncludeTests { get; set; }
        public bool? IncludeExternalDependencies { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class ProjectStructureAnalysisParams
    {
        public string? WorkspacePath { get; set; }
        public bool? IncludeMetrics { get; set; }
        public bool? IncludeFiles { get; set; }
        public bool? IncludeNuGetPackages { get; set; }
        public string? ResponseMode { get; set; }
    }
    
    private static void RegisterFastTextSearch(ToolRegistry registry, FastTextSearchTool tool)
    {
        registry.RegisterTool<FastTextSearchParams>(
            name: "fast_text_search",
            description: "⚡ Blazing-fast text search across millions of lines in milliseconds - supports wildcards, fuzzy search, and shows context. Works with all file types including C#, TypeScript, JavaScript, and more",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")" },
                    workspacePath = new { type = "string", description = "Path to solution (.sln), project (.csproj), or directory to search" },
                    filePattern = new { type = "string", description = "Optional: Filter by file pattern (e.g., '*.cs' for C# only, 'src/**/*.ts' for TypeScript in src)" },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Limit to specific file types (e.g., ['.cs', '.razor', '.js'])" },
                    contextLines = new { type = "integer", description = "Optional: Show N lines before/after each match for context (default: 0)" },
                    maxResults = new { type = "integer", description = "Maximum number of results", @default = 50 },
                    caseSensitive = new { type = "boolean", description = "Case sensitive search", @default = false },
                    searchType = new { type = "string", description = "Optional: Search mode - 'standard' (default), 'wildcard' (with *), 'fuzzy' (approximate), 'phrase' (exact)", @default = "standard" }
                },
                required = new[] { "query", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Query, "query"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.FilePattern,
                    parameters.Extensions,
                    parameters.ContextLines,
                    parameters.MaxResults ?? 50,
                    parameters.CaseSensitive ?? false,
                    parameters.SearchType ?? "standard",
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastTextSearchParams
    {
        public string? Query { get; set; }
        public string? WorkspacePath { get; set; }
        public string? FilePattern { get; set; }
        public string[]? Extensions { get; set; }
        public int? ContextLines { get; set; }
        public int? MaxResults { get; set; }
        public bool? CaseSensitive { get; set; }
        public string? SearchType { get; set; }
    }
    
    private static void RegisterIndexWorkspace(ToolRegistry registry, IndexWorkspaceTool tool)
    {
        registry.RegisterTool<IndexWorkspaceParams>(
            name: "index_workspace",
            description: "Index or re-index a workspace for fast text search - build search index before using fast_text_search for optimal performance",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "The workspace path to index" },
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
}