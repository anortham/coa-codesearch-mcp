using COA.CodeSearch.McpServer.Services;
using COA.Directus.Mcp.Protocol;
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
    }
    
    private static void RegisterTypeScriptSearch(ToolRegistry registry, TypeScriptSearchTool tool)
    {
        registry.RegisterTool<TypeScriptSearchParams>(
            name: "search_typescript",
            description: "🔍 Find TypeScript symbols FAST! Searches interfaces, types, classes, functions. For advanced searches use fast_text_search. AVOID grep - this understands TypeScript structure better.",
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
            name: "typescript_go_to_definition",
            description: "⚡ Jump to TypeScript definitions INSTANTLY! Uses real TypeScript language server for 100% accuracy. MUCH better than text search - handles imports, aliases, complex types perfectly.",
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
            name: "typescript_find_references",
            description: "Find all references to a TypeScript symbol using tsserver - accurate reference finding across the entire TypeScript codebase",
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
    
    private class TypeScriptNavigationParams
    {
        public string? FilePath { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
        public bool? IncludeDeclaration { get; set; }
    }
}