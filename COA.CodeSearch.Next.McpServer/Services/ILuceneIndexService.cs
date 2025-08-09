using Lucene.Net.Index;
using Lucene.Net.Search;
using COA.CodeSearch.Next.McpServer.Models;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Thread-safe Lucene index service interface for managing multiple workspace indexes
/// </summary>
public interface ILuceneIndexService : IAsyncDisposable
{
    /// <summary>
    /// Gets or creates an IndexWriter for the specified workspace
    /// </summary>
    Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets an IndexSearcher for the specified workspace
    /// </summary>
    Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commits changes for the specified workspace
    /// </summary>
    Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes the index for the specified workspace
    /// </summary>
    Task DeleteIndexAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an index exists for the specified workspace
    /// </summary>
    Task<bool> IndexExistsAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets statistics about the index for the specified workspace
    /// </summary>
    Task<IndexStatistics> GetIndexStatisticsAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all indexed workspaces
    /// </summary>
    Task<List<WorkspaceIndexInfo>> ListIndexedWorkspacesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics about a Lucene index
/// </summary>
public class IndexStatistics
{
    public long DocumentCount { get; set; }
    public long IndexSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsOptimized { get; set; }
    public int SegmentCount { get; set; }
}