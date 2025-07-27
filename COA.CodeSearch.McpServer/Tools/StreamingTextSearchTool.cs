using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Demonstration tool for streaming search results with pagination
/// Shows async patterns and memory-efficient processing of large result sets
/// </summary>
public class StreamingTextSearchTool : ITool
{
    public string ToolName => "streaming_text_search";
    public string Description => "Memory-efficient streaming text search with pagination";
    public ToolCategory Category => ToolCategory.Search;
    
    private readonly ILogger<StreamingTextSearchTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IStreamingResultService _streamingResultService;
    private readonly IFieldSelectorService _fieldSelectorService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public StreamingTextSearchTool(
        ILogger<StreamingTextSearchTool> logger,
        ILuceneIndexService luceneIndexService,
        IStreamingResultService streamingResultService,
        IFieldSelectorService fieldSelectorService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _streamingResultService = streamingResultService;
        _fieldSelectorService = fieldSelectorService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        string query,
        string mode = "stream", // "stream", "page", or "count"
        int pageNumber = 1,
        int pageSize = 50,
        int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            
            // Build query
            using var analyzer = new StandardAnalyzer(Version);
            var parser = new QueryParser(Version, "content", analyzer);
            var luceneQuery = parser.Parse(query);
            
            // Execute search
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            _logger.LogInformation("Found {Count} results for query '{Query}' in {Duration}ms", 
                topDocs.TotalHits, query, searchDuration);
            
            return mode.ToLower() switch
            {
                "stream" => await ProcessStreamingResults(searcher, topDocs, query, searchDuration, cancellationToken),
                "page" => await ProcessPagedResults(searcher, topDocs, query, pageNumber, pageSize, searchDuration, cancellationToken),
                "count" => ProcessCountOnly(topDocs, query, searchDuration),
                _ => throw new ArgumentException($"Invalid mode: {mode}. Use 'stream', 'page', or 'count'")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming text search for query: {Query}", query);
            return new
            {
                success = false,
                error = $"Search failed: {ex.Message}",
                query = query
            };
        }
    }

    private async Task<object> ProcessStreamingResults(
        IndexSearcher searcher, 
        TopDocs topDocs, 
        string query, 
        double searchDuration,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var batches = new List<object>();
        var totalProcessed = 0;
        
        var streamingOptions = new StreamingOptions
        {
            BatchSize = 25,
            BatchDelay = TimeSpan.FromMilliseconds(5),
            YieldBetweenDocuments = true
        };
        
        // Stream results using field selector for optimal performance
        var fieldNames = new[] { "path", "filename", "content", "extension", "language" };
        
        await foreach (var batch in _streamingResultService.StreamResultsWithFieldSelectorAsync(
            searcher, topDocs, ProcessDocument, fieldNames, streamingOptions, cancellationToken))
        {
            batches.Add(new
            {
                batchNumber = batch.BatchNumber,
                resultCount = batch.Results.Count,
                totalProcessed = batch.TotalProcessed,
                isLastBatch = batch.IsLastBatch,
                processingTimeMs = batch.ProcessingTime.TotalMilliseconds,
                results = batch.Results.Take(5).ToList() // Show first 5 for demo
            });
            
            totalProcessed = batch.TotalProcessed;
            
            // Log progress for large result sets
            if (batch.BatchNumber % 10 == 0)
            {
                _logger.LogDebug("Processed batch {BatchNumber}, total: {TotalProcessed}", 
                    batch.BatchNumber, batch.TotalProcessed);
            }
        }
        
        var totalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        return new
        {
            success = true,
            mode = "streaming",
            query = query,
            searchDurationMs = searchDuration,
            streamingDurationMs = totalDuration,
            totalResults = topDocs.TotalHits,
            processedResults = totalProcessed,
            batchCount = batches.Count,
            performance = totalDuration < 100 ? "excellent streaming" : "efficient streaming",
            batches = batches
        };
    }

    private async Task<object> ProcessPagedResults(
        IndexSearcher searcher, 
        TopDocs topDocs, 
        string query, 
        int pageNumber, 
        int pageSize, 
        double searchDuration,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        var page = await _streamingResultService.GetPageAsync(
            searcher, topDocs, ProcessDocumentSimple, pageNumber, pageSize, cancellationToken);
        
        var processingDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        return new
        {
            success = true,
            mode = "paged",
            query = query,
            searchDurationMs = searchDuration,
            processingDurationMs = processingDuration,
            pagination = new
            {
                currentPage = page.PageNumber,
                pageSize = page.PageSize,
                totalResults = page.TotalResults,
                totalPages = page.TotalPages,
                hasNextPage = page.HasNextPage,
                hasPreviousPage = page.HasPreviousPage
            },
            results = page.Results,
            performance = processingDuration < 50 ? "excellent pagination" : "efficient pagination"
        };
    }

    private object ProcessCountOnly(TopDocs topDocs, string query, double searchDuration)
    {
        return new
        {
            success = true,
            mode = "count",
            query = query,
            searchDurationMs = searchDuration,
            totalResults = topDocs.TotalHits,
            performance = searchDuration < 10 ? "instant count" : "fast count"
        };
    }

    /// <summary>
    /// Document processor for field-optimized streaming
    /// </summary>
    private object ProcessDocument(IndexSearcher searcher, int docId, string[] fieldNames)
    {
        var doc = _fieldSelectorService.LoadDocument(searcher, docId, fieldNames);
        
        return new
        {
            path = doc.Get("path"),
            filename = doc.Get("filename"),
            extension = doc.Get("extension"),
            language = doc.Get("language"),
            contentSnippet = TruncateContent(doc.Get("content"), 100)
        };
    }

    /// <summary>
    /// Simple document processor for pagination
    /// </summary>
    private object ProcessDocumentSimple(IndexSearcher searcher, int docId)
    {
        // Use field selector for better performance even in simple processing
        var doc = _fieldSelectorService.LoadDocument(searcher, docId, FieldSetType.SearchResults);
        
        return new
        {
            path = doc.Get("path"),
            filename = doc.Get("filename"),
            extension = doc.Get("extension"),
            contentSnippet = TruncateContent(doc.Get("content"), 150)
        };
    }

    private static string TruncateContent(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content ?? "";
        
        return content.Substring(0, maxLength) + "...";
    }
}