using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Locates the Razor Language Server (rzls.exe) on the system
/// Primary location: VS Code C# extension directory
/// </summary>
public class RazorServerLocator
{
    private readonly ILogger<RazorServerLocator> _logger;
    private readonly IConfiguration _configuration;

    public RazorServerLocator(ILogger<RazorServerLocator> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Finds the Razor Language Server executable using multiple strategies
    /// </summary>
    /// <returns>Path to rzls.exe or null if not found</returns>
    public async Task<string?> FindRazorServerAsync()
    {
        _logger.LogDebug("Searching for Razor Language Server...");

        // 1. Check user configuration first
        var configuredPath = _configuration["Razor:ServerPath"];
        if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
        {
            _logger.LogInformation("Found Razor server at configured path: {Path}", configuredPath);
            return configuredPath;
        }

        // 2. Check VS Code extensions directory (primary location)
        var vsCodePaths = GetVSCodeExtensionPaths();
        foreach (var extensionPath in vsCodePaths)
        {
            var rzlsPath = await FindRzlsInExtensionAsync(extensionPath);
            if (rzlsPath != null)
            {
                _logger.LogInformation("Found Razor server in VS Code extension: {Path}", rzlsPath);
                return rzlsPath;
            }
        }

        // 3. Check if available as dotnet global tool
        var toolPath = await CheckDotnetToolAsync();
        if (toolPath != null)
        {
            _logger.LogInformation("Found Razor server as dotnet tool: {Path}", toolPath);
            return toolPath;
        }

        _logger.LogWarning("Razor Language Server not found. Install VS Code C# extension or configure Razor:ServerPath");
        return null;
    }

    /// <summary>
    /// Gets VS Code extension directory paths for current platform
    /// </summary>
    private string[] GetVSCodeExtensionPaths()
    {
        var paths = new List<string>();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            // User extensions directory
            paths.Add(Path.Combine(userProfile, ".vscode", "extensions"));
            
            // VS Code Insiders
            paths.Add(Path.Combine(userProfile, ".vscode-insiders", "extensions"));
        }

        // System-wide extensions (Windows)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
            {
                paths.Add(Path.Combine(programFiles, "Microsoft VS Code", "resources", "app", "extensions"));
            }
        }

        return paths.Where(Directory.Exists).ToArray();
    }

    /// <summary>
    /// Searches for rzls in a VS Code extension directory
    /// </summary>
    private async Task<string?> FindRzlsInExtensionAsync(string extensionsPath)
    {
        try
        {
            // Look for C# extension directories
            var csharpExtensions = Directory.GetDirectories(extensionsPath, "ms-dotnettools.csharp-*")
                .Concat(Directory.GetDirectories(extensionsPath, "ms-dotnettools.csdevkit-*"))
                .ToArray();

            foreach (var extensionDir in csharpExtensions)
            {
                _logger.LogDebug("Checking C# extension: {Path}", extensionDir);

                // Primary location: .razor subdirectory
                var razorDir = Path.Combine(extensionDir, ".razor");
                if (Directory.Exists(razorDir))
                {
                    var rzlsPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? Path.Combine(razorDir, "rzls.exe")
                        : Path.Combine(razorDir, "rzls");

                    if (File.Exists(rzlsPath) && await VerifyRzlsExecutableAsync(rzlsPath))
                    {
                        return rzlsPath;
                    }
                }

                // Alternative location: languageserver subdirectory
                var languageServerDir = Path.Combine(extensionDir, "languageserver");
                if (Directory.Exists(languageServerDir))
                {
                    var rzlsPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? Path.Combine(languageServerDir, "rzls.exe")
                        : Path.Combine(languageServerDir, "rzls");

                    if (File.Exists(rzlsPath) && await VerifyRzlsExecutableAsync(rzlsPath))
                    {
                        return rzlsPath;
                    }
                }

                // Check package.json for server location hints
                var packageJsonPath = Path.Combine(extensionDir, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var razorPath = await ParsePackageJsonForRazorServerAsync(packageJsonPath, extensionDir);
                    if (razorPath != null && await VerifyRzlsExecutableAsync(razorPath))
                    {
                        return razorPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching for Razor server in {Path}", extensionsPath);
        }

        return null;
    }

    /// <summary>
    /// Parses package.json for Razor server location information
    /// </summary>
    private async Task<string?> ParsePackageJsonForRazorServerAsync(string packageJsonPath, string extensionDir)
    {
        try
        {
            var json = await File.ReadAllTextAsync(packageJsonPath);
            using var doc = JsonDocument.Parse(json);

            // This is a simplified implementation
            // In practice, the extension's package.json may contain configuration
            // pointing to the Razor server location
            // For now, we rely on standard locations

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing package.json at {Path}", packageJsonPath);
        }

        return null;
    }

    /// <summary>
    /// Checks if rzls is available as a dotnet global tool
    /// </summary>
    private async Task<string?> CheckDotnetToolAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool list -g",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && 
                    (output.Contains("microsoft.aspnetcore.razor.languageserver") || 
                     output.Contains("rzls")))
                {
                    return "rzls"; // Available as global tool
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for dotnet tool");
        }

        return null;
    }

    /// <summary>
    /// Verifies that the rzls executable is working
    /// </summary>
    private async Task<bool> VerifyRzlsExecutableAsync(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    var completed = true;
                    if (completed && process.ExitCode == 0)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        _logger.LogDebug("Razor server version check output: {Output}", output);
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout occurred
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error verifying Razor server at {Path}", path);
        }

        return false;
    }

    /// <summary>
    /// Gets installation instructions for the Razor Language Server
    /// </summary>
    public string GetInstallationInstructions()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Install VS Code with the C# extension from https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp"
            : "Install VS Code with the C# extension: code --install-extension ms-dotnettools.csharp";
    }
}