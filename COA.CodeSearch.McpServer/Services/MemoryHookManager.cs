using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Manages cross-platform hook scripts for Claude's memory system
/// Copies hook scripts from embedded resources to user's project
/// </summary>
public class MemoryHookManager
{
    private readonly ILogger<MemoryHookManager> _logger;
    private readonly string _projectRoot;
    private readonly string _hooksDirectory;
    private readonly bool _isWindows;
    private readonly string _scriptExtension;
    private readonly string _platformName;

    public MemoryHookManager(ILogger<MemoryHookManager> logger, string? projectRoot = null)
    {
        _logger = logger;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _hooksDirectory = Path.Combine(_projectRoot, ".claude", "hooks");
        
        // Determine platform
        _isWindows = OperatingSystem.IsWindows();
        _scriptExtension = _isWindows ? ".ps1" : ".sh";
        _platformName = _isWindows ? "Windows" : "Unix/Linux/macOS";
    }

    /// <summary>
    /// Initializes memory hooks for the current project
    /// </summary>
    public async Task<bool> InitializeHooksAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Claude Memory System hooks for {Platform} in {Directory}", 
                _platformName, _projectRoot);

            // Create hooks directory if it doesn't exist
            Directory.CreateDirectory(_hooksDirectory);

            // Get all embedded hook resources
            var assembly = Assembly.GetExecutingAssembly();
            var allResourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.Contains("Resources.Hooks"))
                .ToList();

            if (allResourceNames.Count == 0)
            {
                _logger.LogError("No embedded hook resources found. Please ensure hooks are included as embedded resources.");
                return false;
            }

            // Copy platform-specific hooks and README
            var copiedFiles = await CopyHookResourcesAsync(assembly, allResourceNames);

            if (copiedFiles.Count > 0)
            {
                _logger.LogInformation("Created {Count} files in {Directory}", 
                    copiedFiles.Count, _hooksDirectory);
            }

            // Create or update settings.local.json
            await EnsureSettingsConfigurationAsync();

            _logger.LogInformation("✅ Memory hooks initialized successfully!");
            _logger.LogInformation("📝 To complete setup, ensure hooks are registered in .claude/settings.local.json");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize memory hooks");
            return false;
        }
    }

    private async Task<List<string>> CopyHookResourcesAsync(Assembly assembly, List<string> resourceNames)
    {
        var copiedFiles = new List<string>();
        
        // Always copy README
        var readmeResource = resourceNames.FirstOrDefault(n => n.EndsWith("README.md"));
        if (readmeResource != null)
        {
            var readmePath = Path.Combine(_hooksDirectory, "README.md");
            if (!File.Exists(readmePath))
            {
                await CopyResourceToFileAsync(assembly, readmeResource, readmePath);
                copiedFiles.Add("README.md");
            }
        }

        // Copy platform-specific scripts
        var scriptResources = resourceNames
            .Where(name => name.EndsWith(_scriptExtension))
            .ToList();

        foreach (var resourceName in scriptResources)
        {
            // Extract filename
            var parts = resourceName.Split('.');
            var fileName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : Path.GetFileName(resourceName);
            var targetPath = Path.Combine(_hooksDirectory, fileName);

            // Skip if exists (preserve user modifications)
            if (File.Exists(targetPath))
            {
                _logger.LogDebug("Preserving existing hook: {FileName}", fileName);
                continue;
            }

            await CopyResourceToFileAsync(assembly, resourceName, targetPath);
            copiedFiles.Add(fileName);

            // Make executable on Unix
            if (!_isWindows && fileName.EndsWith(".sh"))
            {
                MakeFileExecutable(targetPath);
            }
        }

        return copiedFiles;
    }

    private async Task CopyResourceToFileAsync(Assembly assembly, string resourceName, string targetPath)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Resource {resourceName} not found");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        
        // Normalize line endings
        if (_isWindows && targetPath.EndsWith(".ps1"))
        {
            content = content.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }
        else if (!_isWindows && targetPath.EndsWith(".sh"))
        {
            content = content.Replace("\r\n", "\n");
        }

        await File.WriteAllTextAsync(targetPath, content, Encoding.UTF8);
        _logger.LogInformation("Created: {FileName}", Path.GetFileName(targetPath));
    }

    private void MakeFileExecutable(string filePath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to make file executable: {FilePath}", filePath);
        }
    }

    private async Task EnsureSettingsConfigurationAsync()
    {
        var settingsPath = Path.Combine(_projectRoot, ".claude", "settings.local.json");
        
        if (File.Exists(settingsPath))
        {
            // Check if hooks are configured
            try
            {
                var content = await File.ReadAllTextAsync(settingsPath);
                if (content.Contains("\"hooks\""))
                {
                    _logger.LogInformation("Found existing settings.local.json with hooks configuration");
                    return;
                }
                _logger.LogWarning("settings.local.json exists but lacks hooks configuration");
            }
            catch
            {
                _logger.LogWarning("Could not read settings.local.json");
            }
        }

        // Create example configuration
        var examplePath = settingsPath + ".example";
        var hookCommand = _isWindows 
            ? "powershell -ExecutionPolicy Bypass -File \\\".claude/hooks/{0}.ps1\\\""
            : "bash .claude/hooks/{0}.sh";

        var settings = new
        {
            hooks = new Dictionary<string, object>
            {
                ["UserPromptSubmit"] = CreateHookConfig("user-prompt-submit", hookCommand),
                ["PreToolUse"] = CreateHookConfig("pre-tool-use", hookCommand),
                ["PostToolUse"] = CreateHookConfig("post-tool-use", hookCommand),
                ["Stop"] = CreateHookConfig("stop", hookCommand),
                ["PreCompact"] = CreateHookConfig("pre-compact", hookCommand)
            }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(examplePath, json);
        _logger.LogInformation("Created example configuration: {Path}", examplePath);
        
        if (!File.Exists(settingsPath))
        {
            _logger.LogInformation("💡 Copy {Example} to {Settings} to enable hooks", 
                Path.GetFileName(examplePath), Path.GetFileName(settingsPath));
        }
    }

    private static object CreateHookConfig(string hookName, string commandTemplate)
    {
        return new[]
        {
            new
            {
                matcher = "*",
                hooks = new[]
                {
                    new
                    {
                        type = "command",
                        command = string.Format(commandTemplate, hookName)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Tests if hooks are properly set up
    /// </summary>
    public async Task<bool> TestHookAsync(string hookType)
    {
        try
        {
            var hookPath = Path.Combine(_hooksDirectory, $"{hookType}{_scriptExtension}");

            if (!File.Exists(hookPath))
            {
                _logger.LogWarning("Hook not found: {HookPath}", hookPath);
                return false;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _isWindows ? "powershell" : "bash",
                Arguments = _isWindows ? $"-ExecutionPolicy Bypass -File \"{hookPath}\"" : hookPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _projectRoot
            };

            // Set test environment variables
            startInfo.Environment["CLAUDE_HOOK_EVENT_NAME"] = hookType;
            startInfo.Environment["CLAUDE_TOOL_NAME"] = "test";
            startInfo.Environment["CLAUDE_TOOL_PARAMS"] = "{}";

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start hook process");
                return false;
            }

            await process.WaitForExitAsync();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("✅ Hook {HookType} executed successfully", hookType);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogInformation("Output: {Output}", output.Trim());
                }
                return true;
            }
            else
            {
                _logger.LogError("❌ Hook {HookType} failed with exit code {ExitCode}", 
                    hookType, process.ExitCode);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogError("Error: {Error}", error.Trim());
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test hook {HookType}", hookType);
            return false;
        }
    }

    /// <summary>
    /// Gets the status of all hooks
    /// </summary>
    public Dictionary<string, HookStatus> GetHookStatus()
    {
        var status = new Dictionary<string, HookStatus>();
        var requiredHooks = new[]
        {
            "user-prompt-submit", "pre-tool-use", "post-tool-use", 
            "stop", "pre-compact", "file-edit"
        };

        foreach (var hookName in requiredHooks)
        {
            var expectedPath = Path.Combine(_hooksDirectory, $"{hookName}{_scriptExtension}");
            var exists = File.Exists(expectedPath);

            status[hookName] = new HookStatus
            {
                Name = hookName,
                Platform = _platformName,
                ScriptPath = expectedPath,
                Exists = exists,
                Extension = _scriptExtension
            };
        }

        return status;
    }
}

public class HookStatus
{
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string ScriptPath { get; set; } = "";
    public bool Exists { get; set; }
    public string Extension { get; set; } = "";
}