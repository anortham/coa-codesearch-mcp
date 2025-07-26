using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for streaming large result sets with pagination and async processing
/// Prevents memory issues with large search results and improves responsiveness
/// </summary>
public interface IStreamingResultService
{
    /// <summary>
    /// Stream search results with pagination support
    /// </summary>
    IAsyncEnumerable<SearchResultBatch<T>> StreamResultsAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, T> documentProcessor,
        StreamingOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a single page of results
    /// </summary>
    Task<SearchResultPage<T>> GetPageAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, T> documentProcessor,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stream results with field selector optimization
    /// </summary>
    IAsyncEnumerable<SearchResultBatch<T>> StreamResultsWithFieldSelectorAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, string[], T> documentProcessor,
        string[] fieldNames,
        StreamingOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class StreamingOptions
{
    /// <summary>
    /// Number of documents to process in each batch
    /// </summary>
    public int BatchSize { get; set; } = 50;
    
    /// <summary>
    /// Delay between batches to prevent overwhelming the system
    /// </summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(1);
    
    /// <summary>
    /// Maximum number of results to process
    /// </summary>
    public int MaxResults { get; set; } = int.MaxValue;
    
    /// <summary>
    /// Whether to yield control between documents within a batch
    /// </summary>
    public bool YieldBetweenDocuments { get; set; } = false;
}

public class SearchResultBatch<T>
{
    public IList<T> Results { get; set; } = new List<T>();
    public int BatchNumber { get; set; }
    public int TotalProcessed { get; set; }
    public bool IsLastBatch { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

public class SearchResultPage<T>
{
    public IList<T> Results { get; set; } = new List<T>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalResults { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}