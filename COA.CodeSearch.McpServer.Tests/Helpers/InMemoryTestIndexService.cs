using System.Collections.Concurrent;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// In-memory test implementation of ILuceneIndexService for unit testing
/// </summary>
public class InMemoryTestIndexService : ILuceneIndexService, IDisposable
{
    private readonly ConcurrentDictionary<string, InMemoryIndex> _indexes = new();
    private readonly StandardAnalyzer _analyzer = new(LuceneVersion.LUCENE_48);
    
    private class InMemoryIndex
    {
        public List<Document> Documents { get; } = new();
        public object Lock { get; } = new();
    }
    
    public Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var index = _indexes.GetOrAdd(workspacePath, _ => new InMemoryIndex());
        
        // Create a RAM-based writer that we can actually use
        var directory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer);
        var writer = new TestableIndexWriter(directory, config, index);
        
        return Task.FromResult<IndexWriter>(writer);
    }
    
    public Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var index = _indexes.GetOrAdd(workspacePath, _ => new InMemoryIndex());
        
        // Create a searcher that searches our in-memory documents
        var directory = new RAMDirectory();
        var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer));
        
        // Add all documents to the RAM directory
        lock (index.Lock)
        {
            foreach (var doc in index.Documents)
            {
                writer.AddDocument(doc);
            }
        }
        
        writer.Commit();
        var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        
        return Task.FromResult(searcher);
    }
    
    public Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // No-op for in-memory implementation
        return Task.CompletedTask;
    }
    
    public Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // No-op for in-memory implementation
        return Task.CompletedTask;
    }
    
    public Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        if (_indexes.TryGetValue(workspacePath, out var index))
        {
            lock (index.Lock)
            {
                index.Documents.Clear();
            }
        }
        return Task.CompletedTask;
    }
    
    public string GetPhysicalIndexPath(string workspacePath)
    {
        return $"memory://{workspacePath}";
    }
    
    public Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Analyzer>(_analyzer);
    }
    
    public Dictionary<string, string> GetAllIndexMappings()
    {
        return _indexes.ToDictionary(kvp => kvp.Key, kvp => GetPhysicalIndexPath(kvp.Key));
    }
    
    public void Dispose()
    {
        _analyzer?.Dispose();
        _indexes.Clear();
    }
    
    private class TestableIndexWriter : IndexWriter
    {
        private readonly InMemoryIndex _index;
        
        public TestableIndexWriter(Lucene.Net.Store.Directory d, IndexWriterConfig conf, InMemoryIndex index) 
            : base(d, conf)
        {
            _index = index;
        }
        
        public override void AddDocument(IEnumerable<IIndexableField> doc)
        {
            var document = new Document();
            foreach (var field in doc)
            {
                document.Add(field);
            }
            
            lock (_index.Lock)
            {
                _index.Documents.Add(document);
            }
            
            // Also add to the real index for searching
            base.AddDocument(doc);
        }
        
        public override void DeleteDocuments(Term term)
        {
            if (term.Field == "id")
            {
                lock (_index.Lock)
                {
                    _index.Documents.RemoveAll(d => d.Get("id") == term.Text);
                }
            }
            base.DeleteDocuments(term);
        }
    }
}