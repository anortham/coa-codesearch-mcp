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
        RegisterFindReferences(registry, serviceProvider.GetRequiredService<FindReferencesTool>());
        RegisterSearchSymbols(registry, serviceProvider.GetRequiredService<SearchSymbolsTool>());
        RegisterGetImplementations(registry, serviceProvider.GetRequiredService<GetImplementationsTool>());
        
        // Code information tools
        RegisterGetHoverInfo(registry, serviceProvider.GetRequiredService<GetHoverInfoTool>());
        RegisterGetDocumentSymbols(registry, serviceProvider.GetRequiredService<GetDocumentSymbolsTool>());
        RegisterGetDiagnostics(registry, serviceProvider.GetRequiredService<GetDiagnosticsTool>());
        
        // Advanced analysis tools
        RegisterGetCallHierarchy(registry, serviceProvider.GetRequiredService<GetCallHierarchyTool>());
        RegisterRenameSymbol(registry, serviceProvider.GetRequiredService<RenameSymbolTool>());
        RegisterBatchOperations(registry, serviceProvider.GetRequiredService<BatchOperationsTool>());
        RegisterAdvancedSymbolSearch(registry, serviceProvider.GetRequiredService<AdvancedSymbolSearchTool>());
        RegisterDependencyAnalysis(registry, serviceProvider.GetRequiredService<DependencyAnalysisTool>());
        RegisterProjectStructureAnalysis(registry, serviceProvider.GetRequiredService<ProjectStructureAnalysisTool>());
    }

    private static void RegisterGoToDefinition(ToolRegistry registry, GoToDefinitionTool tool)
    {
        registry.RegisterTool<GoToDefinitionParams>(
            name: "go_to_definition",
            description: "Navigate to the definition of a symbol at a specific position in a file",
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

    private static void RegisterFindReferences(ToolRegistry registry, FindReferencesTool tool)
    {
        registry.RegisterTool<FindReferencesParams>(
            name: "find_references",
            description: "Find all references to a symbol at a specific position",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    includeDeclaration = new { type = "boolean", description = "Include the declaration", @default = true }
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
                    parameters.IncludeDeclaration ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterSearchSymbols(ToolRegistry registry, SearchSymbolsTool tool)
    {
        registry.RegisterTool<SearchSymbolsParams>(
            name: "search_symbols",
            description: "Search for symbols by name pattern across the solution",
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
            description: "Find implementations of an interface or abstract member",
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
            description: "Get hover information (type info, documentation) for a symbol",
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
            description: "Get all symbols defined in a document",
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

    private static void RegisterGetDiagnostics(ToolRegistry registry, GetDiagnosticsTool tool)
    {
        registry.RegisterTool<GetDiagnosticsParams>(
            name: "get_diagnostics",
            description: "Get compilation diagnostics (errors, warnings) for a file or project",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to file, project, or solution" },
                    severities = new { type = "array", items = new { type = "string" }, description = "Filter by severity: Error, Warning, Info, Hidden" },
                    includeSuppressions = new { type = "boolean", description = "Include suppressed diagnostics", @default = false }
                },
                required = new[] { "path" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Path, "path"),
                    parameters.Severities,
                    100, // maxResults
                    false, // summaryOnly
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetCallHierarchy(ToolRegistry registry, GetCallHierarchyTool tool)
    {
        registry.RegisterTool<GetCallHierarchyParams>(
            name: "get_call_hierarchy",
            description: "Get incoming or outgoing calls for a method",
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

    private static void RegisterRenameSymbol(ToolRegistry registry, RenameSymbolTool tool)
    {
        registry.RegisterTool<RenameSymbolParams>(
            name: "rename_symbol",
            description: "Rename a symbol across the entire solution",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    newName = new { type = "string", description = "New name for the symbol" }
                },
                required = new[] { "filePath", "line", "column", "newName" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ValidateRequired(parameters.NewName, "newName"),
                    true, // preview
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterBatchOperations(ToolRegistry registry, BatchOperationsTool tool)
    {
        registry.RegisterTool<BatchOperationsParams>(
            name: "batch_operations",
            description: "Execute multiple operations in a single request",
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
                
                // Convert operations list to JsonElement
                var operationsJson = JsonSerializer.Serialize(parameters.Operations ?? throw new InvalidParametersException("operations are required"));
                var operationsElement = JsonSerializer.Deserialize<JsonElement>(operationsJson);
                
                var result = await tool.ExecuteAsync(operationsElement, ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterAdvancedSymbolSearch(ToolRegistry registry, AdvancedSymbolSearchTool tool)
    {
        registry.RegisterTool<AdvancedSymbolSearchParams>(
            name: "advanced_symbol_search",
            description: "Advanced symbol search with semantic filters",
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

    private static void RegisterDependencyAnalysis(ToolRegistry registry, DependencyAnalysisTool tool)
    {
        registry.RegisterTool<DependencyAnalysisParams>(
            name: "dependency_analysis",
            description: "Analyze symbol dependencies and references",
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
                    includeExternalDependencies = new { type = "boolean", description = "Include external dependencies", @default = false }
                },
                required = new[] { "symbol", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.Symbol, "symbol"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.Direction ?? "both",
                    parameters.Depth ?? 3,
                    parameters.IncludeTests ?? false,
                    parameters.IncludeExternalDependencies ?? false,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterProjectStructureAnalysis(ToolRegistry registry, ProjectStructureAnalysisTool tool)
    {
        registry.RegisterTool<ProjectStructureAnalysisParams>(
            name: "project_structure_analysis",
            description: "Analyze solution and project structure with metrics",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution or project" },
                    includeMetrics = new { type = "boolean", description = "Include code metrics", @default = true },
                    includeFiles = new { type = "boolean", description = "Include file listings", @default = false },
                    includeNuGetPackages = new { type = "boolean", description = "Include NuGet packages", @default = false }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.IncludeMetrics ?? true,
                    parameters.IncludeFiles ?? false,
                    parameters.IncludeNuGetPackages ?? false,
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
    }

    private class ProjectStructureAnalysisParams
    {
        public string? WorkspacePath { get; set; }
        public bool? IncludeMetrics { get; set; }
        public bool? IncludeFiles { get; set; }
        public bool? IncludeNuGetPackages { get; set; }
    }
}