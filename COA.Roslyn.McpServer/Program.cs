using COA.Roslyn.McpServer.Services;
using COA.Roslyn.McpServer.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Register MSBuild before anything else
// Suppress MSBuild console output to avoid interfering with MCP protocol
Environment.SetEnvironmentVariable("MSBUILDLOGASYNC", "0");
Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
Environment.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");
Environment.SetEnvironmentVariable("MSBUILDLOGTASKINPUTS", "0");
Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "0");
Environment.SetEnvironmentVariable("MSBUILDCONSOLELOGGERPARAMETERS", "NoSummary;Verbosity=quiet");
Environment.SetEnvironmentVariable("MSBUILDENABLEALLPROPERTYFUNCTIONS", "1");
Environment.SetEnvironmentVariable("MSBUILDLOGVERBOSERETHROW", "0");
Environment.SetEnvironmentVariable("MSBUILDLOGIMPORTS", "0");
Environment.SetEnvironmentVariable("MSBUILDLOGGINGPREPROCESSOR", "0");
Environment.SetEnvironmentVariable("MSBUILDLOGALLPROJECTFROMSOLUTION", "0");
Environment.SetEnvironmentVariable("DOTNET_CLI_CAPTURE_TIMING", "0");
Environment.SetEnvironmentVariable("NUGET_SHOW_STACK", "false");

// Also suppress Roslyn analyzer output
Environment.SetEnvironmentVariable("ROSLYN_COMPILER_LOCATION", "");
Environment.SetEnvironmentVariable("ROSLYN_ANALYZERS_ENABLED", "false");

try
{
    MSBuildLocator.RegisterDefaults();
}
catch
{
    // Silently ignore MSBuild registration errors
}

// Handle command line arguments
if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
{
    Console.WriteLine("COA Roslyn MCP Server - High-performance code navigation for .NET");
    Console.WriteLine();
    Console.WriteLine("Usage: coa-roslyn-mcp [stdio]");
    Console.WriteLine();
    Console.WriteLine("Runs in STDIO mode for MCP clients (default)");
    return 0;
}

// Default to stdio mode - no need to require the argument

var builder = new HostBuilder()
    .ConfigureHostConfiguration(config =>
    {
        config.AddEnvironmentVariables("DOTNET_");
    })
    .ConfigureLogging((context, logging) =>
    {
        // No logging providers - complete silence for MCP compatibility
        logging.ClearProviders();
    })
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("MCP_");
    })
    .UseConsoleLifetime(options =>
    {
        options.SuppressStatusMessages = true;
    });


// Add services
builder.ConfigureServices((context, services) =>
{
    services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
    services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    services.AddSingleton<RoslynWorkspaceService>();
    services.AddSingleton<GoToDefinitionTool>();
    services.AddSingleton<FindReferencesTool>();
    services.AddSingleton<SearchSymbolsTool>();
    services.AddSingleton<GetDiagnosticsTool>();
    services.AddSingleton<GetHoverInfoTool>();
    services.AddSingleton<GetImplementationsTool>();
    services.AddSingleton<GetDocumentSymbolsTool>();
    services.AddSingleton<GetCallHierarchyTool>();
    services.AddSingleton<RenameSymbolTool>();
    services.AddSingleton<BatchOperationsTool>();
    services.AddSingleton<AdvancedSymbolSearchTool>();
    services.AddSingleton<DependencyAnalysisTool>();
    services.AddSingleton<ProjectStructureAnalysisTool>();
});

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
var batchOperationsTool = host.Services.GetRequiredService<BatchOperationsTool>();
var advancedSymbolSearchTool = host.Services.GetRequiredService<AdvancedSymbolSearchTool>();
var dependencyAnalysisTool = host.Services.GetRequiredService<DependencyAnalysisTool>();
var projectStructureTool = host.Services.GetRequiredService<ProjectStructureAnalysisTool>();

// Suppress startup log to avoid interfering with MCP protocol
// logger.LogInformation("COA Roslyn MCP Server starting...");

// JSON options
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

