using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for managing Lucene index writers with proper lock handling
/// </summary>
public interface ILuceneWriterManager : IDisposable
{
    /// <summary>
    /// Get or create an index writer with proper lock handling
    /// </summary>
    IndexWriter GetOrCreateWriter(string workspacePath, bool forceRecreate = false);
    
    /// <summary>
    /// Safely close and commit an index writer
    /// </summary>
    void CloseWriter(string workspacePath, bool commit = true);
    
    /// <summary>
    /// Clean up old or stuck indexes
    /// </summary>
    void CleanupStuckIndexes();
}