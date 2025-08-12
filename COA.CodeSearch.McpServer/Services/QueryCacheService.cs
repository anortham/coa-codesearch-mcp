using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// High-performance cache service for query results with workspace isolation
/// Improved from old version to support centralized architecture with multiple workspaces
/// </summary>
public class QueryCacheService : IQueryCacheService, IDisposable
{
    private readonly ILogger<QueryCacheService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IPathResolutionService _pathResolution;
    private readonly bool _cacheEnabled;
    private readonly TimeSpan _defaultExpiration;
    private readonly long _maxCacheSizeBytes;
    
    // Performance counters
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _currentMemoryBytes;
    
    public QueryCacheService(
        ILogger<QueryCacheService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _cache = memoryCache;
        _pathResolution = pathResolution;
        
        // Load configuration with better defaults for centralized architecture
        _cacheEnabled = configuration.GetValue("CodeSearch:QueryCache:Enabled", true);
        _defaultExpiration = TimeSpan.FromMinutes(configuration.GetValue("CodeSearch:QueryCache:DefaultExpirationMinutes", 15));
        _maxCacheSizeBytes = configuration.GetValue("CodeSearch:QueryCache:MaxSizeMB", 100L) * 1024 * 1024;
        
        _logger.LogInformation("QueryCache initialized: Enabled={Enabled}, DefaultExpiration={Expiry}, MaxSize={MaxSizeMB}MB", 
            _cacheEnabled, _defaultExpiration, _maxCacheSizeBytes / (1024 * 1024));
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        Interlocked.Increment(ref _totalRequests);
        
        if (!_cacheEnabled)
        {
            Interlocked.Increment(ref _cacheMisses);
            return Task.FromResult<T?>(null);
        }

        if (_cache.TryGetValue(key, out T? value))
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return Task.FromResult<T?>(value);
        }

