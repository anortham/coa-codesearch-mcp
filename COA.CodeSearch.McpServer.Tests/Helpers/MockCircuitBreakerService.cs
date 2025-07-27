using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Mock implementation of ICircuitBreakerService for testing
/// </summary>
public class MockCircuitBreakerService : ICircuitBreakerService
{
    public async Task<T> ExecuteAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        // In test mode, just execute the operation directly without circuit breaker logic
        return await operation().ConfigureAwait(false);
    }

    public async Task ExecuteAsync(string operationName, Func<Task> operation, CancellationToken cancellationToken = default)
    {
        // In test mode, just execute the operation directly without circuit breaker logic
        await operation().ConfigureAwait(false);
    }

    public CircuitBreakerState GetState(string operationName)
    {
        return CircuitBreakerState.Closed; // Always return closed for testing
    }

    public Dictionary<string, CircuitBreakerMetrics> GetMetrics()
    {
        return new Dictionary<string, CircuitBreakerMetrics>();
    }

    public void Reset(string operationName)
    {
        // No-op for testing
    }

    public bool IsOperationAllowed(string operationName)
    {
        return true; // Always allow operations in testing
    }
}

/// <summary>
/// Mock implementation of IMemoryPressureService for testing
/// </summary>
public class MockMemoryPressureService : IMemoryPressureService
{
    public MemoryPressureLevel GetCurrentPressureLevel()
    {
        return MemoryPressureLevel.Normal; // Always return normal for testing
    }

    public bool ShouldThrottleOperation(string operationType)
    {
        return false; // Never throttle in testing
    }

    public int GetRecommendedBatchSize(int maxBatchSize)
    {
        return maxBatchSize; // Always return max for testing
    }

    public int GetRecommendedConcurrency(int maxConcurrency)
    {
        return maxConcurrency; // Always return max for testing
    }

    public Task TriggerMemoryCleanupIfNeededAsync()
    {
        return Task.CompletedTask; // No-op for testing
    }
}