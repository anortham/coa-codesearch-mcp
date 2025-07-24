using Microsoft.Extensions.Logging;
using Serilog.Events;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for controlling file-based logging dynamically
/// </summary>
public class SetLoggingTool : ITool
{
    public string ToolName => "log_diagnostics";
    public string Description => "View and manage log files";
    public ToolCategory Category => ToolCategory.Infrastructure;
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
                "status" => await GetStatusAsync(),
                "list" => await ListLogsAsync(),
                "cleanup" => await CleanupLogsAsync(cleanup ?? false),
                _ => new
                {
                    success = false,
                    error = $"Unknown action: {action}",
                    validActions = new[] { "status", "list", "cleanup" }
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

    // Removed StartLoggingAsync and StopLoggingAsync - configuration-driven now

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

    // Removed SetLogLevelAsync - configuration-driven now

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

    // Removed ParseLogLevel - configuration-driven now

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