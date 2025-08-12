namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for caching query results to improve performance
/// </summary>
public interface IQueryCacheService
{
    /// <summary>
    /// Gets a cached result if available
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Sets a cached result with default expiration
    /// </summary>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Sets a cached result with specific expiration
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Removes a cached result
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clears all cached results for a workspace
    /// </summary>
    Task ClearWorkspaceCacheAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clears all cached results
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generates a cache key from query parameters
    /// </summary>
    string GenerateCacheKey(string operation, params object[] parameters);
    
    /// <summary>
    /// Gets cache statistics
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Statistics about cache usage
/// </summary>
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
    public int CurrentItemCount { get; set; }
    public long TotalMemoryBytes { get; set; }
}