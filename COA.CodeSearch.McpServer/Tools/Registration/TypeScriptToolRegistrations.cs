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
        
        // Future: Register TSServer-based tools when ready
        // var goToDefTool = serviceProvider.GetService<TypeScriptGoToDefinitionTool>();
        // if (goToDefTool != null)
        // {
        //     RegisterTypeScriptGoToDefinition(registry, goToDefTool);
        // }
    }
    
    private static void RegisterTypeScriptSearch(ToolRegistry registry, TypeScriptSearchTool tool)
    {
        registry.RegisterTool<TypeScriptSearchParams>(
            name: "search_typescript",
            description: "Search for TypeScript symbols (interfaces, types, classes, functions) using text search. NOTE: Basic text matching only - tsserver integration coming soon. Use fast_text_search for more advanced TypeScript searches.",
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
    
    private class TypeScriptSearchParams
    {
        public string? SymbolName { get; set; }
        public string? WorkspacePath { get; set; }
        public string? Mode { get; set; }
        public int? MaxResults { get; set; }
    }
}