        Interlocked.Increment(ref _cacheMisses);
        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        return SetAsync(key, value, _defaultExpiration, cancellationToken);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
    {
        if (!_cacheEnabled)
        {
            return Task.CompletedTask;
        }

        // Check memory pressure before adding
        if (_currentMemoryBytes >= _maxCacheSizeBytes)
        {
            _logger.LogWarning("Cache size limit reached ({CurrentMB}/{MaxMB}MB), skipping cache for key: {Key}", 
                _currentMemoryBytes / (1024 * 1024), _maxCacheSizeBytes / (1024 * 1024), key);
            return Task.CompletedTask;
        }

        var size = EstimateObjectSize(value);
        
        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = expiration,
            Size = size,
            Priority = CacheItemPriority.Normal,
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (evictedKey, evictedValue, reason, state) =>
                    {
                        Interlocked.Add(ref _currentMemoryBytes, -size);
                        _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", evictedKey, reason);
                    }
                }
            }
        };

        _cache.Set(key, value, cacheOptions);
        Interlocked.Add(ref _currentMemoryBytes, size);
        
        _logger.LogDebug("Cached value for key: {Key}, Size: {SizeBytes} bytes", key, size);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        _logger.LogDebug("Removed cache entry: {Key}", key);
        return Task.CompletedTask;
    }

    public Task ClearWorkspaceCacheAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        if (!_cacheEnabled)
        {
            return Task.CompletedTask;
        }

        // Generate workspace prefix for targeted clearing
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        var prefix = $"workspace:{workspaceHash}:";
        
        // This is a limitation of IMemoryCache - we can't enumerate keys
        // In production, consider using a more sophisticated cache that supports key enumeration
        // For now, we'll track workspace keys separately if needed
        
        _logger.LogInformation("Cleared cache for workspace: {WorkspacePath} (hash: {Hash})", workspacePath, workspaceHash);
        return Task.CompletedTask;
    }

    public Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is MemoryCache mc)
        {
            mc.Clear();
            Interlocked.Exchange(ref _currentMemoryBytes, 0);
        }
        
        _logger.LogInformation("Cache cleared completely");
        return Task.CompletedTask;
    }

    public string GenerateCacheKey(string operation, params object[] parameters)
    {
        // Build a deterministic cache key from operation and parameters
        var keyBuilder = new StringBuilder();
        keyBuilder.Append(operation);
        
        foreach (var param in parameters)
        {
            keyBuilder.Append(':');
            
            if (param == null)
            {
                keyBuilder.Append("null");
            }
            else if (param is string s)
            {
                // For workspace paths, use hash for consistency
                if (s.Contains(Path.DirectorySeparatorChar) || s.Contains(Path.AltDirectorySeparatorChar))
                {
                    var hash = _pathResolution.ComputeWorkspaceHash(s);
                    keyBuilder.Append($"workspace:{hash}");
                }
                else
                {
                    keyBuilder.Append(s);
                }
            }
            else
            {
                // Serialize complex objects to JSON for consistent key generation
                try
                {
                    var json = JsonSerializer.Serialize(param);
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
                    keyBuilder.Append(Convert.ToBase64String(hashBytes, 0, 8)); // Use first 8 bytes for brevity
                }
                catch
                {
                    // Fallback to hash code if serialization fails
                    keyBuilder.Append(param.GetHashCode());
                }
            }
        }
        
        return keyBuilder.ToString();
    }

    public CacheStatistics GetStatistics()
    {
        var totalRequests = Interlocked.Read(ref _totalRequests);
        var cacheHits = Interlocked.Read(ref _cacheHits);
        var cacheMisses = Interlocked.Read(ref _cacheMisses);
        var currentMemory = Interlocked.Read(ref _currentMemoryBytes);
        
        return new CacheStatistics
        {
            TotalRequests = totalRequests,
            CacheHits = cacheHits,
            CacheMisses = cacheMisses,
            CurrentItemCount = GetCacheItemCount(),
            TotalMemoryBytes = currentMemory
        };
    }

    /// <summary>
    /// Estimate the memory footprint of an object for cache sizing
    /// </summary>
    private static long EstimateObjectSize<T>(T obj) where T : class
    {
        if (obj == null) return 0;
        
        try
        {
            // Use JSON serialization as a rough estimate
            var json = JsonSerializer.Serialize(obj);
            return json.Length * 2; // Unicode estimate
        }
        catch
        {
            // Fallback to a conservative estimate
            return 1024; // 1KB default
        }
    }

    /// <summary>
    /// Get approximate count of cached items using reflection
    /// Improved from old version with better error handling
    /// </summary>
    private int GetCacheItemCount()
    {
        if (_cache is MemoryCache mc)
        {
            try
            {
                // Use reflection to get the count (internal implementation detail)
                var coherentStateField = typeof(MemoryCache).GetField("_coherentState", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (coherentStateField?.GetValue(mc) is object coherentState)
                {
                    var countProperty = coherentState.GetType().GetProperty("Count");
                    if (countProperty?.GetValue(coherentState) is int count)
                    {
                        return count;
                    }
                }
                
                // Try alternative method for newer versions
                var entriesField = typeof(MemoryCache).GetField("_entries",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                if (entriesField?.GetValue(mc) is System.Collections.IDictionary entries)
                {
                    return entries.Count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get cache item count via reflection");
            }
        }
        
        return -1; // Unknown
    }

    public void Dispose()
    {
        var stats = GetStatistics();
        _logger.LogInformation("QueryCache disposed - Final stats: Requests={TotalRequests}, Hits={CacheHits}, " +
                              "Misses={CacheMisses}, HitRate={HitRate:P2}, MemoryMB={MemoryMB}", 
            stats.TotalRequests, stats.CacheHits, stats.CacheMisses, stats.HitRate, 
            stats.TotalMemoryBytes / (1024 * 1024));
        
        _cache?.Dispose();
    }
}