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
        RegisterFastFileSearch(registry, serviceProvider.GetRequiredService<FastFileSearchTool>());
        RegisterFastRecentFiles(registry, serviceProvider.GetRequiredService<FastRecentFilesTool>());
        RegisterFastFileSizeAnalysis(registry, serviceProvider.GetRequiredService<FastFileSizeAnalysisTool>());
        RegisterFastSimilarFiles(registry, serviceProvider.GetRequiredService<FastSimilarFilesTool>());
        RegisterFastDirectorySearch(registry, serviceProvider.GetRequiredService<FastDirectorySearchTool>());
        RegisterIndexWorkspace(registry, serviceProvider.GetRequiredService<IndexWorkspaceTool>());
        
        // Claude Memory System tools
        MemoryToolRegistrations.RegisterMemoryTools(registry, serviceProvider);
        
        // Flexible Memory System tools
        FlexibleMemoryToolRegistrations.RegisterFlexibleMemoryTools(registry, serviceProvider);
        
        // Memory Linking tools
        MemoryLinkingToolRegistrations.RegisterAll(registry, serviceProvider.GetRequiredService<MemoryLinkingTools>());
        
        // TypeScript tools
        TypeScriptToolRegistrations.RegisterTypeScriptTools(registry, serviceProvider);
        
        // Logging control tool
        RegisterSetLogging(registry, serviceProvider.GetRequiredService<SetLoggingTool>());
        
        // Version information tool
        RegisterGetVersion(registry, serviceProvider.GetRequiredService<GetVersionTool>());
    }

    private static void RegisterGoToDefinition(ToolRegistry registry, GoToDefinitionTool tool)
    {
        registry.RegisterTool<GoToDefinitionParams>(
            name: "go_to_definition",
            description: "Navigate to where any symbol (class, method, property) is defined - works across entire solutions. Supports C# and TypeScript. ~50ms for cached workspaces.",
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
            description: "Find every place a symbol is used throughout your codebase. Supports C# and TypeScript - returns references ONLY within the same language. C# symbols return C# references, TypeScript symbols return TypeScript references. Returns full details for <5000 tokens, otherwise auto-switches to smart summary with hotspots and insights. Use responseMode='summary' to force summary view.",
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
            description: "Find C# symbols by name with basic filters - fast prefix/contains matching for classes, methods, properties. C# ONLY. Use for simple searches; for semantic filters use advanced_symbol_search. ~100ms response.",
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
            description: "Discover all concrete implementations of interfaces or abstract classes - essential for navigating inheritance hierarchies. C# ONLY. For TypeScript, use typescript_find_references on interfaces.",
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
            description: "Get detailed type information, signatures, and documentation for any symbol - like IDE hover tooltips. Works with C# and TypeScript. Shows XML docs when available.",
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
            description: "Get a complete outline of all classes, methods, and properties in a file - perfect for understanding file structure. Supports C# and TypeScript.",
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
            description: "Check for compilation errors and warnings across your codebase. C# ONLY - uses Roslyn compiler diagnostics. TypeScript diagnostics not supported. Returns full diagnostics for <5000 tokens, otherwise groups by severity with smart insights. Use responseMode='summary' for overview.",
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
            description: "Trace method call chains to understand execution flow - see what calls a method and what it calls. C# ONLY - uses Roslyn semantic analysis.",
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
            description: "Safely rename any symbol across your entire codebase - all references updated automatically. C# ONLY - uses Roslyn refactoring engine. For TypeScript renaming, use typescript tools. Shows full changes for <5000 tokens, otherwise provides impact summary with risk assessment. Set preview=false to apply changes.",
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
            description: "Perform multiple code analysis operations in one request - dramatically faster for complex workflows. Language support varies by operation: C# only (search_symbols, get_implementations, get_diagnostics, get_call_hierarchy, analyze_dependencies), Both C# and TypeScript (find_references, go_to_definition, get_hover_info, get_document_symbols), All languages (text_search).",
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
            description: "Find C# symbols with semantic filters - search by accessibility (public/private), modifiers (static/abstract), return types, or namespace. Use when search_symbols isn't specific enough.",
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
            description: "Analyze code dependencies to understand coupling and architecture. C# ONLY - analyzes .NET project references and namespace dependencies. Does not include TypeScript/JavaScript dependencies. Returns full graph for <5000 tokens, otherwise provides insights on circular dependencies, high coupling, and suggested refactorings. Use responseMode='summary' for overview.",
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
            description: "Analyze project structure with metrics on size, complexity, and organization. .NET PROJECTS ONLY - analyzes .sln/.csproj files using MSBuildWorkspace. Does not include TypeScript, JavaScript, or frontend code. Shows full details for small projects (<5000 tokens), otherwise provides key metrics, hotspots, and architectural insights. Use responseMode='summary' for overview.",
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
            description: "Search text across entire codebase with millisecond performance - supports wildcards (*), fuzzy (~), phrases (\"exact\"), and regex. Works with all file types. PREREQUISITE: Run index_workspace first. ~50ms for typical searches.",
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
    
    private static void RegisterFastFileSearch(ToolRegistry registry, FastFileSearchTool tool)
    {
        registry.RegisterTool<FastFileSearchParams>(
            name: "fast_file_search",
            description: "Find files by name with typo tolerance and pattern matching - uses pre-built index for instant results. Supports wildcards (*), fuzzy (~), regex. PREREQUISITE: Run index_workspace first. ~10ms response time.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "File name to search for - examples: 'UserService' (contains), 'UserSrvc~' (fuzzy), 'User*.cs' (wildcard), '^User' (regex start)" },
                    workspacePath = new { type = "string", description = "Path to solution, project, or directory to search" },
                    searchType = new { type = "string", description = "Search mode: 'standard' (default), 'fuzzy' (UserSrvc finds UserService), 'wildcard' (User*), 'exact' (exact match), 'regex' (/pattern/)", @default = "standard" },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 50 },
                    includeDirectories = new { type = "boolean", description = "Include directory names in search", @default = false }
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
                    parameters.MaxResults ?? 50,
                    parameters.IncludeDirectories ?? false,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class FastFileSearchParams
    {
        public string? Query { get; set; }
        public string? WorkspacePath { get; set; }
        public string? SearchType { get; set; }
        public int? MaxResults { get; set; }
        public bool? IncludeDirectories { get; set; }
    }
    
    private static void RegisterFastRecentFiles(ToolRegistry registry, FastRecentFilesTool tool)
    {
        registry.RegisterTool<FastRecentFilesParams>(
            name: "fast_recent_files",
            description: "Find recently modified files using indexed timestamps - discover what changed in the last hour, day, or week. Shows modification time in friendly format.",
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
            name: "fast_file_size_analysis",
            description: "Analyze files by size - find large files, empty files, or analyze size distributions. Uses indexed data for instant results across entire codebase.",
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
            name: "fast_similar_files",
            description: "Find files with similar content using 'More Like This' algorithm - ideal for discovering duplicate code, related implementations, or similar patterns. Shows similarity scores and matching terms.",
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
            name: "fast_directory_search",
            description: "Search for directories/folders with fuzzy matching - locate project folders, discover structure, find namespaces. Shows file counts and supports typo correction.",
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
            name: "index_workspace",
            description: "Build or rebuild search index for fast text/file search tools. REQUIRED before using: fast_text_search, fast_file_search, fast_recent_files, etc. Takes 5-60 seconds depending on codebase size.",
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
    
    private static void RegisterSetLogging(ToolRegistry registry, SetLoggingTool tool)
    {
        registry.RegisterTool<SetLoggingParams>(
            name: "set_logging",
            description: "Control file-based logging for debugging. Logs are written to .codesearch/logs directory. Actions: start, stop, status, list, setlevel, cleanup",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    action = new { 
                        type = "string", 
                        description = "Action to perform: 'start', 'stop', 'status', 'list', 'setlevel', 'cleanup'",
                        @enum = new[] { "start", "stop", "status", "list", "setlevel", "cleanup" }
                    },
                    level = new { 
                        type = "string", 
                        description = "Log level for 'start' or 'setlevel' actions: Verbose, Debug, Information, Warning, Error, Fatal" 
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
                    parameters.Level,
                    parameters.Cleanup,
                    ct);
                
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class SetLoggingParams
    {
        public string? Action { get; set; }
        public string? Level { get; set; }
        public bool? Cleanup { get; set; }
    }
    
    private static void RegisterGetVersion(ToolRegistry registry, GetVersionTool tool)
    {
        registry.RegisterTool<object>(
            name: "get_version",
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
}