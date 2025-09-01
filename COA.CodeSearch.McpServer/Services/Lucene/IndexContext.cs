using Lucene.Net.Index;
using Lucene.Net.Search;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace COA.CodeSearch.McpServer.Services.Lucene;

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
    private long _refreshVersion; // increments each refresh

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
    /// Standard cached searcher (may reuse existing).
    /// </summary>
    public IndexSearcher GetSearcher(IndexWriter writer)
    {
        lock (_readerLock)
        {
            var now = DateTime.UtcNow;
            var shouldRefresh = false;

            if (_reader == null)
            {
                _reader = writer.GetReader(applyAllDeletes: true);
                _searcher = new IndexSearcher(_reader);
                _lastReaderUpdate = now;
                _lastCommitGeneration = writer.MaxDoc;
                _refreshVersion++;
            }
            else
            {
                var currentGeneration = writer.MaxDoc;
                var isStale = _enableAutoRefresh &&
                              (now - _lastReaderUpdate > _maxReaderAge ||
                               currentGeneration > _lastCommitGeneration);

                if (isStale)
                {
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                // Use writer-aware OpenIfChanged to ensure NRT reopen (important)
                var newReader = DirectoryReader.OpenIfChanged(_reader, writer, applyAllDeletes: true);
                if (newReader != null)
                {
                    _reader.Dispose();
                    _reader = newReader;
                    _searcher = new IndexSearcher(_reader);
                    _lastReaderUpdate = now;
                    _lastCommitGeneration = writer.MaxDoc;
                    _refreshVersion++;
                }
            }

            _lastAccess = now;
            return _searcher ?? new IndexSearcher(_reader!);
        }
    }

    /// <summary>
    /// Always reopen via writer (force fresh NRT view).
    /// </summary>
    public IndexSearcher GetFreshSearcher(IndexWriter writer)
    {
        lock (_readerLock)
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
            _reader = writer.GetReader(applyAllDeletes: true);
            _searcher = new IndexSearcher(_reader);
            _lastReaderUpdate = DateTime.UtcNow;
            _lastCommitGeneration = writer.MaxDoc;
            _refreshVersion++;
            return _searcher;
        }
    }

    public void InvalidateReader()
    {
        lock (_readerLock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
            _lastReaderUpdate = DateTime.MinValue;
        }
    }

    public void ForceReaderRefresh()
    {
        lock (_readerLock)
        {
            if (_writer == null) return;
            _reader?.Dispose();
            _reader = _writer.GetReader(applyAllDeletes: true);
            _searcher = new IndexSearcher(_reader);
            _lastReaderUpdate = DateTime.UtcNow;
            _lastCommitGeneration = _writer.MaxDoc;
            _refreshVersion++;
        }
    }

    public bool ShouldEvict(TimeSpan inactivityThreshold) =>
        DateTime.UtcNow - _lastAccess > inactivityThreshold;

    public ReaderStats GetReaderStats()
    {
        lock (_readerLock)
        {
            return new ReaderStats
            {
                HasReader = _reader != null,
                LastUpdate = _lastReaderUpdate,
                Generation = _lastCommitGeneration,
                Age = _lastReaderUpdate == DateTime.MinValue ? TimeSpan.MaxValue : DateTime.UtcNow - _lastReaderUpdate,
                Version = _refreshVersion,
                ReaderMaxDoc = _reader?.MaxDoc ?? -1,
                ReaderNumDocs = _reader?.NumDocs ?? -1
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // First, dispose the reader and searcher
        lock (_readerLock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
        }
        
        // Then, properly close the writer with commit
        if (_writer != null)
        {
            try
            {
                // Ensure all pending changes are committed
                if (_writer.HasUncommittedChanges())
                {
                    _writer.Commit();
                }
                _writer.Dispose();
            }
            catch (Exception ex)
            {
                // Log but don't throw - we're in Dispose
                System.Diagnostics.Debug.WriteLine($"Error disposing IndexWriter: {ex.Message}");
            }
            finally
            {
                _writer = null;
            }
        }
        
        // Finally, dispose the directory and lock
        Directory?.Dispose();
        _lock.Dispose();
        _disposed = true;
    }
}

public class ReaderStats
{
    public bool HasReader { get; set; }
    public DateTime LastUpdate { get; set; }
    public long Generation { get; set; }
    public TimeSpan Age { get; set; }
    public long Version { get; set; }
    public int ReaderMaxDoc { get; set; }
    public int ReaderNumDocs { get; set; }
}