using COA.Roslyn.McpServer.Services;
using COA.Roslyn.McpServer.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Register MSBuild before anything else
MSBuildLocator.RegisterDefaults();

// Handle command line arguments
if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    Console.WriteLine("COA Roslyn MCP Server - High-performance code navigation for .NET");
    Console.WriteLine();
    Console.WriteLine("Usage: coa-roslyn-mcp [command]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  stdio    Run in STDIO mode for MCP clients");
    Console.WriteLine("  --help   Show this help message");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  coa-roslyn-mcp stdio");
    return 0;
}

if (args[0] != "stdio")
{
    Console.Error.WriteLine($"Unknown command: {args[0]}");
    Console.Error.WriteLine("Run 'coa-roslyn-mcp --help' for usage information.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Debug;
});

// Add configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("MCP_");

// Add services
builder.Services.AddSingleton<RoslynWorkspaceService>();
builder.Services.AddSingleton<GoToDefinitionTool>();
builder.Services.AddSingleton<FindReferencesTool>();
builder.Services.AddSingleton<SearchSymbolsTool>();
builder.Services.AddSingleton<GetDiagnosticsTool>();
builder.Services.AddSingleton<GetHoverInfoTool>();
builder.Services.AddSingleton<GetImplementationsTool>();
builder.Services.AddSingleton<GetDocumentSymbolsTool>();
builder.Services.AddSingleton<GetCallHierarchyTool>();
builder.Services.AddSingleton<RenameSymbolTool>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// Get services
var goToDefTool = host.Services.GetRequiredService<GoToDefinitionTool>();
var findRefsTool = host.Services.GetRequiredService<FindReferencesTool>();
var searchTool = host.Services.GetRequiredService<SearchSymbolsTool>();
var diagnosticsTool = host.Services.GetRequiredService<GetDiagnosticsTool>();
var hoverTool = host.Services.GetRequiredService<GetHoverInfoTool>();
var implementationsTool = host.Services.GetRequiredService<GetImplementationsTool>();
var documentSymbolsTool = host.Services.GetRequiredService<GetDocumentSymbolsTool>();
var callHierarchyTool = host.Services.GetRequiredService<GetCallHierarchyTool>();
var renameSymbolTool = host.Services.GetRequiredService<RenameSymbolTool>();

logger.LogInformation("COA Roslyn MCP Server starting...");

// Set up console for binary mode
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// JSON options
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

// Handle JSON-RPC messages
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Start reading messages
_ = Task.Run(async () =>
{
    using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
    using var writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
    
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            
            logger.LogDebug("Received: {Json}", line);
            
            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, jsonOptions);
                if (request != null)
                {
                    // Check if this is a notification (no ID) or a request (has ID)
                    if (request.Id == null)
                    {
                        // Handle notifications - don't send a response
                        await HandleNotification(request, logger);
                    }
                    else
                    {
                        // Handle requests - send a response
                        var response = await HandleRequest(request, goToDefTool, findRefsTool, searchTool, diagnosticsTool, hoverTool, implementationsTool, documentSymbolsTool, callHierarchyTool, renameSymbolTool, logger);
                        var responseJson = JsonSerializer.Serialize(response, jsonOptions);
                        
                        await writer.WriteLineAsync(responseJson);
                        
                        logger.LogDebug("Sent: {Json}", responseJson);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing request");
                var errorResponse = new JsonRpcResponse
                {
                    JsonRpc = "2.0",
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Internal error",
                        Data = ex.Message
                    }
                };
                
                var errorJson = JsonSerializer.Serialize(errorResponse, jsonOptions);
                await writer.WriteLineAsync(errorJson);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Fatal error in message loop");
    }
}, cts.Token);

logger.LogInformation("MCP Server started and waiting for requests");

// Keep the host running
await host.RunAsync();
return 0;

static async Task HandleNotification(JsonRpcRequest notification, ILogger logger)
{
    logger.LogDebug("Handling notification: {Method}", notification.Method);
    
    switch (notification.Method)
    {
        case "initialized":
            logger.LogInformation("MCP server initialization completed successfully");
            break;
            
        default:
            logger.LogWarning("Unknown notification method: {Method}", notification.Method);
            break;
    }
}

