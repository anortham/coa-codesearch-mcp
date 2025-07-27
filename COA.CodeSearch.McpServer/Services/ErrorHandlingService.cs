using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Error severity levels for classification and handling strategy
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Expected errors that are part of normal operation (e.g., file access denied)
    /// </summary>
    Expected,
    
    /// <summary>
    /// Recoverable errors that may indicate transient issues (e.g., network timeout)
    /// </summary>
    Recoverable,
    
    /// <summary>
    /// Critical errors that indicate serious problems (e.g., index corruption)
    /// </summary>
    Critical,
    
    /// <summary>
    /// Fatal errors that require immediate attention (e.g., out of memory)
    /// </summary>
    Fatal
}

/// <summary>
/// Error context information for centralized error handling
/// </summary>
public class ErrorContext
{
    public string Operation { get; }
    public string? FilePath { get; }
    public string? WorkspacePath { get; }
    public TimeSpan? Duration { get; }
    public Dictionary<string, object>? AdditionalData { get; }
    public string CallerMemberName { get; }
    public string CallerFilePath { get; }
    public int CallerLineNumber { get; }

    public ErrorContext(
        string operation,
        string? filePath = null,
        string? workspacePath = null,
        TimeSpan? duration = null,
        Dictionary<string, object>? additionalData = null,
        [CallerMemberName] string callerMemberName = "",
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = 0)
    {
        Operation = operation;
        FilePath = filePath;
        WorkspacePath = workspacePath;
        Duration = duration;
        AdditionalData = additionalData;
        CallerMemberName = callerMemberName;
        CallerFilePath = callerFilePath;
        CallerLineNumber = callerLineNumber;
    }
}

/// <summary>
/// Error handling result containing recovery information and metrics
/// </summary>
public class ErrorHandlingResult
{
    public bool ShouldRetry { get; }
    public bool ShouldCircuitBreak { get; }
    public bool ShouldLog { get; }
    public LogLevel LogLevel { get; }
    public string? RecoveryAction { get; }
    public TimeSpan? RetryDelay { get; }

    public ErrorHandlingResult(
        bool shouldRetry = false,
        bool shouldCircuitBreak = false,
        bool shouldLog = true,
        LogLevel logLevel = LogLevel.Error,
        string? recoveryAction = null,
        TimeSpan? retryDelay = null)
    {
        ShouldRetry = shouldRetry;
        ShouldCircuitBreak = shouldCircuitBreak;
        ShouldLog = shouldLog;
        LogLevel = logLevel;
        RecoveryAction = recoveryAction;
        RetryDelay = retryDelay;
    }
}

/// <summary>
/// Centralized error handling service that provides consistent error logging,
/// classification, and recovery strategies across all services.
/// 
/// Key Features:
/// - Standardized error classification by severity and type
/// - Consistent logging patterns with context information
/// - Recovery strategy recommendations
/// - Integration with circuit breaker patterns
/// - Performance impact tracking
/// </summary>
public interface IErrorHandlingService
{
    /// <summary>
    /// Handle an exception with context and return recovery strategy
    /// </summary>
    ErrorHandlingResult HandleException(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable);
    
    /// <summary>
    /// Log an error with consistent formatting and context
    /// </summary>
    void LogError(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable);
    
    /// <summary>
    /// Log a warning with context
    /// </summary>
    void LogWarning(string message, ErrorContext context, Exception? exception = null);
    
    /// <summary>
    /// Classify an exception to determine its severity
    /// </summary>
    ErrorSeverity ClassifyException(Exception exception);
    
    /// <summary>
    /// Execute an operation with centralized error handling
    /// </summary>
    Task<T> ExecuteWithErrorHandlingAsync<T>(
        Func<Task<T>> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute an operation with centralized error handling (no return value)
    /// </summary>
    Task ExecuteWithErrorHandlingAsync(
        Func<Task> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default);
}

public class ErrorHandlingService : IErrorHandlingService
{
    private readonly ILogger<ErrorHandlingService> _logger;
    private readonly IIndexingMetricsService _metricsService;

