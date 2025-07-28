using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace COA.CodeSearch.McpServer.Services.Caching;

/// <summary>
/// Interface for L2 persistent cache operations
/// </summary>
public interface IL2PersistentCache : IDisposable
{
    Task<CacheEntry<T>?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, CacheEntry<T> entry) where T : class;
    Task RemoveAsync(string key);
    Task ClearAsync();
    Task CleanupExpiredAsync();
    Task<bool> ExistsAsync(string key);
    L2CacheStatistics GetStatistics();
}

/// <summary>
/// L2 cache statistics
/// </summary>
public class L2CacheStatistics
{
    public int EntryCount { get; set; }
    public long DiskUsage { get; set; }
    public int ExpiredEntries { get; set; }
    public DateTime LastCleanup { get; set; }
}

/// <summary>
/// File-based persistent cache implementation for L2 caching
/// </summary>
public class L2PersistentCache : IL2PersistentCache
{
    private readonly string _cacheDirectory;
    private readonly CacheOptions _options;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DateTime> _keyIndex = new();
    private readonly ReaderWriterLockSlim _indexLock = new();
    private readonly SemaphoreSlim _operationSemaphore = new(10); // Limit concurrent disk operations
    
    // Statistics
    private long _totalDiskUsage;
    private int _entryCount;
    private DateTime _lastCleanup = DateTime.UtcNow;
    
    public L2PersistentCache(string cacheDirectory, CacheOptions options, ILogger logger)
    {
        _cacheDirectory = cacheDirectory;
        _options = options;
        _logger = logger;
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
        
        // Initialize index from existing files
        InitializeIndex();
        
        _logger.LogDebug("L2PersistentCache initialized at: {Directory}, MaxSize: {MaxSize}MB", 
            _cacheDirectory, _options.L2MaxSizeMB);
    }
    
