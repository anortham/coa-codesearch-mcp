using COA.CodeSearch.McpServer.Services.Caching;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for cache management operations
/// </summary>
public class CacheManagementParams
{
    [Description("Operation to perform: 'statistics', 'clear', 'warm', 'invalidate', 'health', 'config'")]
    public string Operation { get; set; } = "statistics";
    
    [Description("Cache keys to warm or invalidate (comma-separated)")]
    public string? Keys { get; set; }
    
    [Description("Pattern for pattern-based invalidation (supports wildcards)")]
    public string? Pattern { get; set; }
    
    [Description("Cache level to operate on: 'L1', 'L2', or 'both'")]
    public string Level { get; set; } = "both";
    
    [Description("Invalidation strategy: 'immediate', 'delayed', 'lazy'")]
    public string? InvalidationStrategy { get; set; }
    
    [Description("Include detailed breakdown in statistics")]
    public bool IncludeDetails { get; set; } = true;
    
    [Description("Force operation even if it may impact performance")]
    public bool Force { get; set; } = false;
}

/// <summary>
/// Tool for managing and monitoring the multi-level cache system
/// </summary>
public class CacheManagementTool
{
    public string Name => "cache_management";
    public string Description => "Manage and monitor the multi-level cache system with statistics, clear, warm, and invalidate operations";
    public ToolCategory Category => ToolCategory.Infrastructure;
    
    private readonly ILogger<CacheManagementTool> _logger;
    private readonly IMultiLevelCache _cache;
    private readonly ICacheInvalidationService _invalidationService;
    
    public CacheManagementTool(
        ILogger<CacheManagementTool> logger,
        IMultiLevelCache cache,
        ICacheInvalidationService invalidationService)
    {
        _logger = logger;
        _cache = cache;
        _invalidationService = invalidationService;
    }
    
