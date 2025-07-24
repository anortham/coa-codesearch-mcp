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
        // Core navigation tools
        RegisterGoToDefinition(registry, serviceProvider.GetRequiredService<GoToDefinitionTool>());
        RegisterFindReferences(registry, serviceProvider.GetRequiredService<FindReferencesToolV2>());
        RegisterSearchSymbolsV2(registry, serviceProvider.GetRequiredService<SearchSymbolsToolV2>());
        RegisterGetImplementationsV2(registry, serviceProvider.GetRequiredService<GetImplementationsToolV2>());
        
        // Code information tools
        RegisterGetHoverInfo(registry, serviceProvider.GetRequiredService<GetHoverInfoTool>());
        RegisterGetDocumentSymbols(registry, serviceProvider.GetRequiredService<GetDocumentSymbolsTool>());
        RegisterGetDiagnostics(registry, serviceProvider.GetRequiredService<GetDiagnosticsToolV2>());
        
        // Advanced analysis tools
        RegisterGetCallHierarchyV2(registry, serviceProvider.GetRequiredService<GetCallHierarchyToolV2>());
        RegisterRenameSymbol(registry, serviceProvider.GetRequiredService<RenameSymbolToolV2>());
        RegisterBatchOperationsV2(registry, serviceProvider.GetRequiredService<BatchOperationsToolV2>());
        RegisterAdvancedSymbolSearch(registry, serviceProvider.GetRequiredService<AdvancedSymbolSearchTool>());
        RegisterDependencyAnalysis(registry, serviceProvider.GetRequiredService<DependencyAnalysisToolV2>());
        RegisterProjectStructureAnalysis(registry, serviceProvider.GetRequiredService<ProjectStructureAnalysisToolV2>());
        
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
            description: "Navigate to symbol definitions. Auto-detects language (C# or TypeScript) based on file extension and uses the appropriate analyzer.",
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
            description: "Find all references to a C# symbol across the codebase using Roslyn analysis. Returns locations where the symbol is used, with smart summarization for large result sets. For TypeScript references, use typescript_find_references.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    includeDeclaration = new { type = "boolean", description = "Include the declaration", @default = true },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "full" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    "compact" => ResponseMode.Compact,
                    _ => ResponseMode.Summary  // Default to summary
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


    private static void RegisterSearchSymbolsV2(ToolRegistry registry, SearchSymbolsToolV2 tool)
    {
        registry.RegisterTool<SearchSymbolsV2Params>(
            name: "search_symbols",
            description: "Find C# symbols by name using patterns and wildcards. USE THIS for most symbol searches - just searches by name. Only use advanced_symbol_search if you need to filter by access level (public/private) or modifiers (static/abstract).",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution or project" },
                    pattern = new { type = "string", description = "Search pattern (supports wildcards)" },
                    kinds = new { type = "array", items = new { type = "string" }, description = "Filter by symbol types (class, interface, method, property, field, event)" },
                    fuzzy = new { type = "boolean", description = "Use fuzzy matching", @default = false },
                    maxResults = new { type = "integer", description = "Maximum results", @default = 100 },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'" }
                },
                required = new[] { "workspacePath", "pattern" }
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
                    ValidateRequired(parameters.Pattern, "pattern"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.Kinds,
                    parameters.Fuzzy ?? false,
                    parameters.MaxResults ?? 100,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetImplementationsV2(ToolRegistry registry, GetImplementationsToolV2 tool)
    {
        registry.RegisterTool<GetImplementationsV2Params>(
            name: "get_implementations",
            description: "Find all classes that implement an interface or inherit from a base class. Use when exploring polymorphism, understanding inheritance hierarchies, or finding all concrete implementations.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "filePath", "line", "column" }
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
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterGetHoverInfo(ToolRegistry registry, GetHoverInfoTool tool)
    {
        registry.RegisterTool<GetHoverInfoParams>(
            name: "get_hover_info",
            description: "Get type information and documentation for a symbol at a specific location. Use when you need to understand what a symbol is, its signature, or read its documentation without navigating away.",
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
            description: "Get structural outline of a C# file showing all classes, methods, and properties. Use when you need to understand file organization or quickly see all members in a class.",
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
            description: "Check for compilation errors and warnings in C# code. Use when debugging build issues, code quality checks, or before committing changes. Helps identify syntax errors, unused variables, and other issues.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to file, project, or solution" },
                    severities = new { type = "array", items = new { type = "string" }, description = "Filter by severity: Error, Warning, Info, Hidden" },
                    includeSuppressions = new { type = "boolean", description = "Include suppressed diagnostics", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "full" }
                },
                required = new[] { "path" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Default to Summary mode to prevent token explosions
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    _ => ResponseMode.Summary  // Default to summary
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

    private static void RegisterGetCallHierarchyV2(ToolRegistry registry, GetCallHierarchyToolV2 tool)
    {
        registry.RegisterTool<GetCallHierarchyV2Params>(
            name: "get_call_hierarchy",
            description: "Analyze who calls a method and what it calls. Use when understanding code flow, impact analysis before changes, or identifying circular dependencies and complex call chains.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both'", @default = "both" },
                    maxDepth = new { type = "integer", description = "Maximum depth to traverse", @default = 2 },
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "summary" }
                },
                required = new[] { "filePath", "line", "column" }
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
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    parameters.Direction ?? "both",
                    parameters.MaxDepth ?? 2,
                    mode,
                    null,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }

    private static void RegisterRenameSymbol(ToolRegistry registry, RenameSymbolToolV2 tool)
    {
        registry.RegisterTool<RenameSymbolParams>(
            name: "rename_symbol",
            description: "Safely rename a symbol throughout the entire codebase with automatic reference updates. Use when refactoring to improve naming or when a symbol name no longer reflects its purpose.",
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
                    responseMode = new { type = "string", description = "Response mode: 'summary' (default) or 'full' for complete details", @default = "summary" }
                },
                required = new[] { "filePath", "line", "column", "newName" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Default to Summary mode to prevent token explosions
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    _ => ResponseMode.Summary  // Default to summary, not full
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


    private static void RegisterBatchOperationsV2(ToolRegistry registry, BatchOperationsToolV2 tool)
    {
        registry.RegisterTool<BatchOperationsV2Params>(
            name: "batch_operations",
            description: "Execute multiple code analysis operations in parallel for comprehensive insights. Combines results across different analysis types, identifies patterns, and suggests next steps. Faster than running operations sequentially. Supported: search_symbols, find_references, go_to_definition, get_hover_info, get_implementations, get_diagnostics, get_call_hierarchy, text_search, analyze_dependencies.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    operations = new 
                    { 
                        type = "array", 
                        items = new { type = "object" },
                        description = "Array of operations to execute. Format: [{\"operation\": \"search_symbols\", \"searchPattern\": \"User*\"}, {\"operation\": \"text_search\", \"query\": \"TODO\"}]. Each operation must have 'operation' field plus operation-specific parameters."
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

    private static void RegisterAdvancedSymbolSearch(ToolRegistry registry, AdvancedSymbolSearchTool tool)
    {
        registry.RegisterTool<AdvancedSymbolSearchParams>(
            name: "advanced_symbol_search",
            description: "ADVANCED: Search C# symbols with semantic filters beyond name matching. Most users should use search_symbols instead. Only use this when you need complex filters like: find public static methods, private fields in specific namespaces, virtual methods returning Task, abstract classes.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to workspace" },
                    query = new { type = "string", description = "Search query" },
                    filters = new { type = "object", description = "Semantic filters. Available keys: kinds (array), accessibility (array: public/private/protected/internal), isStatic/isAbstract/isVirtual/isOverride (bool), returnType/containingType/containingNamespace (string). Example: {\"accessibility\": [\"public\"], \"isStatic\": true, \"returnType\": \"Task\"}" }
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
            description: "C# ONLY: Analyze code dependencies to understand coupling and architecture. Analyzes .NET project references and namespace dependencies. Returns full graph for <5000 tokens, otherwise provides insights on circular dependencies, high coupling, and suggested refactorings. Use responseMode='summary' for overview.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string", description = "C# symbol to analyze (class, interface, method name)" },
                    workspacePath = new { type = "string", description = "Path to .NET solution or project directory" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both'", @default = "both" },
                    depth = new { type = "integer", description = "Analysis depth", @default = 3 },
                    includeTests = new { type = "boolean", description = "Include test projects", @default = false },
                    includeExternalDependencies = new { type = "boolean", description = "Include external dependencies", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary when graph exceeds 5000 tokens.", @default = "full" }
                },
                required = new[] { "symbol", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                // Default to Summary mode to prevent token explosions
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    _ => ResponseMode.Summary  // Default to summary
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
            description: ".NET PROJECTS ONLY: Analyze project structure with metrics on size, complexity, and organization. Analyzes .sln/.csproj files using MSBuildWorkspace. Does not include TypeScript, JavaScript, or frontend code. Shows full details for small projects (<5000 tokens), otherwise provides key metrics, hotspots, and architectural insights. Use responseMode='summary' for overview.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to solution or project" },
                    includeMetrics = new { type = "boolean", description = "Include code metrics", @default = true },
                    includeFiles = new { type = "boolean", description = "Include file listings", @default = false },
                    includeNuGetPackages = new { type = "boolean", description = "Include NuGet packages", @default = false },
                    responseMode = new { type = "string", description = "Response mode: 'full' (default) or 'summary'. Auto-switches to summary when response exceeds 5000 tokens.", @default = "full" }
                },
                required = new[] { "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var mode = parameters.ResponseMode?.ToLowerInvariant() switch
                {
                    "full" => ResponseMode.Full,
                    "compact" => ResponseMode.Compact,
                    _ => ResponseMode.Summary  // Default to summary
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


    private class SearchSymbolsV2Params
    {
        public string? WorkspacePath { get; set; }
        public string? Pattern { get; set; }
        public string[]? Kinds { get; set; }
        public bool? Fuzzy { get; set; }
        public int? MaxResults { get; set; }
        public string? ResponseMode { get; set; }
    }

    private class GetImplementationsV2Params
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string? ResponseMode { get; set; }
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

    private class GetCallHierarchyV2Params
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string? Direction { get; set; }
        public int? MaxDepth { get; set; }
        public string? ResponseMode { get; set; }
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


    private class BatchOperationsV2Params
    {
        public JsonElement Operations { get; set; }
        public string? WorkspacePath { get; set; }
        public string? Mode { get; set; } = "summary";
        public DetailRequest? DetailRequest { get; set; }
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
    
    private static void RegisterFastTextSearchV2(ToolRegistry registry, FastTextSearchToolV2 tool)
    {
        registry.RegisterTool<FastTextSearchV2Params>(
            name: "text_search",
            description: "Search for text content within files across the codebase. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed. Use when looking for specific strings, error messages, configuration values, or any text that appears in code, comments, or documentation.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")" },
                    workspacePath = new { type = "string", description = "Directory path to search in (e.g., C:\\MyProject). Always use the project root directory. To search in specific folders, use the filePattern parameter instead of passing subdirectories." },
                    filePattern = new { type = "string", description = "Optional: Filter by file pattern (e.g., '*.cs' for C# only, 'src/**/*.ts' for TypeScript in src)" },
                    extensions = new { type = "array", items = new { type = "string" }, description = "Optional: Limit to specific file types (e.g., ['.cs', '.razor', '.js'])" },
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
            name: "file_search",
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
            name: "recent_files",
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
            name: "file_size_analysis",
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
            name: "similar_files",
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
            name: "directory_search",
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
            name: "index_workspace",
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
            name: "log_diagnostics",
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
    
    private static void RegisterRecallContext(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RecallContextParams>(
            name: "recall_context",
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
            name: "backup_memories",
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
            name: "restore_memories",
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
    
}