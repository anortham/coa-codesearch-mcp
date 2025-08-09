namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Service for monitoring and managing memory pressure
/// </summary>
public interface IMemoryPressureService
{
    /// <summary>
    /// Checks if an operation should be throttled due to memory pressure
    /// </summary>
    bool ShouldThrottleOperation(string operationName);
    
    /// <summary>
    /// Gets the recommended concurrency level based on current memory pressure
    /// </summary>
    int GetRecommendedConcurrency(int baseConcurrency);
    
    /// <summary>
    /// Gets the current memory pressure level
    /// </summary>
    MemoryPressureLevel GetCurrentPressureLevel();
    
    /// <summary>
    /// Reports memory usage for monitoring
    /// </summary>
    void ReportMemoryUsage(string component, long bytesUsed);
    
    /// <summary>
    /// Forces garbage collection if memory pressure is high
    /// </summary>
    void ForceGarbageCollectionIfNeeded();
}

/// <summary>
/// Memory pressure levels
/// </summary>
public enum MemoryPressureLevel
{
    Normal,
    Moderate,
    High,
    Critical
}