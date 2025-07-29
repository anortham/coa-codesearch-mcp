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
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// In-memory test implementation of ILuceneIndexService for unit testing
/// </summary>
public class InMemoryTestIndexService : ILuceneIndexService
{
    private readonly ConcurrentDictionary<string, InMemoryIndex> _indexes = new();
    private readonly MemoryAnalyzer _analyzer;
    
    public InMemoryTestIndexService()
    {
        // TEMPORARY: Use StandardAnalyzer to test if MemoryAnalyzer is the issue
        // var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        // var logger = loggerFactory.CreateLogger<MemoryAnalyzer>();
        // _analyzer = new MemoryAnalyzer(logger);
        
        // For now, use a simple StandardAnalyzer that should work
        _analyzer = null!; // Will be set below
    }
    
    private Analyzer GetAnalyzer()
    {
        return new StandardAnalyzer(LuceneVersion.LUCENE_48);
    }
    
    private class InMemoryIndex
    {
        public RAMDirectory Directory { get; } = new();
        public IndexWriter? Writer { get; set; }
        public DirectoryReader? Reader { get; set; }
        public IndexSearcher? Searcher { get; set; }
        public object Lock { get; } = new();
        
        public void RefreshSearcher()
        {
            try
            {
                if (Writer != null)
                {
                    // Force writer to commit and flush any pending changes
                    Writer.Commit();
                    Writer.Flush(true, true);
                    
                    // Use DirectoryReader.OpenIfChanged to get latest changes
                    if (Reader != null)
                    {
                        var newReader = DirectoryReader.OpenIfChanged(Reader);
                        if (newReader != null)
                        {
                            Reader.Dispose();
                            Reader = newReader;
                            Searcher = new IndexSearcher(Reader);
                            System.Diagnostics.Debug.WriteLine($"RefreshSearcher: Updated reader, index now has {Reader.NumDocs} documents");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"RefreshSearcher: No changes, index still has {Reader.NumDocs} documents");
                        }
                    }
                    else
                    {
                        // First time - create new reader
                        Reader = DirectoryReader.Open(Directory);
                        Searcher = new IndexSearcher(Reader);
                        System.Diagnostics.Debug.WriteLine($"RefreshSearcher: Created new reader, index has {Reader.NumDocs} documents");
                    }
                }
                else if (Reader == null)
                {
                    // No writer but no reader either - try to open directory directly
                    try
                    {
                        Reader = DirectoryReader.Open(Directory);
                        Searcher = new IndexSearcher(Reader);
                        System.Diagnostics.Debug.WriteLine($"RefreshSearcher (no writer): Index has {Reader.NumDocs} documents");
                    }
                    catch (IndexNotFoundException)
                    {
                        // Directory doesn't exist yet - this is normal for empty tests
                        System.Diagnostics.Debug.WriteLine("RefreshSearcher: IndexNotFoundException - no index yet");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshSearcher error: {ex.Message}");
                // Ignore refresh errors in tests
            }
        }
        
        public void Dispose()
        {
            try
            {
                // IndexSearcher doesn't implement IDisposable in Lucene.NET
                Searcher = null;
                Reader?.Dispose();
                Writer?.Dispose();
                Directory?.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
    }
    
    public Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var index = _indexes.GetOrAdd(workspacePath, _ => new InMemoryIndex());
        
        lock (index.Lock)
        {
            if (index.Writer == null)
            {
                var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, GetAnalyzer());
                index.Writer = new IndexWriter(index.Directory, config);
            }
            
            return Task.FromResult(index.Writer);
        }
    }
    
    public Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var index = _indexes.GetOrAdd(workspacePath, _ => new InMemoryIndex());
        
        lock (index.Lock)
        {
            // Always refresh the searcher to see latest changes - critical for tests
            index.RefreshSearcher();
            
            // If still no searcher (empty index), create one
            if (index.Searcher == null)
            {
                try
                {
                    index.Reader = DirectoryReader.Open(index.Directory);
                    index.Searcher = new IndexSearcher(index.Reader);
                }
                catch (IndexNotFoundException)
                {
                    // Index doesn't exist yet, create empty writer first
                    if (index.Writer == null)
                    {
                        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, GetAnalyzer());
                        index.Writer = new IndexWriter(index.Directory, config);
                        index.Writer.Commit();
                    }
                    
                    index.Reader = DirectoryReader.Open(index.Directory);
                    index.Searcher = new IndexSearcher(index.Reader);
                }
            }
            
