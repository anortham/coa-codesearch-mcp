using Lucene.Net.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// High-performance LRU cache for parsed Lucene queries to avoid repeated parsing overhead
/// </summary>
public interface IQueryCacheService
{
    /// <summary>
    /// Get or create a parsed query from cache
    /// </summary>
    Query GetOrCreateQuery(string queryString, string queryType, Func<Query> queryFactory);
    
    /// <summary>
    /// Clear the query cache
    /// </summary>
    void ClearCache();
    
    /// <summary>
    /// Get cache statistics
    /// </summary>
    QueryCacheStats GetStats();
}

public class QueryCacheStats
{
    public int TotalQueries { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double HitRatio => TotalQueries > 0 ? (double)CacheHits / TotalQueries : 0;
    public int CachedItemCount { get; set; }
    public long MemoryUsageBytes { get; set; }
}

public class QueryCacheService : IQueryCacheService, IDisposable
{
    private readonly ILogger<QueryCacheService> _logger;
    private readonly IMemoryCache _cache;
    private readonly bool _cacheEnabled;
    private readonly TimeSpan _cacheExpiry;
    private readonly int _maxCacheSize;
    
    // Performance counters
    private long _totalQueries;
    private long _cacheHits;
    private long _cacheMisses;
    
    public QueryCacheService(
        ILogger<QueryCacheService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _cache = memoryCache;
        
        // Load configuration
        _cacheEnabled = configuration.GetValue("QueryCache:Enabled", true);
        _cacheExpiry = TimeSpan.FromMinutes(configuration.GetValue("QueryCache:ExpiryMinutes", 30));
        _maxCacheSize = configuration.GetValue("QueryCache:MaxSize", 1000);
        
        _logger.LogInformation("QueryCache initialized: Enabled={Enabled}, Expiry={Expiry}, MaxSize={MaxSize}", 
            _cacheEnabled, _cacheExpiry, _maxCacheSize);
    }

    public Query GetOrCreateQuery(string queryString, string queryType, Func<Query> queryFactory)
    {
        Interlocked.Increment(ref _totalQueries);
        
        if (!_cacheEnabled)
        {
            Interlocked.Increment(ref _cacheMisses);
            return queryFactory();
        }

        // Create cache key that includes query type for better specificity
        var cacheKey = $"query:{queryType}:{queryString.GetHashCode():X8}:{queryString}";
        
        if (_cache.TryGetValue(cacheKey, out Query? cachedQuery) && cachedQuery != null)
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Query cache hit for: {QueryString}", queryString);
            return cachedQuery;
        }

        // Cache miss - create query
        Interlocked.Increment(ref _cacheMisses);
        var query = queryFactory();
        
        // Cache the query with sliding expiration and size limit
        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = _cacheExpiry,
            Size = EstimateQuerySize(query),
            Priority = CacheItemPriority.Normal
        };

        _cache.Set(cacheKey, query, cacheOptions);
        
        _logger.LogDebug("Query cached for: {QueryString}", queryString);
        return query;
    }

    public void ClearCache()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Clear();
        }
        
        _logger.LogInformation("Query cache cleared");
    }

    public QueryCacheStats GetStats()
    {
        var totalQueries = Interlocked.Read(ref _totalQueries);
        var cacheHits = Interlocked.Read(ref _cacheHits);
        var cacheMisses = Interlocked.Read(ref _cacheMisses);
        
        // Estimate memory usage (rough approximation)
        var estimatedMemory = GetCacheItemCount() * 200; // Rough estimate per query
        
        return new QueryCacheStats
        {
            TotalQueries = (int)totalQueries,
            CacheHits = (int)cacheHits,
            CacheMisses = (int)cacheMisses,
            CachedItemCount = GetCacheItemCount(),
            MemoryUsageBytes = estimatedMemory
        };
    }

    /// <summary>
    /// Estimate the memory footprint of a query for cache sizing
    /// </summary>
    private static int EstimateQuerySize(Query query)
    {
        // Base size estimate
        var baseSize = 100;
        
        // Add estimate based on query string length
        var queryString = query.ToString();
        var stringSize = queryString?.Length * 2 ?? 0; // Rough Unicode estimate
        
        return baseSize + stringSize;
    }

    /// <summary>
    /// Get approximate count of cached items
    /// </summary>
    private int GetCacheItemCount()
    {
        if (_cache is MemoryCache mc)
        {
            // Use reflection to get the coherent state (this is internal but useful for monitoring)
            var field = typeof(MemoryCache).GetField("_coherentState", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field?.GetValue(mc) is object coherentState)
            {
                var countProperty = coherentState.GetType().GetProperty("Count");
                if (countProperty?.GetValue(coherentState) is int count)
                {
                    return count;
                }
            }
        }
        
        return 0; // Fallback if reflection fails
    }

    public void Dispose()
    {
        _cache?.Dispose();
        
        var stats = GetStats();
        _logger.LogInformation("QueryCache disposed - Final stats: Queries={TotalQueries}, Hits={CacheHits}, " +
                              "Misses={CacheMisses}, HitRatio={HitRatio:P2}", 
            stats.TotalQueries, stats.CacheHits, stats.CacheMisses, stats.HitRatio);
    }
}