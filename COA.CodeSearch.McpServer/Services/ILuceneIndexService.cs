using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

public interface ILuceneIndexService : IAsyncDisposable, IDisposable
{
    Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task ForceMergeAsync(string workspacePath, int maxNumSegments = 1, CancellationToken cancellationToken = default);
    [Obsolete("Use ForceMergeAsync instead. 'Optimize' is deprecated terminology in Lucene.")]
    Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetAllIndexMappingsAsync();
    
    /// <summary>
    /// Get the physical index path for a workspace - single source of truth for path resolution
    /// </summary>
    Task<string> GetPhysicalIndexPathAsync(string workspacePath);
    
    /// <summary>
    /// Diagnose stuck index locks from previous sessions (does not automatically clean)
    /// </summary>
    Task DiagnoseStuckIndexesAsync();
    
    /// <summary>
    /// Clean up duplicate indices created due to path normalization issues
    /// </summary>
    Task CleanupDuplicateIndicesAsync();
    
    /// <summary>
    /// Comprehensive index defragmentation for long-running installations
    /// </summary>
    Task<IndexDefragmentationResult> DefragmentIndexAsync(string workspacePath, 
        IndexDefragmentationOptions? options = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Repairs a corrupted index by removing bad segments
    /// </summary>
    Task<IndexRepairResult> RepairCorruptedIndexAsync(string workspacePath, 
        IndexRepairOptions? options = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs a comprehensive health check of the Lucene index service
    /// </summary>
    Task<IndexHealthCheckResult> CheckHealthAsync(bool includeAutoRepair = false, 
        CancellationToken cancellationToken = default);
}