using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

public interface ILuceneIndexService : IDisposable
{
    Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default);
    Dictionary<string, string> GetAllIndexMappings();
    
    /// <summary>
    /// Get the physical index path for a workspace - single source of truth for path resolution
    /// </summary>
    string GetPhysicalIndexPath(string workspacePath);
}