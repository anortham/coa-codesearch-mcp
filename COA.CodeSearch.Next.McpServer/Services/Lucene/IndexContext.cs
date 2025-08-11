using Lucene.Net.Index;
using Lucene.Net.Search;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace COA.CodeSearch.Next.McpServer.Services.Lucene;

/// <summary>
/// Manages the state and resources for a single Lucene index with optimized NRT search support
/// </summary>
internal class IndexContext : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _readerLock = new object();
    private IndexWriter? _writer;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private DateTime _lastAccess;
    private DateTime _lastReaderUpdate;
    private bool _disposed;
    private long _lastCommitGeneration;
    
    // Configuration for reader refresh policy
    private readonly TimeSpan _maxReaderAge = TimeSpan.FromSeconds(30);
    private readonly bool _enableAutoRefresh = true;
    
    public string WorkspacePath { get; }
    public string WorkspaceHash { get; }
    public string IndexPath { get; }
    public LuceneDirectory Directory { get; }
    
    public IndexWriter? Writer
    {
        get => _writer;
        set
        {
            _writer = value;
            _lastAccess = DateTime.UtcNow;
        }
    }
    
    public DirectoryReader? Reader
    {
        get => _reader;
        set
        {
            _reader = value;
            _lastAccess = DateTime.UtcNow;
        }
    }
    
    public DateTime LastAccess => _lastAccess;
    
    public SemaphoreSlim Lock => _lock;
    
    public IndexContext(string workspacePath, string workspaceHash, string indexPath, LuceneDirectory directory)
    {
        WorkspacePath = workspacePath;
        WorkspaceHash = workspaceHash;
        IndexPath = indexPath;
        Directory = directory;
        _lastAccess = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Get or refresh the reader and searcher for this index with optimized NRT support
    /// </summary>
    public IndexSearcher GetSearcher(IndexWriter writer)
    {
        lock (_readerLock)
        {
            var now = DateTime.UtcNow;
            var shouldRefresh = false;
            
            // Initial creation
            if (_reader == null)
            {
                _reader = writer.GetReader(applyAllDeletes: true);
                _searcher = new IndexSearcher(_reader);
                _lastReaderUpdate = now;
                _lastCommitGeneration = writer.CommitData?.Generation ?? 0;
                shouldRefresh = false;
            }
            else
            {
                // Check if we should refresh based on age or commits
                var currentGeneration = writer.CommitData?.Generation ?? 0;
                var isStale = _enableAutoRefresh && 
                             (now - _lastReaderUpdate > _maxReaderAge || 
                              currentGeneration > _lastCommitGeneration);
                
                if (isStale)
                {
                    shouldRefresh = true;
                }
            }
            
            // Refresh if needed
            if (shouldRefresh)
            {
                var newReader = DirectoryReader.OpenIfChanged(_reader);
                if (newReader != null)
                {
                    // Dispose old resources
                    _reader.Dispose();
                    
                    // Update to new resources
                    _reader = newReader;
                    _searcher = new IndexSearcher(_reader);
                    _lastReaderUpdate = now;
                    _lastCommitGeneration = writer.CommitData?.Generation ?? 0;
                }
            }
            
            _lastAccess = now;
            return _searcher ?? new IndexSearcher(_reader!);
        }
    }
    
    /// <summary>
    /// Invalidate the cached reader and searcher after a commit to ensure NRT visibility
    /// </summary>
    public void InvalidateReader()
    {
        lock (_readerLock)
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
            _searcher = null;
            _lastReaderUpdate = DateTime.MinValue; // Force refresh on next access
        }
    }
    
    /// <summary>
    /// Force a reader refresh regardless of age or generation
    /// </summary>
    public void ForceReaderRefresh()
    {
        lock (_readerLock)
        {
            if (_reader != null && _writer != null)
            {
                var newReader = DirectoryReader.OpenIfChanged(_reader);
                if (newReader != null)
                {
                    _reader.Dispose();
                    _reader = newReader;
                    _searcher = new IndexSearcher(_reader);
                    _lastReaderUpdate = DateTime.UtcNow;
                    _lastCommitGeneration = _writer.CommitData?.Generation ?? 0;
                }
            }
        }
    }
    
    /// <summary>
    /// Check if this context should be evicted based on inactivity
    /// </summary>
    public bool ShouldEvict(TimeSpan inactivityThreshold)
    {
        return DateTime.UtcNow - _lastAccess > inactivityThreshold;
    }
    
    /// <summary>
    /// Get reader statistics for diagnostics
    /// </summary>
    public ReaderStats GetReaderStats()
    {
        lock (_readerLock)
        {
            return new ReaderStats
            {
                HasReader = _reader != null,
                LastUpdate = _lastReaderUpdate,
                Generation = _lastCommitGeneration,
                Age = DateTime.UtcNow - _lastReaderUpdate
            };
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_readerLock)
        {
            _reader?.Dispose();
            _searcher = null;
        }
        
        _writer?.Dispose();
        Directory?.Dispose();
        _lock?.Dispose();
        
        _disposed = true;
    }
}

/// <summary>
/// Statistics about the current reader state
/// </summary>
public class ReaderStats
{
    public bool HasReader { get; set; }
    public DateTime LastUpdate { get; set; }
    public long Generation { get; set; }
    public TimeSpan Age { get; set; }
}
}