    public ErrorHandlingService(
        ILogger<ErrorHandlingService> logger,
        IIndexingMetricsService metricsService)
    {
        _logger = logger;
        _metricsService = metricsService;
    }

    public ErrorHandlingResult HandleException(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable)
    {
        // Auto-classify if not explicitly specified
        if (severity == ErrorSeverity.Recoverable)
        {
            severity = ClassifyException(exception);
        }

        // Log the error
        LogError(exception, context, severity);

        // Determine recovery strategy based on exception type and severity
        return DetermineRecoveryStrategy(exception, context, severity);
    }

    public void LogError(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable)
    {
        var logLevel = GetLogLevel(severity);
        var message = FormatErrorMessage(exception, context, severity);

        _logger.Log(logLevel, exception, message);

        // Record error metrics
        RecordErrorMetrics(exception, context, severity);
    }

    public void LogWarning(string message, ErrorContext context, Exception? exception = null)
    {
        var formattedMessage = FormatWarningMessage(message, context);
        _logger.LogWarning(exception, formattedMessage);
    }

    public ErrorSeverity ClassifyException(Exception exception)
    {
        return exception switch
        {
            // MCP protocol validation errors - should propagate to client
            COA.Mcp.Protocol.InvalidParametersException => ErrorSeverity.Expected,
            
            // Expected/recoverable file system errors
            UnauthorizedAccessException => ErrorSeverity.Expected,
            DirectoryNotFoundException => ErrorSeverity.Expected,
            FileNotFoundException => ErrorSeverity.Expected,
            IOException when exception.Message.Contains("being used by another process") => ErrorSeverity.Expected,
            
            // Recoverable transient errors
            TimeoutException => ErrorSeverity.Recoverable,
            TaskCanceledException => ErrorSeverity.Expected,
            OperationCanceledException => ErrorSeverity.Expected,
            
            // Critical Lucene/indexing errors
            Lucene.Net.Store.LockObtainFailedException => ErrorSeverity.Critical,
            Lucene.Net.Index.CorruptIndexException => ErrorSeverity.Critical,
            
            // Circuit breaker errors
            CircuitBreakerOpenException => ErrorSeverity.Expected,
            
            // Fatal system errors
            OutOfMemoryException => ErrorSeverity.Fatal,
            StackOverflowException => ErrorSeverity.Fatal,
            
            // Default classification
            _ => ErrorSeverity.Recoverable
        };
    }

    public async Task<T> ExecuteWithErrorHandlingAsync<T>(
        Func<Task<T>> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();
            
            // Record successful operation metrics
            RecordSuccessMetrics(context, stopwatch.Elapsed);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // Update context with timing information
            var contextWithTiming = new ErrorContext(
                context.Operation,
                context.FilePath,
                context.WorkspacePath,
                stopwatch.Elapsed,
                context.AdditionalData,
                context.CallerMemberName,
                context.CallerFilePath,
                context.CallerLineNumber);

            var result = HandleException(ex, contextWithTiming, expectedSeverity);
            
            // Re-throw unless it's an expected error that should be suppressed
            if (result.LogLevel != LogLevel.Debug)
            {
                throw;
            }
            
            // For expected errors that should be suppressed, return default
            return default!;
        }
    }

