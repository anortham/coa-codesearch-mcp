using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// LSP client for communicating with the Razor Language Server (rzls.exe)
/// Handles JSON-RPC protocol communication over STDIO
/// </summary>
public class RazorLspClient : IDisposable
{
    private readonly ILogger<RazorLspClient> _logger;
    private readonly RazorServerLocator _serverLocator;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    
    private Process? _razorProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private int _nextRequestId = 1;
    private bool _isInitialized;
    private bool _disposed;
    private readonly Timer _healthCheckTimer;

    public RazorLspClient(ILogger<RazorLspClient> logger, RazorServerLocator serverLocator, IMemoryCache cache)
    {
        _logger = logger;
        _serverLocator = serverLocator;
        _cache = cache;
        
        // Setup health check timer to monitor connection every 30 seconds
        _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Gets whether the Razor LSP server is running and initialized
    /// </summary>
    public bool IsAvailable => _razorProcess != null && !_razorProcess.HasExited && _isInitialized;

    /// <summary>
    /// Initializes the Razor Language Server process and LSP connection
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return false;
        }

        await _requestSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsAvailable)
            {
                return true;
            }

            // Find the Razor server executable
            var serverPath = await _serverLocator.FindRazorServerAsync();
            if (string.IsNullOrEmpty(serverPath))
            {
                _logger.LogWarning("Razor Language Server not found. {Instructions}", 
                    _serverLocator.GetInstallationInstructions());
                return false;
            }

            // Start the Razor server process
            _logger.LogInformation("Starting Razor Language Server: {Path}", serverPath);
            
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = "--stdio",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            _razorProcess = Process.Start(startInfo);
            if (_razorProcess == null)
            {
                _logger.LogError("Failed to start Razor Language Server process");
                return false;
            }

            _stdin = _razorProcess.StandardInput;
            _stdout = _razorProcess.StandardOutput;

            // Start reading responses
            _ = Task.Run(ReadResponsesAsync, _disposeCts.Token);

            // Initialize LSP connection
            var initializeResult = await SendInitializeRequestAsync(cancellationToken);
            if (initializeResult == null)
            {
                _logger.LogError("Failed to initialize Razor Language Server");
                await ShutdownAsync();
                return false;
            }

            // Send initialized notification
            await SendNotificationAsync("initialized", new JsonObject(), cancellationToken);
            
            _isInitialized = true;
            _logger.LogInformation("Razor Language Server initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Razor Language Server");
            await ShutdownAsync();
            return false;
        }
        finally
        {
            _requestSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends a textDocument/definition request to get symbol definitions
    /// </summary>
    public async Task<JsonNode?> GetDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var textDocumentParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1, // LSP uses 0-based indexing
                ["character"] = column - 1
            }
        };

        return await SendRequestAsync("textDocument/definition", textDocumentParams, cancellationToken);
    }

    /// <summary>
    /// Sends a textDocument/references request to find symbol references
    /// </summary>
    public async Task<JsonNode?> FindReferencesAsync(string filePath, int line, int column, bool includeDeclaration, CancellationToken cancellationToken = default)
    {
        var referenceParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1,
                ["character"] = column - 1
            },
            ["context"] = new JsonObject
            {
                ["includeDeclaration"] = includeDeclaration
            }
        };

        return await SendRequestAsync("textDocument/references", referenceParams, cancellationToken);
    }

    /// <summary>
    /// Sends a textDocument/hover request to get hover information
    /// Implements caching for better performance
    /// </summary>
    public async Task<JsonNode?> GetHoverInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        // Create cache key for hover request
        var cacheKey = $"hover:{filePath}:{line}:{column}";
        
        // Check cache first
        if (_cache.TryGetValue(cacheKey, out JsonNode? cachedResult))
        {
            _logger.LogTrace("Cache hit for hover request: {CacheKey}", cacheKey);
            return cachedResult;
        }

        var hoverParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1,
                ["character"] = column - 1
            }
        };

        var result = await SendRequestAsync("textDocument/hover", hoverParams, cancellationToken);
        
        // Cache the result for 5 minutes if it's not null
        if (result != null)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            _logger.LogTrace("Cached hover result: {CacheKey}", cacheKey);
        }

        return result;
    }

    /// <summary>
    /// Sends a textDocument/rename request to rename a symbol
    /// </summary>
    public async Task<JsonNode?> RenameSymbolAsync(string filePath, int line, int column, string newName, CancellationToken cancellationToken = default)
    {
        var renameParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1,
                ["character"] = column - 1
            },
            ["newName"] = newName
        };

        return await SendRequestAsync("textDocument/rename", renameParams, cancellationToken);
    }

    /// <summary>
    /// Sends a textDocument/documentSymbol request to get document symbols
    /// Implements caching for better performance
    /// </summary>
    public async Task<JsonNode?> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Create cache key for document symbols
        var cacheKey = $"symbols:{filePath}";
        
        // Check cache first
        if (_cache.TryGetValue(cacheKey, out JsonNode? cachedResult))
        {
            _logger.LogTrace("Cache hit for document symbols: {CacheKey}", cacheKey);
            return cachedResult;
        }

        var symbolParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            }
        };

        var result = await SendRequestAsync("textDocument/documentSymbol", symbolParams, cancellationToken);
        
        // Cache the result for 10 minutes if it's not null
        if (result != null)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            _logger.LogTrace("Cached document symbols: {CacheKey}", cacheKey);
        }

        return result;
    }

    /// <summary>
    /// Notifies the server that a document was opened
    /// </summary>
    public async Task OpenDocumentAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var didOpenParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}",
                ["languageId"] = "razor",
                ["version"] = 1,
                ["text"] = content
            }
        };

        await SendNotificationAsync("textDocument/didOpen", didOpenParams, cancellationToken);
    }

    /// <summary>
    /// Notifies the server that a document was changed
    /// Invalidates cache for the changed document
    /// </summary>
    public async Task ChangeDocumentAsync(string filePath, string content, int version, CancellationToken cancellationToken = default)
    {
        // Invalidate cache for this file when it changes
        InvalidateDocumentCache(filePath);

        var didChangeParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}",
                ["version"] = version
            },
            ["contentChanges"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = content
                }
            }
        };

        await SendNotificationAsync("textDocument/didChange", didChangeParams, cancellationToken);
    }

    /// <summary>
    /// Notifies the server that a document was closed
    /// </summary>
    public async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var didCloseParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            }
        };

        await SendNotificationAsync("textDocument/didClose", didCloseParams, cancellationToken);
    }

    /// <summary>
    /// Gets code actions available at a specific location
    /// </summary>
    public async Task<JsonNode?> GetCodeActionsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var codeActionParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject
                {
                    ["line"] = line - 1,
                    ["character"] = column - 1
                },
                ["end"] = new JsonObject
                {
                    ["line"] = line - 1,
                    ["character"] = column - 1
                }
            },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray()
            }
        };

        return await SendRequestAsync("textDocument/codeAction", codeActionParams, cancellationToken);
    }

    /// <summary>
    /// Gets completion items at a specific location
    /// </summary>
    public async Task<JsonNode?> GetCompletionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var completionParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1,
                ["character"] = column - 1
            }
        };

        return await SendRequestAsync("textDocument/completion", completionParams, cancellationToken);
    }

    /// <summary>
    /// Gets signature help at a specific location
    /// </summary>
    public async Task<JsonNode?> GetSignatureHelpAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var signatureParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            },
            ["position"] = new JsonObject
            {
                ["line"] = line - 1,
                ["character"] = column - 1
            }
        };

        return await SendRequestAsync("textDocument/signatureHelp", signatureParams, cancellationToken);
    }

    /// <summary>
    /// Requests diagnostics for a specific document
    /// </summary>
    public async Task RequestDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var diagnosticParams = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = $"file:///{filePath.Replace('\\', '/')}"
            }
        };

        // Send as notification since diagnostics are typically published by the server
        await SendNotificationAsync("textDocument/diagnostic", diagnosticParams, cancellationToken);
    }

    private async Task<JsonNode?> SendInitializeRequestAsync(CancellationToken cancellationToken)
    {
        var initializeParams = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "COA CodeSearch MCP Server",
                ["version"] = "1.0.0"
            },
            ["capabilities"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["definition"] = new JsonObject
                    {
                        ["linkSupport"] = true
                    },
                    ["references"] = new JsonObject(),
                    ["hover"] = new JsonObject
                    {
                        ["contentFormat"] = new JsonArray { "markdown", "plaintext" }
                    },
                    ["rename"] = new JsonObject
                    {
                        ["prepareSupport"] = true
                    },
                    ["documentSymbol"] = new JsonObject(),
                    ["synchronization"] = new JsonObject
                    {
                        ["didOpen"] = true,
                        ["didChange"] = true,
                        ["didClose"] = true
                    }
                }
            }
        };

        return await SendRequestAsync("initialize", initializeParams, cancellationToken);
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonNode parameters, CancellationToken cancellationToken)
    {
        if (!IsAvailable && method != "initialize")
        {
            _logger.LogWarning("Razor LSP server not available for request: {Method}", method);
            return null;
        }

        const int maxRetries = 3;
        const int baseDelayMs = 100;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var tcs = new TaskCompletionSource<JsonNode?>();
            
            _pendingRequests[requestId] = tcs;

            try
            {
                var request = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = requestId,
                    ["method"] = method,
                    ["params"] = parameters
                };

                await SendMessageAsync(request, cancellationToken);
                
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout
                
                await using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var result = await tcs.Task;
                    
                    // Success - log if this was a retry
                    if (attempt > 0)
                    {
                        _logger.LogInformation("LSP request {Method} succeeded on attempt {Attempt}", method, attempt + 1);
                    }
                    
                    return result;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User cancellation - don't retry
                _logger.LogDebug("LSP request {Method} was cancelled by user", method);
                _pendingRequests.TryRemove(requestId, out _);
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries - 1 && IsRetryableException(ex))
            {
                _logger.LogWarning(ex, "LSP request {Method} failed on attempt {Attempt}, retrying...", method, attempt + 1);
                
                // Exponential backoff with jitter
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt) + Random.Shared.Next(0, 50));
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send LSP request: {Method} after {Attempts} attempts", method, attempt + 1);
                tcs.TrySetException(ex);
                return null;
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        _logger.LogError("LSP request {Method} failed after {MaxRetries} attempts", method, maxRetries);
        return null;
    }

    private async Task SendNotificationAsync(string method, JsonNode parameters, CancellationToken cancellationToken)
    {
        if (!IsAvailable && method != "initialized")
        {
            _logger.LogWarning("Razor LSP server not available for notification: {Method}", method);
            return;
        }

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters
        };

        await SendMessageAsync(notification, cancellationToken);
    }

    private async Task SendMessageAsync(JsonNode message, CancellationToken cancellationToken)
    {
        if (_stdin == null)
        {
            throw new InvalidOperationException("LSP server not initialized");
        }

        var content = message.ToJsonString();
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var header = $"Content-Length: {contentBytes.Length}\r\n\r\n";

        await _stdin.WriteAsync(header.AsMemory(), cancellationToken);
        await _stdin.WriteAsync(content.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);

        _logger.LogTrace("Sent LSP message: {Content}", content);
    }

    private async Task ReadResponsesAsync()
    {
        if (_stdout == null)
        {
            return;
        }

        try
        {
            while (!_disposeCts.Token.IsCancellationRequested && _stdout != null)
            {
                var message = await ReadMessageAsync(_stdout, _disposeCts.Token);
                if (message != null)
                {
                    await ProcessMessageAsync(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading LSP responses");
        }
    }

    private async Task<JsonNode?> ReadMessageAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            // Read headers
            var headers = new Dictionary<string, string>();
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break; // End of headers
                }

                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = line[..colonIndex].Trim();
                    var value = line[(colonIndex + 1)..].Trim();
                    headers[key] = value;
                }
            }

            // Read content
            if (headers.TryGetValue("Content-Length", out var contentLengthStr) &&
                int.TryParse(contentLengthStr, out var contentLength))
            {
                var buffer = new char[contentLength];
                var totalRead = 0;
                
                while (totalRead < contentLength)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Unexpected end of stream");
                    }
                    totalRead += read;
                }

                var content = new string(buffer);
                _logger.LogTrace("Received LSP message: {Content}", content);
                
                return JsonNode.Parse(content);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading LSP message");
        }

        return null;
    }

    private async Task ProcessMessageAsync(JsonNode message)
    {
        try
        {
            if (message["id"] != null)
            {
                // Response to a request
                var id = message["id"]?.GetValue<int>() ?? 0;
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (message["error"] != null)
                    {
                        var error = message["error"]?.ToJsonString() ?? "Unknown error";
                        tcs.TrySetException(new Exception($"LSP error: {error}"));
                    }
                    else
                    {
                        tcs.TrySetResult(message["result"]);
                    }
                }
            }
            else
            {
                // Notification from server
                var method = message["method"]?.GetValue<string>();
                if (method != null)
                {
                    await HandleNotificationAsync(method, message["params"]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LSP message");
        }
    }

    private async Task HandleNotificationAsync(string method, JsonNode? parameters)
    {
        _logger.LogTrace("Received LSP notification: {Method}", method);
        
        // Handle server notifications as needed
        switch (method)
        {
            case "window/logMessage":
                if (parameters?["message"] != null)
                {
                    var logMessage = parameters["message"]?.GetValue<string>();
                    _logger.LogInformation("Razor LSP: {Message}", logMessage);
                }
                break;
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Determines if an exception is retryable for LSP requests
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        return ex switch
        {
            IOException => true,
            TimeoutException => true,
            InvalidOperationException when ex.Message.Contains("stream") => true,
            JsonException => false, // Don't retry JSON parsing errors
            ArgumentException => false, // Don't retry parameter validation errors
            _ => false
        };
    }

    /// <summary>
    /// Invalidates all cached data for a specific document
    /// </summary>
    private void InvalidateDocumentCache(string filePath)
    {
        try
        {
            // Remove all cache entries related to this file
            var patterns = new[]
            {
                $"hover:{filePath}:",
                $"symbols:{filePath}",
                $"definition:{filePath}:",
                $"references:{filePath}:",
                $"completion:{filePath}:",
                $"codeactions:{filePath}:",
                $"signature:{filePath}:"
            };

            // Note: IMemoryCache doesn't have a pattern-based removal method
            // In a production system, you might want to use a more sophisticated caching solution
            // For now, we'll just log that cache invalidation was requested
            _logger.LogTrace("Cache invalidation requested for file: {FilePath}", filePath);
            
            // Alternative: Use a custom cache wrapper that tracks keys by file path
            // or implement a tag-based cache invalidation system
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating cache for file: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Performs periodic health check on the LSP connection
    /// </summary>
    private async void PerformHealthCheck(object? state)
    {
        if (_disposed || !_isInitialized)
        {
            return;
        }

        try
        {
            // Check if process is still running
            if (_razorProcess == null || _razorProcess.HasExited)
            {
                _logger.LogWarning("Razor LSP server process has exited unexpectedly, attempting to restart...");
                await RestartServerAsync();
                return;
            }

            // Optional: Send a lightweight request to verify responsiveness
            // This could be a simple workspace/configuration request
            _logger.LogTrace("Razor LSP server health check passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Razor LSP server health check");
        }
    }

    /// <summary>
    /// Attempts to restart the LSP server after a failure
    /// </summary>
    private async Task RestartServerAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to restart Razor LSP server...");

            // Shutdown current instance
            await ShutdownAsync();

            // Wait a moment before restarting
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Try to reinitialize
            var restarted = await InitializeAsync(CancellationToken.None);
            if (restarted)
            {
                _logger.LogInformation("Razor LSP server restarted successfully");
            }
            else
            {
                _logger.LogError("Failed to restart Razor LSP server");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting Razor LSP server");
        }
    }

    private async Task ShutdownAsync()
    {
        try
        {
            if (_isInitialized && _stdin != null)
            {
                // Send shutdown request
                await SendRequestAsync("shutdown", new JsonObject(), CancellationToken.None);
                
                // Send exit notification
                await SendNotificationAsync("exit", new JsonObject(), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LSP shutdown");
        }

        _isInitialized = false;
        
        // Clean up process
        if (_razorProcess != null && !_razorProcess.HasExited)
        {
            try
            {
                _razorProcess.Kill();
                await _razorProcess.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error killing Razor process");
            }
        }

        _stdin?.Dispose();
        _stdout?.Dispose();
        _razorProcess?.Dispose();
        
        _stdin = null;
        _stdout = null;
        _razorProcess = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        
        // Stop health check timer
        _healthCheckTimer?.Dispose();
        
        // Complete any pending requests
        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();

        _ = Task.Run(async () =>
        {
            try
            {
                await ShutdownAsync();
            }
            finally
            {
                _requestSemaphore.Dispose();
                _disposeCts.Dispose();
            }
        });
    }
}