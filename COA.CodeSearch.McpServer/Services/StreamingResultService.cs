using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// High-performance streaming service for processing large Lucene search results
/// Provides pagination, batching, and async enumeration to prevent memory issues
/// </summary>
public class StreamingResultService : IStreamingResultService
{
    private readonly ILogger<StreamingResultService> _logger;
    private readonly IFieldSelectorService _fieldSelectorService;

    public StreamingResultService(
        ILogger<StreamingResultService> logger,
        IFieldSelectorService fieldSelectorService)
    {
        _logger = logger;
        _fieldSelectorService = fieldSelectorService;
    }

    public async IAsyncEnumerable<SearchResultBatch<T>> StreamResultsAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, T> documentProcessor,
        StreamingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new StreamingOptions();
        var totalResults = Math.Min(topDocs.ScoreDocs.Length, options.MaxResults);
        var batchNumber = 0;
        var totalProcessed = 0;

        _logger.LogDebug("Starting streaming of {TotalResults} results with batch size {BatchSize}", 
            totalResults, options.BatchSize);

        for (int start = 0; start < totalResults; start += options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var batchStopwatch = Stopwatch.StartNew();
            var batchSize = Math.Min(options.BatchSize, totalResults - start);
            var batchResults = new List<T>(batchSize);
            
            // Process batch
            for (int i = 0; i < batchSize; i++)
            {
                var docIndex = start + i;
                var scoreDoc = topDocs.ScoreDocs[docIndex];
                
                try
                {
                    var result = documentProcessor(searcher, scoreDoc.Doc);
                    batchResults.Add(result);
                    totalProcessed++;
                    
                    // Yield control between documents if requested
                    if (options.YieldBetweenDocuments && i % 10 == 0)
                    {
                        await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing document {DocId} in batch {BatchNumber}", 
                        scoreDoc.Doc, batchNumber);
                    // Continue processing other documents
                }
            }
            
            batchStopwatch.Stop();
            var isLastBatch = start + batchSize >= totalResults;
            
            yield return new SearchResultBatch<T>
            {
                Results = batchResults,
                BatchNumber = batchNumber++,
                TotalProcessed = totalProcessed,
                IsLastBatch = isLastBatch,
                ProcessingTime = batchStopwatch.Elapsed
            };
            
            // Add delay between batches if specified
            if (!isLastBatch && options.BatchDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.BatchDelay, cancellationToken);
            }
        }
        
        _logger.LogDebug("Completed streaming {TotalProcessed} results in {BatchCount} batches", 
            totalProcessed, batchNumber);
    }

    public async Task<SearchResultPage<T>> GetPageAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, T> documentProcessor,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalResults = topDocs.ScoreDocs.Length;
        var totalPages = (int)Math.Ceiling((double)totalResults / pageSize);
        
        // Validate page number
        if (pageNumber < 1 || pageNumber > totalPages)
        {
            return new SearchResultPage<T>
            {
                Results = new List<T>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalResults = totalResults,
                TotalPages = totalPages,
                HasNextPage = false,
                HasPreviousPage = false,
                ProcessingTime = stopwatch.Elapsed
            };
        }
        
        var startIndex = (pageNumber - 1) * pageSize;
        var endIndex = Math.Min(startIndex + pageSize, totalResults);
        var pageResults = new List<T>(endIndex - startIndex);
        
        // Process documents for this page
        for (int i = startIndex; i < endIndex; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var scoreDoc = topDocs.ScoreDocs[i];
            try
            {
                var result = documentProcessor(searcher, scoreDoc.Doc);
                pageResults.Add(result);
                
                // Yield control every 10 documents for responsiveness
                if ((i - startIndex) % 10 == 0)
                {
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing document {DocId} on page {PageNumber}", 
                    scoreDoc.Doc, pageNumber);
            }
        }
        
        stopwatch.Stop();
        
        return new SearchResultPage<T>
        {
            Results = pageResults,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalResults = totalResults,
            TotalPages = totalPages,
            HasNextPage = pageNumber < totalPages,
            HasPreviousPage = pageNumber > 1,
            ProcessingTime = stopwatch.Elapsed
        };
    }

    public async IAsyncEnumerable<SearchResultBatch<T>> StreamResultsWithFieldSelectorAsync<T>(
        IndexSearcher searcher,
        TopDocs topDocs,
        Func<IndexSearcher, int, string[], T> documentProcessor,
        string[] fieldNames,
        StreamingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new StreamingOptions();
        var totalResults = Math.Min(topDocs.ScoreDocs.Length, options.MaxResults);
        var batchNumber = 0;
        var totalProcessed = 0;

        _logger.LogDebug("Starting field-optimized streaming of {TotalResults} results with {FieldCount} fields", 
            totalResults, fieldNames.Length);

        for (int start = 0; start < totalResults; start += options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var batchStopwatch = Stopwatch.StartNew();
            var batchSize = Math.Min(options.BatchSize, totalResults - start);
            var batchResults = new List<T>(batchSize);
            
            // Extract document IDs for batch field loading
            var docIds = new int[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                docIds[i] = topDocs.ScoreDocs[start + i].Doc;
            }
            
            // Batch load documents with field selector for better performance
            var documents = _fieldSelectorService.LoadDocuments(searcher, docIds, fieldNames);
            
            // Process batch
            for (int i = 0; i < batchSize; i++)
            {
                try
                {
                    var result = documentProcessor(searcher, docIds[i], fieldNames);
                    batchResults.Add(result);
                    totalProcessed++;
                    
                    // Yield control between documents if requested
                    if (options.YieldBetweenDocuments && i % 10 == 0)
                    {
                        await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing document {DocId} in field-optimized batch {BatchNumber}", 
                        docIds[i], batchNumber);
                }
            }
            
            batchStopwatch.Stop();
            var isLastBatch = start + batchSize >= totalResults;
            
            yield return new SearchResultBatch<T>
            {
                Results = batchResults,
                BatchNumber = batchNumber++,
                TotalProcessed = totalProcessed,
                IsLastBatch = isLastBatch,
                ProcessingTime = batchStopwatch.Elapsed
            };
            
            // Add delay between batches if specified
            if (!isLastBatch && options.BatchDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.BatchDelay, cancellationToken);
            }
        }
        
        _logger.LogDebug("Completed field-optimized streaming {TotalProcessed} results in {BatchCount} batches", 
            totalProcessed, batchNumber);
    }
}