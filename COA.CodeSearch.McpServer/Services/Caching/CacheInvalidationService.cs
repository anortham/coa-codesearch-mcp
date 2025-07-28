using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services.Caching;

/// <summary>
/// Service for smart cache invalidation based on file and memory changes
/// </summary>
public interface ICacheInvalidationService
{
    /// <summary>
    /// Register cache invalidation for file changes
    /// </summary>
    void InvalidateOnFileChange(string filePath, params string[] cacheKeys);
    
    /// <summary>
    /// Register cache invalidation for memory changes
    /// </summary>
    void InvalidateOnMemoryChange(string memoryId, params string[] cacheKeys);
    
    /// <summary>
    /// Register cache invalidation for workspace changes
    /// </summary>
    void InvalidateOnWorkspaceChange(string workspacePath, params string[] cacheKeys);
    
    /// <summary>
    /// Manually invalidate cache keys
    /// </summary>
    Task InvalidateAsync(params string[] cacheKeys);
    
    /// <summary>
    /// Invalidate cache keys matching a pattern
    /// </summary>
    Task InvalidateByPatternAsync(string pattern);
    
    /// <summary>
    /// Set invalidation strategy
    /// </summary>
    void SetInvalidationStrategy(InvalidationStrategy strategy);
    
    /// <summary>
    /// Get invalidation statistics
    /// </summary>
    CacheInvalidationStats GetStatistics();
}

/// <summary>
/// Cache invalidation statistics
/// </summary>
public class CacheInvalidationStats
{
    public int TotalInvalidations { get; set; }
    public int FileChangeInvalidations { get; set; }
    public int MemoryChangeInvalidations { get; set; }
    public int WorkspaceChangeInvalidations { get; set; }
    public int ManualInvalidations { get; set; }
    public int PatternInvalidations { get; set; }
    public DateTime LastInvalidation { get; set; }
    public int PendingInvalidations { get; set; }
}

/// <summary>
/// Smart cache invalidation service implementation
/// </summary>
public class CacheInvalidationService : ICacheInvalidationService, IDisposable
{
    private readonly ILogger<CacheInvalidationService> _logger;
    private readonly IMultiLevelCache _cache;
    
    // Invalidation mappings
    private readonly ConcurrentDictionary<string, HashSet<string>> _fileToKeysMap = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _memoryToKeysMap = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _workspaceToKeysMap = new();
    
    // Delayed invalidation support
    private readonly ConcurrentQueue<InvalidationRequest> _pendingInvalidations = new();
    private readonly Timer _invalidationTimer;
    private InvalidationStrategy _strategy = InvalidationStrategy.Immediate;
    