    public async Task<CacheEntry<T>?> GetAsync<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key)) return null;
        
        await _operationSemaphore.WaitAsync();
        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                return null;
            }
            
            var json = await File.ReadAllTextAsync(filePath);
            var wrapper = JsonSerializer.Deserialize<CacheEntryWrapper>(json);
            
            if (wrapper == null) return null;
            
            // Check expiration
            if (wrapper.ExpiresAt.HasValue && DateTime.UtcNow > wrapper.ExpiresAt.Value)
            {
                // Remove expired entry
                await RemoveFileAsync(filePath, key);
                return null;
            }
            
            // Deserialize the actual value
            var value = JsonSerializer.Deserialize<T>(wrapper.ValueJson);
            if (value == null) return null;
            
            var entry = new CacheEntry<T>
            {
                Key = key,
                Value = value,
                CreatedAt = wrapper.CreatedAt,
                LastAccessed = wrapper.LastAccessed,
                ExpiresAt = wrapper.ExpiresAt,
                AccessCount = wrapper.AccessCount,
                SizeBytes = wrapper.SizeBytes,
                Level = CacheLevel.L2,
                TypeName = wrapper.TypeName
            };
            
            // Update access time in wrapper and save back
            wrapper.LastAccessed = DateTime.UtcNow;
            wrapper.AccessCount++;
            
            try
            {
                var updatedJson = JsonSerializer.Serialize(wrapper);
                await File.WriteAllTextAsync(filePath, updatedJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update access time for L2 cache entry: {Key}", key);
            }
            
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading L2 cache entry: {Key}", key);
            return null;
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }
    
    public async Task SetAsync<T>(string key, CacheEntry<T> entry) where T : class
    {
        if (string.IsNullOrEmpty(key) || entry?.Value == null) return;
        
        await _operationSemaphore.WaitAsync();
        try
        {
            // Check disk space limit
            await EnsureDiskSpaceAsync();
            
            var filePath = GetFilePath(key);
            var wrapper = new CacheEntryWrapper
            {
                Key = key,
                ValueJson = JsonSerializer.Serialize(entry.Value),
                TypeName = entry.TypeName,
                CreatedAt = entry.CreatedAt,
                LastAccessed = entry.LastAccessed,
                ExpiresAt = entry.ExpiresAt,
                AccessCount = entry.AccessCount,
                SizeBytes = entry.SizeBytes
            };
            
            var json = JsonSerializer.Serialize(wrapper);
            var fileBytes = Encoding.UTF8.GetBytes(json);
            
            // Write to temporary file first, then move (atomic operation)
            var tempPath = filePath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, fileBytes);
            File.Move(tempPath, filePath, overwrite: true);
            
            // Update index
            _indexLock.EnterWriteLock();
            try
            {
                var wasNew = !_keyIndex.ContainsKey(key);
                _keyIndex[key] = DateTime.UtcNow;
                
                if (wasNew)
                {
                    Interlocked.Increment(ref _entryCount);
                }
                
                Interlocked.Add(ref _totalDiskUsage, fileBytes.Length);
            }
            finally
            {
                _indexLock.ExitWriteLock();
            }
            
            _logger.LogDebug("L2 cache entry saved: {Key}, Size: {Size} bytes", key, fileBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing L2 cache entry: {Key}", key);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }
    
    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        
        await _operationSemaphore.WaitAsync();
        try
        {
            var filePath = GetFilePath(key);
            await RemoveFileAsync(filePath, key);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }
    
    public async Task ClearAsync()
    {
        await _operationSemaphore.WaitAsync();
        try
        {
            // Delete all cache files
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.cache");
                var tasks = files.Select(async file =>
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete cache file: {File}", file);
                    }
                });
                
                await Task.WhenAll(tasks);
            }
            
            // Clear index
            _indexLock.EnterWriteLock();
            try
            {
                _keyIndex.Clear();
                Interlocked.Exchange(ref _entryCount, 0);
                Interlocked.Exchange(ref _totalDiskUsage, 0);
            }
            finally
            {
                _indexLock.ExitWriteLock();
            }
            
            _logger.LogInformation("L2 cache cleared completely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing L2 cache");
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }
    
    public async Task CleanupExpiredAsync()
    {
        var cleanupStart = DateTime.UtcNow;
        var expiredCount = 0;
        
        try
        {
            var files = Directory.GetFiles(_cacheDirectory, "*.cache");
            
            var cleanupTasks = files.Select(async filePath =>
            {
                try
                {
                    await _operationSemaphore.WaitAsync();
                    
                    try
                    {
                        if (!File.Exists(filePath)) return;
                        
                        var json = await File.ReadAllTextAsync(filePath);
                        var wrapper = JsonSerializer.Deserialize<CacheEntryWrapper>(json);
                        
                        if (wrapper?.ExpiresAt.HasValue == true && DateTime.UtcNow > wrapper.ExpiresAt.Value)
                        {
                            await RemoveFileAsync(filePath, wrapper.Key);
                            Interlocked.Increment(ref expiredCount);
                        }
                    }
                    finally
                    {
                        _operationSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during cleanup of file: {File}", filePath);
                }
            });
            
            await Task.WhenAll(cleanupTasks);
            
            _lastCleanup = DateTime.UtcNow;
            var duration = DateTime.UtcNow - cleanupStart;
            
            _logger.LogInformation("L2 cache cleanup completed: {ExpiredCount} expired entries removed in {Duration}ms", 
                expiredCount, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during L2 cache cleanup");
        }
    }
    
    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        
        _indexLock.EnterReadLock();
        try
        {
            if (!_keyIndex.ContainsKey(key)) return false;
        }
        finally
        {
            _indexLock.ExitReadLock();
        }
        
        var filePath = GetFilePath(key);
        return File.Exists(filePath);
    }
    
    public L2CacheStatistics GetStatistics()
    {
        _indexLock.EnterReadLock();
        try
        {
            return new L2CacheStatistics
            {
                EntryCount = _entryCount,
                DiskUsage = Interlocked.Read(ref _totalDiskUsage),
                LastCleanup = _lastCleanup
            };
        }
        finally
        {
            _indexLock.ExitReadLock();
        }
    }
    
    private void InitializeIndex()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return;
            
            var files = Directory.GetFiles(_cacheDirectory, "*.cache");
            var totalSize = 0L;
            var count = 0;
            
            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var key = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    
                    _keyIndex[key] = fileInfo.LastWriteTime;
                    totalSize += fileInfo.Length;
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index cache file: {File}", filePath);
                }
            }
            
            Interlocked.Exchange(ref _entryCount, count);
            Interlocked.Exchange(ref _totalDiskUsage, totalSize);
            
            _logger.LogInformation("L2 cache index initialized: {Count} entries, {Size}MB", 
                count, totalSize / 1024 / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing L2 cache index");
        }
    }
    
    private async Task EnsureDiskSpaceAsync()
    {
        var maxSizeBytes = _options.L2MaxSizeMB * 1024 * 1024;
        var currentSize = Interlocked.Read(ref _totalDiskUsage);
        
        if (currentSize > maxSizeBytes)
        {
            // Remove oldest entries using LRU
            await EvictOldestEntriesAsync((long)(maxSizeBytes * 0.8)); // Target 80% of max size
        }
    }
    
    private async Task EvictOldestEntriesAsync(long targetSize)
    {
        try
        {
            _indexLock.EnterReadLock();
            List<KeyValuePair<string, DateTime>> entries;
            try
            {
                entries = _keyIndex.OrderBy(kv => kv.Value).ToList();
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
            
            var currentSize = Interlocked.Read(ref _totalDiskUsage);
            var evictedCount = 0;
            
            foreach (var entry in entries)
            {
                if (currentSize <= targetSize) break;
                
                try
                {
                    var filePath = GetFilePath(entry.Key);
                    if (File.Exists(filePath))
                    {
                        var fileSize = new FileInfo(filePath).Length;
                        await RemoveFileAsync(filePath, entry.Key);
                        currentSize -= fileSize;
                        evictedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evict cache entry: {Key}", entry.Key);
                }
            }
            
            _logger.LogInformation("L2 cache eviction completed: {Count} entries removed, {Size}MB freed", 
                evictedCount, (Interlocked.Read(ref _totalDiskUsage) - currentSize) / 1024 / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during L2 cache eviction");
        }
    }
    
    private async Task RemoveFileAsync(string filePath, string key)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var fileSize = new FileInfo(filePath).Length;
                File.Delete(filePath);
                
                Interlocked.Add(ref _totalDiskUsage, -fileSize);
                Interlocked.Decrement(ref _entryCount);
            }
            
            _indexLock.EnterWriteLock();
            try
            {
                _keyIndex.TryRemove(key, out _);
            }
            finally
            {
                _indexLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove cache file: {File}", filePath);
        }
    }
    
    private string GetFilePath(string key)
    {
        // Create a safe filename from the cache key
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var filename = Convert.ToHexString(hash)[..16] + ".cache"; // Use first 16 chars of hash
        return Path.Combine(_cacheDirectory, filename);
    }
    
    public void Dispose()
    {
        try
        {
            _indexLock?.Dispose();
            _operationSemaphore?.Dispose();
            
            var stats = GetStatistics();
            _logger.LogInformation("L2PersistentCache disposed - Entries: {Count}, Disk Usage: {Size}MB", 
                stats.EntryCount, stats.DiskUsage / 1024 / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing L2PersistentCache");
        }
    }
    
    /// <summary>
    /// Wrapper class for serializing cache entries to disk
    /// </summary>
    private class CacheEntryWrapper
    {
        public string Key { get; set; } = string.Empty;
        public string ValueJson { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int AccessCount { get; set; }
        public long SizeBytes { get; set; }
    }
}