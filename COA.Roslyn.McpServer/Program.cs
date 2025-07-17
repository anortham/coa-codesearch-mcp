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

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// Get services
var goToDefTool = host.Services.GetRequiredService<GoToDefinitionTool>();
var findRefsTool = host.Services.GetRequiredService<FindReferencesTool>();
var searchTool = host.Services.GetRequiredService<SearchSymbolsTool>();
var diagnosticsTool = host.Services.GetRequiredService<GetDiagnosticsTool>();
var hoverTool = host.Services.GetRequiredService<GetHoverInfoTool>();
var implementationsTool = host.Services.GetRequiredService<GetImplementationsTool>();

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
            
            if (line.StartsWith("Content-Length:"))
            {
                var length = int.Parse(line.Substring("Content-Length:".Length).Trim());
                await reader.ReadLineAsync(); // Empty line
                
                var buffer = new char[length];
                var read = await reader.ReadBlockAsync(buffer, 0, length);
                var json = new string(buffer, 0, read);
                
                logger.LogDebug("Received: {Json}", json);
                
                try
                {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(json, jsonOptions);
                    if (request != null)
                    {
                        var response = await HandleRequest(request, goToDefTool, findRefsTool, searchTool, diagnosticsTool, hoverTool, implementationsTool, logger);
                        var responseJson = JsonSerializer.Serialize(response, jsonOptions);
                        
                        await writer.WriteLineAsync($"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}");
                        await writer.WriteLineAsync();
                        await writer.WriteAsync(responseJson);
                        
                        logger.LogDebug("Sent: {Json}", responseJson);
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
                    await writer.WriteLineAsync($"Content-Length: {Encoding.UTF8.GetByteCount(errorJson)}");
                    await writer.WriteLineAsync();
                    await writer.WriteAsync(errorJson);
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Fatal error in message loop");
    }
}, cts.Token);

// Send initialize response
var initResponse = new
{
    protocolVersion = "0.1.0",
    capabilities = new
    {
        tools = new
        {
            go_to_definition = new
            {
                description = "Navigate to the definition of a symbol",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "The absolute path to the file" },
                        line = new { type = "integer", description = "The line number (1-based)" },
                        column = new { type = "integer", description = "The column number (1-based)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            find_references = new
            {
                description = "Find all references to a symbol",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "The absolute path to the file" },
                        line = new { type = "integer", description = "The line number (1-based)" },
                        column = new { type = "integer", description = "The column number (1-based)" },
                        includePotential = new { type = "boolean", description = "Include potential references", @default = false }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            search_symbols = new
            {
                description = "Search for symbols in the workspace",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Search pattern (supports wildcards)" },
                        workspacePath = new { type = "string", description = "Path to solution or project" },
                        kinds = new { type = "array", items = new { type = "string" }, description = "Symbol kinds" },
                        fuzzy = new { type = "boolean", description = "Use fuzzy matching", @default = false },
                        maxResults = new { type = "integer", description = "Max results", @default = 100 }
                    },
                    required = new[] { "pattern", "workspacePath" }
                }
            }
        }
    },
    serverInfo = new
    {
        name = "coa-roslyn-mcp",
        version = "1.0.0"
    }
};

logger.LogInformation("MCP Server initialized: {Info}", JsonSerializer.Serialize(initResponse, jsonOptions));

// Keep the host running
await host.RunAsync();
return 0;

static async Task<JsonRpcResponse> HandleRequest(
    JsonRpcRequest request,
    GoToDefinitionTool goToDefTool,
    FindReferencesTool findRefsTool,
    SearchSymbolsTool searchTool,
    GetDiagnosticsTool diagnosticsTool,
    GetHoverInfoTool hoverTool,
    GetImplementationsTool implementationsTool,
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
                        protocolVersion = "0.1.0",
                        capabilities = new { tools = true },
                        serverInfo = new { name = "coa-roslyn-mcp", version = "1.0.0" }
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
                            
                        _ => throw new NotSupportedException($"Unknown tool: {toolName}")
                    };
                    
                    return new JsonRpcResponse
                    {
                        JsonRpc = "2.0",
                        Id = request.Id,
                        Result = result
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
                        tools = new[]
                        {
                            new { name = "go_to_definition", description = "Navigate to symbol definition" },
                            new { name = "find_references", description = "Find all references" },
                            new { name = "search_symbols", description = "Search for symbols" },
                            new { name = "get_diagnostics", description = "Get compilation errors and warnings" },
                            new { name = "get_hover_info", description = "Get symbol information at position" },
                            new { name = "get_implementations", description = "Find implementations of interfaces/abstract members" }
                        }
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