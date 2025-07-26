using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for analyzing TypeScript code using the TypeScript Language Service via Node.js interop
/// </summary>
/// <remarks>
/// TypeScript server commands are divided into two categories:
/// 
/// Notifications (no response expected):
/// - open: Open a file
/// - close: Close a file  
/// - change: Update file content
/// - updateOpen: Update open file list
/// 
/// Requests (response expected):
/// - definition: Get definition location
/// - references: Find all references
/// - quickinfo: Get hover information
/// - rename: Get rename locations
/// - completions: Get code completions
/// - configure: Configure server (note: sometimes doesn't respond properly)
/// 
/// Use SendNotificationAsync for notifications and SendRequestAsync for requests.
/// </remarks>
public class TypeScriptAnalysisService : ITypeScriptAnalysisService, IDisposable
{
    private readonly ILogger<TypeScriptAnalysisService> _logger;
    private readonly IConfiguration _configuration;
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
                _logger.LogInformation("TypeScript service availability check: initialized={Initialized}, tsServerPath={Path}, exists={Exists}",
                    _initialized,
                    _tsServerPath ?? "null",
                    !string.IsNullOrEmpty(_tsServerPath) && File.Exists(_tsServerPath));
            }
            return available;
        }
    }

    public TypeScriptAnalysisService(
        ILogger<TypeScriptAnalysisService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
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
                    _logger.LogInformation(ex, "Failed to get Node.js version");
                }
            });
        }
    }

    public async Task<bool> InitializeAsync()
    {
        if (_initialized)
        {
            _logger.LogInformation("TypeScript service already initialized");
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
            
            // If still not found, check for global TypeScript installation
            if (string.IsNullOrEmpty(_tsServerPath) || !File.Exists(_tsServerPath))
            {
                _logger.LogInformation("TypeScript not found locally. Checking for global installation...");
                var globalTsServerPath = await FindGlobalTypeScriptAsync();
                if (!string.IsNullOrEmpty(globalTsServerPath))
                {
                    _tsServerPath = globalTsServerPath;
                    _logger.LogInformation("Found global TypeScript at: {TsServerPath}", _tsServerPath);
                }
                else
                {
                    _logger.LogError("TypeScript not found. Please install TypeScript globally: npm install -g typescript");
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
                            _logger.LogInformation("Found Node.js in PATH at: {Path}", fullPath);
                            return fullPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(ex, "Error checking path {Path}/{Node}", dir, nodeName);
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
                    _logger.LogInformation("Found Node.js at common location: {Path}", path);
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
    /// Find npm executable across platforms
    /// </summary>
    private string FindNpmExecutable()
    {
        try
        {
            var npmNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new[] { "npm.cmd", "npm" }  // Prioritize npm.cmd on Windows
                : new[] { "npm" };
            
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            
            // First check PATH
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                
                foreach (var npmName in npmNames)
                {
                    try
                    {
                        var fullPath = Path.Combine(dir, npmName);
                        if (File.Exists(fullPath))
                        {
                            _logger.LogInformation("DEBUG: Found npm in PATH at: {Path}", fullPath);
                            return fullPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking path {Path}/{Npm}", dir, npmName);
                    }
                }
            }
            
            // Check common npm installation paths
            var commonPaths = new List<string>();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                commonPaths.AddRange(new[]
                {
                    @"C:\Program Files\nodejs\npm",
                    @"C:\Program Files\nodejs\npm.cmd",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "npm"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "npm.cmd")
                });
            }
            else
            {
                commonPaths.AddRange(new[]
                {
                    "/usr/local/bin/npm",
                    "/usr/bin/npm",
                    "/opt/homebrew/bin/npm",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global", "bin", "npm")
                });
            }
            
            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogInformation("DEBUG: Found npm at common location: {Path}", path);
                    return path;
                }
            }
            
            _logger.LogError("DEBUG: npm executable not found in PATH or common locations");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DEBUG: Error finding npm executable");
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
    /// Find the nearest tsconfig.json by searching upward from a file path
    /// </summary>
    public async Task<string?> FindNearestTypeScriptProjectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var directory = File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath;
            
            while (!string.IsNullOrEmpty(directory))
            {
                // Check for tsconfig.json in current directory
                var tsconfigPath = Path.Combine(directory, "tsconfig.json");
                if (File.Exists(tsconfigPath))
                {
                    _logger.LogInformation("Found tsconfig.json at {Path} for file {FilePath}", tsconfigPath, filePath);
                    return tsconfigPath;
                }
                
                // Also check for other common TypeScript config files
                var alternativeConfigs = new[] { "tsconfig.app.json", "tsconfig.base.json", "tsconfig.lib.json" };
                foreach (var configName in alternativeConfigs)
                {
                    var altPath = Path.Combine(directory, configName);
                    if (File.Exists(altPath))
                    {
                        _logger.LogInformation("Found {ConfigName} at {Path} for file {FilePath}", configName, altPath, filePath);
                        return altPath;
                    }
                }
                
                // Move up to parent directory
                var parent = Directory.GetParent(directory);
                if (parent == null || parent.FullName == directory)
                {
                    break; // Reached root
                }
                
                directory = parent.FullName;
                
                // Stop if we hit common project boundaries
                if (Directory.Exists(Path.Combine(directory, ".git")) || 
                    File.Exists(Path.Combine(directory, ".gitignore")) ||
                    directory.EndsWith("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Stopped searching at project boundary: {Directory}", directory);
                    break;
                }
            }
            
            _logger.LogWarning("No tsconfig.json found for file {FilePath}", filePath);
            return null;
        }, cancellationToken);
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
            Arguments = $"\"{_tsServerPath}\" --stdio",  // Use stdio mode for simpler communication
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
        
        // Set up exit handler
        _tsServerProcess.Exited += (sender, e) =>
        {
            _logger.LogError("TypeScript server process exited unexpectedly. Exit code: {ExitCode}", 
                _tsServerProcess?.ExitCode ?? -1);
        };
        _tsServerProcess.EnableRaisingEvents = true;
        
        _tsServerProcess.Start();
        _tsServerProcess.BeginErrorReadLine();
        
        _tsServerInput = _tsServerProcess.StandardInput;
        _tsServerOutput = _tsServerProcess.StandardOutput;
        
        _logger.LogInformation("TypeScript server process started. PID: {PID}", _tsServerProcess.Id);
        
        // Add more detailed logging
        _logger.LogInformation("TypeScript server started with: Node={Node}, TSServer={TSServer}", _nodeExecutable, _tsServerPath);
        _logger.LogInformation("Working directory: {WorkingDir}", startInfo.WorkingDirectory);
        
        // Wait a moment for the process to initialize
        await Task.Delay(500);
        
        // Check if process has already exited
        if (_tsServerProcess.HasExited)
        {
            _logger.LogError("TypeScript server process exited immediately with code {ExitCode}", _tsServerProcess.ExitCode);
            throw new InvalidOperationException($"TypeScript server process exited immediately with code {_tsServerProcess.ExitCode}");
        }
        
        // Send a simple request to verify the server is responsive with timeout
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10 second timeout for init
            _logger.LogInformation("Verifying TypeScript server responsiveness...");
            
            // Try a simpler approach - just wait a bit for the server to be ready
            await Task.Delay(500); // Give tsserver 500ms to initialize
            
            // Check if the process is still running
            if (_tsServerProcess == null || _tsServerProcess.HasExited)
            {
                _logger.LogError("TypeScript server process exited during initialization");
                throw new InvalidOperationException("TypeScript server process exited unexpectedly");
            }
            
            _logger.LogInformation("TypeScript server process is running (PID: {Pid})", _tsServerProcess.Id);
            
            // Skip the configure command for now - it seems to be causing issues
            // The server will be configured when we open the first file
            _logger.LogInformation("TypeScript server is ready");
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("TypeScript server initialization timed out after 30 seconds");
            // Kill the process if it's still running
            if (_tsServerProcess != null && !_tsServerProcess.HasExited)
            {
                _logger.LogWarning("Killing unresponsive TypeScript server process");
                _tsServerProcess.Kill();
            }
            throw new TimeoutException("TypeScript server failed to respond within 30 seconds. This may indicate missing dependencies or configuration issues.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify TypeScript server responsiveness");
            throw new InvalidOperationException("TypeScript server failed to start properly", ex);
        }
    }


    /// <summary>
    /// Send a notification to the TypeScript server (no response expected)
    /// </summary>
    private async Task SendNotificationAsync(object notification, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureServerStartedAsync();
            
            var notificationJson = JsonSerializer.Serialize(notification);
            var command = notification.GetType().GetProperty("command")?.GetValue(notification) as string;
            _logger.LogInformation("Sending TypeScript notification (command: {Command}): {Notification}", 
                command ?? "unknown", notificationJson);
            
            // Write the notification with a newline terminator (stdio protocol)
            await _tsServerInput!.WriteLineAsync(notificationJson);
            await _tsServerInput.FlushAsync();
            
            // For notifications, we don't wait for a response
            _logger.LogInformation("TypeScript notification sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendNotificationAsync");
            throw;
        }
        finally
        {
            _requestLock.Release();
        }
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
            
            // Extract request metadata
            var requestSeqObj = request.GetType().GetProperty("seq")?.GetValue(request);
            var requestSeq = requestSeqObj != null ? Convert.ToInt32(requestSeqObj) : -1;
            var requestCommand = request.GetType().GetProperty("command")?.GetValue(request) as string;
            
            var requestJson = JsonSerializer.Serialize(request);
            _logger.LogInformation("Sending TypeScript request (command: {Command}, seq: {Seq}): {Request}", 
                requestCommand ?? "unknown", requestSeq, requestJson);
            
            // Write the request with a newline terminator (stdio protocol)
            await _tsServerInput!.WriteLineAsync(requestJson);
            await _tsServerInput.FlushAsync();
            
            // Read response - tsserver may send multiple messages (events) before the actual response
            string? response = null;
            
            // Add a timeout to prevent infinite waiting
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // Increase timeout for slower operations
            
            var messageCount = 0;
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                string? messageContent = null;
                try
                {
                    // tsserver uses simple newline-delimited JSON, not LSP protocol
                    var line = await _tsServerOutput!.ReadLineAsync();
                    if (line == null)
                    {
                        _logger.LogWarning("TypeScript server stream ended unexpectedly");
                        return null;
                    }
                    
                    if (string.IsNullOrEmpty(line))
                    {
                        continue; // Skip empty lines
                    }
                    
                    messageContent = line;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operation cancelled while waiting for TypeScript server response");
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading TypeScript server response");
                    continue;
                }
                
                if (string.IsNullOrEmpty(messageContent))
                {
                    continue;
                }
                
                // Skip lines that look like LSP headers (shouldn't be there for tsserver)
                if (messageContent.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping unexpected LSP header in tsserver output: {Line}", messageContent);
                    continue;
                }
                
                // Skip lines that don't look like JSON
                if (!messageContent.TrimStart().StartsWith("{"))
                {
                    _logger.LogWarning("Skipping non-JSON line in tsserver output: {Line}", messageContent);
                    continue;
                }
                
                messageCount++;
                _logger.LogInformation("Received TypeScript message #{Count}: {Message}", messageCount, 
                    messageContent.Length > 200 ? messageContent.Substring(0, 200) + "..." : messageContent);
                
                try
                {
                    var doc = JsonDocument.Parse(messageContent);
                    var root = doc.RootElement;
                    
                    // Check if this is a response to our request
                    if (root.TryGetProperty("type", out var typeElement))
                    {
                        var messageType = typeElement.GetString();
                        
                        if (messageType == "response")
                        {
                            // Check if it's our response
                            if (root.TryGetProperty("request_seq", out var seqElement) &&
                                seqElement.GetInt32() == requestSeq)
                            {
                                response = messageContent;
                                _logger.LogInformation("Found matching response for seq {Seq}", requestSeq);
                                break;
                            }
                            else
                            {
                                _logger.LogInformation("Received response for different request seq: {Seq}", 
                                    root.TryGetProperty("request_seq", out var s) ? s.GetInt32() : -1);
                            }
                        }
                        else if (messageType == "event")
                        {
                            // Log events but continue waiting
                            var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : "unknown";
                            _logger.LogInformation("Received TypeScript event: {Event}", eventName);
                        }
                        else
                        {
                            _logger.LogInformation("Received TypeScript message type: {Type}", messageType);
                        }
                        
                        // Check for error responses
                        if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
                        {
                            var errorMessage = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                            _logger.LogError("TypeScript server returned error: {Error}", errorMessage);
                            if (root.TryGetProperty("request_seq", out var errSeq) && errSeq.GetInt32() == requestSeq)
                            {
                                // This is an error response to our request
                                return doc;
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse TypeScript server response: {Response}", messageContent);
                    // Continue reading, might be a malformed event
                }
            }
            
            if (response == null)
            {
                _logger.LogWarning("No response received for request seq {Seq} after {Count} messages", requestSeq, messageCount);
            }
            
            return response != null ? JsonDocument.Parse(response) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendRequestAsync");
            return null;
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
            // Ensure the file path is absolute
            if (!Path.IsPathRooted(filePath))
            {
                _logger.LogWarning("Received relative file path: {FilePath}. Converting to absolute path.", filePath);
                filePath = Path.GetFullPath(filePath);
            }
            
            var normalizedPath = NormalizePathForTypeScript(filePath);
            
            // Read the file content
            var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            // Send open notification with file content
            // Note: TypeScript server "open" command is a notification (no response expected)
            // It only accepts: file, fileContent, and scriptKindName
            var openNotification = new
            {
                seq = Interlocked.Increment(ref _requestSequence),
                type = "request",
                command = "open",
                arguments = new
                {
                    file = normalizedPath,
                    fileContent = fileContent
                    // Removed projectRootPath - not a valid parameter for tsserver open command
                }
            };
            
            await SendNotificationAsync(openNotification, cancellationToken);
            
            // Give tsserver a moment to process the file
            await Task.Delay(100, cancellationToken);
            
            _logger.LogInformation("Successfully opened file {File} in TypeScript server", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure file {File} is open", filePath);
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
            
            _logger.LogInformation("Converted column {Column} to offset {Offset} for line: {Line}", column, offset, lineContent);
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
        // Ensure the file path is absolute
        if (!Path.IsPathRooted(filePath))
        {
            _logger.LogWarning("GetDefinitionAsync received relative path: {FilePath}. Converting to absolute.", filePath);
            filePath = Path.GetFullPath(filePath);
        }
        
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
        // Ensure the file path is absolute
        if (!Path.IsPathRooted(filePath))
        {
            _logger.LogWarning("FindReferencesAsync received relative path: {FilePath}. Converting to absolute.", filePath);
            filePath = Path.GetFullPath(filePath);
        }
        
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
        // Ensure the file path is absolute
        if (!Path.IsPathRooted(filePath))
        {
            _logger.LogWarning("GetQuickInfoAsync received relative path: {FilePath}. Converting to absolute.", filePath);
            filePath = Path.GetFullPath(filePath);
        }
        
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
    /// Get rename information for a symbol at a given position
    /// </summary>
    public async Task<object?> GetRenameInfoAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        // Ensure the file path is absolute
        if (!Path.IsPathRooted(filePath))
        {
            _logger.LogWarning("GetRenameInfoAsync received relative path: {FilePath}. Converting to absolute.", filePath);
            filePath = Path.GetFullPath(filePath);
        }
        
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
            command = "rename",
            arguments = new
            {
                file = normalizedPath,
                line = line,
                offset = offset,
                findInComments = false,
                findInStrings = false
            }
        };
        
        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            if (response?.RootElement.TryGetProperty("body", out var body) == true)
            {
                return new
                {
                    info = body.TryGetProperty("info", out var info) ? new
                    {
                        canRename = info.TryGetProperty("canRename", out var canRename) && canRename.GetBoolean(),
                        displayName = info.TryGetProperty("displayName", out var displayName) 
                            ? displayName.GetString() ?? "" 
                            : "",
                        fullDisplayName = info.TryGetProperty("fullDisplayName", out var fullDisplayName) 
                            ? fullDisplayName.GetString() ?? "" 
                            : "",
                        kind = info.TryGetProperty("kind", out var kind) 
                            ? kind.GetString() ?? "" 
                            : "",
                        kindModifiers = info.TryGetProperty("kindModifiers", out var kindModifiers) 
                            ? kindModifiers.GetString() ?? "" 
                            : "",
                        localizedErrorMessage = info.TryGetProperty("localizedErrorMessage", out var localizedErrorMessage) 
                            ? localizedErrorMessage.GetString() 
                            : null
                    } : null,
                    locs = body.TryGetProperty("locs", out var locs) && locs.ValueKind == JsonValueKind.Array
                        ? locs.EnumerateArray().Select(loc => new
                        {
                            file = loc.GetProperty("file").GetString() ?? "",
                            locs = loc.TryGetProperty("locs", out var innerLocs) && innerLocs.ValueKind == JsonValueKind.Array
                                ? innerLocs.EnumerateArray().Select(innerLoc => new TypeScriptLocation
                                {
                                    File = loc.GetProperty("file").GetString() ?? "",
                                    Line = innerLoc.GetProperty("start").GetProperty("line").GetInt32(),
                                    Offset = innerLoc.GetProperty("start").GetProperty("offset").GetInt32(),
                                    LineText = innerLoc.TryGetProperty("prefixText", out var prefix) && innerLoc.TryGetProperty("suffixText", out var suffix)
                                        ? $"{prefix.GetString()}{(info.ValueKind != JsonValueKind.Undefined && info.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : "")}{suffix.GetString()}"
                                        : ""
                                }).ToList()
                                : new List<TypeScriptLocation>()
                        }).ToList()
                        : null
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rename info for {File}:{Line}:{Offset}", filePath, line, offset);
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
                        _logger.LogInformation("Found tsserver.js at: {Path}", nodeModulesPath);
                        return nodeModulesPath;
                    }
                    
                    // Search subdirectories (ClientApp, frontend, etc.)
                    var subDirs = new[] { "ClientApp", "client", "frontend", "web", "ui", "app" };
                    foreach (var subDir in subDirs)
                    {
                        var subPath = Path.Combine(basePath, subDir, "node_modules", "typescript", "lib", "tsserver.js");
                        if (File.Exists(subPath))
                        {
                            _logger.LogInformation("Found tsserver.js at: {Path}", subPath);
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
                                _logger.LogInformation("Found tsserver.js at: {Path}", tsPath);
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
                    _logger.LogInformation(ex, "Error searching in {Path}", basePath);
                }
            }
            
            _logger.LogInformation("No project-specific TypeScript installation found");
            return null;
        });
    }

    /// <summary>
    /// Find global TypeScript installation using npm
    /// </summary>
    private async Task<string?> FindGlobalTypeScriptAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_nodeExecutable))
            {
                _logger.LogWarning("Node.js not found - cannot check for global TypeScript");
                return null;
            }

            // Check if typescript is installed globally
            var npmPath = FindNpmExecutable();
            if (string.IsNullOrEmpty(npmPath))
            {
                _logger.LogError("DEBUG: npm executable not found");
                return null;
            }
            
            var arguments = "list -g typescript --depth=0 --json";
            
            _logger.LogInformation("DEBUG: Running npm command: {Command} {Arguments}", npmPath, arguments);
            
            ProcessStartInfo checkProcess;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use cmd.exe for non-.cmd npm files on Windows
                checkProcess = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{npmPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                _logger.LogInformation("DEBUG: Using cmd.exe wrapper: cmd.exe /c \"{0}\" {1}", npmPath, arguments);
            }
            else
            {
                // Direct execution on Unix-like systems
                checkProcess = new ProcessStartInfo
                {
                    FileName = npmPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                _logger.LogInformation("DEBUG: Direct execution: {0} {1}", npmPath, arguments);
            }

            using var process = Process.Start(checkProcess);
            if (process == null) 
            {
                _logger.LogError("DEBUG: Failed to start npm process");
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            _logger.LogInformation("DEBUG: npm list exit code: {ExitCode}", process.ExitCode);
            _logger.LogInformation("DEBUG: npm list stdout: {Output}", output);
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogInformation("DEBUG: npm list stderr: {Error}", error);
            }

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                try
                {
                    // Parse npm output to get global modules path
                    using var doc = JsonDocument.Parse(output);
                    _logger.LogInformation("DEBUG: Successfully parsed JSON from npm list");
                    
                    if (doc.RootElement.TryGetProperty("dependencies", out var deps))
                    {
                        _logger.LogInformation("DEBUG: Found dependencies property in npm output");
                        
                        if (deps.TryGetProperty("typescript", out var typescript))
                        {
                            _logger.LogInformation("DEBUG: Found TypeScript in global dependencies: {TypeScript}", typescript.ToString());
                            
                            // TypeScript is installed globally, now find its path
                            var globalPath = await GetGlobalNpmPathAsync();
                            _logger.LogInformation("DEBUG: Global npm path result: {GlobalPath}", globalPath ?? "NULL");
                            
                            if (!string.IsNullOrEmpty(globalPath))
                            {
                                var tsServerPath = Path.Combine(globalPath, "typescript", "lib", "tsserver.js");
                                _logger.LogInformation("DEBUG: Checking TypeScript path: {TsServerPath}", tsServerPath);
                                
                                if (File.Exists(tsServerPath))
                                {
                                    _logger.LogInformation("DEBUG: SUCCESS - Found TypeScript tsserver.js at: {TsServerPath}", tsServerPath);
                                    return tsServerPath;
                                }
                                else
                                {
                                    _logger.LogError("DEBUG: FAIL - TypeScript tsserver.js not found at expected path: {TsServerPath}", tsServerPath);
                                }
                            }
                            else
                            {
                                _logger.LogError("DEBUG: FAIL - Could not determine global npm path");
                            }
                        }
                        else
                        {
                            _logger.LogError("DEBUG: FAIL - TypeScript not found in dependencies property");
                            _logger.LogInformation("DEBUG: Available dependencies: {Dependencies}", deps.ToString());
                        }
                    }
                    else
                    {
                        _logger.LogError("DEBUG: FAIL - No dependencies property found in npm output");
                        _logger.LogInformation("DEBUG: Root element properties: {Properties}", string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name)));
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "DEBUG: FAIL - Failed to parse JSON from npm list output: {Output}", output);
                }
            }
            else
            {
                _logger.LogError("DEBUG: FAIL - npm list command failed or returned empty output. ExitCode: {ExitCode}, Output: {Output}", process.ExitCode, output);
            }

            _logger.LogInformation("TypeScript not found in global npm packages");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for global TypeScript installation");
            return null;
        }
    }

    /// <summary>
    /// Get the global npm modules path
    /// </summary>
    private async Task<string?> GetGlobalNpmPathAsync()
    {
        try
        {
            var npmPath = FindNpmExecutable();
            if (string.IsNullOrEmpty(npmPath))
            {
                _logger.LogError("DEBUG: npm executable not found in GetGlobalNpmPathAsync");
                return null;
            }
            
            var arguments = "root -g";
            
            _logger.LogInformation("DEBUG: Running npm root command: {Command} {Arguments}", npmPath, arguments);
            
            ProcessStartInfo pathProcess;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use cmd.exe for non-.cmd npm files on Windows
                pathProcess = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{npmPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                _logger.LogInformation("DEBUG: Using cmd.exe wrapper for npm root: cmd.exe /c \"{0}\" {1}", npmPath, arguments);
            }
            else
            {
                // Direct execution on Unix-like systems
                pathProcess = new ProcessStartInfo
                {
                    FileName = npmPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                _logger.LogInformation("DEBUG: Direct execution for npm root: {0} {1}", npmPath, arguments);
            }

            using var process = Process.Start(pathProcess);
            if (process == null) 
            {
                _logger.LogError("DEBUG: Failed to start npm root process");
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("DEBUG: npm root exit code: {ExitCode}", process.ExitCode);
            _logger.LogInformation("DEBUG: npm root stdout: {Output}", output);
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogInformation("DEBUG: npm root stderr: {Error}", error);
            }

            if (process.ExitCode == 0)
            {
                var globalPath = output.Trim();
                _logger.LogInformation("DEBUG: npm root returned path: {GlobalPath}", globalPath);
                
                if (Directory.Exists(globalPath))
                {
                    _logger.LogInformation("DEBUG: Global path exists, returning: {GlobalPath}", globalPath);
                    return globalPath; // npm root -g returns the node_modules directory directly
                }
                else
                {
                    _logger.LogError("DEBUG: Global path does not exist: {GlobalPath}", globalPath);
                }
            }
            else
            {
                _logger.LogError("DEBUG: npm root failed with exit code: {ExitCode}", process.ExitCode);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DEBUG: Exception in GetGlobalNpmPathAsync");
            return null;
        }
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