using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Caches data for detail requests to support progressive disclosure
/// </summary>
public interface IDetailRequestCache
{
    /// <summary>
    /// Stores data for later detail requests
    /// </summary>
    string StoreDetailData<T>(T data, TimeSpan? expiration = null);
    
    /// <summary>
    /// Retrieves cached data for a detail request
    /// </summary>
    T? GetDetailData<T>(string token);
    
    /// <summary>
    /// Checks if a token is valid
    /// </summary>
    bool IsTokenValid(string token);
}

/// <summary>
/// Memory-based implementation of detail request cache
/// </summary>
public class DetailRequestCache : IDetailRequestCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<DetailRequestCache> _logger;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(15);
    
    public DetailRequestCache(IMemoryCache cache, ILogger<DetailRequestCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }
    
    public string StoreDetailData<T>(T data, TimeSpan? expiration = null)
    {
        var token = GenerateToken();
        var cacheKey = GetCacheKey(token);
        
        var cacheData = new CachedDetailData
        {
            Data = JsonSerializer.Serialize(data),
            DataType = typeof(T).FullName ?? typeof(T).Name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        _cache.Set(cacheKey, cacheData, expiration ?? DefaultExpiration);
        
        _logger.LogDebug("Stored detail data with token {Token}, expires in {Minutes} minutes", 
            token, (expiration ?? DefaultExpiration).TotalMinutes);
        
        return token;
    }
    
    public T? GetDetailData<T>(string token)
    {
        var cacheKey = GetCacheKey(token);
        
        if (_cache.TryGetValue<CachedDetailData>(cacheKey, out var cachedData) && cachedData != null)
        {
            try
            {
                var data = JsonSerializer.Deserialize<T>(cachedData.Data);
                _logger.LogDebug("Retrieved detail data for token {Token}", token);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize cached data for token {Token}", token);
                return default;
            }
        }
        
        _logger.LogDebug("No cached data found for token {Token}", token);
        return default;
    }
    
    public bool IsTokenValid(string token)
    {
        var cacheKey = GetCacheKey(token);
        return _cache.TryGetValue<CachedDetailData>(cacheKey, out _);
    }
    
    private static string GenerateToken()
    {
        // Generate a unique token
        var guid = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tokenData = $"{guid}:{timestamp}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tokenData));
    }
    
    private static string GetCacheKey(string token)
    {
        return $"detail_request:{token}";
    }
    
    private class CachedDetailData
    {
        public string Data { get; set; } = "";
        public string DataType { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }
}