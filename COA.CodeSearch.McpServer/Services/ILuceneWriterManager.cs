using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for managing Lucene index writers with proper lock handling
/// </summary>
public interface ILuceneWriterManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Get or create an index writer with proper lock handling
    /// </summary>
    Task<IndexWriter> GetOrCreateWriterAsync(string workspacePath, bool forceRecreate = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Safely close and commit an index writer
    /// </summary>
    Task CloseWriterAsync(string workspacePath, bool commit = true, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Diagnose stuck indexes and report them (does not automatically clean)
    /// </summary>
    Task DiagnoseStuckIndexesAsync();
}