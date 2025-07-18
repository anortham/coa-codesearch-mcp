using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Directus.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers the GoToDefinition tool with the MCP server
/// </summary>
public static class GoToDefinitionRegistration
{
    public static void Register(ToolRegistry registry, GoToDefinitionTool tool)
    {
        registry.RegisterTool<GoToDefinitionParams>(
            name: "go_to_definition",
            description: "Navigate to the definition of a symbol at a specific position in a file",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new 
                    { 
                        type = "string", 
                        description = "Path to the source file"
                    },
                    line = new 
                    { 
                        type = "integer", 
                        description = "Line number (1-based)"
                    },
                    column = new 
                    { 
                        type = "integer", 
                        description = "Column number (1-based)"
                    }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, cancellationToken) =>
            {
                if (parameters == null)
                    throw new InvalidParametersException("Parameters are required");

                var result = await tool.ExecuteAsync(
                    parameters.FilePath ?? throw new InvalidParametersException("filePath is required"),
                    parameters.Line,
                    parameters.Column,
                    cancellationToken);

                return new CallToolResult
                {
                    Content = new List<ToolContent>
                    {
                        new ToolContent
                        {
                            Type = "text",
                            Text = System.Text.Json.JsonSerializer.Serialize(result)
                        }
                    }
                };
            }
        );
    }

    private class GoToDefinitionParams
    {
        public string? FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}