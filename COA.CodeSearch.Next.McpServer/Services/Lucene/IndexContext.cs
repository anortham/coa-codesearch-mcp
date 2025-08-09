using Lucene.Net.Index;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace COA.CodeSearch.Next.McpServer.Services.Lucene;

/// <summary>
/// Manages the state and resources for a single Lucene index
/// </summary>
internal class IndexContext : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IndexWriter? _writer;
    private DirectoryReader? _reader;
    private DateTime _lastAccess;
    private bool _disposed;
    
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
    /// Get or refresh the reader for this index
    /// </summary>
    public DirectoryReader GetReader(IndexWriter writer)
    {
        if (_reader == null)
        {
            _reader = writer.GetReader(applyAllDeletes: true);
        }
        else
        {
            var newReader = DirectoryReader.OpenIfChanged(_reader);
            if (newReader != null)
            {
                _reader.Dispose();
                _reader = newReader;
            }
        }
        
        _lastAccess = DateTime.UtcNow;
        return _reader;
    }
    
    /// <summary>
    /// Check if this context should be evicted based on inactivity
    /// </summary>
    public bool ShouldEvict(TimeSpan inactivityThreshold)
    {
        return DateTime.UtcNow - _lastAccess > inactivityThreshold;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _reader?.Dispose();
        _writer?.Dispose();
        Directory?.Dispose();
        
        _disposed = true;
    }
}