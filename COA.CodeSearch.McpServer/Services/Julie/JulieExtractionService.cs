using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Services.Julie;

/// <summary>
/// Service for invoking julie-extract CLI to extract symbols from source files.
/// Supports single file, bulk directory, and streaming extraction modes.
/// </summary>
public interface IJulieExtractionService
{
    /// <summary>
    /// Extract symbols from a single file (fast, JSON output)
    /// </summary>
    Task<List<JulieSymbol>> ExtractSingleFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk extract entire directory to SQLite (parallel, optimized for initial indexing)
    /// </summary>
    Task<BulkExtractionResult> BulkExtractDirectoryAsync(
        string directoryPath,
        string outputDbPath,
        int? threads = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream symbols from directory (NDJSON, memory-efficient for large workspaces)
    /// </summary>
    IAsyncEnumerable<JulieSymbol> StreamExtractDirectoryAsync(
        string directoryPath,
        int? threads = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if julie-extract is available
    /// </summary>
    bool IsAvailable();
}

public class JulieExtractionService : IJulieExtractionService, ITypeExtractionService
{
    private readonly ILogger<JulieExtractionService> _logger;
    private readonly string _julieExtractPath;

    public JulieExtractionService(ILogger<JulieExtractionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _julieExtractPath = FindJulieExtractBinary();

        // Debug logging for initialization
        if (string.IsNullOrEmpty(_julieExtractPath))
        {
            _logger.LogWarning("JulieExtractionService initialized but julie-extract binary not found");
        }
        else
        {
            _logger.LogInformation("JulieExtractionService initialized with julie-extract at: {Path}", _julieExtractPath);
        }
    }

    public bool IsAvailable()
    {
        var available = !string.IsNullOrEmpty(_julieExtractPath) && File.Exists(_julieExtractPath);
        _logger.LogInformation("🔍 Julie extraction IsAvailable(): {Available}, Path: {Path}", available, _julieExtractPath ?? "null");
        return available;
    }

    public async Task<List<JulieSymbol>> ExtractSingleFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("julie-extract binary not found");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _julieExtractPath,
            Arguments = $"single --file \"{filePath}\" --output json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogDebug("Extracting symbols from {FilePath}", filePath);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-extract process");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var errors = await errorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError("julie-extract failed: {Errors}", errors);
            throw new InvalidOperationException($"julie-extract exited with code {process.ExitCode}: {errors}");
        }

        if (!string.IsNullOrWhiteSpace(errors))
        {
            _logger.LogDebug("julie-extract stderr: {Errors}", errors);
        }

        // Deserialize JSON output
        var symbols = JsonSerializer.Deserialize<List<JulieSymbol>>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<JulieSymbol>();

        _logger.LogInformation("Extracted {Count} symbols from {FilePath}", symbols.Count, filePath);

