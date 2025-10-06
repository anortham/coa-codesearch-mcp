using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Sqlite;

/// <summary>
/// Service for loading sqlite-vec extension for vector similarity search.
/// Provides cross-platform support for Windows, macOS (Intel/ARM), and Linux (x64/ARM).
/// </summary>
public interface ISqliteVecExtensionService
{
    /// <summary>
    /// Load the sqlite-vec extension into a SQLite connection.
    /// </summary>
    void LoadExtension(SqliteConnection connection);

    /// <summary>
    /// Check if sqlite-vec extension is available for current platform.
    /// </summary>
    bool IsAvailable();
}

public class SqliteVecExtensionService : ISqliteVecExtensionService
{
    private readonly ILogger<SqliteVecExtensionService> _logger;
    private readonly string? _extensionPath;
    private bool _extensionLoaded;

    public SqliteVecExtensionService(ILogger<SqliteVecExtensionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extensionPath = FindExtensionPath();
    }

    public bool IsAvailable() => !string.IsNullOrEmpty(_extensionPath) && File.Exists(_extensionPath);

    public void LoadExtension(SqliteConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        if (_extensionLoaded)
        {
            _logger.LogDebug("sqlite-vec extension already loaded");
            return;
        }

        if (!IsAvailable())
        {
            throw new InvalidOperationException(
                "sqlite-vec extension not found for current platform. " +
                "Ensure binaries are present in bin/sqlite-vec/{platform}/");
        }

        try
        {
            // Enable extension loading (required by Microsoft.Data.Sqlite)
            connection.EnableExtensions(true);

            // Load extension without entry point (uses default sqlite3_vec_init)
            connection.LoadExtension(_extensionPath!);
            _extensionLoaded = true;

            _logger.LogInformation("✅ Loaded sqlite-vec extension from {Path}", _extensionPath);
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Failed to load sqlite-vec extension from {Path}", _extensionPath);
            throw new InvalidOperationException($"Failed to load sqlite-vec extension: {ex.Message}", ex);
        }
    }

    private string? FindExtensionPath()
    {
        // Determine platform and architecture
        var (platform, extension) = GetPlatformInfo();

        // Path: bin/sqlite-vec/{platform}/vec0.{extension}
        var baseDir = AppContext.BaseDirectory;
        var relativePath = Path.Combine("bin", "sqlite-vec", platform, $"vec0{extension}");
        var fullPath = Path.Combine(baseDir, relativePath);

        if (File.Exists(fullPath))
        {
            _logger.LogInformation("Found sqlite-vec extension at {Path}", fullPath);

            // Make executable on Unix platforms
            if (!OperatingSystem.IsWindows())
            {
                MakeExecutable(fullPath);
            }

            return fullPath;
        }

        _logger.LogWarning(
            "sqlite-vec extension not found at {Path} for platform {Platform}",
            fullPath,
            platform);

        return null;
    }

    private static (string platform, string extension) GetPlatformInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("windows-x64", ".dll");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var platform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "macos-arm64"
                : "macos-x64";
            return (platform, ".dylib");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var platform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
            return (platform, ".so");
        }

        throw new PlatformNotSupportedException(
            $"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    private void MakeExecutable(string path)
    {
        try
        {
            var chmod = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            chmod.Start();
            chmod.WaitForExit(timeout: TimeSpan.FromSeconds(5));

            _logger.LogDebug("Set executable permission on {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to chmod extension (may already be executable)");
            // Ignore - file might already be executable
        }
    }
}
