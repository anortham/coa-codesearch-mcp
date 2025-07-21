using Microsoft.Extensions.Logging;
using Serilog.Events;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for controlling file-based logging dynamically
/// </summary>
public class SetLoggingTool
{
    private readonly ILogger<SetLoggingTool> _logger;
    private readonly FileLoggingService _fileLoggingService;
    private readonly IPathResolutionService _pathResolution;

    public SetLoggingTool(
        ILogger<SetLoggingTool> logger,
        FileLoggingService fileLoggingService,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _fileLoggingService = fileLoggingService;
        _pathResolution = pathResolution;
    }

    /// <summary>
    /// Control file logging settings
    /// </summary>
    public async Task<object> ExecuteAsync(
        string action,
        string? level = null,
        bool? cleanup = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return action.ToLowerInvariant() switch
            {
                "start" => await StartLoggingAsync(level),
                "stop" => await StopLoggingAsync(),
                "status" => await GetStatusAsync(),
                "list" => await ListLogsAsync(),
                "setlevel" => await SetLogLevelAsync(level),
                "cleanup" => await CleanupLogsAsync(cleanup ?? false),
                _ => new
                {
                    success = false,
                    error = $"Unknown action: {action}",
                    validActions = new[] { "start", "stop", "status", "list", "setlevel", "cleanup" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SetLoggingTool");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private Task<object> StartLoggingAsync(string? level)
    {
        var logLevel = ParseLogLevel(level);
        
        _fileLoggingService.StartLogging(logLevel);
        
        return Task.FromResult<object>(new
        {
            success = true,
            message = "File logging started",
            logFile = _fileLoggingService.CurrentLogFile,
            logLevel = logLevel.ToString(),
            hint = "Logs are being written to the .codesearch/logs directory"
        });
    }

    private Task<object> StopLoggingAsync()
    {
        _fileLoggingService.StopLogging();
        
        return Task.FromResult<object>(new
        {
            success = true,
            message = "File logging stopped"
        });
    }

    private Task<object> GetStatusAsync()
    {
        var logFiles = _fileLoggingService.GetLogFiles();
        
        return Task.FromResult<object>(new
        {
            success = true,
            isEnabled = _fileLoggingService.IsEnabled,
            currentLogFile = _fileLoggingService.CurrentLogFile,
            currentLogLevel = _fileLoggingService.CurrentLogLevel.ToString(),
            logDirectory = _pathResolution.GetLogsPath(),
            logFileCount = logFiles.Count,
            totalSize = logFiles.Sum(f => f.SizeInBytes),
            totalSizeFormatted = FormatFileSize(logFiles.Sum(f => f.SizeInBytes))
        });
    }

    private Task<object> ListLogsAsync()
    {
        var logFiles = _fileLoggingService.GetLogFiles();
        
        return Task.FromResult<object>(new
        {
            success = true,
            logFiles = logFiles.Select(f => new
            {
                fileName = f.FileName,
                size = f.FormattedSize,
                created = f.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                lastModified = f.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                isCurrentLog = f.IsCurrentLog,
                path = f.FilePath
            }).ToArray(),
            totalCount = logFiles.Count,
            totalSize = FormatFileSize(logFiles.Sum(f => f.SizeInBytes))
        });
    }

    private Task<object> SetLogLevelAsync(string? level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return Task.FromResult<object>(new
            {
                success = false,
                error = "Log level is required",
                validLevels = new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" }
            });
        }

        var logLevel = ParseLogLevel(level);
        _fileLoggingService.SetLogLevel(logLevel);
        
        return Task.FromResult<object>(new
        {
            success = true,
            message = $"Log level changed to {logLevel}",
            previousLevel = _fileLoggingService.CurrentLogLevel.ToString(),
            newLevel = logLevel.ToString()
        });
    }

    private Task<object> CleanupLogsAsync(bool confirm)
    {
        if (!confirm)
        {
            var logFiles = _fileLoggingService.GetLogFiles();
            var oldFiles = logFiles.Skip(10).ToList();
            
            return Task.FromResult<object>(new
            {
                success = false,
                message = "Cleanup requires confirmation",
                hint = "Set cleanup parameter to true to confirm deletion",
                filesWouldBeDeleted = oldFiles.Count,
                sizeWouldBeFreed = FormatFileSize(oldFiles.Sum(f => f.SizeInBytes))
            });
        }

        _fileLoggingService.CleanupOldLogs();
        
        return Task.FromResult<object>(new
        {
            success = true,
            message = "Old log files cleaned up (kept most recent 10 files)"
        });
    }

    private static LogEventLevel ParseLogLevel(string? level)
    {
        if (string.IsNullOrEmpty(level))
            return LogEventLevel.Debug;  // Default to Debug for temporary debugging sessions

        return level.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" or "info" => LogEventLevel.Information,
            "warning" or "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Debug  // Default to Debug for unrecognized levels
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}