    // Statistics
    private long _totalInvalidations, _fileChangeInvalidations, _memoryChangeInvalidations;
    private long _workspaceChangeInvalidations, _manualInvalidations, _patternInvalidations;
    private DateTime _lastInvalidation = DateTime.MinValue;
    
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    public CacheInvalidationService(
        ILogger<CacheInvalidationService> logger,
        IMultiLevelCache cache)
    {
        _logger = logger;
        _cache = cache;
        
        // Start invalidation timer for delayed invalidation
        _invalidationTimer = new Timer(ProcessPendingInvalidations, null, 
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        _logger.LogInformation("CacheInvalidationService initialized with strategy: {Strategy}", _strategy);
    }
    
    public void InvalidateOnFileChange(string filePath, params string[] cacheKeys)
    {
        if (string.IsNullOrEmpty(filePath) || cacheKeys.Length == 0) return;
        
        try
        {
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            
            _fileToKeysMap.AddOrUpdate(normalizedPath,
                new HashSet<string>(cacheKeys),
                (key, existing) =>
                {
                    foreach (var cacheKey in cacheKeys)
                    {
                        existing.Add(cacheKey);
                    }
                    return existing;
                });
            
            _logger.LogDebug("Registered {Count} cache keys for file change invalidation: {FilePath}", 
                cacheKeys.Length, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering file change invalidation for: {FilePath}", filePath);
        }
    }
    
    public void InvalidateOnMemoryChange(string memoryId, params string[] cacheKeys)
    {
        if (string.IsNullOrEmpty(memoryId) || cacheKeys.Length == 0) return;
        
        try
        {
            _memoryToKeysMap.AddOrUpdate(memoryId,
                new HashSet<string>(cacheKeys),
                (key, existing) =>
                {
                    foreach (var cacheKey in cacheKeys)
                    {
                        existing.Add(cacheKey);
                    }
                    return existing;
                });
            
            _logger.LogDebug("Registered {Count} cache keys for memory change invalidation: {MemoryId}", 
                cacheKeys.Length, memoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering memory change invalidation for: {MemoryId}", memoryId);
        }
    }
    
    public void InvalidateOnWorkspaceChange(string workspacePath, params string[] cacheKeys)
    {
        if (string.IsNullOrEmpty(workspacePath) || cacheKeys.Length == 0) return;
        
        try
        {
            var normalizedPath = Path.GetFullPath(workspacePath).ToLowerInvariant();
            
            _workspaceToKeysMap.AddOrUpdate(normalizedPath,
                new HashSet<string>(cacheKeys),
                (key, existing) =>
                {
                    foreach (var cacheKey in cacheKeys)
                    {
                        existing.Add(cacheKey);
                    }
                    return existing;
                });
            
            _logger.LogDebug("Registered {Count} cache keys for workspace change invalidation: {WorkspacePath}", 
                cacheKeys.Length, workspacePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering workspace change invalidation for: {WorkspacePath}", workspacePath);
        }
    }
    
    public async Task InvalidateAsync(params string[] cacheKeys)
    {
        if (cacheKeys.Length == 0) return;
        
        try
        {
            switch (_strategy)
            {
                case InvalidationStrategy.Immediate:
                    await InvalidateImmediateAsync(cacheKeys, "manual");
                    break;
                    
                case InvalidationStrategy.Delayed:
                    foreach (var key in cacheKeys)
                    {
                        _pendingInvalidations.Enqueue(new InvalidationRequest 
                        { 
                            CacheKey = key, 
                            RequestedAt = DateTime.UtcNow,
                            Source = "manual"
                        });
                    }
                    break;
                    
                case InvalidationStrategy.LazyInvalidation:
                    // For lazy invalidation, we just mark entries as expired but don't remove them
                    // They'll be removed on next access
                    await MarkEntriesExpiredAsync(cacheKeys);
                    break;
            }
            
            Interlocked.Add(ref _manualInvalidations, cacheKeys.Length);
            _logger.LogDebug("Queued {Count} cache keys for manual invalidation", cacheKeys.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache keys manually");
        }
    }
    
    public async Task InvalidateByPatternAsync(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return;
        
        try
        {
            await _cache.RemoveByPatternAsync(pattern);
            Interlocked.Increment(ref _patternInvalidations);
            Interlocked.Increment(ref _totalInvalidations);
            _lastInvalidation = DateTime.UtcNow;
            
            _logger.LogInformation("Invalidated cache entries matching pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache by pattern: {Pattern}", pattern);
        }
    }
    
    public void SetInvalidationStrategy(InvalidationStrategy strategy)
    {
        _strategy = strategy;
        _logger.LogInformation("Cache invalidation strategy changed to: {Strategy}", strategy);
    }
    
    public CacheInvalidationStats GetStatistics()
    {
        return new CacheInvalidationStats
        {
            TotalInvalidations = (int)Interlocked.Read(ref _totalInvalidations),
            FileChangeInvalidations = (int)Interlocked.Read(ref _fileChangeInvalidations),
            MemoryChangeInvalidations = (int)Interlocked.Read(ref _memoryChangeInvalidations),
            WorkspaceChangeInvalidations = (int)Interlocked.Read(ref _workspaceChangeInvalidations),
            ManualInvalidations = (int)Interlocked.Read(ref _manualInvalidations),
            PatternInvalidations = (int)Interlocked.Read(ref _patternInvalidations),
            LastInvalidation = _lastInvalidation,
            PendingInvalidations = _pendingInvalidations.Count
        };
    }
    
    /// <summary>
    /// Handle file change notifications from file watcher
    /// </summary>
    public async Task HandleFileChangeAsync(string filePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            
            if (_fileToKeysMap.TryGetValue(normalizedPath, out var cacheKeys))
            {
                await InvalidateAsync(cacheKeys.ToArray());
                Interlocked.Add(ref _fileChangeInvalidations, cacheKeys.Count);
                
                _logger.LogDebug("Invalidated {Count} cache keys due to file change: {FilePath}", 
                    cacheKeys.Count, filePath);
            }
            
            // Also check if any workspace paths contain this file
            foreach (var workspace in _workspaceToKeysMap.Keys)
            {
                if (normalizedPath.StartsWith(workspace))
                {
                    if (_workspaceToKeysMap.TryGetValue(workspace, out var workspaceKeys))
                    {
                        await InvalidateAsync(workspaceKeys.ToArray());
                        Interlocked.Add(ref _workspaceChangeInvalidations, workspaceKeys.Count);
                        
                        _logger.LogDebug("Invalidated {Count} cache keys due to workspace file change: {FilePath}", 
                            workspaceKeys.Count, filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change for cache invalidation: {FilePath}", filePath);
        }
    }
    
    /// <summary>
    /// Handle memory change notifications
    /// </summary>
    public async Task HandleMemoryChangeAsync(string memoryId)
    {
        try
        {
            if (_memoryToKeysMap.TryGetValue(memoryId, out var cacheKeys))
            {
                await InvalidateAsync(cacheKeys.ToArray());
                Interlocked.Add(ref _memoryChangeInvalidations, cacheKeys.Count);
                
                _logger.LogDebug("Invalidated {Count} cache keys due to memory change: {MemoryId}", 
                    cacheKeys.Count, memoryId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling memory change for cache invalidation: {MemoryId}", memoryId);
        }
    }
    
    /// <summary>
    /// Process pending invalidations for delayed strategy
    /// </summary>
    private void ProcessPendingInvalidations(object? state)
    {
        if (_pendingInvalidations.IsEmpty) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var keysToInvalidate = new List<string>();
                var cutoffTime = DateTime.UtcNow.AddSeconds(-2); // 2-second delay
                
                while (_pendingInvalidations.TryPeek(out var request))
                {
                    if (request.RequestedAt > cutoffTime) break;
                    
                    if (_pendingInvalidations.TryDequeue(out request))
                    {
                        keysToInvalidate.Add(request.CacheKey);
                    }
                }
                
                if (keysToInvalidate.Count > 0)
                {
                    await InvalidateImmediateAsync(keysToInvalidate.ToArray(), "delayed-batch");
                    _logger.LogDebug("Processed {Count} pending cache invalidations", keysToInvalidate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending cache invalidations");
            }
        }, _cancellationTokenSource.Token);
    }
    
    private async Task InvalidateImmediateAsync(string[] cacheKeys, string source)
    {
        var tasks = cacheKeys.Select(async key =>
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate cache key: {Key}", key);
            }
        });
        
        await Task.WhenAll(tasks);
        
        Interlocked.Add(ref _totalInvalidations, cacheKeys.Length);
        _lastInvalidation = DateTime.UtcNow;
        
        _logger.LogDebug("Invalidated {Count} cache keys immediately (source: {Source})", 
            cacheKeys.Length, source);
    }
    
    private async Task MarkEntriesExpiredAsync(string[] cacheKeys)
    {
        // For lazy invalidation, we would need to modify the cache entries to mark them as expired
        // For now, we'll just remove them (same as immediate)
        await InvalidateImmediateAsync(cacheKeys, "lazy");
    }
    
    public void Dispose()
    {
        try
        {
            _cancellationTokenSource.Cancel();
            _invalidationTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            var stats = GetStatistics();
            _logger.LogInformation("CacheInvalidationService disposed - Total invalidations: {Total}, " +
                                  "File: {File}, Memory: {Memory}, Workspace: {Workspace}, Manual: {Manual}",
                stats.TotalInvalidations, stats.FileChangeInvalidations, stats.MemoryChangeInvalidations,
                stats.WorkspaceChangeInvalidations, stats.ManualInvalidations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing CacheInvalidationService");
        }
    }
    
    /// <summary>
    /// Invalidation request for delayed processing
    /// </summary>
    private class InvalidationRequest
    {
        public string CacheKey { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}