    public async Task<object> ExecuteAsync(CacheManagementParams parameters)
    {
        try
        {
            return parameters.Operation.ToLowerInvariant() switch
            {
                "statistics" or "stats" => await GetCacheStatisticsAsync(parameters),
                "clear" => await ClearCacheAsync(parameters),
                "warm" => await WarmCacheAsync(parameters),
                "invalidate" => await InvalidateCacheAsync(parameters),
                "health" => await GetCacheHealthAsync(parameters),
                "config" => await GetCacheConfigAsync(parameters),
                _ => new { error = $"Unknown operation: {parameters.Operation}. Use 'statistics', 'clear', 'warm', 'invalidate', 'health', or 'config'" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing cache management operation: {Operation}", parameters.Operation);
            return new { error = "Cache management operation failed", details = ex.Message };
        }
    }
    
    private async Task<object> GetCacheStatisticsAsync(CacheManagementParams parameters)
    {
        try
        {
            var cacheStats = _cache.GetStatistics();
            var invalidationStats = _invalidationService.GetStatistics();
            
            var result = new
            {
                operation = "statistics",
                timestamp = DateTime.UtcNow,
                cache = new
                {
                    l1 = new
                    {
                        hits = cacheStats.L1Hits,
                        misses = cacheStats.L1Misses,
                        hitRatio = cacheStats.L1HitRatio,
                        entryCount = cacheStats.L1EntryCount,
                        memoryUsage = new
                        {
                            bytes = cacheStats.L1MemoryUsage,
                            mb = Math.Round(cacheStats.L1MemoryUsage / 1024.0 / 1024.0, 2)
                        }
                    },
                    l2 = new
                    {
                        hits = cacheStats.L2Hits,
                        misses = cacheStats.L2Misses,
                        hitRatio = cacheStats.L2HitRatio,
                        entryCount = cacheStats.L2EntryCount,
                        diskUsage = new
                        {
                            bytes = cacheStats.L2DiskUsage,
                            mb = Math.Round(cacheStats.L2DiskUsage / 1024.0 / 1024.0, 2)
                        }
                    },
                    overall = new
                    {
                        totalRequests = cacheStats.TotalRequests,
                        overallHitRatio = cacheStats.OverallHitRatio,
                        evictions = cacheStats.CacheEvictions,
                        warmups = cacheStats.CacheWarmups,
                        lastClear = cacheStats.LastClearTime
                    }
                },
                invalidation = new
                {
                    totalInvalidations = invalidationStats.TotalInvalidations,
                    breakdown = new
                    {
                        fileChanges = invalidationStats.FileChangeInvalidations,
                        memoryChanges = invalidationStats.MemoryChangeInvalidations,
                        workspaceChanges = invalidationStats.WorkspaceChangeInvalidations,
                        manual = invalidationStats.ManualInvalidations,
                        pattern = invalidationStats.PatternInvalidations
                    },
                    lastInvalidation = invalidationStats.LastInvalidation,
                    pendingInvalidations = invalidationStats.PendingInvalidations
                },
                performance = new
                {
                    cacheEffectiveness = GetCacheEffectiveness(cacheStats),
                    memoryEfficiency = GetMemoryEfficiency(cacheStats),
                    recommendations = GetPerformanceRecommendations(cacheStats, invalidationStats)
                }
            };
            
            if (parameters.IncludeDetails)
            {
                return new
                {
                    result,
                    details = new
                    {
                        cacheKeyDistribution = await GetCacheKeyDistribution(),
                        topAccessedKeys = await GetTopAccessedKeys(),
                        cacheAgeDistribution = await GetCacheAgeDistribution()
                    }
                };
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            return new { error = "Failed to get cache statistics", details = ex.Message };
        }
    }
    
    private async Task<object> ClearCacheAsync(CacheManagementParams parameters)
    {
        try
        {
            if (!parameters.Force)
            {
                return new
                {
                    warning = "Cache clear operation requires 'force: true' to prevent accidental data loss",
                    suggestion = "Use 'force: true' if you really want to clear the cache"
                };
            }
            
            var statsBefore = _cache.GetStatistics();
            
            await _cache.ClearAsync();
            
            _logger.LogWarning("Cache cleared by user request - L1: {L1Entries} entries, L2: {L2Entries} entries", 
                statsBefore.L1EntryCount, statsBefore.L2EntryCount);
            
            return new
            {
                operation = "clear",
                success = true,
                timestamp = DateTime.UtcNow,
                clearedEntries = new
                {
                    l1 = statsBefore.L1EntryCount,
                    l2 = statsBefore.L2EntryCount,
                    total = statsBefore.L1EntryCount + statsBefore.L2EntryCount
                },
                freedSpace = new
                {
                    memoryMB = Math.Round(statsBefore.L1MemoryUsage / 1024.0 / 1024.0, 2),
                    diskMB = Math.Round(statsBefore.L2DiskUsage / 1024.0 / 1024.0, 2)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            return new { error = "Failed to clear cache", details = ex.Message };
        }
    }
    
    private async Task<object> WarmCacheAsync(CacheManagementParams parameters)
    {
        try
        {
            if (string.IsNullOrEmpty(parameters.Keys))
            {
                return new { error = "Cache keys required for warm operation. Provide comma-separated keys." };
            }
            
            var keys = parameters.Keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
            
            if (keys.Count == 0)
            {
                return new { error = "No valid cache keys provided" };
            }
            
            var statsBefore = _cache.GetStatistics();
            
            await _cache.WarmCacheAsync(keys);
            
            var statsAfter = _cache.GetStatistics();
            var warmedCount = statsAfter.CacheWarmups - statsBefore.CacheWarmups;
            
            return new
            {
                operation = "warm",
                success = true,
                timestamp = DateTime.UtcNow,
                requestedKeys = keys.Count,
                warmedKeys = warmedCount,
                keysProvided = keys
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming cache");
            return new { error = "Failed to warm cache", details = ex.Message };
        }
    }
    
    private async Task<object> InvalidateCacheAsync(CacheManagementParams parameters)
    {
        try
        {
            var invalidatedCount = 0;
            
            // Set invalidation strategy if provided
            if (!string.IsNullOrEmpty(parameters.InvalidationStrategy))
            {
                if (Enum.TryParse<InvalidationStrategy>(parameters.InvalidationStrategy, true, out var strategy))
                {
                    _invalidationService.SetInvalidationStrategy(strategy);
                }
                else
                {
                    return new { error = $"Invalid invalidation strategy: {parameters.InvalidationStrategy}" };
                }
            }
            
            // Invalidate by pattern
            if (!string.IsNullOrEmpty(parameters.Pattern))
            {
                await _invalidationService.InvalidateByPatternAsync(parameters.Pattern);
                invalidatedCount++; // Approximate - pattern could match multiple keys
                
                return new
                {
                    operation = "invalidate",
                    method = "pattern",
                    success = true,
                    timestamp = DateTime.UtcNow,
                    pattern = parameters.Pattern,
                    note = "Pattern-based invalidation completed. Exact count not available."
                };
            }
            
            // Invalidate specific keys
            if (!string.IsNullOrEmpty(parameters.Keys))
            {
                var keys = parameters.Keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToArray();
                
                if (keys.Length == 0)
                {
                    return new { error = "No valid cache keys provided" };
                }
                
                await _invalidationService.InvalidateAsync(keys);
                invalidatedCount = keys.Length;
                
                return new
                {
                    operation = "invalidate",
                    method = "keys",
                    success = true,
                    timestamp = DateTime.UtcNow,
                    invalidatedKeys = keys,
                    count = invalidatedCount
                };
            }
            
            return new { error = "Either 'keys' or 'pattern' must be provided for invalidation" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache");
            return new { error = "Failed to invalidate cache", details = ex.Message };
        }
    }
    
    private async Task<object> GetCacheHealthAsync(CacheManagementParams parameters)
    {
        try
        {
            var stats = _cache.GetStatistics();
            var invalidationStats = _invalidationService.GetStatistics();
            
            // Health score calculation (0-100)
            var healthScore = CalculateHealthScore(stats, invalidationStats);
            var healthStatus = healthScore switch
            {
                >= 90 => "excellent",
                >= 75 => "good", 
                >= 50 => "fair",
                >= 25 => "poor",
                _ => "critical"
            };
            
            var issues = new List<string>();
            var recommendations = new List<string>();
            
            // Check for common issues
            if (stats.OverallHitRatio < 0.5)
            {
                issues.Add("Low overall hit ratio");
                recommendations.Add("Consider cache warming for frequently accessed data");
            }
            
            if (stats.L1MemoryUsage > 150 * 1024 * 1024) // 150MB
            {
                issues.Add("High L1 memory usage");
                recommendations.Add("Consider reducing L1 cache size or increasing eviction frequency");
            }
            
            if (stats.L2DiskUsage > 500 * 1024 * 1024) // 500MB
            {
                issues.Add("High L2 disk usage");
                recommendations.Add("Consider L2 cache cleanup or size reduction");
            }
            
            if (invalidationStats.PendingInvalidations > 100)
            {
                issues.Add("High number of pending invalidations");
                recommendations.Add("Consider switching to immediate invalidation strategy");
            }
            
            return new
            {
                operation = "health",
                timestamp = DateTime.UtcNow,
                healthScore = healthScore,
                status = healthStatus,
                issues = issues,
                recommendations = recommendations,
                metrics = new
                {
                    hitRatio = stats.OverallHitRatio,
                    memoryUsageMB = Math.Round(stats.L1MemoryUsage / 1024.0 / 1024.0, 2),
                    diskUsageMB = Math.Round(stats.L2DiskUsage / 1024.0 / 1024.0, 2),
                    totalEntries = stats.L1EntryCount + stats.L2EntryCount,
                    pendingInvalidations = invalidationStats.PendingInvalidations
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache health");
            return new { error = "Failed to get cache health", details = ex.Message };
        }
    }
    
    private async Task<object> GetCacheConfigAsync(CacheManagementParams parameters)
    {
        try
        {
            // This would normally come from configuration
            return new
            {
                operation = "config",
                timestamp = DateTime.UtcNow,
                configuration = new
                {
                    l1Cache = new
                    {
                        enabled = true,
                        maxSizeMB = 150,
                        cleanupIntervalMinutes = 10
                    },
                    l2Cache = new
                    {
                        enabled = true,
                        maxSizeMB = 500,
                        cleanupIntervalMinutes = 30,
                        directory = ".codesearch/cache"
                    },
                    defaults = new
                    {
                        ttlHours = 2,
                        enableCacheWarming = true
                    },
                    invalidation = new
                    {
                        strategy = "immediate",
                        delaySeconds = 2
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache configuration");
            return new { error = "Failed to get cache configuration", details = ex.Message };
        }
    }
    
    private static string GetCacheEffectiveness(CacheStatistics stats)
    {
        return stats.OverallHitRatio switch
        {
            >= 0.9 => "excellent",
            >= 0.8 => "very-good",
            >= 0.7 => "good",
            >= 0.5 => "fair",
            _ => "poor"
        };
    }
    
    private static string GetMemoryEfficiency(CacheStatistics stats)
    {
        var memoryUsageMB = stats.L1MemoryUsage / 1024.0 / 1024.0;
        return memoryUsageMB switch
        {
            < 50 => "excellent",
            < 100 => "good",
            < 150 => "fair",
            _ => "high"
        };
    }
    
    private static List<string> GetPerformanceRecommendations(CacheStatistics cacheStats, CacheInvalidationStats invalidationStats)
    {
        var recommendations = new List<string>();
        
        if (cacheStats.OverallHitRatio < 0.8)
        {
            recommendations.Add("Implement cache warming for frequently accessed data");
        }
        
        if (cacheStats.CacheEvictions > cacheStats.CacheWarmups * 2)
        {
            recommendations.Add("Consider increasing cache size limits to reduce evictions");
        }
        
        if (invalidationStats.PendingInvalidations > 50)
        {
            recommendations.Add("Switch to immediate invalidation strategy for better consistency");
        }
        
        if (cacheStats.L1MemoryUsage > 100 * 1024 * 1024)
        {
            recommendations.Add("Monitor L1 memory usage and consider size optimization");
        }
        
        return recommendations;
    }
    
    private static int CalculateHealthScore(CacheStatistics cacheStats, CacheInvalidationStats invalidationStats)
    {
        var score = 100;
        
        // Hit ratio impact (40% of total score)
        score -= (int)((1.0 - cacheStats.OverallHitRatio) * 40);
        
        // Memory usage impact (20% of total score)
        var memoryUsageMB = cacheStats.L1MemoryUsage / 1024.0 / 1024.0;
        if (memoryUsageMB > 150) score -= 20;
        else if (memoryUsageMB > 100) score -= 10;
        
        // Disk usage impact (20% of total score)
        var diskUsageMB = cacheStats.L2DiskUsage / 1024.0 / 1024.0;
        if (diskUsageMB > 500) score -= 20;
        else if (diskUsageMB > 300) score -= 10;
        
        // Invalidation efficiency (20% of total score)
        if (invalidationStats.PendingInvalidations > 100) score -= 20;
        else if (invalidationStats.PendingInvalidations > 50) score -= 10;
        
        return Math.Max(0, score);
    }
    
    private async Task<object> GetCacheKeyDistribution()
    {
        // This would require extending the cache interface to provide key enumeration
        // For now, return a placeholder
        return new
        {
            note = "Cache key distribution analysis requires additional instrumentation",
            placeholder = true
        };
    }
    
    private async Task<object> GetTopAccessedKeys()
    {
        // This would require access counting in cache entries
        return new
        {
            note = "Top accessed keys analysis requires additional instrumentation",
            placeholder = true
        };
    }
    
    private async Task<object> GetCacheAgeDistribution()
    {
        // This would require age tracking in cache entries
        return new
        {
            note = "Cache age distribution analysis requires additional instrumentation",
            placeholder = true
        };
    }
}