        return symbols;
    }

    public async Task<BulkExtractionResult> BulkExtractDirectoryAsync(
        string directoryPath,
        string outputDbPath,
        int? threads = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("julie-extract binary not found");
        }

        var threadArg = threads.HasValue ? $"--threads {threads.Value}" : "";
        var startInfo = new ProcessStartInfo
        {
            FileName = _julieExtractPath,
            Arguments = $"bulk --directory \"{directoryPath}\" --output-db \"{outputDbPath}\" {threadArg}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Starting bulk extraction: {Directory} -> {OutputDb}", directoryPath, outputDbPath);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-extract process");
        }

        // Stream stderr for progress updates
        var progressTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogInformation("julie-extract: {Progress}", line);
                }
            }
        }, cancellationToken);

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await progressTask;

        var output = await outputTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"julie-extract exited with code {process.ExitCode}");
        }

        // Parse JSON result
        var result = JsonSerializer.Deserialize<BulkExtractionResult>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new BulkExtractionResult { Success = false };

        _logger.LogInformation(
            "Bulk extraction complete: {SymbolCount} symbols in {OutputDb}",
            result.SymbolCount,
            result.OutputDb);

        return result;
    }

    public async IAsyncEnumerable<JulieSymbol> StreamExtractDirectoryAsync(
        string directoryPath,
        int? threads = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("julie-extract binary not found");
        }

        var threadArg = threads.HasValue ? $"--threads {threads.Value}" : "";
        var startInfo = new ProcessStartInfo
        {
            FileName = _julieExtractPath,
            Arguments = $"stream --directory \"{directoryPath}\" {threadArg}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Starting streaming extraction: {Directory}", directoryPath);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start julie-extract process");
        }

        // Background task to log stderr
        var _ = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogDebug("julie-extract: {Progress}", line);
                }
            }
        }, cancellationToken);

        // Stream NDJSON lines
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var symbol = JsonSerializer.Deserialize<JulieSymbol>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (symbol != null)
            {
                yield return symbol;
            }
        }

        await process.WaitForExitAsync(cancellationToken);
    }

    private string FindJulieExtractBinary()
    {
        // 1. Check bundled binaries (deployed with CodeSearch)
        var bundledPath = Path.Combine(
            AppContext.BaseDirectory,
            "bin",
            "julie-extract" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        if (File.Exists(bundledPath))
        {
            _logger.LogInformation("Found bundled julie-extract: {Path}", bundledPath);
            return bundledPath;
        }

        // 2. Check development path (Julie project)
        var devPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Source",
            "julie",
            "target",
            "release",
            "julie-extract" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        if (File.Exists(devPath))
        {
            _logger.LogInformation("Found development julie-extract: {Path}", devPath);
            return devPath;
        }

        // 3. Check PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            var paths = pathVar.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "julie-extract" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                if (File.Exists(fullPath))
                {
                    _logger.LogInformation("Found julie-extract in PATH: {Path}", fullPath);
                    return fullPath;
                }
            }
        }

        _logger.LogWarning("julie-extract binary not found");
        return string.Empty;
    }

    /// <summary>
    /// ITypeExtractionService implementation - extracts types and methods using julie-extract
    /// </summary>
    public async Task<TypeExtractionResult> ExtractTypes(string content, string filePath)
    {
        if (!IsAvailable())
        {
            _logger.LogDebug("julie-extract not available, returning empty result");
            return new TypeExtractionResult { Success = false };
        }

        try
        {
            // Call julie-extract for single file
            var symbols = await ExtractSingleFileAsync(filePath);

            // Map JulieSymbols to TypeInfo/MethodInfo
            var types = new List<TypeInfo>();
            var methods = new List<MethodInfo>();

            foreach (var symbol in symbols)
            {
                // Map types (class, interface, struct, enum, etc.)
                if (IsTypeSymbol(symbol.Kind))
                {
                    types.Add(new TypeInfo
                    {
                        Name = symbol.Name,
                        Kind = symbol.Kind,
                        Signature = symbol.Signature ?? symbol.Name,
                        Line = symbol.StartLine,
                        Column = symbol.StartColumn,
                        Modifiers = symbol.Visibility != null ? new List<string> { symbol.Visibility } : new()
                    });
                }
                // Map methods/functions
                else if (IsMethodSymbol(symbol.Kind))
                {
                    methods.Add(new MethodInfo
                    {
                        Name = symbol.Name,
                        Signature = symbol.Signature ?? symbol.Name,
                        Line = symbol.StartLine,
                        Column = symbol.StartColumn,
                        Modifiers = symbol.Visibility != null ? new List<string> { symbol.Visibility } : new()
                    });
                }
            }

            return new TypeExtractionResult
            {
                Success = true,
                Types = types,
                Methods = methods,
                Language = symbols.FirstOrDefault()?.Language
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract types from {FilePath} using julie-extract", filePath);
            return new TypeExtractionResult { Success = false };
        }
    }

    private static bool IsTypeSymbol(string kind)
    {
        return kind.Equals("class", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("interface", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("struct", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("enum", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("trait", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("type", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMethodSymbol(string kind)
    {
        return kind.Equals("method", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("function", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("constructor", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Symbol extracted by julie-extract (matches Julie's Symbol struct)
/// </summary>
public class JulieSymbol
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("start_line")]
    public int StartLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("start_column")]
    public int StartColumn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_line")]
    public int EndLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_column")]
    public int EndColumn { get; set; }

    public string? Signature { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("doc_comment")]
    public string? DocComment { get; set; }

    public string? Visibility { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }
}

/// <summary>
/// Result from bulk extraction operation
/// </summary>
public class BulkExtractionResult
{
    public bool Success { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("symbol_count")]
    public int SymbolCount { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("output_db")]
    public string OutputDb { get; set; } = string.Empty;
}
