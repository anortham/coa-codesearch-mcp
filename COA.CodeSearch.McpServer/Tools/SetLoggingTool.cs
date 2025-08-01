using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Attributes;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for viewing and managing log files
/// </summary>
[McpServerToolType]
public class SetLoggingTool : ITool
{
    public string ToolName => "log_diagnostics";
    public string Description => "View and manage log files";
    public ToolCategory Category => ToolCategory.Infrastructure;
    private readonly ILogger<SetLoggingTool> _logger;
    private readonly IPathResolutionService _pathResolution;

    public SetLoggingTool(
        ILogger<SetLoggingTool> logger,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _pathResolution = pathResolution;
    }

    /// <summary>
    /// Control file logging settings - new parameter-based overload for attribute registration
    /// </summary>
    [McpServerTool(Name = "log_diagnostics")]
    [Description("View and manage log files. Logs are written to .codesearch/logs directory. Actions: status, list, cleanup")]
    public async Task<object> ExecuteAsync(SetLoggingParams parameters)
    {
        if (parameters?.Action == null)
        {
            throw new ArgumentNullException(nameof(parameters), "Action parameter is required");
        }
        
        // Call the existing implementation
        return await ExecuteAsync(
            parameters.Action,
            null, // level parameter removed - configuration-driven now
            parameters.Cleanup,
            CancellationToken.None);
    }
    
    /// <summary>
    /// Control file logging settings - existing implementation
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
        var logFiles = GetLogFiles();
        
        return Task.FromResult<object>(new
        {
            success = true,
            isEnabled = true, // Serilog is always enabled
            logDirectory = _pathResolution.GetLogsPath(),
            logFileCount = logFiles.Count,
            totalSize = logFiles.Sum(f => new FileInfo(f).Length),
            totalSizeFormatted = FormatFileSize(logFiles.Sum(f => new FileInfo(f).Length)),
            message = "Logging via Serilog - always enabled, Debug level minimum"
        });
    }

    private Task<object> ListLogsAsync()
    {
        var logFiles = GetLogFiles();
        
        return Task.FromResult<object>(new
        {
            success = true,
            logFiles = logFiles.Select(f => new
            {
                fileName = Path.GetFileName(f),
                size = FormatFileSize(new FileInfo(f).Length),
                created = new FileInfo(f).CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                lastModified = new FileInfo(f).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                path = f
            }).ToArray(),
            totalCount = logFiles.Count,
            totalSize = FormatFileSize(logFiles.Sum(f => new FileInfo(f).Length))
        });
    }

    // Removed SetLogLevelAsync - configuration-driven now

    private Task<object> CleanupLogsAsync(bool confirm)
    {
        if (!confirm)
        {
            var logFiles = GetLogFiles();
            var oldFiles = logFiles.Skip(10).ToList();
            
            return Task.FromResult<object>(new
            {
                success = false,
                message = "Cleanup requires confirmation",
                hint = "Set cleanup parameter to true to confirm deletion",
                filesWouldBeDeleted = oldFiles.Count,
                sizeWouldBeFreed = FormatFileSize(oldFiles.Sum(f => new FileInfo(f).Length))
            });
        }

        CleanupOldLogs();
        
        return Task.FromResult<object>(new
        {
            success = true,
            message = "Old log files cleaned up (kept most recent 10 files)"
        });
    }

    /// <summary>
    /// Get list of existing log files
    /// </summary>
    private List<string> GetLogFiles()
    {
        var files = new List<string>();
        
        try
        {
            var logDirectory = _pathResolution.GetLogsPath();
            if (Directory.Exists(logDirectory))
            {
                files = Directory.GetFiles(logDirectory, "codesearch*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(20) // Limit to last 20 files
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get log files");
        }

        return files;
    }

    /// <summary>
    /// Delete old log files (keep only the most recent 10 files)
    /// </summary>
    private void CleanupOldLogs()
    {
        try
        {
            var logDirectory = _pathResolution.GetLogsPath();
            if (Directory.Exists(logDirectory))
            {
                var files = Directory.GetFiles(logDirectory, "codesearch*.log", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(10)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                        _logger.LogInformation("Deleted old log file: {FileName}", file.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete log file: {FileName}", file.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old logs");
        }
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

/// <summary>
/// Parameters for the SetLoggingTool
/// </summary>
public class SetLoggingParams
{
    [Description("Action to perform: 'status', 'list', 'cleanup'")]
    public string? Action { get; set; }
    
    [Description("For 'cleanup' action: set to true to confirm deletion of old log files")]
    public bool? Cleanup { get; set; }
}