using Microsoft.Extensions.Logging;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Custom logging provider that ensures logs are written ONLY to files via Serilog
/// and never to stdout to avoid contaminating the MCP protocol stream
/// </summary>
public class FileLoggingProvider : ILoggerProvider
{
    private readonly ILogger _serilogLogger;
    private readonly Dictionary<string, FileLogger> _loggers = new();
    private readonly object _lock = new();

    public FileLoggingProvider(ILogger serilogLogger)
    {
        _serilogLogger = serilogLogger ?? throw new ArgumentNullException(nameof(serilogLogger));
    }

    public ILogger<T> CreateLogger<T>()
    {
        return (ILogger<T>)CreateLogger(typeof(T).FullName ?? typeof(T).Name);
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        lock (_lock)
        {
            if (!_loggers.TryGetValue(categoryName, out var logger))
            {
                logger = new FileLogger(_serilogLogger.ForContext("SourceContext", categoryName));
                _loggers[categoryName] = logger;
            }
            return logger;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _loggers.Clear();
        }
    }
}

/// <summary>
/// Logger implementation that writes to Serilog file logger
/// </summary>
internal class FileLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly ILogger _serilogLogger;

    public FileLogger(ILogger serilogLogger)
    {
        _serilogLogger = serilogLogger;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _serilogLogger.IsEnabled(ConvertLogLevel(logLevel));
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        var message = formatter(state, exception);

        switch (logLevel)
        {
            case LogLevel.Trace:
                _serilogLogger.Verbose(exception, message);
                break;
            case LogLevel.Debug:
                _serilogLogger.Debug(exception, message);
                break;
            case LogLevel.Information:
                _serilogLogger.Information(exception, message);
                break;
            case LogLevel.Warning:
                _serilogLogger.Warning(exception, message);
                break;
            case LogLevel.Error:
                _serilogLogger.Error(exception, message);
                break;
            case LogLevel.Critical:
                _serilogLogger.Fatal(exception, message);
                break;
            case LogLevel.None:
                break;
        }
    }

    private static LogEventLevel ConvertLogLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            LogLevel.None => LogEventLevel.Fatal + 1,
            _ => LogEventLevel.Information
        };
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}