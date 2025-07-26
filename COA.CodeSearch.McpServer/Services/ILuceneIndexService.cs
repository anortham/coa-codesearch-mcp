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
}