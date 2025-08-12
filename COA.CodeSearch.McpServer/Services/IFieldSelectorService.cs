using Lucene.Net.Documents;
using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for efficient field retrieval using Lucene field selectors
/// Reduces memory usage and improves performance by only loading required fields
/// </summary>
public interface IFieldSelectorService
{
    /// <summary>
    /// Load document with only the specified fields
    /// </summary>
    Document LoadDocument(IndexSearcher searcher, int docId, params string[] fieldNames);
    
    /// <summary>
    /// Load documents in batch with only the specified fields
    /// </summary>
    Document[] LoadDocuments(IndexSearcher searcher, int[] docIds, params string[] fieldNames);
    
    /// <summary>
    /// Get commonly used field sets for different operations
    /// </summary>
    FieldSet GetFieldSet(FieldSetType type);
}

public enum FieldSetType
{
    FileInfo,      // path, filename, directory, extension
    SearchResults, // path, filename, content (snippet), extension, score
    SizeAnalysis,  // path, size, extension
    DirectoryListing, // path, filename, directory, relativeDirectory, directoryName, extension
    Minimal        // just path and filename
}

public class FieldSet
{
    public string[] Fields { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = "";
}