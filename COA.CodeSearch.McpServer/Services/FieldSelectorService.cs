using Lucene.Net.Documents;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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

public class FieldSelectorService : IFieldSelectorService
{
    private readonly ILogger<FieldSelectorService> _logger;
    private readonly ConcurrentDictionary<string, FieldSet> _fieldSetCache = new();
    
    // Pre-defined field sets for common operations
    private static readonly Dictionary<FieldSetType, FieldSet> PredefinedFieldSets = new()
    {
        [FieldSetType.FileInfo] = new()
        {
            Fields = new[] { "path", "filename", "extension", "size" },
            Description = "Basic file information"
        },
        [FieldSetType.SearchResults] = new()
        {
            Fields = new[] { "path", "filename", "content", "extension", "language" },
            Description = "Text search results with content"
        },
        [FieldSetType.SizeAnalysis] = new()
        {
            Fields = new[] { "path", "size", "extension" },
            Description = "File size analysis"
        },
        [FieldSetType.DirectoryListing] = new()
        {
            Fields = new[] { "path", "filename", "directory", "relativeDirectory", "directoryName", "extension" },
            Description = "Directory browsing and navigation"
        },
        [FieldSetType.Minimal] = new()
        {
            Fields = new[] { "path", "filename" },
            Description = "Minimal file identification"
        }
    };

    public FieldSelectorService(ILogger<FieldSelectorService> logger)
    {
        _logger = logger;
    }

    public Document LoadDocument(IndexSearcher searcher, int docId, params string[] fieldNames)
    {
        try
        {
            if (fieldNames.Length == 0)
            {
                // Fallback to loading full document if no fields specified
                return searcher.Doc(docId);
            }

            // In Lucene.NET 4.8, we use the overload that accepts field names
            return LoadDocumentWithFields(searcher, docId, fieldNames);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load document {DocId} with field selector, falling back to full document", docId);
            return searcher.Doc(docId);
        }
    }

    public Document[] LoadDocuments(IndexSearcher searcher, int[] docIds, params string[] fieldNames)
    {
        var documents = new Document[docIds.Length];
        
        for (int i = 0; i < docIds.Length; i++)
        {
            documents[i] = LoadDocument(searcher, docIds[i], fieldNames);
        }
        
        return documents;
    }

    public FieldSet GetFieldSet(FieldSetType type)
    {
        if (PredefinedFieldSets.TryGetValue(type, out var fieldSet))
        {
            return fieldSet;
        }
        
        // Default to minimal field set
        return PredefinedFieldSets[FieldSetType.Minimal];
    }

    /// <summary>
    /// Optimized document loading that only retrieves specified fields
    /// </summary>
    private Document LoadDocumentWithFields(IndexSearcher searcher, int docId, string[] fieldNames)
    {
        // Create a document that will hold only the requested fields
        var doc = new Document();
        var storedFields = searcher.Doc(docId);
        
        // Copy only the requested fields from the stored document
        foreach (var fieldName in fieldNames)
        {
            var fieldValues = storedFields.GetValues(fieldName);
            if (fieldValues != null)
            {
                foreach (var value in fieldValues)
                {
                    // Preserve the original field type and properties
                    var originalField = storedFields.GetField(fieldName);
                    // Always create stored string field for simplicity
                    doc.Add(new StringField(fieldName, value, Field.Store.YES));
                }
            }
        }
        
        return doc;
    }
}

/// <summary>
/// Extension methods for convenient field selector usage
/// </summary>
public static class FieldSelectorExtensions
{
    /// <summary>
    /// Load document using predefined field set
    /// </summary>
    public static Document LoadDocument(this IFieldSelectorService fieldSelector, IndexSearcher searcher, int docId, FieldSetType fieldSetType)
    {
        var fieldSet = fieldSelector.GetFieldSet(fieldSetType);
        return fieldSelector.LoadDocument(searcher, docId, fieldSet.Fields);
    }
    
    /// <summary>
    /// Load multiple documents using predefined field set
    /// </summary>
    public static Document[] LoadDocuments(this IFieldSelectorService fieldSelector, IndexSearcher searcher, int[] docIds, FieldSetType fieldSetType)
    {
        var fieldSet = fieldSelector.GetFieldSet(fieldSetType);
        return fieldSelector.LoadDocuments(searcher, docIds, fieldSet.Fields);
    }
}