            return Task.FromResult(index.Searcher);
        }
    }
    
    public Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var index = _indexes.GetOrAdd(workspacePath, _ => new InMemoryIndex());
        
        lock (index.Lock)
        {
            index.Writer?.Commit();
            // Refresh searcher after commit so searches see the new data
            index.RefreshSearcher();
        }
        
        return Task.CompletedTask;
    }
    
    public Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // No-op for in-memory implementation
        return Task.CompletedTask;
    }
    
    public Task ForceMergeAsync(string workspacePath, int maxNumSegments = 1, CancellationToken cancellationToken = default)
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
                index.Dispose();
                _indexes.TryRemove(workspacePath, out _);
            }
        }
        return Task.CompletedTask;
    }
    
    public Task<string> GetPhysicalIndexPathAsync(string workspacePath)
    {
        return Task.FromResult($"memory://{workspacePath}");
    }
    
    public Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Analyzer>(GetAnalyzer());
    }
    
    public Task<Dictionary<string, string>> GetAllIndexMappingsAsync()
    {
        return Task.FromResult(_indexes.ToDictionary(kvp => kvp.Key, kvp => $"memory://{kvp.Key}"));
    }
    
    public Task DiagnoseStuckIndexesAsync()
    {
        // No-op for in-memory implementation - no file locks to diagnose
        return Task.CompletedTask;
    }
    
    public Task CleanupDuplicateIndicesAsync()
    {
        // No-op for in-memory implementation - no duplicates in memory
        return Task.CompletedTask;
    }
    
    public ValueTask DisposeAsync()
    {
        foreach (var index in _indexes.Values)
        {
            index.Dispose();
        }
        _indexes.Clear();
        _analyzer?.Dispose();
        return ValueTask.CompletedTask;
    }
    
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    
    public Task<IndexDefragmentationResult> DefragmentIndexAsync(string workspacePath, 
        IndexDefragmentationOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        // Mock implementation for testing
        var result = new IndexDefragmentationResult
        {
            StartTime = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddSeconds(1),
            Duration = TimeSpan.FromSeconds(1),
            Success = true,
            ActionTaken = DefragmentationAction.Skipped,
            Reason = "In-memory index does not require defragmentation",
            InitialFragmentationLevel = 0,
            FinalFragmentationLevel = 0,
            FragmentationReduction = 0,
            InitialSegmentCount = 1,
            FinalSegmentCount = 1,
            SegmentReduction = 0,
            InitialSizeBytes = 1024,
            FinalSizeBytes = 1024,
            SizeReductionBytes = 0
        };
        
        result.DefragmentationSteps.Add("Mock defragmentation completed");
        
        return Task.FromResult(result);
    }
    
    public Task<IndexRepairResult> RepairCorruptedIndexAsync(string workspacePath, 
        IndexRepairOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        // Mock implementation for testing
        var result = new IndexRepairResult
        {
            Success = true,
            Message = "In-memory index repair simulation completed successfully",
            RemovedSegments = 0,
            LostDocuments = 0,
            BackupPath = null,
            Exception = null,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(1)
        };
        
        return Task.FromResult(result);
    }
    
    public Task<IndexHealthCheckResult> CheckHealthAsync(bool includeAutoRepair = false, 
        CancellationToken cancellationToken = default)
    {
        // Mock implementation for testing
        var data = new Dictionary<string, object>
        {
            ["totalIndexes"] = _indexes.Count,
            ["healthyIndexes"] = _indexes.Count,
            ["unhealthyIndexes"] = 0,
            ["stuckLocks"] = 0,
            ["corruptedIndexes"] = 0,
            ["projectMemoryIndex"] = "healthy",
            ["localMemoryIndex"] = "healthy"
        };
        
        var result = new IndexHealthCheckResult(
            IndexHealthCheckResult.HealthStatus.Healthy,
            "In-memory test index is healthy",
            data);
        
        return Task.FromResult(result);
    }
    
}