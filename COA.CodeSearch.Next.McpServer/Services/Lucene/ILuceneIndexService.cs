using Lucene.Net.Documents;
using Lucene.Net.Search;

namespace COA.CodeSearch.Next.McpServer.Services.Lucene;

/// <summary>
/// Core interface for Lucene index operations
/// </summary>
public interface ILuceneIndexService
{
    /// <summary>
    /// Initialize or open an index for a workspace
    /// </summary>
    Task<IndexInitResult> InitializeIndexAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Add or update a document in the index
    /// </summary>
    Task IndexDocumentAsync(string workspacePath, Document document, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Batch index multiple documents
    /// </summary>
    Task IndexDocumentsAsync(string workspacePath, IEnumerable<Document> documents, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a document from the index
    /// </summary>
    Task DeleteDocumentAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Search the index
    /// </summary>
    Task<SearchResult> SearchAsync(string workspacePath, Query query, int maxResults = 100, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get total document count in the index
    /// </summary>
    Task<int> GetDocumentCountAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clear all documents from an index
    /// </summary>
    Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commit any pending changes to the index
    /// </summary>
    Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if an index exists for a workspace
    /// </summary>
    Task<bool> IndexExistsAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get health status of an index
    /// </summary>
    Task<IndexHealthStatus> GetHealthAsync(string workspacePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get statistics for an index
    /// </summary>
    Task<IndexStatistics> GetStatisticsAsync(string workspacePath, CancellationToken cancellationToken = default);
}