static async Task<JsonRpcResponse> HandleRequest(
    JsonRpcRequest request,
    GoToDefinitionTool goToDefTool,
    FindReferencesTool findRefsTool,
    SearchSymbolsTool searchTool,
    GetDiagnosticsTool diagnosticsTool,
    GetHoverInfoTool hoverTool,
    GetImplementationsTool implementationsTool,
    GetDocumentSymbolsTool documentSymbolsTool,
    GetCallHierarchyTool callHierarchyTool,
    RenameSymbolTool renameSymbolTool,
    ILogger logger)
{
    try
    {
        switch (request.Method)
        {
            case "initialize":
                return new JsonRpcResponse
                {
                    JsonRpc = "2.0",
                    Id = request.Id,
                    Result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new 
                        { 
                            tools = new { }  // Empty object indicates tool support
                        },
                        serverInfo = new 
                        { 
                            name = "COA Roslyn MCP Server", 
                            version = "1.0.0" 
                        }
                    }
                };
                
            case "tools/call":
                if (request.Params is JsonElement paramsEl)
                {
                    var toolName = paramsEl.GetProperty("name").GetString();
                    var args = paramsEl.GetProperty("arguments");
                    
                    object? result = toolName switch
                    {
                        "go_to_definition" => await goToDefTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32()),
                            
                        "find_references" => await findRefsTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32(),
                            args.TryGetProperty("includePotential", out var ip) && ip.GetBoolean()),
                            
                        "search_symbols" => await searchTool.ExecuteAsync(
                            args.GetProperty("pattern").GetString()!,
                            args.GetProperty("workspacePath").GetString()!,
                            args.TryGetProperty("kinds", out var k) && k.ValueKind == JsonValueKind.Array
                                ? k.EnumerateArray().Select(e => e.GetString()!).ToArray()
                                : null,
                            args.TryGetProperty("fuzzy", out var f) && f.GetBoolean(),
                            args.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 100),
                            
                        "get_diagnostics" => await diagnosticsTool.ExecuteAsync(
                            args.GetProperty("path").GetString()!,
                            args.TryGetProperty("severities", out var s) && s.ValueKind == JsonValueKind.Array
                                ? s.EnumerateArray().Select(e => e.GetString()!).ToArray()
                                : null),
                                
                        "get_hover_info" => await hoverTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32()),
                            
                        "get_implementations" => await implementationsTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32()),
                            
                        "get_document_symbols" => await documentSymbolsTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.TryGetProperty("includeMembers", out var im) && im.GetBoolean()),
                            
                        "get_call_hierarchy" => await callHierarchyTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32(),
                            args.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "both" : "both",
                            args.TryGetProperty("maxDepth", out var md) ? md.GetInt32() : 2),
                            
                        "rename_symbol" => await renameSymbolTool.ExecuteAsync(
                            args.GetProperty("filePath").GetString()!,
                            args.GetProperty("line").GetInt32(),
                            args.GetProperty("column").GetInt32(),
                            args.GetProperty("newName").GetString()!,
                            args.TryGetProperty("preview", out var p) ? p.GetBoolean() : true),
                            
                        _ => throw new NotSupportedException($"Unknown tool: {toolName}")
                    };
                    
                    // Wrap the result in the MCP-expected format
                    var mcpResult = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = JsonSerializer.Serialize(result, new JsonSerializerOptions
                                {
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                                    WriteIndented = false
                                })
                            }
                        }
                    };
                    
                    return new JsonRpcResponse
                    {
                        JsonRpc = "2.0",
                        Id = request.Id,
                        Result = mcpResult
                    };
                }
                break;
                
            case "tools/list":
                return new JsonRpcResponse
                {
                    JsonRpc = "2.0",
                    Id = request.Id,
                    Result = new
                    {
                        tools = GetToolsList()
                    }
                };
        }
        
        return new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = request.Id,
            Error = new JsonRpcError
            {
                Code = -32601,
                Message = "Method not found"
            }
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error handling request");
        return new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = request.Id,
            Error = new JsonRpcError
            {
                Code = -32603,
                Message = "Internal error",
                Data = ex.Message
            }
        };
    }
}

static object[] GetToolsList()
{
    return new object[]
    {
        new 
        { 
            name = "go_to_definition", 
            description = "Navigate to the definition of a symbol at a specific position in a file",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new string[] { "filePath", "line", "column" }
            }
        },
        new 
        { 
            name = "find_references", 
            description = "Find all references to a symbol at a specific position",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    includePotential = new { type = "boolean", description = "Include potential references (default: false)" }
                },
                required = new string[] { "filePath", "line", "column" }
            }
        },
        new 
        { 
            name = "search_symbols", 
            description = "Search for symbols in a workspace by pattern",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    pattern = new { type = "string", description = "Search pattern" },
                    workspacePath = new { type = "string", description = "Path to workspace or solution" },
                    kinds = new { type = "array", items = new { type = "string" }, description = "Symbol kinds to include" },
                    fuzzy = new { type = "boolean", description = "Use fuzzy matching (default: false)" },
                    maxResults = new { type = "integer", description = "Maximum results to return (default: 100)" }
                },
                required = new string[] { "pattern", "workspacePath" }
            }
        },
        new 
        { 
            name = "get_diagnostics", 
            description = "Get compilation errors and warnings for a file or project",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to file or project" },
                    severities = new { type = "array", items = new { type = "string" }, description = "Severities to include (Error, Warning, Info, Hidden)" }
                },
                required = new string[] { "path" }
            }
        },
        new 
        { 
            name = "get_hover_info", 
            description = "Get hover information for a symbol at a specific position",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new string[] { "filePath", "line", "column" }
            }
        },
        new 
        { 
            name = "get_implementations", 
            description = "Find implementations of interfaces or abstract members",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new string[] { "filePath", "line", "column" }
            }
        },
        new 
        { 
            name = "get_document_symbols", 
            description = "Get all symbols defined in a document",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    includeMembers = new { type = "boolean", description = "Include class members (default: true)" }
                },
                required = new string[] { "filePath" }
            }
        },
        new 
        { 
            name = "get_call_hierarchy", 
            description = "Get incoming or outgoing call hierarchy for a method",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both' (default: 'both')" },
                    maxDepth = new { type = "integer", description = "Maximum depth to traverse (default: 2)" }
                },
                required = new string[] { "filePath", "line", "column" }
            }
        },
        new 
        { 
            name = "rename_symbol", 
            description = "Rename a symbol across the entire codebase",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the source file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    newName = new { type = "string", description = "New name for the symbol" },
                    preview = new { type = "boolean", description = "Preview changes without applying (default: true)" }
                },
                required = new string[] { "filePath", "line", "column", "newName" }
            }
        }
    };
}

// JSON-RPC types
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; set; }
    
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";
    
    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; set; }
    
    [JsonPropertyName("result")]
    public object? Result { get; set; }
    
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

// Minimal Program class
public partial class Program { }