using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Type extraction service using Bun-based Tree-sitter implementation.
/// Communicates with external Bun process via JSON over stdin/stdout.
/// </summary>
public class BunTreeSitterService : ITypeExtractionService, IDisposable
{
    private readonly ILogger<BunTreeSitterService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _servicePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Process? _process;
    private bool _disposed;

    public BunTreeSitterService(ILogger<BunTreeSitterService> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Determine the service executable path
        _servicePath = GetServiceExecutablePath();
        _logger.LogInformation("BunTreeSitterService using executable: {ServicePath}", _servicePath);
    }

    private string GetServiceExecutablePath()
    {
        // Check for configured path first
        var configuredPath = _configuration.GetValue<string>("CodeSearch:TreeSitterServicePath");
        if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // Determine platform-specific executable
        var baseDir = AppContext.BaseDirectory;
        string executableName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executableName = "tree-sitter-service.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            executableName = "tree-sitter-service-linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            executableName = "tree-sitter-service-macos";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform for Tree-sitter service");
        }

        // Look for the executable in various locations
        var searchPaths = new[]
        {
            Path.Combine(baseDir, "TreeSitterService", executableName),
            Path.Combine(baseDir, executableName),
            Path.Combine(baseDir, "..", "TreeSitterService", executableName),
            Path.Combine(baseDir, "runtimes", GetRuntimeIdentifier(), "native", executableName)
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        // Fallback to development mode - run with Bun directly
        var devServicePath = Path.Combine(baseDir, "TreeSitterService", "src", "index.ts");
        if (File.Exists(devServicePath))
        {
            _logger.LogWarning("Running Tree-sitter service in development mode with Bun");
            return devServicePath;
        }

        throw new FileNotFoundException($"Tree-sitter service executable not found. Searched paths: {string.Join(", ", searchPaths)}");
    }

    private string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        return "any";
    }

    private async Task EnsureProcessRunning()
    {
        if (_process != null && !_process.HasExited)
        {
            return;
        }

        // Note: Caller should already hold the semaphore
        // Double-check after caller acquired lock
        if (_process != null && !_process.HasExited)
        {
            return;
        }

        // Clean up old process if needed
        if (_process != null)
        {
            _process.Dispose();
            _process = null;
        }

        // Start new process
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Check if running in dev mode (TypeScript file)
        if (_servicePath.EndsWith(".ts"))
        {
            startInfo.FileName = "bun";
            startInfo.Arguments = $"run \"{_servicePath}\"";
            startInfo.WorkingDirectory = Path.GetDirectoryName(_servicePath);
        }
        else
        {
            startInfo.FileName = _servicePath;
            // Set working directory to where the exe is, so it can find node_modules
            startInfo.WorkingDirectory = Path.GetDirectoryName(_servicePath);
        }

        _process = new Process { StartInfo = startInfo };

        // Capture stderr for debugging
        _process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug("Tree-sitter service: {Message}", e.Data);
            }
        };

        _process.Start();
        _process.BeginErrorReadLine();

        _logger.LogInformation("Started Tree-sitter service process (PID: {ProcessId})", _process.Id);

        // Verify service is healthy
        await VerifyServiceHealth();
    }

    private async Task VerifyServiceHealth()
    {
        if (_process == null)
        {
            throw new InvalidOperationException("Process not started");
        }

        var request = new { action = "health" };
        var requestJson = JsonSerializer.Serialize(request);

        await _process.StandardInput.WriteLineAsync(requestJson);
        await _process.StandardInput.FlushAsync();

        var responseTask = _process.StandardOutput.ReadLineAsync();
        var timeoutTask = Task.Delay(5000);

        var completedTask = await Task.WhenAny(responseTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("Tree-sitter service health check timed out");
        }

        var response = await responseTask;
        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("Tree-sitter service returned empty health response");
        }

        _logger.LogDebug("Tree-sitter service health response: {Response}", response);
    }

    public async Task<TypeExtractionResult> ExtractTypes(string content, string filePath)
    {
        var language = DetectLanguage(filePath);
        return await ExtractTypesAsync(content, language, filePath);
    }

    public async Task<TypeExtractionResult> ExtractTypesAsync(string content, string language, string? filePath = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureProcessRunning();

            if (_process == null)
            {
                _logger.LogError("Failed to start Tree-sitter service");
                return new TypeExtractionResult { Success = false };
            }

            // Prepare request
            var request = new
            {
                action = "extract",
                content,
                language,
                filePath
            };

            var requestJson = JsonSerializer.Serialize(request);

            // Send request
            await _process.StandardInput.WriteLineAsync(requestJson);
            await _process.StandardInput.FlushAsync();

            // Read response with timeout
            var responseTask = _process.StandardOutput.ReadLineAsync();
            var timeoutTask = Task.Delay(30000); // 30 second timeout

            var completedTask = await Task.WhenAny(responseTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                _logger.LogError("Tree-sitter service timed out processing file: {FilePath}", filePath);
                return new TypeExtractionResult { Success = false };
            }

            var responseJson = await responseTask;
            if (string.IsNullOrEmpty(responseJson))
            {
                _logger.LogError("Empty response from Tree-sitter service");
                return new TypeExtractionResult { Success = false };
            }

            // Parse response
            var response = JsonSerializer.Deserialize<BunServiceResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (response == null)
            {
                _logger.LogError("Failed to parse Tree-sitter service response");
                return new TypeExtractionResult { Success = false };
            }

            // Convert to TypeExtractionResult
            if (!response.Success && !string.IsNullOrEmpty(response.Error))
            {
                _logger.LogError("Tree-sitter service error: {Error}", response.Error);
            }

            return new TypeExtractionResult
            {
                Success = response.Success,
                Types = response.Types ?? new List<TypeInfo>(),
                Methods = response.Methods ?? new List<MethodInfo>(),
                Language = response.Language ?? language
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting types for file: {FilePath}", filePath);
            return new TypeExtractionResult
            {
                Success = false
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string DetectLanguage(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return "unknown";
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "c-sharp",
            ".py" => "python",
            ".go" => "go",
            ".js" or ".jsx" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".java" => "java",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".cpp" or ".cxx" or ".cc" => "cpp",
            ".php" => "php",
            // New language support!
            ".razor" or ".cshtml" => "razor",
            ".swift" => "swift",
            ".kt" or ".kts" => "kotlin",
            _ => "unknown"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _logger.LogInformation("Stopping Tree-sitter service process");
                _process.Kill();
                _process.WaitForExit(5000);
            }

            _process?.Dispose();
            _semaphore?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing BunTreeSitterService");
        }
    }

    // Response model matching the Bun service output
    private class BunServiceResponse
    {
        public bool Success { get; set; }
        public List<TypeInfo>? Types { get; set; }
        public List<MethodInfo>? Methods { get; set; }
        public string? Language { get; set; }
        public string? Error { get; set; }
    }
}