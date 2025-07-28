using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services.Caching;

/// <summary>
/// Multi-level cache interface providing L1 (memory) and L2 (disk) caching with smart invalidation
/// </summary>
public interface IMultiLevelCache
{
    /// <summary>
    /// Get value from cache (L1 first, then L2)
    /// </summary>
    Task<T?> GetAsync<T>(string key) where T : class;
    
    /// <summary>
    /// Set value in both L1 and L2 cache
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    
    /// <summary>  
    /// Remove value from both cache levels
    /// </summary>
    Task RemoveAsync(string key);
    
    /// <summary>
    /// Remove multiple keys matching a pattern
    /// </summary>
    Task RemoveByPatternAsync(string pattern);
    
    /// <summary>
    /// Clear all cache data
    /// </summary>
    Task ClearAsync();
    
    /// <summary>
    /// Get cache statistics
    /// </summary>
    CacheStatistics GetStatistics();
    
    /// <summary>
    /// Warm cache with commonly accessed data
    /// </summary>
    Task WarmCacheAsync(IEnumerable<string> keys);
    
    /// <summary>
    /// Check if key exists in cache (L1 or L2)
    /// </summary>
    Task<bool> ExistsAsync(string key);
}

/// <summary>
/// Cache entry with metadata for multi-level caching
/// </summary>
public class CacheEntry<T> where T : class
{
    public string Key { get; set; } = string.Empty;
    public T Value { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public int AccessCount { get; set; } = 1;
    public long SizeBytes { get; set; }
    public CacheLevel Level { get; set; } = CacheLevel.L1;
    public string TypeName { get; set; } = typeof(T).Name;
    
    /// <summary>
    /// Check if entry is expired
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    /// <summary>
    /// Update access tracking
    /// </summary>
    public void MarkAccessed()
    {
        LastAccessed = DateTime.UtcNow;
        AccessCount++;
    }
}

/// <summary>
/// Cache level enumeration
/// </summary>
public enum CacheLevel
{
    L1 = 1,  // Memory cache
    L2 = 2   // Persistent cache
}

/// <summary>
/// Cache statistics for monitoring
/// </summary>
public class CacheStatistics
{
    public int L1Hits { get; set; }
    public int L1Misses { get; set; }
    public int L2Hits { get; set; }
    public int L2Misses { get; set; }
    public int TotalRequests => L1Hits + L1Misses + L2Hits + L2Misses;
    public double L1HitRatio => TotalRequests > 0 ? (double)L1Hits / TotalRequests : 0;
    public double L2HitRatio => TotalRequests > 0 ? (double)L2Hits / TotalRequests : 0;
    public double OverallHitRatio => TotalRequests > 0 ? (double)(L1Hits + L2Hits) / TotalRequests : 0;
    
    public int L1EntryCount { get; set; }
    public int L2EntryCount { get; set; }
    public long L1MemoryUsage { get; set; }
    public long L2DiskUsage { get; set; }
    
    public DateTime LastClearTime { get; set; } = DateTime.UtcNow;
    public int CacheEvictions { get; set; }
    public int CacheWarmups { get; set; }
}

/// <summary>
/// Cache configuration options
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Enable L1 memory cache
    /// </summary>
    public bool EnableL1Cache { get; set; } = true;
    
    /// <summary>
    /// Enable L2 persistent cache
    /// </summary>
    public bool EnableL2Cache { get; set; } = true;
    
    /// <summary>
    /// L1 cache size limit in MB
    /// </summary>
    public int L1MaxSizeMB { get; set; } = 150;
    
    /// <summary>
    /// L2 cache size limit in MB
    /// </summary>
    public int L2MaxSizeMB { get; set; } = 500;
    
    /// <summary>
    /// Default TTL for cache entries
    /// </summary>
    public TimeSpan DefaultTTL { get; set; } = TimeSpan.FromHours(2);
    
    /// <summary>
    /// L1 cache cleanup interval
    /// </summary>
    public TimeSpan L1CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
    
    /// <summary>
    /// L2 cache cleanup interval
    /// </summary>
    public TimeSpan L2CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// Enable cache warming on startup
    /// </summary>
    public bool EnableCacheWarming { get; set; } = true;
    
    /// <summary>
    /// Cache directory for L2 storage
    /// </summary>
    public string CacheDirectory { get; set; } = ".codesearch/cache";
}

/// <summary>
/// Cache invalidation strategy
/// </summary>
public enum InvalidationStrategy
{
    /// <summary>
    /// Invalidate immediately
    /// </summary>
    Immediate,
    
    /// <summary>
    /// Invalidate after short delay (batch operations)
    /// </summary>
    Delayed,
    
    /// <summary>
    /// Invalidate on next access
    /// </summary>
    LazyInvalidation
}

/// <summary>
/// Cache key generator for consistent key creation
/// </summary>
public static class CacheKeyGenerator
{
    /// <summary>
    /// Generate cache key for search queries
    /// </summary>
    public static string SearchKey(string query, string workspace, string? filters = null)
    {
        var baseKey = $"search:{workspace.GetHashCode():X8}:{query.GetHashCode():X8}";
        return filters != null ? $"{baseKey}:{filters.GetHashCode():X8}" : baseKey;
    }
    
    /// <summary>
    /// Generate cache key for memory operations
    /// </summary>
    public static string MemoryKey(string operation, string memoryId)
    {
        return $"memory:{operation}:{memoryId}";
    }
    
    /// <summary>
    /// Generate cache key for embeddings
    /// </summary>
    public static string EmbeddingKey(string text)
    {
        return $"embedding:{text.GetHashCode():X8}";
    }
    
    /// <summary>
    /// Generate cache key for file analysis
    /// </summary>
    public static string FileAnalysisKey(string filePath, long lastModified)
    {
        return $"file:{filePath.GetHashCode():X8}:{lastModified:X16}";
    }
    
    /// <summary>
    /// Generate cache key for batch operations
    /// </summary>
    public static string BatchKey(string operation, string workspacePath, string parameters)
    {
        return $"batch:{operation}:{workspacePath.GetHashCode():X8}:{parameters.GetHashCode():X8}";
    }
}