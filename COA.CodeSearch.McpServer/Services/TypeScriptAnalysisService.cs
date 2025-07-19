using System.Diagnostics;
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
        if (_initialized || string.IsNullOrEmpty(_nodeExecutable))
        {
            return _initialized;
        }

        // First check configured path
        _tsServerPath = _configuration["TypeScript:ServerPath"];
        
        // Then check local node_modules
        if (string.IsNullOrEmpty(_tsServerPath) || !File.Exists(_tsServerPath))
        {
            _tsServerPath = Path.Combine(AppContext.BaseDirectory, "node_modules", "typescript", "lib", "tsserver.js");
        }
        
        // If still not found, try to install
        if (!File.Exists(_tsServerPath))
        {
            _logger.LogInformation("TypeScript not found locally. Attempting automatic installation...");
            _tsServerPath = await _installer.GetTsServerPathAsync();
        }

        if (!string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath))
        {
            _logger.LogInformation("TypeScript Analysis Service initialized with tsserver at {TsServerPath}", _tsServerPath);
            _initialized = true;
        }
        else
        {
            _logger.LogWarning("TypeScript server could not be initialized. TypeScript features will not be available.");
        }

        return _initialized;
    }

    /// <summary>
    /// Find Node.js executable in the system PATH
    /// </summary>
    private string FindNodeExecutable()
    {
        var nodeNames = new[] { "node", "node.exe" };
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        
        foreach (var dir in paths)
        {
            foreach (var nodeName in nodeNames)
            {
                var fullPath = Path.Combine(dir, nodeName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        
        // Check common installation paths
        var commonPaths = new[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            "/usr/local/bin/node",
            "/usr/bin/node"
        };
        
        return commonPaths.FirstOrDefault(File.Exists) ?? string.Empty;
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
            throw new InvalidOperationException("TypeScript server is not available");
        }

        if (_tsServerProcess != null && !_tsServerProcess.HasExited)
        {
            return;
        }
        
        _logger.LogInformation("Starting TypeScript language server...");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _nodeExecutable,
            Arguments = $"\"{_tsServerPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        
        _tsServerProcess = new Process { StartInfo = startInfo };
        _tsServerProcess.Start();
        
        _tsServerInput = _tsServerProcess.StandardInput;
        _tsServerOutput = _tsServerProcess.StandardOutput;
        
        // Read the initial server ready message
        var readyMsg = await _tsServerOutput.ReadLineAsync();
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
            await _tsServerInput!.WriteLineAsync(requestJson);
            await _tsServerInput.FlushAsync();
            
            // Read response
            var response = await _tsServerOutput!.ReadLineAsync();
            if (string.IsNullOrEmpty(response))
            {
                return null;
            }
            
            return JsonDocument.Parse(response);
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