using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing file-based logging that can be dynamically enabled/disabled
/// IMPORTANT: This service ensures logs are ONLY written to files, never to stdout
/// to avoid contaminating the MCP protocol stream
/// </summary>
public class FileLoggingService : IDisposable
{
    private readonly ILogger<FileLoggingService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILuceneIndexService _luceneIndexService;
    private Logger? _fileLogger;
    private FileLoggingProvider? _fileLoggingProvider;
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private bool _isEnabled = false;
    private readonly LoggingLevelSwitch _levelSwitch;

    public bool IsEnabled => _isEnabled;
    public string CurrentLogFile { get; private set; } = string.Empty;
    public LogEventLevel CurrentLogLevel => _levelSwitch.MinimumLevel;

    public FileLoggingService(ILogger<FileLoggingService> logger, ILoggerFactory loggerFactory, ILuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _luceneIndexService = luceneIndexService;
        
        // Use the same base path as indexes and memories, but in a logs subdirectory
        var basePath = _luceneIndexService.GetPhysicalIndexPath(".codesearch");
        // Strip the .codesearch suffix since it's already in the base path
        _logDirectory = Path.Combine(Path.GetDirectoryName(basePath)!, "logs");
        _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        
        // Ensure log directory exists
        try
        {
            Directory.CreateDirectory(_logDirectory);
            _logger.LogInformation("File logging service initialized. Log directory: {LogDirectory}", _logDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create log directory: {LogDirectory}", _logDirectory);
        }
    }

    /// <summary>
    /// Start file logging with the specified log level
    /// </summary>
    public void StartLogging(LogEventLevel logLevel = LogEventLevel.Information)
    {
        lock (_lock)
        {
            if (_isEnabled)
            {
                _logger.LogInformation("File logging is already enabled");
                return;
            }

            try
            {
                // Create timestamped log file name
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var logFileName = $"codesearch_{timestamp}.log";
                CurrentLogFile = Path.Combine(_logDirectory, logFileName);

                // Update log level
                _levelSwitch.MinimumLevel = logLevel;

                // Create file logger - IMPORTANT: Only writes to file, no console/stdout output
                _fileLogger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(_levelSwitch)
                    .WriteTo.File(
                        CurrentLogFile,
                        rollingInterval: RollingInterval.Hour,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB
                        retainedFileCountLimit: 10,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                // Create our custom file logging provider and add it to the logger factory
                // This ensures logs go through the normal ILogger interface but are written only to our file
                _fileLoggingProvider = new FileLoggingProvider(_fileLogger);
                (_loggerFactory as LoggerFactory)?.AddProvider(_fileLoggingProvider);

                _isEnabled = true;
                _logger.LogInformation("File logging started. Log file: {LogFile}, Level: {LogLevel}", CurrentLogFile, logLevel);
                
                // Write initial log entry
                _fileLogger.Information("=== COA CodeSearch MCP Server Log Started ===");
                _fileLogger.Information("Log Level: {LogLevel}", logLevel);
                _fileLogger.Information("Server Version: {Version}", typeof(FileLoggingService).Assembly.GetName().Version);
                _fileLogger.Information("Operating System: {OS}", Environment.OSVersion);
                _fileLogger.Information("CLR Version: {CLR}", Environment.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file logging");
                throw;
            }
        }
    }

    /// <summary>
    /// Stop file logging
    /// </summary>
    public void StopLogging()
    {
        lock (_lock)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("File logging is already disabled");
                return;
            }

            try
            {
                _fileLogger?.Information("=== COA CodeSearch MCP Server Log Stopped ===");
                
                // Remove the provider from the logger factory
                if (_fileLoggingProvider != null && _loggerFactory is LoggerFactory factory)
                {
                    // Note: LoggerFactory doesn't have a RemoveProvider method in .NET Core
                    // We'll just dispose the provider which will stop it from logging
                    _fileLoggingProvider.Dispose();
                    _fileLoggingProvider = null;
                }
                
                _fileLogger?.Dispose();
                _fileLogger = null;
                _isEnabled = false;
                CurrentLogFile = string.Empty;
                
                _logger.LogInformation("File logging stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping file logging");
            }
        }
    }

    /// <summary>
    /// Change the logging level dynamically
    /// </summary>
    public void SetLogLevel(LogEventLevel logLevel)
    {
        _levelSwitch.MinimumLevel = logLevel;
        _logger.LogInformation("Log level changed to: {LogLevel}", logLevel);
        _fileLogger?.Information("Log level changed to: {LogLevel}", logLevel);
    }

    /// <summary>
    /// Get list of existing log files
    /// </summary>
    public List<LogFileInfo> GetLogFiles()
    {
        var files = new List<LogFileInfo>();
        
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                var logFiles = Directory.GetFiles(_logDirectory, "codesearch_*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(20); // Limit to last 20 files

                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    files.Add(new LogFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FilePath = file,
                        SizeInBytes = fileInfo.Length,
                        CreatedAt = fileInfo.CreationTime,
                        LastModified = fileInfo.LastWriteTime,
                        IsCurrentLog = file == CurrentLogFile
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get log files");
        }

        return files;
    }

    /// <summary>
    /// Delete old log files (keep only the most recent N files)
    /// </summary>
    public void CleanupOldLogs(int keepCount = 10)
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                var files = Directory.GetFiles(_logDirectory, "codesearch_*.log", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(keepCount)
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

    public void Dispose()
    {
        StopLogging();
    }
}

/// <summary>
/// Information about a log file
/// </summary>
public class LogFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsCurrentLog { get; set; }
    
    public string FormattedSize => FormatFileSize(SizeInBytes);
    
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