    public async Task ExecuteWithErrorHandlingAsync(
        Func<Task> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await operation().ConfigureAwait(false);
                return true; // Dummy return value
            },
            context,
            expectedSeverity,
            cancellationToken).ConfigureAwait(false);
    }

    private ErrorHandlingResult DetermineRecoveryStrategy(Exception exception, ErrorContext context, ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Expected => new ErrorHandlingResult(
                shouldRetry: false,
                shouldCircuitBreak: false,
                shouldLog: true,
                logLevel: LogLevel.Debug,
                recoveryAction: "Continue operation - expected condition"),

            ErrorSeverity.Recoverable => new ErrorHandlingResult(
                shouldRetry: ShouldRetryOperation(exception),
                shouldCircuitBreak: false,
                shouldLog: true,
                logLevel: LogLevel.Warning,
                recoveryAction: GetRecoveryAction(exception),
                retryDelay: GetRetryDelay(exception)),

            ErrorSeverity.Critical => new ErrorHandlingResult(
                shouldRetry: false,
                shouldCircuitBreak: true,
                shouldLog: true,
                logLevel: LogLevel.Error,
                recoveryAction: "Circuit breaker triggered - requires manual intervention"),

            ErrorSeverity.Fatal => new ErrorHandlingResult(
                shouldRetry: false,
                shouldCircuitBreak: true,
                shouldLog: true,
                logLevel: LogLevel.Critical,
                recoveryAction: "Fatal error - immediate shutdown recommended"),

            _ => new ErrorHandlingResult()
        };
    }

    private bool ShouldRetryOperation(Exception exception)
    {
        return exception switch
        {
            TimeoutException => true,
            IOException when exception.Message.Contains("being used by another process") => true,
            _ => false
        };
    }

    private string GetRecoveryAction(Exception exception)
    {
        return exception switch
        {
            TimeoutException => "Retry with exponential backoff",
            UnauthorizedAccessException => "Skip file and continue",
            DirectoryNotFoundException => "Skip directory and continue",
            FileNotFoundException => "Skip file and continue",
            IOException => "Retry after brief delay",
            _ => "Log and continue"
        };
    }

    private TimeSpan? GetRetryDelay(Exception exception)
    {
        return exception switch
        {
            TimeoutException => TimeSpan.FromSeconds(1),
            IOException => TimeSpan.FromMilliseconds(100),
            _ => null
        };
    }

    private LogLevel GetLogLevel(ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Expected => LogLevel.Debug,
            ErrorSeverity.Recoverable => LogLevel.Warning,
            ErrorSeverity.Critical => LogLevel.Error,
            ErrorSeverity.Fatal => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }

    private string FormatErrorMessage(Exception exception, ErrorContext context, ErrorSeverity severity)
    {
        var message = $"[{severity}] {context.Operation} failed";
        
        if (!string.IsNullOrEmpty(context.FilePath))
            message += $" for file: {context.FilePath}";
            
        if (!string.IsNullOrEmpty(context.WorkspacePath))
            message += $" in workspace: {context.WorkspacePath}";
            
        if (context.Duration.HasValue)
            message += $" after {context.Duration.Value.TotalMilliseconds:F0}ms";

        message += $" | {exception.GetType().Name}: {exception.Message}";
        
        if (context.AdditionalData?.Any() == true)
        {
            var additionalInfo = string.Join(", ", context.AdditionalData.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            message += $" | Additional: {additionalInfo}";
        }

        message += $" | Source: {context.CallerMemberName} ({Path.GetFileName(context.CallerFilePath)}:{context.CallerLineNumber})";

        return message;
    }

    private string FormatWarningMessage(string message, ErrorContext context)
    {
        var formattedMessage = $"{context.Operation}: {message}";
        
        if (!string.IsNullOrEmpty(context.FilePath))
            formattedMessage += $" | File: {context.FilePath}";
            
        if (!string.IsNullOrEmpty(context.WorkspacePath))
            formattedMessage += $" | Workspace: {context.WorkspacePath}";

        formattedMessage += $" | Source: {context.CallerMemberName} ({Path.GetFileName(context.CallerFilePath)}:{context.CallerLineNumber})";

        return formattedMessage;
    }

    private void RecordErrorMetrics(Exception exception, ErrorContext context, ErrorSeverity severity)
    {
        // Record error in metrics system for monitoring and analysis
        var errorType = exception.GetType().Name;
        var operation = context.Operation;
        
        // Use additional data for metrics recording if available
        var metricsData = new Dictionary<string, object>
        {
            ["ErrorType"] = errorType,
            ["Severity"] = severity.ToString(),
            ["Operation"] = operation
        };

        if (context.Duration.HasValue)
            metricsData["Duration"] = context.Duration.Value.TotalMilliseconds;

        if (!string.IsNullOrEmpty(context.FilePath))
            metricsData["FilePath"] = context.FilePath;

        // Record error metrics (assuming the metrics service can handle error recording)
        // This would integrate with the existing metrics collection system
    }

    private void RecordSuccessMetrics(ErrorContext context, TimeSpan duration)
    {
        // Record successful operation metrics
        // This helps track success rates and performance
    }
}