using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Julie;

/// <summary>
/// Service for invoking julie-semantic CLI to generate embeddings and perform semantic search.
/// Enables cross-language semantic understanding for smart refactoring and code discovery.
/// </summary>
public interface ISemanticIntelligenceService
{
    /// <summary>
    /// Generate embeddings for symbols in a database and save HNSW index
    /// </summary>
    Task<EmbeddingStats> GenerateEmbeddingsAsync(
        string symbolsDbPath,
        string? outputPath = null,
        string model = "bge-small",
        int batchSize = 100,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update embeddings for a single changed file (incremental)
    /// </summary>
    Task<EmbeddingStats> UpdateFileAsync(
        string filePath,
        string symbolsDbPath,
        string outputPath,
        string model = "bge-small",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if julie-semantic is available
    /// </summary>
    bool IsAvailable();
}

public class SemanticIntelligenceService : ISemanticIntelligenceService
{
    private readonly ILogger<SemanticIntelligenceService> _logger;
    private readonly string _julieSemanticPath;

    public SemanticIntelligenceService(ILogger<SemanticIntelligenceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _julieSemanticPath = FindJulieSemanticBinary();
    }

    public bool IsAvailable() => !string.IsNullOrEmpty(_julieSemanticPath) && File.Exists(_julieSemanticPath);

    public async Task<EmbeddingStats> GenerateEmbeddingsAsync(
        string symbolsDbPath,
        string? outputPath = null,
        string model = "bge-small",
        int batchSize = 100,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("julie-semantic binary not found");
        }

        var outputArg = !string.IsNullOrEmpty(outputPath) ? $"--output \"{outputPath}\"" : "";
        var limitArg = limit.HasValue ? $"--limit {limit.Value}" : "";
        var startInfo = new ProcessStartInfo
        {
            FileName = _julieSemanticPath,
            Arguments = $"embed --symbols-db \"{symbolsDbPath}\" {outputArg} --write-db --model {model} --batch-size {batchSize} {limitArg}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation(
            "Generating embeddings for {SymbolsDb} with model {Model}",
            symbolsDbPath,
            model);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-semantic process");
        }

        // Stream stderr for progress updates
        var progressTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogInformation("julie-semantic: {Progress}", line);
                }
            }
        }, cancellationToken);

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await progressTask;

        var output = await outputTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"julie-semantic exited with code {process.ExitCode}");
        }

        // Parse JSON statistics (julie-semantic outputs snake_case)
        var stats = JsonSerializer.Deserialize<EmbeddingStats>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }) ?? new EmbeddingStats { Success = false };

        _logger.LogInformation(
            "Embedding generation complete: {SymbolsProcessed} symbols, {Dimensions}D embeddings, {Rate:F1}ms avg",
            stats.SymbolsProcessed,
            stats.Dimensions,
            stats.AvgEmbeddingTimeMs);

        return stats;
    }

    private string FindJulieSemanticBinary()
    {
        // 1. Check bundled binaries (deployed with CodeSearch) - platform-specific names
        var binaryName = GetPlatformSpecificBinaryName();
        var bundledPath = Path.Combine(
            AppContext.BaseDirectory,
            "bin",
            "julie-binaries",
            binaryName);

        if (File.Exists(bundledPath))
        {
            _logger.LogInformation("Found bundled julie-semantic: {Path}", bundledPath);

            // Make executable on Unix platforms (Git LFS might lose permissions)
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var chmod = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{bundledPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    chmod.Start();
                    chmod.WaitForExit();
                }
                catch
                {
                    // Ignore chmod errors - binary might already be executable
                }
            }

            return bundledPath;
        }

        // 2. Check development path (Julie project)
        var devPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Source",
            "julie",
            "target",
            "release",
            "julie-semantic" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        if (File.Exists(devPath))
        {
            _logger.LogInformation("Found development julie-semantic: {Path}", devPath);
            return devPath;
        }

        // 3. Check PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            var paths = pathVar.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "julie-semantic" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                if (File.Exists(fullPath))
                {
                    _logger.LogInformation("Found julie-semantic in PATH: {Path}", fullPath);
                    return fullPath;
                }
            }
        }

        _logger.LogWarning("julie-semantic binary not found");
        return string.Empty;
    }

    private string GetPlatformSpecificBinaryName()
    {
        // Match Git LFS packaged binary naming convention
        if (OperatingSystem.IsWindows())
        {
            return "julie-semantic-windows-x64.exe";
        }

        if (OperatingSystem.IsLinux())
        {
            return "julie-semantic-linux-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "julie-semantic-macos-arm64"
                : "julie-semantic-macos-x64";
        }

        // Fallback
        return "julie-semantic";
    }

    public async Task<EmbeddingStats> UpdateFileAsync(
        string filePath,
        string symbolsDbPath,
        string outputPath,
        string model = "bge-small",
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("julie-semantic binary not found");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _julieSemanticPath,
            Arguments = $"update --file \"{filePath}\" --symbols-db \"{symbolsDbPath}\" --output \"{outputPath}\" --write-db --model {model}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Updating embeddings for {FilePath}", filePath);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-semantic process");
        }

        // Stream stderr for progress updates
        var progressTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogInformation("julie-semantic: {Message}", line);
                }
            }
        }, cancellationToken);

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await progressTask;

        var output = await outputTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"julie-semantic exited with code {process.ExitCode}");
        }

        // DEBUG: Log raw output to diagnose parsing issues
        _logger.LogDebug("julie-semantic update stdout: {Output}", output);

        // Parse JSON statistics (julie-semantic outputs snake_case)
        var stats = JsonSerializer.Deserialize<EmbeddingStats>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }) ?? new EmbeddingStats { Success = false };

        if (!stats.Success)
        {
            _logger.LogWarning("julie-semantic update failed. Raw: {Output}", output);
        }
        else if (stats.EmbeddingsGenerated == 0)
        {
            _logger.LogDebug("No embeddings generated for file (may have no symbols). Raw: {Output}", output);
        }

        _logger.LogInformation(
            "Updated embeddings for {FilePath}: {Symbols} symbols, {Embeddings} embeddings",
            filePath,
            stats.SymbolsProcessed,
            stats.EmbeddingsGenerated);

        return stats;
    }
}

/// <summary>
/// Statistics from embedding generation
/// </summary>
public class EmbeddingStats
{
    public bool Success { get; set; }
    public int SymbolsProcessed { get; set; }
    public int EmbeddingsGenerated { get; set; }
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public double AvgEmbeddingTimeMs { get; set; }
}
