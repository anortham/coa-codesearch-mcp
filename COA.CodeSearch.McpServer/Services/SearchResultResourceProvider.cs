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
            // Check if this is a paginated request
            var pageNumber = 1;
            var pageSize = 50;
            var parts = uri.Split('/');
            var id = parts[2]; // Extract base ID

            if (parts.Length > 3 && parts[3].StartsWith("page"))
            {
                if (int.TryParse(parts[3].Substring(4), out var page))
                {
                    pageNumber = page;
                }
            }

            if (string.IsNullOrEmpty(id) || !_searchResults.TryGetValue(id, out var data))
            {
                _logger.LogWarning("Search result not found: {Uri}", uri);
                return Task.FromResult<ReadResourceResult?>(null);
            }

            // Update last accessed time
            data.LastAccessed = DateTime.UtcNow;

            // Handle pagination
            object responseData;
            if (data.Results is System.Collections.IList list)
            {
                var totalResults = list.Count;
                var totalPages = (int)Math.Ceiling(totalResults / (double)pageSize);
                var skip = (pageNumber - 1) * pageSize;
                var take = Math.Min(pageSize, totalResults - skip);

                var pagedResults = new List<object>();
                for (int i = skip; i < skip + take && i < totalResults; i++)
                {
                    pagedResults.Add(list[i]!);
                }

                responseData = new
                {
                    query = data.Query,
                    metadata = data.Metadata,
                    results = pagedResults,
                    pagination = new
                    {
                        currentPage = pageNumber,
                        pageSize = pageSize,
                        totalPages = totalPages,
                        totalResults = totalResults,
                        hasNextPage = pageNumber < totalPages,
                        hasPreviousPage = pageNumber > 1,
                        nextPageUri = pageNumber < totalPages ? $"{Scheme}://{id}/page{pageNumber + 1}" : null,
                        previousPageUri = pageNumber > 1 ? $"{Scheme}://{id}/page{pageNumber - 1}" : null
                    }
                };
            }
            else
            {
                // Non-paginated response
                responseData = data;
            }

            var result = new ReadResourceResult();
            result.Contents.Add(new ResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            });

            _logger.LogDebug("Retrieved search result {Id} for query '{Query}' (page {Page})", id, data.Query, pageNumber);
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