using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers TypeScript-related tools with the MCP server
/// </summary>
public static class TypeScriptToolRegistrations
{
    public static void RegisterTypeScriptTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        // Text-based TypeScript search tool
        var searchTool = serviceProvider.GetService<TypeScriptSearchTool>();
        if (searchTool != null)
        {
            RegisterTypeScriptSearch(registry, searchTool);
        }
        
        // TSServer-based tools
        var goToDefTool = serviceProvider.GetService<TypeScriptGoToDefinitionTool>();
        if (goToDefTool != null)
        {
            RegisterTypeScriptGoToDefinition(registry, goToDefTool);
        }
        
        var findRefsTool = serviceProvider.GetService<TypeScriptFindReferencesTool>();
        if (findRefsTool != null)
        {
            RegisterTypeScriptFindReferences(registry, findRefsTool);
        }
        
        var renameTool = serviceProvider.GetService<TypeScriptRenameTool>();
        if (renameTool != null)
        {
            RegisterTypeScriptRename(registry, renameTool);
        }
        
        var hoverTool = serviceProvider.GetService<TypeScriptHoverInfoTool>();
        if (hoverTool != null)
        {
            RegisterTypeScriptHoverInfo(registry, hoverTool);
        }
    }
    
    private static void RegisterTypeScriptSearch(ToolRegistry registry, TypeScriptSearchTool tool)
    {
        registry.RegisterTool<TypeScriptSearchParams>(
            name: ToolNames.SearchTypeScript,
            description: "Search for TypeScript symbols (interfaces, types, classes, functions) by name. Uses TypeScript language server for semantic understanding. For text search use text_search.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    symbolName = new { type = "string", description = "The TypeScript symbol name to search for" },
                    workspacePath = new { type = "string", description = "Path to solution or directory to search" },
                    mode = new 
                    { 
                        type = "string", 
                        description = "Search mode: 'definition' to find where it's defined, 'references' to find all usages, 'both' for everything",
                        @default = "both"
                    },
                    maxResults = new { type = "integer", description = "Maximum results to return", @default = 50 }
                },
                required = new[] { "symbolName", "workspacePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.SearchTypeScriptAsync(
                    ValidateRequired(parameters.SymbolName, "symbolName"),
                    ValidateRequired(parameters.WorkspacePath, "workspacePath"),
                    parameters.Mode ?? "both",
                    parameters.MaxResults ?? 50,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterTypeScriptGoToDefinition(ToolRegistry registry, TypeScriptGoToDefinitionTool tool)
    {
        registry.RegisterTool<TypeScriptNavigationParams>(
            name: ToolNames.TypeScriptGoToDefinition,
            description: "Direct TypeScript/JavaScript definition navigation using tsserver. Use only if go_to_definition fails on TypeScript files or you need TypeScript-specific behavior. For most cases, use go_to_definition which auto-detects language.",
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
                
                var result = await tool.GoToDefinitionAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.Line ?? throw new InvalidParametersException("line is required"),
                    parameters.Column ?? throw new InvalidParametersException("column is required"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterTypeScriptFindReferences(ToolRegistry registry, TypeScriptFindReferencesTool tool)
    {
        registry.RegisterTool<TypeScriptNavigationParams>(
            name: ToolNames.TypeScriptFindReferences,
            description: "Find all references to a TypeScript/JavaScript symbol using tsserver. Provides TypeScript-specific semantic analysis. For C# references, use find_references.",
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
                
                var result = await tool.FindReferencesAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.Line ?? throw new InvalidParametersException("line is required"),
                    parameters.Column ?? throw new InvalidParametersException("column is required"),
                    parameters.IncludeDeclaration ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class TypeScriptSearchParams
    {
        public string? SymbolName { get; set; }
        public string? WorkspacePath { get; set; }
        public string? Mode { get; set; }
        public int? MaxResults { get; set; }
    }
    
    private static void RegisterTypeScriptRename(ToolRegistry registry, TypeScriptRenameTool tool)
    {
        registry.RegisterTool<TypeScriptRenameParams>(
            name: ToolNames.TypeScriptRenameSymbol,
            description: "Rename TypeScript symbols with preview of affected locations. TypeScript-only rename analysis. For C# renaming use rename_symbol. Shows changes but doesn't modify files.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    newName = new { type = "string", description = "New name for the symbol" },
                    preview = new { type = "boolean", description = "Preview changes without applying them", @default = true }
                },
                required = new[] { "filePath", "line", "column", "newName" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.RenameSymbolAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.Line ?? throw new InvalidParametersException("line is required"),
                    parameters.Column ?? throw new InvalidParametersException("column is required"),
                    ValidateRequired(parameters.NewName, "newName"),
                    parameters.Preview ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class TypeScriptNavigationParams
    {
        public string? FilePath { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
        public bool? IncludeDeclaration { get; set; }
    }
    
    private static void RegisterTypeScriptHoverInfo(ToolRegistry registry, TypeScriptHoverInfoTool tool)
    {
        registry.RegisterTool<TypeScriptNavigationParams>(
            name: ToolNames.TypeScriptHoverInfo,
            description: "Get TypeScript type information and documentation. Shows detailed type info like IDE hover tooltips. Use for .ts/.js files when you need TypeScript semantic information.",
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
                    parameters.Line ?? throw new InvalidParametersException("line is required"),
                    parameters.Column ?? throw new InvalidParametersException("column is required"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private class TypeScriptRenameParams
    {
        public string? FilePath { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
        public string? NewName { get; set; }
        public bool? Preview { get; set; }
    }
}