using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Handles automatic installation of TypeScript for the MCP server
/// </summary>
public class TypeScriptInstaller
{
    private readonly ILogger<TypeScriptInstaller> _logger;
    private readonly IPathResolutionService _pathResolution;
    private readonly HttpClient _httpClient;
    private readonly string _installPath;
    private const string TYPESCRIPT_VERSION = "5.3.3";
    private const string NPM_REGISTRY = "https://registry.npmjs.org";

    public TypeScriptInstaller(
        ILogger<TypeScriptInstaller> logger, 
        IPathResolutionService pathResolution,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _pathResolution = pathResolution;
        _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
        
        // Get TypeScript install path from PathResolutionService
        _installPath = _pathResolution.GetTypeScriptInstallPath();
    }

    /// <summary>
    /// Get the path to tsserver.js, installing TypeScript if necessary
    /// </summary>
    public async Task<string?> GetTsServerPathAsync(CancellationToken cancellationToken = default)
    {
        var tsServerPath = Path.Combine(_installPath, "node_modules", "typescript", "lib", "tsserver.js");
        
        if (File.Exists(tsServerPath))
        {
            _logger.LogInformation("TypeScript already installed at {Path}", tsServerPath);
            return tsServerPath;
        }

        _logger.LogInformation("TypeScript not found. Installing version {Version}...", TYPESCRIPT_VERSION);
        
        try
        {
            // Method 1: Try using npm if available
            if (await TryInstallWithNpmAsync(cancellationToken))
            {
                if (File.Exists(tsServerPath))
                {
                    _logger.LogInformation("TypeScript installed successfully using npm");
                    return tsServerPath;
                }
            }

            // Method 2: Direct download from npm registry
            if (await TryDirectDownloadAsync(cancellationToken))
            {
                if (File.Exists(tsServerPath))
                {
                    _logger.LogInformation("TypeScript installed successfully via direct download");
                    return tsServerPath;
                }
            }

            _logger.LogError("Failed to install TypeScript. TypeScript features will not be available.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing TypeScript");
            return null;
        }
    }

    private async Task<bool> TryInstallWithNpmAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if npm is available
            var npmCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "npm.cmd" : "npm";
            
            var npmCheck = new ProcessStartInfo
            {
                FileName = npmCommand,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var checkProcess = Process.Start(npmCheck))
            {
                if (checkProcess == null || !await WaitForExitAsync(checkProcess, 5000, cancellationToken))
                {
                    return false;
                }

                if (checkProcess.ExitCode != 0)
                {
                    return false;
                }
            }

            // Create package.json (directory already created by PathResolutionService)
            var packageJson = Path.Combine(_installPath, "package.json");
            await File.WriteAllTextAsync(packageJson, $$"""
                {
                  "name": "coa-codesearch-typescript",
                  "version": "1.0.0",
                  "private": true,
                  "dependencies": {
                    "typescript": "^{{TYPESCRIPT_VERSION}}"
                  }
                }
                """, cancellationToken);

            // Run npm install
            var npmInstall = new ProcessStartInfo
            {
                FileName = npmCommand,
                Arguments = "install",
                WorkingDirectory = _installPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var installProcess = Process.Start(npmInstall);
            if (installProcess == null)
            {
                return false;
            }

            var output = await installProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await installProcess.StandardError.ReadToEndAsync(cancellationToken);
            
            if (!await WaitForExitAsync(installProcess, 60000, cancellationToken))
            {
                installProcess.Kill();
                return false;
            }

            if (installProcess.ExitCode != 0)
            {
                _logger.LogWarning("npm install failed: {Error}", error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install TypeScript using npm");
            return false;
        }
    }

    private async Task<bool> TryDirectDownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get package metadata from npm registry
            var metadataUrl = $"{NPM_REGISTRY}/typescript/{TYPESCRIPT_VERSION}";
            var response = await _httpClient.GetAsync(metadataUrl, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch TypeScript metadata: {StatusCode}", response.StatusCode);
                return false;
            }

            var metadata = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Extract tarball URL (simple string search to avoid JSON dependency)
            var tarballStart = metadata.IndexOf("\"tarball\":\"", StringComparison.Ordinal);
            if (tarballStart < 0) return false;
            
            tarballStart += 11;
            var tarballEnd = metadata.IndexOf("\"", tarballStart, StringComparison.Ordinal);
            if (tarballEnd < 0) return false;
            
            var tarballUrl = metadata.Substring(tarballStart, tarballEnd - tarballStart);
            
            // Download tarball
            _logger.LogInformation("Downloading TypeScript from {Url}", tarballUrl);
            var tarballResponse = await _httpClient.GetAsync(tarballUrl, cancellationToken);
            
            if (!tarballResponse.IsSuccessStatusCode)
            {
                return false;
            }

            // Save and extract
            var tempFile = Path.GetTempFileName();
            try
            {
                await using (var fs = File.Create(tempFile))
                {
                    await tarballResponse.Content.CopyToAsync(fs, cancellationToken);
                }

                // Note: .tgz extraction would require additional libraries
                // For now, we'll return false and rely on npm method
                _logger.LogWarning("Direct download completed but extraction not implemented. Please ensure npm is available.");
                return false;
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download TypeScript directly");
            return false;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}