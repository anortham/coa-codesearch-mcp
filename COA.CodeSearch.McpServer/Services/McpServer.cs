using COA.Mcp.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
    private readonly IResourceRegistry _resourceRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    
    // Track active requests for cancellation support
    private readonly ConcurrentDictionary<object, CancellationTokenSource> _activeRequests = new();

    public McpServer(
        ILogger<McpServer> logger,
        ToolRegistry toolRegistry,
        IResourceRegistry resourceRegistry)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _resourceRegistry = resourceRegistry;
        
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
                    _logger.LogError(ex, "Failed to parse JSON-RPC request: {Line}", line);
                    var errorResponse = new JsonRpcResponse
                    {
                        Id = null!,
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

                // Handle notifications/cancelled specially
                if (request.Method == "notifications/cancelled" && request.Id == null)
                {
                    await HandleCancellationNotificationAsync(request);
                    continue;
                }

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

        // Cancel all active requests
        foreach (var cts in _activeRequests.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activeRequests.Clear();

        _logger.LogInformation("COA CodeSearch MCP Server stopped");
    }

    private Task HandleCancellationNotificationAsync(JsonRpcRequest notification)
    {
        try
        {
            // Extract the request ID from the params
            if (notification.Params is JsonElement paramsElement && 
                paramsElement.TryGetProperty("requestId", out var requestIdElement))
            {
                object? requestId = requestIdElement.ValueKind switch
                {
                    JsonValueKind.String => requestIdElement.GetString(),
                    JsonValueKind.Number => requestIdElement.GetInt64(),
                    _ => null
                };

                if (requestId != null && _activeRequests.TryRemove(requestId, out var cts))
                {
                    _logger.LogDebug("Cancelling request {RequestId}", requestId);
                    cts.Cancel();
                    cts.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling cancellation notification");
        }
        
        return Task.CompletedTask;
    }

    private async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        CancellationTokenSource? requestCts = null;
        CancellationToken requestToken = cancellationToken;

        try
        {
            // For requests with IDs, create a cancellation token that can be cancelled
            if (request.Id != null && request.Method == "tools/call")
            {
                requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestToken = requestCts.Token;
                _activeRequests[request.Id] = requestCts;
            }

            object? result = request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "initialized" => null, // No response needed
                "notifications/initialized" => null, // MCP client sends this as notification
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(request, requestToken),
                "resources/list" => await HandleResourcesListAsync(requestToken),
                "resources/read" => await HandleResourcesReadAsync(request, requestToken),
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
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Request {RequestId} was cancelled", request.Id);
            if (request.Id == null)
                return null;

            return new JsonRpcResponse
            {
                JsonRpc = "2.0",
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.OperationCancelled,
                    Message = "Operation cancelled",
                    Data = "The operation was cancelled by the client"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request {Method}", request.Method);
            if (request.Id == null)
                return null; // Notifications don't get error responses

            return new JsonRpcResponse
            {
                JsonRpc = "2.0",
                Id = request.Id,
                Error = CreateError(ex)
            };
        }
        finally
        {
            // Clean up the cancellation token for this request
            if (request.Id != null && requestCts != null)
            {
                _activeRequests.TryRemove(request.Id, out _);
                requestCts.Dispose();
            }
        }
    }

    private InitializeResult HandleInitialize(JsonRpcRequest request)
    {
        _logger.LogInformation("Initialize request received");
        return new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo = new Implementation
            {
                Name = "COA CodeSearch MCP Server",
                Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new { },
                Resources = new ResourceCapabilities
                {
                    Subscribe = false, // Not implemented yet
                    ListChanged = false // Not implemented yet
                }
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
        var callRequest = JsonSerializer.Deserialize<CallToolRequest>(
            JsonSerializer.Serialize(request.Params, _jsonOptions), 
            _jsonOptions);

        if (callRequest == null)
            throw new ArgumentException("Invalid tool call request");

        // Convert arguments to JsonElement for the tool registry
        JsonElement arguments = default;
        if (callRequest.Arguments != null)
        {
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

    private async Task<ListResourcesResult> HandleResourcesListAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling resources/list request");
        
        var resources = await _resourceRegistry.ListResourcesAsync(cancellationToken);
        
        return new ListResourcesResult
        {
            Resources = resources
        };
    }

    private async Task<ReadResourceResult> HandleResourcesReadAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling resources/read request");

        if (request.Params == null)
        {
            throw new ArgumentException("resources/read requires parameters");
        }

        ReadResourceRequest? readRequest;
        try
        {
            readRequest = JsonSerializer.Deserialize<ReadResourceRequest>(
                request.Params.ToString()!, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid parameters for resources/read: {ex.Message}", ex);
        }

        if (readRequest?.Uri == null)
        {
            throw new ArgumentException("resources/read requires a 'uri' parameter");
        }

        return await _resourceRegistry.ReadResourceAsync(readRequest.Uri, cancellationToken);
    }

    public override void Dispose()
    {
        _writer?.Dispose();
        _writerLock?.Dispose();
        base.Dispose();
    }
}