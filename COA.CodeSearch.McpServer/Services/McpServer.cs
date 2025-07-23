using COA.Mcp.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Main MCP server that handles JSON-RPC communication via STDIO
/// </summary>
public class McpServer : BackgroundService, INotificationService
{
    private readonly ILogger<McpServer> _logger;
    private readonly ToolRegistry _toolRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writerLock = new(1, 1);

    public McpServer(
        ILogger<McpServer> logger,
        ToolRegistry toolRegistry)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("COA CodeSearch MCP Server starting...");

        using var reader = new StreamReader(Console.OpenStandardInput());
        _writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var line = await reader.ReadLineAsync(stoppingToken);
                if (line == null) 
                {
                    _logger.LogInformation("Input stream closed, shutting down");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonRpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON received: {Line}", line);
                    // Send parse error response
                    var errorResponse = new JsonRpcResponse
                    {
                        JsonRpc = "2.0",
                        Id = null!, // JSON-RPC 2.0 spec requires null id for parse errors
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcErrorCodes.ParseError,
                            Message = "Parse error",
                            Data = "Invalid JSON was received by the server"
                        }
                    };
                    await _writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse, _jsonOptions));
                    continue;
                }

                if (request == null) continue;

                var response = await HandleRequestAsync(request, stoppingToken);
                if (response != null)
                {
                    var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    await _writer.WriteLineAsync(responseJson);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Server shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in server loop");
                // Don't write errors to stdout - it corrupts the protocol
            }
        }

        _logger.LogInformation("COA CodeSearch MCP Server stopped");
    }

    private async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            object? result = request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "initialized" => null, // No response needed
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(request, cancellationToken),
                _ => throw new NotSupportedException($"Method '{request.Method}' is not supported")
            };

            if (request.Id == null)
                return null; // Notification, no response

            return new JsonRpcResponse
            {
                JsonRpc = "2.0",
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request {Method}", request.Method);

            if (request.Id == null)
                return null; // Can't send error for notification

            return new JsonRpcResponse
            {
                JsonRpc = "2.0",
                Id = request.Id,
                Error = CreateError(ex)
            };
        }
    }

    private InitializeResult HandleInitialize(JsonRpcRequest request)
    {
        InitializeRequest? initRequest = null;
        if (request.Params is JsonElement paramsElement)
        {
            initRequest = JsonSerializer.Deserialize<InitializeRequest>(paramsElement.GetRawText(), _jsonOptions);
        }
        
        _logger.LogInformation("Client connected: {Name} {Version}", 
            initRequest?.ClientInfo?.Name ?? "Unknown",
            initRequest?.ClientInfo?.Version ?? "Unknown");

        return new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo = new Implementation
            {
                Name = "COA CodeSearch MCP Server",
                Version = "2.0.0"
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new { }
            }
        };
    }

    private ListToolsResult HandleToolsList()
    {
        return new ListToolsResult
        {
            Tools = _toolRegistry.GetTools()
        };
    }

    private async Task<CallToolResult> HandleToolsCallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        CallToolRequest? callRequest = null;
        if (request.Params is JsonElement paramsElement)
        {
            callRequest = JsonSerializer.Deserialize<CallToolRequest>(paramsElement.GetRawText(), _jsonOptions);
        }
        if (callRequest == null)
        {
            throw new InvalidParametersException("Invalid tool call parameters");
        }

        JsonElement? arguments = null;
        if (callRequest.Arguments is JsonElement argElement)
        {
            arguments = argElement;
        }
        else if (callRequest.Arguments != null)
        {
            // Convert non-JsonElement arguments to JsonElement
            var json = JsonSerializer.Serialize(callRequest.Arguments, _jsonOptions);
            arguments = JsonSerializer.Deserialize<JsonElement>(json, _jsonOptions);
        }
        
        return await _toolRegistry.CallToolAsync(callRequest.Name, arguments, cancellationToken);
    }

    private JsonRpcError CreateError(Exception ex)
    {
        return ex switch
        {
            InvalidParametersException => new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InvalidParams,
                Message = "Invalid parameters",
                Data = ex.Message
            },
            NotSupportedException => new JsonRpcError
            {
                Code = JsonRpcErrorCodes.MethodNotFound,
                Message = "Method not found",
                Data = ex.Message
            },
            _ => new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InternalError,
                Message = "Internal error",
                Data = ex.Message
            }
        };
    }

    #region INotificationService Implementation

    public async Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken = default)
    {
        if (_writer == null)
        {
            _logger.LogWarning("Cannot send notification - writer not initialized");
            return;
        }

        await _writerLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(notification, _jsonOptions);
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification");
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task SendProgressAsync(string progressToken, int progress, int? total = null, string? message = null, CancellationToken cancellationToken = default)
    {
        var notification = new ProgressNotification(progressToken, progress, total, message);
        await SendNotificationAsync(notification, cancellationToken);
    }

    #endregion

    public override void Dispose()
    {
        _writer?.Dispose();
        _writerLock?.Dispose();
        base.Dispose();
    }
}