// Create a background task to run the MCP server
var serverTask = Task.Run(async () =>
{
    // Redirect stderr to null to prevent any error output
    Console.SetError(TextWriter.Null);
    
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    using var reader = new StreamReader(stdin);
    using var writer = new StreamWriter(stdout) { AutoFlush = true };

    while (true)
    {
        try
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, jsonOptions);
            if (request == null) continue;

            var response = await HandleRequest(request, goToDefTool, findRefsTool, searchTool, diagnosticsTool, hoverTool, implementationsTool, documentSymbolsTool, callHierarchyTool, renameSymbolTool, batchOperationsTool, advancedSymbolSearchTool, dependencyAnalysisTool, projectStructureTool, logger);
            var responseJson = JsonSerializer.Serialize(response, jsonOptions);
            await writer.WriteLineAsync(responseJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing JSON-RPC request");
            // DO NOT send error response here - just like Directus
        }
    }
});

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
    GetDocumentSymbolsTool documentSymbolsTool,
    GetCallHierarchyTool callHierarchyTool,
    RenameSymbolTool renameSymbolTool,
    BatchOperationsTool batchOperationsTool,
    AdvancedSymbolSearchTool advancedSymbolSearchTool,
    DependencyAnalysisTool dependencyAnalysisTool,
    ProjectStructureAnalysisTool projectStructureTool,
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
                
            case "initialized":
                // MCP protocol expects a response for initialized
                return new JsonRpcResponse
                {
                    JsonRpc = "2.0",
                    Id = request.Id,
                    Result = new { }
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
                            
                        "batch_operations" => await batchOperationsTool.ExecuteAsync(
                            args.GetProperty("operations")),
                            
                        "advanced_symbol_search" => await advancedSymbolSearchTool.ExecuteAsync(
                            args.GetProperty("pattern").GetString()!,
                            args.GetProperty("workspacePath").GetString()!,
                            args.TryGetProperty("kinds", out var ak) && ak.ValueKind == JsonValueKind.Array
                                ? ak.EnumerateArray().Select(e => e.GetString()!).ToArray()
                                : null,
                            args.TryGetProperty("accessibility", out var aa) && aa.ValueKind == JsonValueKind.Array
                                ? aa.EnumerateArray().Select(e => e.GetString()!).ToArray()
                                : null,
                            args.TryGetProperty("isStatic", out var ais) ? ais.GetBoolean() : null,
                            args.TryGetProperty("isAbstract", out var aia) ? aia.GetBoolean() : null,
                            args.TryGetProperty("isVirtual", out var aiv) ? aiv.GetBoolean() : null,
                            args.TryGetProperty("isOverride", out var aio) ? aio.GetBoolean() : null,
                            args.TryGetProperty("returnType", out var art) ? art.GetString() : null,
                            args.TryGetProperty("containingType", out var act) ? act.GetString() : null,
                            args.TryGetProperty("containingNamespace", out var acn) ? acn.GetString() : null,
                            args.TryGetProperty("fuzzy", out var af) && af.GetBoolean(),
                            args.TryGetProperty("maxResults", out var amr) ? amr.GetInt32() : 100),
                            
                        "dependency_analysis" => await dependencyAnalysisTool.ExecuteAsync(
                            args.GetProperty("symbol").GetString()!,
                            args.GetProperty("workspacePath").GetString()!,
                            args.TryGetProperty("direction", out var ddir) ? ddir.GetString() ?? "both" : "both",
                            args.TryGetProperty("depth", out var ddepth) ? ddepth.GetInt32() : 3,
                            args.TryGetProperty("includeTests", out var dit) && dit.GetBoolean(),
                            args.TryGetProperty("includeExternalDependencies", out var died) && died.GetBoolean()),
                            
                        "project_structure_analysis" => await projectStructureTool.ExecuteAsync(
                            args.GetProperty("workspacePath").GetString()!,
                            args.TryGetProperty("includeMetrics", out var pim) ? pim.GetBoolean() : true,
                            args.TryGetProperty("includeFiles", out var pif) && pif.GetBoolean(),
                            args.TryGetProperty("includeNuGetPackages", out var pinp) && pinp.GetBoolean()),
                            
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
        },
        new 
        { 
            name = "batch_operations", 
            description = "Execute multiple Roslyn operations in a single call",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    operations = new 
                    { 
                        type = "array", 
                        items = new 
                        { 
                            type = "object",
                            properties = new
                            {
                                type = new { type = "string", description = "Operation type (search_symbols, find_references, etc.)" }
                            },
                            required = new string[] { "type" }
                        },
                        description = "Array of operations to execute" 
                    }
                },
                required = new string[] { "operations" }
            }
        },
        new 
        { 
            name = "advanced_symbol_search", 
            description = "Advanced symbol search with filtering by accessibility, static, return type, etc.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    pattern = new { type = "string", description = "Search pattern" },
                    workspacePath = new { type = "string", description = "Path to workspace or solution" },
                    kinds = new { type = "array", items = new { type = "string" }, description = "Symbol kinds to include (Method, Property, Class, etc.)" },
                    accessibility = new { type = "array", items = new { type = "string" }, description = "Accessibility levels (Public, Private, Internal, etc.)" },
                    isStatic = new { type = "boolean", description = "Filter by static members" },
                    isAbstract = new { type = "boolean", description = "Filter by abstract members" },
                    isVirtual = new { type = "boolean", description = "Filter by virtual members" },
                    isOverride = new { type = "boolean", description = "Filter by override members" },
                    returnType = new { type = "string", description = "Filter by return type (for methods)" },
                    containingType = new { type = "string", description = "Filter by containing type" },
                    containingNamespace = new { type = "string", description = "Filter by containing namespace" },
                    fuzzy = new { type = "boolean", description = "Use fuzzy matching (default: false)" },
                    maxResults = new { type = "integer", description = "Maximum results to return (default: 100)" }
                },
                required = new string[] { "pattern", "workspacePath" }
            }
        },
        new 
        { 
            name = "dependency_analysis", 
            description = "Analyze code dependencies (incoming/outgoing) for a symbol",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string", description = "Symbol name to analyze" },
                    workspacePath = new { type = "string", description = "Path to workspace or solution" },
                    direction = new { type = "string", description = "Direction: 'incoming', 'outgoing', or 'both' (default: 'both')" },
                    depth = new { type = "integer", description = "Analysis depth (default: 3)" },
                    includeTests = new { type = "boolean", description = "Include test projects (default: false)" },
                    includeExternalDependencies = new { type = "boolean", description = "Include external dependencies (default: false)" }
                },
                required = new string[] { "symbol", "workspacePath" }
            }
        },
        new 
        { 
            name = "project_structure_analysis", 
            description = "Analyze project structure including dependencies, metrics, and files",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    workspacePath = new { type = "string", description = "Path to workspace or solution" },
                    includeMetrics = new { type = "boolean", description = "Include code metrics (default: true)" },
                    includeFiles = new { type = "boolean", description = "Include source file listing (default: false)" },
                    includeNuGetPackages = new { type = "boolean", description = "Include NuGet package analysis (default: false)" }
                },
                required = new string[] { "workspacePath" }
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