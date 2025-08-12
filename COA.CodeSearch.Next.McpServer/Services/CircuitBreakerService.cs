using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace COA.CodeSearch.McpServer.Services;

public class CircuitBreakerService : ICircuitBreakerService, IDisposable
{
    private readonly ILogger<CircuitBreakerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();
    
    // Configuration
    private readonly bool _enabled;
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _halfOpenRetryPeriod;
    private readonly Timer? _cleanupTimer;

    public CircuitBreakerService(ILogger<CircuitBreakerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Load configuration
        _enabled = configuration.GetValue("CircuitBreaker:Enabled", true);
        _failureThreshold = configuration.GetValue("CircuitBreaker:FailureThreshold", 5);
        _timeout = TimeSpan.FromSeconds(configuration.GetValue("CircuitBreaker:TimeoutSeconds", 30));
        _halfOpenRetryPeriod = TimeSpan.FromSeconds(configuration.GetValue("CircuitBreaker:HalfOpenRetrySeconds", 10));
        
        if (_enabled)
        {
            // Start periodic cleanup of unused circuit breakers
            _cleanupTimer = new Timer(CleanupUnusedBreakers, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            _logger.LogInformation("CircuitBreakerService started - failure threshold: {Threshold}, timeout: {Timeout}s", 
                _failureThreshold, _timeout.TotalSeconds);
        }
        else
        {
            _logger.LogInformation("CircuitBreakerService started - circuit breaker disabled");
        }
    }

    public async Task<T> ExecuteAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return await operation().ConfigureAwait(false);
        }

        var circuitBreaker = GetOrCreateCircuitBreaker(operationName);
        return await circuitBreaker.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(string operationName, Func<Task> operation, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        var circuitBreaker = GetOrCreateCircuitBreaker(operationName);
        await circuitBreaker.ExecuteAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true; // Dummy return value for consistency
        }, cancellationToken).ConfigureAwait(false);
    }

    public CircuitBreakerState GetState(string operationName)
    {
        if (!_enabled) return CircuitBreakerState.Disabled;
        
        if (_circuitBreakers.TryGetValue(operationName, out var breaker))
        {
            return breaker.State;
        }
        
        return CircuitBreakerState.Closed; // Default state for non-existent breakers
    }

    public Dictionary<string, CircuitBreakerMetrics> GetMetrics()
    {
        if (!_enabled) return new Dictionary<string, CircuitBreakerMetrics>();
        
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetMetrics()
        );
    }

    public void Reset(string operationName)
    {
        if (!_enabled) return;
        
        if (_circuitBreakers.TryGetValue(operationName, out var breaker))
        {
            breaker.Reset();
            _logger.LogInformation("Circuit breaker manually reset for operation: {OperationName}", operationName);
        }
    }

    public bool IsOperationAllowed(string operationName)
    {
        if (!_enabled) return true;
        
        if (_circuitBreakers.TryGetValue(operationName, out var breaker))
        {
            return breaker.State != CircuitBreakerState.Open;
        }
        
        return true; // Allow operations for non-existent breakers
    }

    private CircuitBreaker GetOrCreateCircuitBreaker(string operationName)
    {
        return _circuitBreakers.GetOrAdd(operationName, name =>
        {
            _logger.LogDebug("Creating new circuit breaker for operation: {OperationName}", name);
            return new CircuitBreaker(name, _failureThreshold, _timeout, _halfOpenRetryPeriod, _logger);
        });
    }

    private void CleanupUnusedBreakers(object? state)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-1); // Remove breakers unused for 1 hour
            var toRemove = new List<string>();

            foreach (var kvp in _circuitBreakers)
            {
                if (kvp.Value.LastUsed < cutoff && kvp.Value.State == CircuitBreakerState.Closed)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                if (_circuitBreakers.TryRemove(key, out var breaker))
                {
                    breaker.Dispose();
                    _logger.LogDebug("Cleaned up unused circuit breaker: {OperationName}", key);
                }
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} unused circuit breakers", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during circuit breaker cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        
        foreach (var breaker in _circuitBreakers.Values)
        {
            breaker.Dispose();
        }
        
        _circuitBreakers.Clear();
    }
}

/// <summary>
/// Individual circuit breaker implementation
/// </summary>
internal class CircuitBreaker : IDisposable
{
    private readonly string _operationName;
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _halfOpenRetryPeriod;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    // State tracking
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _lastSuccessTime = DateTime.UtcNow;
    private DateTime _stateChangedAt = DateTime.UtcNow;

