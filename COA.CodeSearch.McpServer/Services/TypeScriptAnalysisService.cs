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
    public bool IsAvailable
    {
        get
        {
            var available = _initialized && !string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath);
            if (!available)
            {
                _logger.LogDebug("TypeScript service availability check: initialized={Initialized}, tsServerPath={Path}, exists={Exists}",
                    _initialized,
                    _tsServerPath ?? "null",
                    !string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath));
            }
            return available;
        }
    }

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
            
            // Log Node.js version for debugging
            Task.Run(async () =>
            {
                try
                {
                    var versionInfo = new ProcessStartInfo
                    {
                        FileName = _nodeExecutable,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(versionInfo);
                    if (process != null)
                    {
                        var version = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        _logger.LogInformation("Node.js version: {Version}", version.Trim());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get Node.js version");
                }
            });
        }
    }

    public async Task<bool> InitializeAsync()
    {
        if (_initialized)
        {
            _logger.LogDebug("TypeScript service already initialized");
            return true;
        }

        _logger.LogInformation("Starting TypeScript service initialization...");

        // Check if Node.js is available
        if (string.IsNullOrEmpty(_nodeExecutable))
        {
            _logger.LogError("Node.js executable not found in PATH. TypeScript features require Node.js to be installed.");
            _logger.LogError("Searched in PATH and common locations. Please install Node.js from https://nodejs.org/");
            return false;
        }

        _logger.LogInformation("Node.js found at: {NodePath}", _nodeExecutable);

        try
        {
            // First check configured path
            _tsServerPath = _configuration["TypeScript:ServerPath"];
            if (!string.IsNullOrEmpty(_tsServerPath))
            {
                _logger.LogInformation("Checking configured TypeScript path: {Path}", _tsServerPath);
                if (!File.Exists(_tsServerPath))
                {
                    _logger.LogWarning("Configured TypeScript path does not exist: {Path}", _tsServerPath);
                    _tsServerPath = null;
                }
                else
                {
                    _logger.LogInformation("Found TypeScript at configured path: {Path}", _tsServerPath);
                }
            }
            else
            {
                _logger.LogInformation("No TypeScript path configured in settings");
            }
            
            // Then check local node_modules
            if (string.IsNullOrEmpty(_tsServerPath))
            {
                var localPath = Path.Combine(AppContext.BaseDirectory, "node_modules", "typescript", "lib", "tsserver.js");
                _logger.LogInformation("Checking local TypeScript path: {Path}", localPath);
                if (File.Exists(localPath))
                {
                    _tsServerPath = localPath;
                    _logger.LogInformation("Found TypeScript at local path: {Path}", localPath);
                }
                else
                {
                    _logger.LogInformation("TypeScript not found at local path: {Path}", localPath);
                }
            }
            
            // Check for project-specific TypeScript installations
            if (string.IsNullOrEmpty(_tsServerPath))
            {
                _logger.LogInformation("Searching for TypeScript in common project locations...");
                var projectTsServerPath = await FindProjectTypeScriptAsync();
                if (!string.IsNullOrEmpty(projectTsServerPath))
                {
                    _tsServerPath = projectTsServerPath;
                    _logger.LogInformation("Found TypeScript in project at: {Path}", _tsServerPath);
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
                _logger.LogInformation("TypeScript Analysis Service initialized successfully with tsserver at {TsServerPath}", _tsServerPath);
                _initialized = true;
                return true;
            }
            else
            {
                _logger.LogError("TypeScript server could not be initialized. tsserver.js not found at any location.");
                _logger.LogError("Attempted locations: configured path, local node_modules, and automatic installation");
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
        if (!_initialized)
        {
            var initSuccess = await InitializeAsync();
            if (!initSuccess)
            {
                var nodeStatus = string.IsNullOrEmpty(_nodeExecutable) ? "Node.js not found in PATH" : $"Node.js found at {_nodeExecutable}";
                var tsStatus = string.IsNullOrEmpty(_tsServerPath) ? "tsserver.js not found" : $"tsserver.js path: {_tsServerPath}";
                throw new InvalidOperationException($"TypeScript server is not available. {nodeStatus}. {tsStatus}");
            }
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
    /// Ensure a file is opened and synchronized with tsserver
    /// </summary>
    private async Task<bool> EnsureFileOpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPath = NormalizePathForTypeScript(filePath);
            
            // Read the file content
            var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            // Send open request with file content
            var openRequest = new
            {
                seq = Interlocked.Increment(ref _requestSequence),
                type = "request",
                command = "open",
                arguments = new
                {
                    file = normalizedPath,
                    fileContent = fileContent,
                    projectRootPath = NormalizePathForTypeScript(Path.GetDirectoryName(filePath) ?? "")
                }
            };
            
            var response = await SendRequestAsync(openRequest, cancellationToken);
            if (response == null)
            {
                _logger.LogWarning("Failed to open file {File} in TypeScript server", filePath);
                return false;
            }
            
            _logger.LogDebug("Successfully opened file {File} in TypeScript server", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure file {File} is open", filePath);
            return false;
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
    /// Convert a 1-based column number to a 1-based character offset for tsserver
    /// </summary>
    private async Task<int> ConvertColumnToOffsetAsync(string filePath, int line, int column)
    {
        try
        {
            // Read the specific line from the file
            var lines = await File.ReadAllLinesAsync(filePath);
            if (line > lines.Length || line < 1)
            {
                _logger.LogWarning("Line {Line} is out of range for file {File}", line, filePath);
                return column; // Fallback to column
            }
            
            // Get the line content (0-based index)
            var lineContent = lines[line - 1];
            
            // Calculate offset: For tsserver, offset is 1-based position in the line
            // Column is also 1-based, so we need to handle tab expansion
            var offset = 1;
            var visualColumn = 1;
            
            for (int i = 0; i < lineContent.Length && visualColumn < column; i++)
            {
                if (lineContent[i] == '\t')
                {
                    // Tab typically expands to next multiple of 4 (or 8)
                    // But for character offset, we count it as 1
                    visualColumn += 4 - ((visualColumn - 1) % 4);
                }
                else
                {
                    visualColumn++;
                }
                offset++;
            }
            
            _logger.LogDebug("Converted column {Column} to offset {Offset} for line: {Line}", column, offset, lineContent);
            return offset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert column to offset for {File}:{Line}:{Column}", filePath, line, column);
            return column; // Fallback
        }
    }

    /// <summary>
    /// Get definition location for a symbol at a given position
    /// </summary>
    public async Task<TypeScriptLocation?> GetDefinitionAsync(
        string filePath, 
        int line, 
        int column, 
        CancellationToken cancellationToken = default)
    {
        // Convert column to offset for tsserver
        var offset = await ConvertColumnToOffsetAsync(filePath, line, column);
        
        // Normalize the file path for TypeScript (forward slashes)
        var normalizedPath = NormalizePathForTypeScript(filePath);
        
        // First ensure the file is opened and synchronized in tsserver
        var fileOpened = await EnsureFileOpenAsync(filePath, cancellationToken);
        if (!fileOpened)
        {
            _logger.LogError("Failed to open file {File} in TypeScript server", filePath);
            return null;
        }
        
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "definition",
            arguments = new
            {
                file = normalizedPath,
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
        int column,
        CancellationToken cancellationToken = default)
    {
        // Convert column to offset for tsserver
        var offset = await ConvertColumnToOffsetAsync(filePath, line, column);
        var references = new List<TypeScriptLocation>();
        
        // Normalize the file path for TypeScript (forward slashes)
        var normalizedPath = NormalizePathForTypeScript(filePath);
        
        // First ensure the file is opened and synchronized in tsserver
        var fileOpened = await EnsureFileOpenAsync(filePath, cancellationToken);
        if (!fileOpened)
        {
            _logger.LogError("Failed to open file {File} in TypeScript server", filePath);
            return references; // Return empty list
        }
        
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "references",
            arguments = new
            {
                file = normalizedPath,
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

    /// <summary>
    /// Get quick info (hover information) for a symbol at a given position
    /// </summary>
    public async Task<object?> GetQuickInfoAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        // Convert column to offset for tsserver
        var offset = await ConvertColumnToOffsetAsync(filePath, line, column);
        
        // Normalize the file path for TypeScript (forward slashes)
        var normalizedPath = NormalizePathForTypeScript(filePath);
        
        // First ensure the file is opened and synchronized in tsserver
        var fileOpened = await EnsureFileOpenAsync(filePath, cancellationToken);
        if (!fileOpened)
        {
            _logger.LogError("Failed to open file {File} in TypeScript server", filePath);
            return null;
        }
        
        var request = new
        {
            seq = Interlocked.Increment(ref _requestSequence),
            type = "request",
            command = "quickinfo",
            arguments = new
            {
                file = normalizedPath,
                line = line,
                offset = offset
            }
        };
        
        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            if (response?.RootElement.TryGetProperty("body", out var body) == true)
            {
                return new
                {
                    type = "typescript",
                    displayString = body.TryGetProperty("displayString", out var displayString) 
                        ? displayString.GetString() ?? "" 
                        : "",
                    documentation = body.TryGetProperty("documentation", out var documentation) 
                        ? documentation.GetString() ?? "" 
                        : "",
                    kind = body.TryGetProperty("kind", out var kind) 
                        ? kind.GetString() ?? "" 
                        : "",
                    kindModifiers = body.TryGetProperty("kindModifiers", out var kindModifiers) 
                        ? kindModifiers.GetString() ?? "" 
                        : "",
                    tags = body.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                        ? tags.EnumerateArray().Select(t => new
                        {
                            name = t.TryGetProperty("name", out var name) ? name.GetString() : "",
                            text = t.TryGetProperty("text", out var text) ? text.GetString() : ""
                        }).ToArray()
                        : Array.Empty<object>()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quick info for {File}:{Line}:{Offset}", filePath, line, offset);
        }
        
        return null;
    }

    /// <summary>
    /// Normalize file paths for TypeScript (convert backslashes to forward slashes)
    /// </summary>
    private string NormalizePathForTypeScript(string path)
    {
        // TypeScript expects forward slashes even on Windows
        return path.Replace('\\', '/');
    }
    
    /// <summary>
    /// Search for TypeScript installations in common project locations
    /// </summary>
    private async Task<string?> FindProjectTypeScriptAsync()
    {
        return await Task.Run(() =>
        {
            var searchPaths = new List<string>();
            
            // Get current directory and its parents up to a reasonable depth
            var currentDir = Directory.GetCurrentDirectory();
            var dir = new DirectoryInfo(currentDir);
            
            // Search up to 5 levels up from current directory
            for (int i = 0; i < 5 && dir != null; i++)
            {
                searchPaths.Add(dir.FullName);
                dir = dir.Parent;
            }
            
            // Also check common project roots
            var driveRoots = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName);
                
            foreach (var drive in driveRoots)
            {
                searchPaths.Add(Path.Combine(drive, "source"));
                searchPaths.Add(Path.Combine(drive, "repos"));
                searchPaths.Add(Path.Combine(drive, "projects"));
                searchPaths.Add(Path.Combine(drive, "dev"));
                searchPaths.Add(Path.Combine(drive, "src"));
            }
            
            // Search for tsserver.js in each path
            foreach (var basePath in searchPaths.Distinct())
            {
                if (!Directory.Exists(basePath))
                    continue;
                    
                try
                {
                    // Look for node_modules/typescript/lib/tsserver.js
                    var nodeModulesPath = Path.Combine(basePath, "node_modules", "typescript", "lib", "tsserver.js");
                    if (File.Exists(nodeModulesPath))
                    {
                        _logger.LogDebug("Found tsserver.js at: {Path}", nodeModulesPath);
                        return nodeModulesPath;
                    }
                    
                    // Search subdirectories (ClientApp, frontend, etc.)
                    var subDirs = new[] { "ClientApp", "client", "frontend", "web", "ui", "app" };
                    foreach (var subDir in subDirs)
                    {
                        var subPath = Path.Combine(basePath, subDir, "node_modules", "typescript", "lib", "tsserver.js");
                        if (File.Exists(subPath))
                        {
                            _logger.LogDebug("Found tsserver.js at: {Path}", subPath);
                            return subPath;
                        }
                    }
                    
                    // Search one level deeper for any directories containing node_modules
                    if (basePath.Length < 50) // Avoid searching too deep
                    {
                        var directories = Directory.GetDirectories(basePath, "node_modules", SearchOption.TopDirectoryOnly);
                        foreach (var nodeModules in directories)
                        {
                            var tsPath = Path.Combine(nodeModules, "typescript", "lib", "tsserver.js");
                            if (File.Exists(tsPath))
                            {
                                _logger.LogDebug("Found tsserver.js at: {Path}", tsPath);
                                return tsPath;
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we can't access
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error searching in {Path}", basePath);
                }
            }
            
            _logger.LogDebug("No project-specific TypeScript installation found");
            return null;
        });
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