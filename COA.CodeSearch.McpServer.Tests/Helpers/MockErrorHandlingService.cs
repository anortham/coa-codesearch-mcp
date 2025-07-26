using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Mock implementation of IErrorHandlingService for testing
/// </summary>
public class MockErrorHandlingService : IErrorHandlingService
{
    public ErrorHandlingResult HandleException(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable)
    {
        // In test mode, just return a basic result without circuit breaking
        return new ErrorHandlingResult(
            shouldRetry: false,
            shouldCircuitBreak: false,
            shouldLog: true,
            logLevel: LogLevel.Warning);
    }

    public void LogError(Exception exception, ErrorContext context, ErrorSeverity severity = ErrorSeverity.Recoverable)
    {
        // No-op for testing
    }

    public void LogWarning(string message, ErrorContext context, Exception? exception = null)
    {
        // No-op for testing
    }

    public ErrorSeverity ClassifyException(Exception exception)
    {
        return ErrorSeverity.Recoverable; // Default classification for testing
    }

    public async Task<T> ExecuteWithErrorHandlingAsync<T>(
        Func<Task<T>> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default)
    {
        // In test mode, just execute the operation directly
        return await operation().ConfigureAwait(false);
    }

    public async Task ExecuteWithErrorHandlingAsync(
        Func<Task> operation,
        ErrorContext context,
        ErrorSeverity expectedSeverity = ErrorSeverity.Recoverable,
        CancellationToken cancellationToken = default)
    {
        // In test mode, just execute the operation directly
        await operation().ConfigureAwait(false);
    }
}