    // Metrics
    private long _totalExecutions = 0;
    private long _successfulExecutions = 0;
    private long _failedExecutions = 0;
    private long _rejectedExecutions = 0;
    private TimeSpan _totalExecutionTime = TimeSpan.Zero;

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                UpdateStateIfNeeded();
                return _state;
            }
        }
    }

    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

    public CircuitBreaker(string operationName, int failureThreshold, TimeSpan timeout, 
        TimeSpan halfOpenRetryPeriod, ILogger logger)
    {
        _operationName = operationName;
        _failureThreshold = failureThreshold;
        _timeout = timeout;
        _halfOpenRetryPeriod = halfOpenRetryPeriod;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        LastUsed = DateTime.UtcNow;

        lock (_lock)
        {
            UpdateStateIfNeeded();
            
            if (_state == CircuitBreakerState.Open)
            {
                _rejectedExecutions++;
                throw new CircuitBreakerOpenException(_operationName, 
                    $"Circuit breaker is OPEN for operation '{_operationName}'. Last failure: {_lastFailureTime:yyyy-MM-dd HH:mm:ss} UTC");
            }
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Interlocked.Increment(ref _totalExecutions);
            
            var result = await operation().ConfigureAwait(false);
            
            stopwatch.Stop();
            
            OnSuccess(stopwatch.Elapsed);
            return result;
        }
        catch (Exception ex) when (!(ex is CircuitBreakerOpenException))
        {
            stopwatch.Stop();
            OnFailure(ex, stopwatch.Elapsed);
            throw;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _lastFailureTime = DateTime.MinValue;
            _stateChangedAt = DateTime.UtcNow;
            
            _logger.LogInformation("Circuit breaker reset to CLOSED state for operation: {OperationName}", _operationName);
        }
    }

    public CircuitBreakerMetrics GetMetrics()
    {
        lock (_lock)
        {
            UpdateStateIfNeeded();
            
            return new CircuitBreakerMetrics
            {
                OperationName = _operationName,
                State = _state,
                FailureCount = _failureCount,
                FailureThreshold = _failureThreshold,
                LastFailureTime = _lastFailureTime,
                LastSuccessTime = _lastSuccessTime,
                StateChangedAt = _stateChangedAt,
                TotalExecutions = _totalExecutions,
                SuccessfulExecutions = _successfulExecutions,
                FailedExecutions = _failedExecutions,
                RejectedExecutions = _rejectedExecutions,
                SuccessRate = _totalExecutions > 0 ? (_successfulExecutions / (double)_totalExecutions) * 100 : 0,
                AverageExecutionTime = _totalExecutions > 0 
                    ? TimeSpan.FromMilliseconds(_totalExecutionTime.TotalMilliseconds / _totalExecutions)
                    : TimeSpan.Zero,
                NextRetryTime = _state == CircuitBreakerState.Open 
                    ? _lastFailureTime.Add(_timeout)
                    : DateTime.MinValue
            };
        }
    }

    private void OnSuccess(TimeSpan executionTime)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _successfulExecutions);
            _totalExecutionTime = _totalExecutionTime.Add(executionTime);
            
            _lastSuccessTime = DateTime.UtcNow;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Successful execution in half-open state -> close the circuit
                _state = CircuitBreakerState.Closed;
                _failureCount = 0;
                _stateChangedAt = DateTime.UtcNow;
                
                _logger.LogInformation("Circuit breaker transitioned to CLOSED state after successful execution: {OperationName}", 
                    _operationName);
            }
            else if (_failureCount > 0)
            {
                // Reset failure count on successful execution
                _failureCount = 0;
            }
        }
    }

    private void OnFailure(Exception exception, TimeSpan executionTime)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _failedExecutions);
            _totalExecutionTime = _totalExecutionTime.Add(executionTime);
            
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Failure in half-open state -> back to open
                _state = CircuitBreakerState.Open;
                _stateChangedAt = DateTime.UtcNow;
                
                _logger.LogWarning("Circuit breaker transitioned back to OPEN state after failure in half-open: {OperationName} - {Error}", 
                    _operationName, exception.Message);
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount >= _failureThreshold)
            {
                // Too many failures -> open the circuit
                _state = CircuitBreakerState.Open;
                _stateChangedAt = DateTime.UtcNow;
                
                _logger.LogError("Circuit breaker OPENED for operation: {OperationName} after {FailureCount} failures. Last error: {Error}", 
                    _operationName, _failureCount, exception.Message);
            }
            else
            {
                _logger.LogWarning("Circuit breaker recorded failure {FailureCount}/{Threshold} for operation: {OperationName} - {Error}", 
                    _failureCount, _failureThreshold, _operationName, exception.Message);
            }
        }
    }

    private void UpdateStateIfNeeded()
    {
        if (_state == CircuitBreakerState.Open)
        {
            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure >= _timeout)
            {
                _state = CircuitBreakerState.HalfOpen;
                _stateChangedAt = DateTime.UtcNow;
                
                _logger.LogInformation("Circuit breaker transitioned to HALF-OPEN state for operation: {OperationName} after {Timeout}s timeout", 
                    _operationName, _timeout.TotalSeconds);
            }
        }
    }

    public void Dispose()
    {
        // Nothing to dispose for now, but keeping for future extensibility
    }
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// Circuit is closed - operations are allowed
    /// </summary>
    Closed,
    
    /// <summary>
    /// Circuit is open - operations are rejected
    /// </summary>
    Open,
    
    /// <summary>
    /// Circuit is half-open - limited operations are allowed to test if service has recovered
    /// </summary>
    HalfOpen,
    
    /// <summary>
    /// Circuit breaker is disabled
    /// </summary>
    Disabled
}

/// <summary>
/// Circuit breaker metrics for monitoring
/// </summary>
public class CircuitBreakerMetrics
{
    public string OperationName { get; set; } = "";
    public CircuitBreakerState State { get; set; }
    public int FailureCount { get; set; }
    public int FailureThreshold { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime LastSuccessTime { get; set; }
    public DateTime StateChangedAt { get; set; }
    public long TotalExecutions { get; set; }
    public long SuccessfulExecutions { get; set; }
    public long FailedExecutions { get; set; }
    public long RejectedExecutions { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public DateTime NextRetryTime { get; set; }
}

/// <summary>
/// Exception thrown when circuit breaker is in open state
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public string OperationName { get; }

    public CircuitBreakerOpenException(string operationName, string message) : base(message)
    {
        OperationName = operationName;
    }

    public CircuitBreakerOpenException(string operationName, string message, Exception innerException) : base(message, innerException)
    {
        OperationName = operationName;
    }
}