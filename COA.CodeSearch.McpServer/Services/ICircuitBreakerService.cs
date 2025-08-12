namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Circuit breaker service for handling transient failures
/// </summary>
public interface ICircuitBreakerService
{
    /// <summary>
    /// Execute an operation with circuit breaker protection
    /// </summary>
    Task<T> ExecuteAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute an operation with circuit breaker protection (no return value)
    /// </summary>
    Task ExecuteAsync(string operationName, Func<Task> operation, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the current state of a circuit breaker
    /// </summary>
    CircuitBreakerState GetState(string operationName);
    
    /// <summary>
    /// Get metrics for all circuit breakers
    /// </summary>
    Dictionary<string, CircuitBreakerMetrics> GetMetrics();
    
    /// <summary>
    /// Manually reset a circuit breaker (force it back to closed state)
    /// </summary>
    void Reset(string operationName);
    
    /// <summary>
    /// Check if an operation is currently allowed by the circuit breaker
    /// </summary>
    bool IsOperationAllowed(string operationName);
}

