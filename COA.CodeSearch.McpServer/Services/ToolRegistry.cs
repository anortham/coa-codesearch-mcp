using COA.Directus.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Registry for managing MCP tools
/// </summary>
public class ToolRegistry
{
    private readonly ILogger<ToolRegistry> _logger;
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a new tool
    /// </summary>
    public void RegisterTool(string name, string description, object inputSchema, Func<JsonElement?, CancellationToken, Task<CallToolResult>> handler)
    {
        var tool = new Tool
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema
        };

        var definition = new ToolDefinition
        {
            Tool = tool,
            Handler = handler
        };

        if (!_tools.TryAdd(name, definition))
        {
            throw new InvalidOperationException($"Tool '{name}' is already registered");
        }

        _logger.LogInformation("Registered tool: {ToolName}", name);
    }

    /// <summary>
    /// Register a tool with typed parameters
    /// </summary>
    public void RegisterTool<TParams>(string name, string description, object inputSchema, Func<TParams?, CancellationToken, Task<CallToolResult>> handler)
        where TParams : class
    {
        RegisterTool(name, description, inputSchema, async (args, ct) =>
        {
            TParams? parameters = default;
            if (args.HasValue)
            {
                try
                {
                    var json = args.Value.GetRawText();
                    parameters = JsonSerializer.Deserialize<TParams>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidParametersException($"Failed to parse parameters: {ex.Message}");
                }
            }

            return await handler(parameters, ct);
        });
    }

    /// <summary>
    /// Get all registered tools
    /// </summary>
    public List<Tool> GetTools()
    {
        return _tools.Values.Select(d => d.Tool).ToList();
    }

    /// <summary>
    /// Call a tool by name
    /// </summary>
    public async Task<CallToolResult> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(name, out var definition))
        {
            throw new InvalidParametersException($"Tool '{name}' not found");
        }

        _logger.LogInformation("Executing tool: {ToolName}", name);

        try
        {
            return await definition.Handler(arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not InvalidParametersException)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", name);
            throw new ToolExecutionException($"Tool execution failed: {ex.Message}", ex);
        }
    }

    private class ToolDefinition
    {
        public required Tool Tool { get; init; }
        public required Func<JsonElement?, CancellationToken, Task<CallToolResult>> Handler { get; init; }
    }
}

/// <summary>
/// Exception thrown when tool execution fails
/// </summary>
public class ToolExecutionException : Exception
{
    public ToolExecutionException(string message, Exception innerException) 
        : base(message, innerException) { }
}