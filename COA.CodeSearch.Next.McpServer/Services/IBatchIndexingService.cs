using Lucene.Net.Documents;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for high-performance batch indexing with configurable batch sizes and commit intervals
/// </summary>
public interface IBatchIndexingService
{
    /// <summary>
    /// Add a document to the current batch for indexing
    /// </summary>
    Task AddDocumentAsync(string workspacePath, Document document, string documentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Flush all pending documents in the current batch
    /// </summary>
    Task FlushBatchAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get batch statistics for monitoring
    /// </summary>
    BatchIndexingStats GetStats(string workspacePath);
    
    /// <summary>
    /// Force commit all pending batches across all workspaces
    /// </summary>
    Task CommitAllAsync(CancellationToken cancellationToken = default);
}

public class BatchIndexingStats
{
    public int PendingDocuments { get; set; }
    public int TotalBatches { get; set; }
    public long TotalDocuments { get; set; }
    public TimeSpan AverageBatchTime { get; set; }
    public DateTime LastCommit { get; set; }
}