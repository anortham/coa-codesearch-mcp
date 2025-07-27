using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Resource provider that exposes search results as persistent, shareable resources.
/// Allows clients to bookmark and access search results via stable URIs.
/// </summary>
public class SearchResultResourceProvider : IResourceProvider
{
    private readonly ILogger<SearchResultResourceProvider> _logger;
    private readonly ConcurrentDictionary<string, SearchResultData> _searchResults = new();
    private readonly Timer _cleanupTimer;

    public string Scheme => "codesearch-search";
    public string Name => "Search Results";
    public string Description => "Provides access to persistent search results";

    public SearchResultResourceProvider(ILogger<SearchResultResourceProvider> logger)
    {
        _logger = logger;
        
        // Clean up old search results every hour
        _cleanupTimer = new Timer(CleanupOldResults, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    /// <inheritdoc />
    public Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<Resource>();

        foreach (var kvp in _searchResults)
        {
            var id = kvp.Key;
            var data = kvp.Value;

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://{id}",
                Name = $"Search: {data.Query}",
                Description = $"Search results for '{data.Query}' ({data.ResultCount} results) - {data.CreatedAt:g}",
                MimeType = "application/json"
            });
        }

        _logger.LogDebug("Listed {Count} search result resources", resources.Count);
        return Task.FromResult(resources);
    }

    /// <inheritdoc />
    public Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return Task.FromResult<ReadResourceResult?>(null);

        try
        {
            var id = ExtractIdFromUri(uri);
            if (string.IsNullOrEmpty(id) || !_searchResults.TryGetValue(id, out var data))
            {
                _logger.LogWarning("Search result not found: {Uri}", uri);
                return Task.FromResult<ReadResourceResult?>(null);
            }

            // Update last accessed time
            data.LastAccessed = DateTime.UtcNow;

            var result = new ReadResourceResult();
            result.Contents.Add(new ResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            });

            _logger.LogDebug("Retrieved search result {Id} for query '{Query}'", id, data.Query);
            return Task.FromResult<ReadResourceResult?>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading search result resource {Uri}", uri);
            return Task.FromResult<ReadResourceResult?>(null);
        }
    }

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        return uri.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stores a search result and returns a URI that can be used to access it later.
    /// </summary>
    /// <param name="query">The search query that was executed.</param>
    /// <param name="results">The search results.</param>
    /// <param name="metadata">Additional metadata about the search.</param>
    /// <returns>A URI that can be used to access the stored search result.</returns>
    public string StoreSearchResult(string query, object results, object? metadata = null)
    {
        var id = GenerateSearchResultId(query);
        var data = new SearchResultData
        {
            Id = id,
            Query = query,
            Results = results,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            ResultCount = GetResultCount(results)
        };

        _searchResults[id] = data;
        _logger.LogInformation("Stored search result {Id} for query '{Query}' with {Count} results", 
            id, query, data.ResultCount);

        return $"{Scheme}://{id}";
    }

    private string ExtractIdFromUri(string uri)
    {
        if (!uri.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return uri.Substring($"{Scheme}://".Length);
    }

    private string GenerateSearchResultId(string query)
    {
        // Create a deterministic ID based on query and timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = query.GetHashCode();
        return $"search_{Math.Abs(hash):x8}_{timestamp}";
    }

    private int GetResultCount(object results)
    {
        return results switch
        {
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => 1
        };
    }

    private void CleanupOldResults(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-7); // Keep results for 7 days
            var toRemove = _searchResults
                .Where(kvp => kvp.Value.LastAccessed < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in toRemove)
            {
                if (_searchResults.TryRemove(id, out var data))
                {
                    _logger.LogDebug("Cleaned up old search result {Id} for query '{Query}'", 
                        id, data.Query);
                }
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old search results", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search result cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Represents stored search result data.
/// </summary>
public class SearchResultData
{
    public string Id { get; set; } = null!;
    public string Query { get; set; } = null!;
    public object Results { get; set; } = null!;
    public object? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public int ResultCount { get; set; }
}