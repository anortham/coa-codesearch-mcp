using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Julie;

/// <summary>
/// Service for invoking julie-codesearch CLI to manage SQLite database with symbols and file content.
/// Handles full directory scans and incremental single-file updates.
/// </summary>
public interface IJulieCodeSearchService
{
    /// <summary>
    /// Scan entire directory and populate SQLite database with files, symbols, and content.
    /// Uses Blake3 hashing for change detection - only processes changed files on subsequent scans.
    /// </summary>
    /// <param name="directoryPath">Directory to scan recursively</param>
    /// <param name="databasePath">SQLite database path (created if doesn't exist)</param>
    /// <param name="logFilePath">Optional log file for debugging (null = no file logging)</param>
    /// <param name="threads">Number of parallel threads (null = CPU count)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with processed/skipped file counts</returns>
    Task<ScanResult> ScanDirectoryAsync(
        string directoryPath,
        string databasePath,
        string? logFilePath = null,
        int? threads = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update single file in database (incremental update for FileWatcher).
    /// Detects changes via Blake3 hash - skips if unchanged, updates symbols if changed.
    /// </summary>
    /// <param name="filePath">File to update</param>
    /// <param name="databasePath">SQLite database path</param>
    /// <param name="logFilePath">Optional log file for debugging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating if file was updated/skipped/added</returns>
    Task<UpdateResult> UpdateFileAsync(
        string filePath,
        string databasePath,
        string? logFilePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if julie-codesearch CLI is available
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Get path to julie-codesearch binary
    /// </summary>
    string GetBinaryPath();
}

/// <summary>
/// Result from directory scan operation
/// </summary>
public class ScanResult
{
    public required int ProcessedFiles { get; init; }
    public required int SkippedFiles { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result from single file update operation
/// </summary>
public class UpdateResult
{
    public required UpdateAction Action { get; init; }
    public required double ElapsedMs { get; init; }
    public int SymbolCount { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum UpdateAction
{
    Added,      // New file added to database
    Updated,    // Existing file updated
    Skipped     // File unchanged (hash match)
}

public class JulieCodeSearchService : IJulieCodeSearchService
{
    private readonly ILogger<JulieCodeSearchService> _logger;
    private readonly string _binaryPath;

    public JulieCodeSearchService(ILogger<JulieCodeSearchService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _binaryPath = FindBinary();

        if (string.IsNullOrEmpty(_binaryPath))
        {
            _logger.LogWarning("JulieCodeSearchService initialized but julie-codesearch binary not found");
        }
        else
        {
            _logger.LogInformation("JulieCodeSearchService initialized with binary at: {Path}", _binaryPath);
        }
    }

    public bool IsAvailable() => !string.IsNullOrEmpty(_binaryPath) && File.Exists(_binaryPath);

    public string GetBinaryPath() => _binaryPath;

    public async Task<ScanResult> ScanDirectoryAsync(
        string directoryPath,
        string databasePath,
        string? logFilePath = null,
        int? threads = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            return new ScanResult
            {
                Success = false,
                ProcessedFiles = 0,
                SkippedFiles = 0,
                ElapsedSeconds = 0,
                ErrorMessage = "julie-codesearch binary not found"
            };
        }

        var args = $"scan --dir \"{directoryPath}\" --db \"{databasePath}\"";
        if (threads.HasValue)
        {
            args += $" --threads {threads.Value}";
        }
        if (!string.IsNullOrEmpty(logFilePath))
        {
            args += $" --log \"{logFilePath}\"";
        }

        _logger.LogInformation("Starting directory scan: {Directory} -> {Database}", directoryPath, databasePath);

        var (output, errorOutput, exitCode, elapsed) = await RunProcessAsync(args, cancellationToken);

        if (exitCode != 0)
        {
            _logger.LogError("julie-codesearch scan failed (exit {ExitCode}): {Error}", exitCode, errorOutput);
            return new ScanResult
            {
                Success = false,
                ProcessedFiles = 0,
                SkippedFiles = 0,
                ElapsedSeconds = elapsed,
                ErrorMessage = $"Exit code {exitCode}: {errorOutput}"
            };
        }

        // Parse output for processed/skipped counts
        var (processed, skipped) = ParseScanOutput(output);

        _logger.LogInformation("Scan complete: {Processed} processed, {Skipped} skipped in {Elapsed:F2}s",
            processed, skipped, elapsed);

        return new ScanResult
        {
            Success = true,
            ProcessedFiles = processed,
            SkippedFiles = skipped,
            ElapsedSeconds = elapsed
        };
    }

    public async Task<UpdateResult> UpdateFileAsync(
        string filePath,
        string databasePath,
        string? logFilePath = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            return new UpdateResult
            {
                Success = false,
                Action = UpdateAction.Skipped,
                ElapsedMs = 0,
                ErrorMessage = "julie-codesearch binary not found"
            };
        }

        var args = $"update --file \"{filePath}\" --db \"{databasePath}\"";
        if (!string.IsNullOrEmpty(logFilePath))
        {
            args += $" --log \"{logFilePath}\"";
        }

        _logger.LogDebug("Updating file: {File}", filePath);

        var (output, errorOutput, exitCode, elapsed) = await RunProcessAsync(args, cancellationToken);

        if (exitCode != 0)
        {
            _logger.LogError("julie-codesearch update failed (exit {ExitCode}): {Error}", exitCode, errorOutput);
            return new UpdateResult
            {
                Success = false,
                Action = UpdateAction.Skipped,
                ElapsedMs = elapsed * 1000,
                ErrorMessage = $"Exit code {exitCode}: {errorOutput}"
            };
        }

        // Parse output for action and symbol count
        var (action, symbolCount) = ParseUpdateOutput(output);

        _logger.LogDebug("Update complete: {Action}, {Symbols} symbols in {Elapsed:F2}ms",
            action, symbolCount, elapsed * 1000);

        return new UpdateResult
        {
            Success = true,
            Action = action,
            ElapsedMs = elapsed * 1000,
            SymbolCount = symbolCount
        };
    }

    private async Task<(string Output, string Error, int ExitCode, double ElapsedSeconds)> RunProcessAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-codesearch process");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        stopwatch.Stop();

        return (output, error, process.ExitCode, stopwatch.Elapsed.TotalSeconds);
    }

    private (int Processed, int Skipped) ParseScanOutput(string output)
    {
        // Example output:
        // ✅ Scan complete in 0.32s
        //    📊 Processed: 190 files
        //    ⏭️  Skipped: 0 files (unchanged)

        int processed = 0, skipped = 0;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("Processed:") && line.Contains("files"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"Processed:\s*(\d+)");
                if (match.Success)
                {
                    processed = int.Parse(match.Groups[1].Value);
                }
            }
            else if (line.Contains("Skipped:") && line.Contains("files"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"Skipped:\s*(\d+)");
                if (match.Success)
                {
                    skipped = int.Parse(match.Groups[1].Value);
                }
            }
        }

        return (processed, skipped);
    }

    private (UpdateAction Action, int SymbolCount) ParseUpdateOutput(string output)
    {
        // Example outputs:
        // ✅ Added in 4.59ms (1 symbols)
        // ✅ Updated in 4.51ms (2 symbols)
        // ⏭️  File unchanged, skipped in 0.94ms

        var action = UpdateAction.Skipped;
        var symbolCount = 0;

        if (output.Contains("Added in"))
        {
            action = UpdateAction.Added;
        }
        else if (output.Contains("Updated in"))
        {
            action = UpdateAction.Updated;
        }

        // Extract symbol count
        var match = System.Text.RegularExpressions.Regex.Match(output, @"\((\d+)\s+symbols?\)");
        if (match.Success)
        {
            symbolCount = int.Parse(match.Groups[1].Value);
        }

        return (action, symbolCount);
    }

    private string FindBinary()
    {
        // Check multiple possible locations for julie-codesearch binary
        var possiblePaths = new[]
        {
            // 1. Configured path (environment variable)
            Environment.GetEnvironmentVariable("JULIE_CODESEARCH_PATH"),

            // 2. Sibling to julie-extract (if that's configured/found)
            FindJulieBinarySibling(),

            // 3. Standard locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Source", "julie", "target", "release", "julie-codesearch"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "julie-codesearch"),
            "/usr/local/bin/julie-codesearch",

            // 4. PATH search
            FindInPath("julie-codesearch")
        };

        foreach (var path in possiblePaths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _logger.LogDebug("Found julie-codesearch at: {Path}", path);
                return path;
            }
        }

        _logger.LogWarning("julie-codesearch binary not found in standard locations");
        return string.Empty;
    }

    private string? FindJulieBinarySibling()
    {
        // If julie-extract is configured, julie-codesearch should be in the same directory
        var julieExtractPath = Environment.GetEnvironmentVariable("JULIE_EXTRACT_PATH");
        if (string.IsNullOrEmpty(julieExtractPath) || !File.Exists(julieExtractPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(julieExtractPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var codeSearchPath = Path.Combine(directory, "julie-codesearch");
        return File.Exists(codeSearchPath) ? codeSearchPath : null;
    }

    private string? FindInPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        var paths = pathVar.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore invalid paths
            }
        }

        return null;
    }
}
