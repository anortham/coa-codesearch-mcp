using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for analyzing TypeScript code using the TypeScript Language Service via Node.js interop
/// </summary>
public class TypeScriptAnalysisService : IDisposable
{
    private readonly ILogger<TypeScriptAnalysisService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TypeScriptInstaller _installer;
    private readonly string _nodeExecutable;
    private string? _tsServerPath;
    private Process? _tsServerProcess;
    private StreamWriter? _tsServerInput;
    private StreamReader? _tsServerOutput;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private int _requestSequence = 0;
    private bool _disposed;
    private bool _initialized = false;

    /// <summary>
    /// Gets whether the TypeScript server is available and ready to process requests
    /// </summary>
    public bool IsAvailable => _initialized && !string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath);

    public TypeScriptAnalysisService(
        ILogger<TypeScriptAnalysisService> logger,
        IConfiguration configuration,
        TypeScriptInstaller installer)
    {
        _logger = logger;
        _configuration = configuration;
        _installer = installer;
        
        // Find Node.js executable
        _nodeExecutable = FindNodeExecutable();
        if (string.IsNullOrEmpty(_nodeExecutable))
        {
            _logger.LogWarning("Node.js is required for TypeScript analysis but was not found in PATH. TypeScript features will be limited.");
        }
        else
        {
            _logger.LogInformation("Node.js found at {NodePath}", _nodeExecutable);
        }
    }

    private async Task<bool> InitializeAsync()
    {
        if (_initialized)
        {
            return true;
        }

        // Check if Node.js is available
        if (string.IsNullOrEmpty(_nodeExecutable))
        {
            _logger.LogError("Node.js executable not found in PATH. TypeScript features require Node.js to be installed.");
            return false;
        }

        try
        {
            // First check configured path
            _tsServerPath = _configuration["TypeScript:ServerPath"];
            if (!string.IsNullOrEmpty(_tsServerPath))
            {
                _logger.LogDebug("Checking configured TypeScript path: {Path}", _tsServerPath);
                if (!File.Exists(_tsServerPath))
                {
                    _logger.LogWarning("Configured TypeScript path does not exist: {Path}", _tsServerPath);
                    _tsServerPath = null;
                }
            }
            
            // Then check local node_modules
            if (string.IsNullOrEmpty(_tsServerPath))
            {
                var localPath = Path.Combine(AppContext.BaseDirectory, "node_modules", "typescript", "lib", "tsserver.js");
                _logger.LogDebug("Checking local TypeScript path: {Path}", localPath);
                if (File.Exists(localPath))
                {
                    _tsServerPath = localPath;
                }
                else
                {
                    _logger.LogDebug("TypeScript not found at local path: {Path}", localPath);
                }
            }
            
            // If still not found, try to install
            if (string.IsNullOrEmpty(_tsServerPath) || !File.Exists(_tsServerPath))
            {
                _logger.LogInformation("TypeScript not found locally. Attempting automatic installation...");
                var installedPath = await _installer.GetTsServerPathAsync();
                if (!string.IsNullOrEmpty(installedPath))
                {
                    _tsServerPath = installedPath;
                }
                else
                {
                    _logger.LogError("TypeScript installation failed. Check that npm is available and internet connection is working.");
                }
            }

            if (!string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath))
            {
                _logger.LogInformation("TypeScript Analysis Service initialized with tsserver at {TsServerPath}", _tsServerPath);
                _initialized = true;
                return true;
            }
            else
            {
                _logger.LogError("TypeScript server could not be initialized. tsserver.js not found at any location.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TypeScript initialization");
            return false;
        }
    }

    /// <summary>
    /// Find Node.js executable in the system PATH
    /// </summary>
    private string FindNodeExecutable()
    {
        try
        {
            var nodeNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new[] { "node.exe", "node" }
                : new[] { "node" };
            
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            
            // First check PATH
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                
                foreach (var nodeName in nodeNames)
                {
                    try
                    {
                        var fullPath = Path.Combine(dir, nodeName);
                        if (File.Exists(fullPath))
                        {
                            _logger.LogDebug("Found Node.js in PATH at: {Path}", fullPath);
                            return fullPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking path {Path}/{Node}", dir, nodeName);
                    }
                }
            }
            
            // Check common installation paths
            var commonPaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[]
                {
                    @"C:\Program Files\nodejs\node.exe",
                    @"C:\Program Files (x86)\nodejs\node.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe")
                }
                : new[]
                {
                    "/usr/local/bin/node",
                    "/usr/bin/node",
                    "/opt/homebrew/bin/node",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm", "versions", "node", "default", "bin", "node")
                };
            
            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogDebug("Found Node.js at common location: {Path}", path);
                    return path;
                }
            }
            
            _logger.LogWarning("Node.js executable not found in PATH or common locations");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding Node.js executable");
            return string.Empty;
        }
    }

    /// <summary>
    /// Detect tsconfig.json files in a workspace
    /// </summary>
    public async Task<List<string>> DetectTypeScriptProjectsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var tsconfigFiles = new List<string>();
        
        try
        {
            await Task.Run(() =>
            {
                var searchPattern = "tsconfig*.json";
                var files = Directory.GetFiles(workspacePath, searchPattern, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                    
                tsconfigFiles.AddRange(files);
                
                // Also look for package.json files that might indicate TypeScript projects
                var packageJsonFiles = Directory.GetFiles(workspacePath, "package.json", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
                    
                foreach (var packageJson in packageJsonFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(packageJson);
                        if (content.Contains("\"typescript\"", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("\"@types/", StringComparison.OrdinalIgnoreCase))
                        {
                            var dir = Path.GetDirectoryName(packageJson);
                            if (dir != null && !tsconfigFiles.Any(f => Path.GetDirectoryName(f) == dir))
                            {
                                // This directory has TypeScript but no tsconfig.json
                                _logger.LogInformation("Found TypeScript project without tsconfig.json at {Path}", dir);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read package.json at {Path}", packageJson);
                    }
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting TypeScript projects in {WorkspacePath}", workspacePath);
        }
        
        _logger.LogInformation("Found {Count} TypeScript projects in {WorkspacePath}", tsconfigFiles.Count, workspacePath);
        return tsconfigFiles;
    }

    /// <summary>
    /// Start the TypeScript language server process
    /// </summary>
    private async Task EnsureServerStartedAsync()
    {
        // Initialize if not already done
        if (!_initialized && !await InitializeAsync())
        {
            var nodeStatus = string.IsNullOrEmpty(_nodeExecutable) ? "Node.js not found in PATH" : $"Node.js found at {_nodeExecutable}";
            var tsStatus = string.IsNullOrEmpty(_tsServerPath) ? "tsserver.js not found" : $"tsserver.js path: {_tsServerPath}";
            throw new InvalidOperationException($"TypeScript server is not available. {nodeStatus}. {tsStatus}");
        }

        if (_tsServerProcess != null && !_tsServerProcess.HasExited)
        {
            return;
        }
        
        _logger.LogInformation("Starting TypeScript language server...");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _nodeExecutable,
            Arguments = $"\"{_tsServerPath}\" --stdio",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            Environment =
            {
                ["TSS_LOG"] = "-level verbose -file " + Path.Combine(Path.GetTempPath(), "tsserver.log")
            }
        };
        
        _tsServerProcess = new Process { StartInfo = startInfo };
        
        // Set up error stream reading
        _tsServerProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("TypeScript server error: {Error}", e.Data);
            }
        };
        
        _tsServerProcess.Start();
        _tsServerProcess.BeginErrorReadLine();
        
        _tsServerInput = _tsServerProcess.StandardInput;
        _tsServerOutput = _tsServerProcess.StandardOutput;
        
        // Read the initial server ready message
        var readyMsg = await _tsServerOutput.ReadLineAsync();
        if (string.IsNullOrEmpty(readyMsg))
        {
            _logger.LogError("TypeScript server failed to start - no ready message received");
            throw new InvalidOperationException("TypeScript server failed to start");
        }
        _logger.LogDebug("TypeScript server started: {Message}", readyMsg);
    }

    /// <summary>
    /// Send a request to the TypeScript server and get the response
    /// </summary>
    private async Task<JsonDocument?> SendRequestAsync(object request, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureServerStartedAsync();
            
            var requestJson = JsonSerializer.Serialize(request);
            _logger.LogDebug("Sending TypeScript request: {Request}", requestJson);
            await _tsServerInput!.WriteLineAsync(requestJson);
            await _tsServerInput.FlushAsync();
            
            // Read response - tsserver may send multiple messages (events) before the actual response
            string? response = null;
            var requestSeq = request.GetType().GetProperty("seq")?.GetValue(request);
            
            // Add a timeout to prevent infinite waiting
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var readTask = _tsServerOutput!.ReadLineAsync();
                var completedTask = await Task.WhenAny(readTask, Task.Delay(-1, timeoutCts.Token));
                
                if (completedTask != readTask)
                {
                    _logger.LogWarning("Timeout waiting for TypeScript server response");
                    return null;
                }
                
                var line = await readTask;
                
                if (string.IsNullOrEmpty(line))
                {
                    _logger.LogWarning("Received empty response from TypeScript server");
                    return null;
                }
                
                _logger.LogDebug("Received TypeScript message: {Message}", line);
                
                try
                {
                    var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    
                    // Check if this is a response to our request
                    if (root.TryGetProperty("type", out var typeElement) && 
                        typeElement.GetString() == "response" &&
                        root.TryGetProperty("request_seq", out var seqElement) &&
                        seqElement.GetInt32() == (int?)requestSeq)
                    {
                        response = line;
                        break;
                    }
                    
                    // Log other message types but continue waiting
                    if (root.TryGetProperty("type", out var msgType))
                    {
                        _logger.LogDebug("Received TypeScript {Type} message, waiting for response...", msgType.GetString());
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse TypeScript server response: {Response}", line);
                    return null;
                }
            }
            
            return response != null ? JsonDocument.Parse(response) : null;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Open a TypeScript project
    /// </summary>
    public async Task<bool> OpenProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "open",
            arguments = new
            {
                file = projectPath,
                projectRootPath = Path.GetDirectoryName(projectPath)
            }
        };
        
        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open TypeScript project at {Path}", projectPath);
            return false;
        }
    }

    /// <summary>
    /// Get definition location for a symbol at a given position
    /// </summary>
    public async Task<TypeScriptLocation?> GetDefinitionAsync(
        string filePath, 
        int line, 
        int offset, 
        CancellationToken cancellationToken = default)
    {
        // First ensure the file is opened in tsserver
        var openRequest = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "open",
            arguments = new
            {
                file = filePath,
                projectRootPath = Path.GetDirectoryName(filePath)
            }
        };
        
        var openResponse = await SendRequestAsync(openRequest, cancellationToken);
        if (openResponse == null)
        {
            _logger.LogWarning("Failed to open file {File} in TypeScript server", filePath);
        }
        
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "definition",
            arguments = new
            {
                file = filePath,
                line = line,
                offset = offset
            }
        };
        
        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            if (response?.RootElement.TryGetProperty("body", out var body) == true)
            {
                var locations = body.EnumerateArray().FirstOrDefault();
                if (locations.ValueKind != JsonValueKind.Undefined)
                {
                    return new TypeScriptLocation
                    {
                        File = locations.GetProperty("file").GetString() ?? string.Empty,
                        Line = locations.GetProperty("start").GetProperty("line").GetInt32(),
                        Offset = locations.GetProperty("start").GetProperty("offset").GetInt32()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get definition for {File}:{Line}:{Offset}", filePath, line, offset);
        }
        
        return null;
    }

    /// <summary>
    /// Find all references to a symbol
    /// </summary>
    public async Task<List<TypeScriptLocation>> FindReferencesAsync(
        string filePath,
        int line,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var references = new List<TypeScriptLocation>();
        
        // First ensure the file is opened in tsserver
        var openRequest = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "open",
            arguments = new
            {
                file = filePath,
                projectRootPath = Path.GetDirectoryName(filePath)
            }
        };
        
        var openResponse = await SendRequestAsync(openRequest, cancellationToken);
        if (openResponse == null)
        {
            _logger.LogWarning("Failed to open file {File} in TypeScript server", filePath);
        }
        
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "references",
            arguments = new
            {
                file = filePath,
                line = line,
                offset = offset
            }
        };
        
        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            if (response?.RootElement.TryGetProperty("body", out var body) == true &&
                body.TryGetProperty("refs", out var refs))
            {
                foreach (var refElement in refs.EnumerateArray())
                {
                    references.Add(new TypeScriptLocation
                    {
                        File = refElement.GetProperty("file").GetString() ?? string.Empty,
                        Line = refElement.GetProperty("start").GetProperty("line").GetInt32(),
                        Offset = refElement.GetProperty("start").GetProperty("offset").GetInt32(),
                        LineText = refElement.TryGetProperty("lineText", out var lineText) 
                            ? lineText.GetString() ?? string.Empty 
                            : string.Empty
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find references for {File}:{Line}:{Offset}", filePath, line, offset);
        }
        
        return references;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _tsServerInput?.Close();
            _tsServerOutput?.Close();
            
            if (_tsServerProcess != null && !_tsServerProcess.HasExited)
            {
                _tsServerProcess.Kill();
                _tsServerProcess.WaitForExit(5000);
            }
            
            _tsServerProcess?.Dispose();
            _requestLock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing TypeScript service");
        }
        
        _disposed = true;
    }
}

/// <summary>
/// Represents a location in a TypeScript file
/// </summary>
public class TypeScriptLocation
{
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Offset { get; set; }
    public string LineText { get; set; } = string.Empty;
}