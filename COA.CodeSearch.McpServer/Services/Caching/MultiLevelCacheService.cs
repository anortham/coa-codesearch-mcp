using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Services.Caching;

/// <summary>
/// Multi-level cache implementation with L1 (memory) and L2 (disk) layers
/// </summary>
public class MultiLevelCacheService : IMultiLevelCache, IDisposable
{
    private readonly ILogger<MultiLevelCacheService> _logger;
    private readonly IMemoryCache _l1Cache;
    private readonly IL2PersistentCache _l2Cache;
    private readonly CacheOptions _options;
    private readonly IPathResolutionService _pathResolver;
    
    // Statistics tracking
    private long _l1Hits, _l1Misses, _l2Hits, _l2Misses, _evictions, _warmups;
    private readonly ConcurrentDictionary<string, DateTime> _keyAccessTimes = new();
    
    // Background cleanup
    private readonly Timer _l1CleanupTimer;
    private readonly Timer _l2CleanupTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    public MultiLevelCacheService(
        ILogger<MultiLevelCacheService> logger,
        IMemoryCache memoryCache,
        IConfiguration configuration,
        IPathResolutionService pathResolver)
    {
        _logger = logger;
        _l1Cache = memoryCache;
        _pathResolver = pathResolver;
        _options = configuration.GetSection("Cache").Get<CacheOptions>() ?? new CacheOptions();
        
        // Create L2 cache directory
        var cacheDir = Path.Combine(_pathResolver.GetBasePath(), _options.CacheDirectory);
        Directory.CreateDirectory(cacheDir);
        
        // Initialize L2 cache
        _l2Cache = new L2PersistentCache(cacheDir, _options, logger);
        
        // Start background cleanup timers
        _l1CleanupTimer = new Timer(CleanupL1Cache, null, _options.L1CleanupInterval, _options.L1CleanupInterval);
        _l2CleanupTimer = new Timer(CleanupL2Cache, null, _options.L2CleanupInterval, _options.L2CleanupInterval);
        
        _logger.LogInformation("MultiLevelCache initialized: L1={L1Enabled}, L2={L2Enabled}, L1MaxMB={L1MaxMB}, L2MaxMB={L2MaxMB}",
            _options.EnableL1Cache, _options.EnableL2Cache, _options.L1MaxSizeMB, _options.L2MaxSizeMB);
    }
    
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key)) return null;
        
        try
        {
            // Try L1 cache first
            if (_options.EnableL1Cache && _l1Cache.TryGetValue<CacheEntry<T>>(key, out var l1Entry) && l1Entry != null)
            {
                if (!l1Entry.IsExpired)
                {
                    l1Entry.MarkAccessed();
                    _keyAccessTimes[key] = DateTime.UtcNow;
                    Interlocked.Increment(ref _l1Hits);
                    
                    _logger.LogDebug("L1 cache hit for key: {Key}", key);
                    return l1Entry.Value;
                }
                else
                {
                    // Remove expired entry
                    _l1Cache.Remove(key);
                }
            }
            
            Interlocked.Increment(ref _l1Misses);
            
            // Try L2 cache
            if (_options.EnableL2Cache)
            {
                var l2Entry = await _l2Cache.GetAsync<T>(key);
                if (l2Entry != null && !l2Entry.IsExpired)
                {
                    l2Entry.MarkAccessed();
                    _keyAccessTimes[key] = DateTime.UtcNow;
                    Interlocked.Increment(ref _l2Hits);
                    
                    // Promote to L1 cache
                    if (_options.EnableL1Cache)
                    {
                        await SetL1CacheAsync(key, l2Entry);
                    }
                    
                    _logger.LogDebug("L2 cache hit for key: {Key}, promoted to L1", key);
                    return l2Entry.Value;
                }
            }
            
            Interlocked.Increment(ref _l2Misses);
            _logger.LogDebug("Cache miss for key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
            return null;
        }
    }
    
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        if (string.IsNullOrEmpty(key) || value == null) return;
        
        try
        {
            var expiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : DateTime.UtcNow.Add(_options.DefaultTTL);
            var sizeBytes = EstimateObjectSize(value);
            
            var entry = new CacheEntry<T>
            {
                Key = key,
                Value = value,
                ExpiresAt = expiresAt,
                SizeBytes = sizeBytes,
                Level = CacheLevel.L1
            };
            
            // Set in L1 cache
            if (_options.EnableL1Cache)
            {
                await SetL1CacheAsync(key, entry);
            }
            
            // Set in L2 cache
            if (_options.EnableL2Cache)
            {
                entry.Level = CacheLevel.L2;
                await _l2Cache.SetAsync(key, entry);
            }
            
            _keyAccessTimes[key] = DateTime.UtcNow;
            _logger.LogDebug("Cached value for key: {Key}, expires: {ExpiresAt}", key, expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
        }
    }
    
    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        
        try
        {
            // Remove from L1 cache
            if (_options.EnableL1Cache)
            {
                _l1Cache.Remove(key);
            }
            
            // Remove from L2 cache
            if (_options.EnableL2Cache)
            {
                await _l2Cache.RemoveAsync(key);
            }
            
            _keyAccessTimes.TryRemove(key, out _);
            _logger.LogDebug("Removed cache entry for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
        }
    }
    
    public async Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            var regex = new Regex(pattern.Replace("*", ".*"), RegexOptions.Compiled);
            var keysToRemove = new List<string>();
            
            // Find matching keys in access times (approximation of all keys)
            foreach (var key in _keyAccessTimes.Keys)
            {
                if (regex.IsMatch(key))
                {
                    keysToRemove.Add(key);
                }
            }
            
            // Remove matching keys
            foreach (var key in keysToRemove)
            {
                await RemoveAsync(key);
            }
            
            _logger.LogInformation("Removed {Count} cache entries matching pattern: {Pattern}", keysToRemove.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries by pattern: {Pattern}", pattern);
        }
    }
    
    public async Task ClearAsync()
    {
        try
        {
            // Clear L1 cache
            if (_options.EnableL1Cache && _l1Cache is MemoryCache mc)
            {
                mc.Clear();
            }
            
            // Clear L2 cache
            if (_options.EnableL2Cache)
            {
                await _l2Cache.ClearAsync();
            }
            
            _keyAccessTimes.Clear();
            
            // Reset statistics
            Interlocked.Exchange(ref _l1Hits, 0);
            Interlocked.Exchange(ref _l1Misses, 0);
            Interlocked.Exchange(ref _l2Hits, 0);
            Interlocked.Exchange(ref _l2Misses, 0);
            Interlocked.Exchange(ref _evictions, 0);
            Interlocked.Exchange(ref _warmups, 0);
            
            _logger.LogInformation("Cache cleared completely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }
    
    public CacheStatistics GetStatistics()
    {
        var l1Stats = GetL1Statistics();
        var l2Stats = _l2Cache.GetStatistics();
        
        return new CacheStatistics
        {
            L1Hits = (int)Interlocked.Read(ref _l1Hits),
            L1Misses = (int)Interlocked.Read(ref _l1Misses),
            L2Hits = (int)Interlocked.Read(ref _l2Hits),  
            L2Misses = (int)Interlocked.Read(ref _l2Misses),
            L1EntryCount = l1Stats.EntryCount,
            L2EntryCount = l2Stats.EntryCount,
            L1MemoryUsage = l1Stats.MemoryUsage,
            L2DiskUsage = l2Stats.DiskUsage,
            CacheEvictions = (int)Interlocked.Read(ref _evictions),
            CacheWarmups = (int)Interlocked.Read(ref _warmups)
        };
    }
    
    public async Task WarmCacheAsync(IEnumerable<string> keys)
    {
        try
        {
            var keyList = keys.ToList();
            _logger.LogInformation("Starting cache warm-up for {Count} keys", keyList.Count);
            
            var tasks = keyList.Select(async key =>
            {
                try
                {
                    // Check if already cached
                    if (await ExistsAsync(key)) return;
                    
                    // Try to load from L2 to L1
                    if (_options.EnableL2Cache && _options.EnableL1Cache)
                    {
                        var entry = await _l2Cache.GetAsync<object>(key);
                        if (entry != null && !entry.IsExpired)
                        {
                            await SetL1CacheAsync(key, entry);
                            Interlocked.Increment(ref _warmups);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to warm cache for key: {Key}", key);
                }
            });
            
            await Task.WhenAll(tasks);
            _logger.LogInformation("Cache warm-up completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache warm-up");
        }
    }
    
    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        
        try
        {
            // Check L1 cache
            if (_options.EnableL1Cache && _l1Cache.TryGetValue(key, out _))
            {
                return true;
            }
            
            // Check L2 cache
            if (_options.EnableL2Cache)
            {
                return await _l2Cache.ExistsAsync(key);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
            return false;
        }
    }
    
    private async Task SetL1CacheAsync<T>(string key, CacheEntry<T> entry) where T : class
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = entry.ExpiresAt,
            Size = entry.SizeBytes,
            Priority = CacheItemPriority.Normal
        };
        
        // Add eviction callback to track statistics
        cacheOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (reason == EvictionReason.Capacity || reason == EvictionReason.Expired)
            {
                Interlocked.Increment(ref _evictions);
            }
            _keyAccessTimes.TryRemove(key.ToString() ?? "", out _);
        });
        
        _l1Cache.Set(key, entry, cacheOptions);
    }
    
    private static long EstimateObjectSize(object obj)
    {
        try
        {
            var json = JsonSerializer.Serialize(obj);
            return json.Length * 2; // Rough Unicode estimate
        }
        catch
        {
            return 1024; // Default estimate
        }
    }
    
    private (int EntryCount, long MemoryUsage) GetL1Statistics()
    {
        if (_l1Cache is MemoryCache mc)
        {
            try
            {
                // Use reflection to get cache statistics
                var field = typeof(MemoryCache).GetField("_coherentState", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field?.GetValue(mc) is object coherentState)
                {
                    var countProperty = coherentState.GetType().GetProperty("Count");
                    var sizeProperty = coherentState.GetType().GetProperty("Size");
                    
                    var count = countProperty?.GetValue(coherentState) as int? ?? 0;
                    var size = sizeProperty?.GetValue(coherentState) as long? ?? 0;
                    
                    return (count, size);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get L1 cache statistics via reflection");
            }
        }
        
        return (0, 0); // Fallback
    }
    
    private void CleanupL1Cache(object? state)
    {
        try
        {
            // L1 cache cleanup is handled automatically by IMemoryCache
            // This is just for logging and additional cleanup if needed
            var stats = GetL1Statistics();
            _logger.LogDebug("L1 cache cleanup - Entries: {Count}, Memory: {Memory}MB", 
                stats.EntryCount, stats.MemoryUsage / 1024 / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during L1 cache cleanup");
        }
    }
    
    private void CleanupL2Cache(object? state)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                await _l2Cache.CleanupExpiredAsync();
                var stats = _l2Cache.GetStatistics();
                _logger.LogDebug("L2 cache cleanup - Entries: {Count}, Disk: {Disk}MB", 
                    stats.EntryCount, stats.DiskUsage / 1024 / 1024);
            }, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during L2 cache cleanup");
        }
    }
    
    public void Dispose()
    {
        try
        {
            _cancellationTokenSource.Cancel();
            _l1CleanupTimer?.Dispose();
            _l2CleanupTimer?.Dispose();
            _l2Cache?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            var stats = GetStatistics();
            _logger.LogInformation("MultiLevelCache disposed - Final stats: L1 Hit Ratio={L1HitRatio:P2}, " +
                                  "L2 Hit Ratio={L2HitRatio:P2}, Overall Hit Ratio={OverallHitRatio:P2}",
                stats.L1HitRatio, stats.L2HitRatio, stats.OverallHitRatio);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing MultiLevelCache");
        }
    }
}