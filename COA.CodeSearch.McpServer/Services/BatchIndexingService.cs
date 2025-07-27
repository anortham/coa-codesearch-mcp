using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// High-performance batch indexing service that accumulates documents and commits them in configurable batches
/// Provides 10-100x performance improvement for bulk indexing operations
/// </summary>
public class BatchIndexingService : IBatchIndexingService, IDisposable
{
    private readonly ILogger<BatchIndexingService> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly int _batchSize;
    private readonly TimeSpan _maxBatchAge;
    private readonly Timer _flushTimer;
    
    // Per-workspace batch tracking
    private readonly ConcurrentDictionary<string, WorkspaceBatch> _workspaceBatches = new();
    
    public BatchIndexingService(
        ILogger<BatchIndexingService> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        
        // Load configuration with sensible defaults
        _batchSize = configuration.GetValue("BatchIndexing:BatchSize", 500);
        _maxBatchAge = TimeSpan.FromSeconds(configuration.GetValue("BatchIndexing:MaxBatchAgeSeconds", 30));
        
        // Auto-flush timer to prevent batches from sitting too long
        _flushTimer = new Timer(FlushStaleBatches, null, _maxBatchAge, _maxBatchAge);
        
        _logger.LogInformation("BatchIndexingService initialized: BatchSize={BatchSize}, MaxAge={MaxAge}", 
            _batchSize, _maxBatchAge);
    }

    public Task AddDocumentAsync(string workspacePath, Document document, string documentId, CancellationToken cancellationToken = default)
    {
        var batch = _workspaceBatches.GetOrAdd(workspacePath, _ => new WorkspaceBatch(workspacePath, _logger));
        
        lock (batch.Lock)
        {
            batch.Documents.Add((document, documentId));
            batch.LastModified = DateTime.UtcNow;
            
            // Check if we should flush this batch
            if (batch.Documents.Count >= _batchSize)
            {
                // Schedule flush on thread pool to avoid blocking caller
                _ = Task.Run(() => FlushBatchInternalAsync(batch, cancellationToken), cancellationToken);
            }
        }
        
        return Task.CompletedTask;
    }

    public async Task FlushBatchAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        if (_workspaceBatches.TryGetValue(workspacePath, out var batch))
        {
            await FlushBatchInternalAsync(batch, cancellationToken).ConfigureAwait(false);
        }
    }

    public BatchIndexingStats GetStats(string workspacePath)
    {
        if (_workspaceBatches.TryGetValue(workspacePath, out var batch))
        {
            lock (batch.Lock)
            {
                return new BatchIndexingStats
                {
                    PendingDocuments = batch.Documents.Count,
                    TotalBatches = batch.TotalBatches,
                    TotalDocuments = batch.TotalDocuments,
                    AverageBatchTime = batch.TotalBatches > 0 ? 
                        TimeSpan.FromMilliseconds(batch.TotalBatchTimeMs / batch.TotalBatches) : 
                        TimeSpan.Zero,
                    LastCommit = batch.LastCommit
                };
            }
        }
        
        return new BatchIndexingStats();
    }

    public async Task CommitAllAsync(CancellationToken cancellationToken = default)
    {
        var flushTasks = _workspaceBatches.Values
            .Select(batch => FlushBatchInternalAsync(batch, cancellationToken))
            .ToList();
            
        await Task.WhenAll(flushTasks).ConfigureAwait(false);
    }

    private async Task FlushBatchInternalAsync(WorkspaceBatch batch, CancellationToken cancellationToken)
    {
        List<(Document document, string id)> documentsToFlush;
        
        // Extract documents under lock to minimize lock time
        lock (batch.Lock)
        {
            if (batch.Documents.Count == 0)
                return;
                
            documentsToFlush = new List<(Document, string)>(batch.Documents);
            batch.Documents.Clear();
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Flushing batch of {Count} documents for workspace: {Workspace}", 
                documentsToFlush.Count, batch.WorkspacePath);
            
            var indexWriter = await _luceneIndexService.GetIndexWriterAsync(batch.WorkspacePath, cancellationToken).ConfigureAwait(false);
            
            // Batch update all documents
            foreach (var (document, id) in documentsToFlush)
            {
                indexWriter.UpdateDocument(new Term("id", id), document);
            }
            
            // Single commit for entire batch
            await _luceneIndexService.CommitAsync(batch.WorkspacePath, cancellationToken).ConfigureAwait(false);
            
            // Update statistics
            lock (batch.Lock)
            {
                batch.TotalBatches++;
                batch.TotalDocuments += documentsToFlush.Count;
                batch.TotalBatchTimeMs += stopwatch.ElapsedMilliseconds;
                batch.LastCommit = DateTime.UtcNow;
            }
            
            _logger.LogDebug("Successfully flushed {Count} documents in {ElapsedMs}ms for workspace: {Workspace}", 
                documentsToFlush.Count, stopwatch.ElapsedMilliseconds, batch.WorkspacePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing batch of {Count} documents for workspace: {Workspace}", 
                documentsToFlush.Count, batch.WorkspacePath);
            
            // Put documents back into batch for retry
            lock (batch.Lock)
            {
                batch.Documents.InsertRange(0, documentsToFlush);
            }
            
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private void FlushStaleBatches(object? state)
    {
        var staleThreshold = DateTime.UtcNow - _maxBatchAge;
        var staleBatches = new List<WorkspaceBatch>();
        
        foreach (var batch in _workspaceBatches.Values)
        {
            lock (batch.Lock)
            {
                if (batch.Documents.Count > 0 && batch.LastModified < staleThreshold)
                {
                    staleBatches.Add(batch);
                }
            }
        }
        
        if (staleBatches.Count > 0)
        {
            _logger.LogDebug("Flushing {Count} stale batches", staleBatches.Count);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var flushTasks = staleBatches.Select(batch => 
                        FlushBatchInternalAsync(batch, CancellationToken.None));
                    await Task.WhenAll(flushTasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error flushing stale batches");
                }
            });
        }
    }

    public void Dispose()
    {
        try
        {
            _flushTimer?.Dispose();
            
            // Flush all remaining batches synchronously
            var flushTasks = _workspaceBatches.Values
                .Select(batch => FlushBatchInternalAsync(batch, CancellationToken.None))
                .ToArray();
                
            Task.WaitAll(flushTasks, TimeSpan.FromSeconds(30));
            
            var totalStats = _workspaceBatches.Values.Aggregate(
                new { TotalDocs = 0, TotalBatches = 0 },
                (acc, batch) => new { 
                    TotalDocs = acc.TotalDocs + batch.TotalDocuments,
                    TotalBatches = acc.TotalBatches + batch.TotalBatches
                });
                
            _logger.LogInformation("BatchIndexingService disposed - Total: {TotalDocs} documents in {TotalBatches} batches", 
                totalStats.TotalDocs, totalStats.TotalBatches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing BatchIndexingService");
        }
    }

    /// <summary>
    /// Per-workspace batch state
    /// </summary>
    private class WorkspaceBatch
    {
        public string WorkspacePath { get; }
        public List<(Document document, string id)> Documents { get; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public DateTime LastCommit { get; set; } = DateTime.UtcNow;
        public object Lock { get; } = new();
        
        // Statistics
        public int TotalBatches { get; set; }
        public int TotalDocuments { get; set; }
        public long TotalBatchTimeMs { get; set; }
        
        private readonly ILogger _logger;

        public WorkspaceBatch(string workspacePath, ILogger logger)
        {
            WorkspacePath = workspacePath;
            _logger = logger;
        }
    }
}