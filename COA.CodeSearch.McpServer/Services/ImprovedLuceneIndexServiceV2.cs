using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Improved Lucene index service with better lock handling and thread safety
/// </summary>
public class ImprovedLuceneIndexServiceV2 : ILuceneIndexService, IImprovedLuceneIndexService
{
    private readonly ILogger<ImprovedLuceneIndexServiceV2> _logger;
    private readonly IConfiguration _configuration;
    private readonly StandardAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly TimeSpan _lockTimeout;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private volatile bool _disposed;
    
    private const string WriteLockFilename = "write.lock";
    private const string SegmentsFilename = "segments.gen";
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    private class IndexContext
    {
        public FSDirectory Directory { get; set; } = null!;
        public IndexWriter? Writer { get; set; }
        public DirectoryReader? Reader { get; set; }
        public DateTime LastAccessed { get; set; }
        public string Path { get; set; } = string.Empty;
        public readonly SemaphoreSlim Lock = new(1, 1);
    }
    
    public ImprovedLuceneIndexServiceV2(ILogger<ImprovedLuceneIndexServiceV2> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _analyzer = new StandardAnalyzer(Version);
        
        // Default 15 minute timeout for stuck locks (same as intranet)
        _lockTimeout = TimeSpan.FromMinutes(configuration.GetValue<int>("Lucene:LockTimeoutMinutes", 15));
    }
    
    /// <summary>
    /// Get or create an index writer with proper lock handling
    /// </summary>
    public IndexWriter GetOrCreateWriter(string workspacePath, bool forceRecreate = false)
    {
        ThrowIfDisposed();
        
        var indexPath = GetIndexPath(workspacePath);
        
        // Use double-check locking pattern for thread safety
        if (!_indexes.TryGetValue(indexPath, out var context))
        {
            _writerLock.Wait();
            try
            {
                // Check again inside the lock
                if (!_indexes.TryGetValue(indexPath, out context))
                {
                    // Check for stuck locks first
                    if (IsIndexLocked(indexPath, out var isStuck))
                    {
                        if (isStuck)
                        {
                            _logger.LogWarning("Index at {Path} has stuck lock. Clearing index for rebuild.", indexPath);
                            ClearIndex(indexPath);
                        }
                        else
                        {
                            // Lock exists but is recent - another process may be using it
                            throw new InvalidOperationException($"Index at {indexPath} is currently locked by another process");
                        }
                    }
                    
                    context = CreateIndexContext(indexPath, forceRecreate);
                    _indexes.TryAdd(indexPath, context);
                }
            }
            finally
            {
                _writerLock.Release();
            }
        }
        
        // Use context-specific lock for writer operations
        context.Lock.Wait();
        try
        {
            // Ensure writer is still valid
            if (context.Writer == null)
            {
                context.Writer = CreateWriter(context.Directory, forceRecreate);
            }
            
            context.LastAccessed = DateTime.UtcNow;
            return context.Writer;
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    /// <summary>
    /// Safely close and commit an index writer
    /// </summary>
    public void CloseWriter(string workspacePath, bool commit = true)
    {
        var indexPath = GetIndexPath(workspacePath);
        
        if (_indexes.TryGetValue(indexPath, out var context))
        {
            context.Lock.Wait();
            try
            {
                if (context.Writer != null)
                {
                    if (commit)
                    {
                        try
                        {
                            context.Writer.Commit();
                            _logger.LogInformation("Committed changes to index at {Path}", indexPath);
                        }
                        catch (ObjectDisposedException)
                        {
                            // Writer already disposed
                        }
                    }
                    
                    context.Writer.Dispose();
                    context.Writer = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing writer for {Path}", indexPath);
                
                // If disposal fails, try to clean up the lock
                var lockPath = Path.Combine(indexPath, WriteLockFilename);
                if (File.Exists(lockPath))
                {
                    try
                    {
                        File.Delete(lockPath);
                        _logger.LogInformation("Cleaned up lock file at {Path}", lockPath);
                    }
                    catch { /* Best effort */ }
                }
            }
            finally
            {
                context.Lock.Release();
            }
        }
    }
    
    /// <summary>
    /// Check if an index is locked and if the lock is stuck
    /// </summary>
    private bool IsIndexLocked(string indexPath, out bool isStuck)
    {
        isStuck = false;
        var lockPath = Path.Combine(indexPath, WriteLockFilename);
        
        if (!File.Exists(lockPath))
            return false;
        
        var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
        isStuck = lockAge > _lockTimeout;
        
        return true;
    }
    
    /// <summary>
    /// Clear an index directory (nuclear option for stuck locks)
    /// </summary>
    private void ClearIndex(string indexPath)
    {
        try
        {
            if (System.IO.Directory.Exists(indexPath))
            {
                System.IO.Directory.Delete(indexPath, recursive: true);
                _logger.LogInformation("Cleared index directory at {Path}", indexPath);
            }
            
            System.IO.Directory.CreateDirectory(indexPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear index at {Path}", indexPath);
            throw;
        }
    }
    
    private IndexContext CreateIndexContext(string indexPath, bool forceRecreate)
    {
        System.IO.Directory.CreateDirectory(indexPath);
        
        var directory = FSDirectory.Open(indexPath);
        var writer = CreateWriter(directory, forceRecreate);
        
        return new IndexContext
        {
            Directory = directory,
            Writer = writer,
            Path = indexPath,
            LastAccessed = DateTime.UtcNow
        };
    }
    
    private IndexWriter CreateWriter(FSDirectory directory, bool forceRecreate)
    {
        var config = new IndexWriterConfig(Version, _analyzer)
        {
            // Use CREATE_OR_APPEND by default, CREATE if forcing recreate
            OpenMode = forceRecreate ? OpenMode.CREATE : OpenMode.CREATE_OR_APPEND
        };
        
        return new IndexWriter(directory, config);
    }
    
    private string GetIndexPath(string workspacePath)
    {
        var basePath = _configuration["Lucene:IndexBasePath"] ?? ".codesearch";
        
        // If the workspace path already starts with our base path, it's a memory path - use it directly
        if (workspacePath.StartsWith(basePath))
        {
            return workspacePath;
        }
        
        // Otherwise it's a code search index - put it under the index subdirectory
        return Path.Combine(basePath, "index", 
            workspacePath.Replace(':', '_').Replace('\\', '_').Replace('/', '_'));
    }
    
    /// <summary>
    /// Clean up old or stuck indexes
    /// </summary>
    public void CleanupStuckIndexes()
    {
        var basePath = _configuration["Lucene:IndexBasePath"] ?? ".codesearch";
        var indexRoot = Path.Combine(basePath, "index");
        
        if (!System.IO.Directory.Exists(indexRoot))
            return;
        
        foreach (var indexDir in System.IO.Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WriteLockFilename);
            
            if (File.Exists(lockPath))
            {
                var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                
                if (lockAge > _lockTimeout)
                {
                    _logger.LogWarning("Found stuck lock at {Path}, age: {Age}", lockPath, lockAge);
                    
                    try
                    {
                        File.Delete(lockPath);
                        _logger.LogInformation("Removed stuck lock at {Path}", lockPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove stuck lock at {Path}", lockPath);
                    }
                }
            }
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ImprovedLuceneIndexServiceV2));
        }
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // Close all writers and readers
        foreach (var kvp in _indexes)
        {
            var context = kvp.Value;
            context.Lock.Wait();
            try
            {
                context.Writer?.Dispose();
                context.Reader?.Dispose();
                context.Directory?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing index context for {Path}", context.Path);
            }
            finally
            {
                context.Lock.Release();
                context.Lock.Dispose();
            }
        }
        
        _indexes.Clear();
        _analyzer?.Dispose();
        _writerLock?.Dispose();
    }
    
    #region ILuceneIndexService Implementation
    
    /// <summary>
    /// Get index writer asynchronously (wraps synchronous GetOrCreateWriter)
    /// </summary>
    public Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetOrCreateWriter(workspacePath), cancellationToken);
    }
    
    /// <summary>
    /// Get index searcher asynchronously
    /// </summary>
    public async Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = GetIndexPath(workspacePath);
        
        // Get or create context
        if (!_indexes.TryGetValue(indexPath, out var context))
        {
            await _writerLock.WaitAsync(cancellationToken);
            try
            {
                if (!_indexes.TryGetValue(indexPath, out context))
                {
                    // Ensure index exists by creating an empty one if needed
                    context = CreateIndexContext(indexPath, false);
                    context.Writer!.Commit(); // Create initial segments
                    _indexes.TryAdd(indexPath, context);
                }
            }
            finally
            {
                _writerLock.Release();
            }
        }
        
        await context.Lock.WaitAsync(cancellationToken);
        try
        {
            // Check if we need to refresh the reader
            if (context.Reader == null || !context.Reader.IsCurrent())
            {
                context.Reader?.Dispose();
                
                // If writer exists, get reader from it; otherwise open directory reader
                if (context.Writer != null)
                {
                    try
                    {
                        context.Reader = context.Writer.GetReader(applyAllDeletes: true);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Writer disposed, fall through to open directory reader
                        context.Writer = null;
                    }
                }
                else
                {
                    context.Reader = DirectoryReader.Open(context.Directory);
                }
            }
            
            context.LastAccessed = DateTime.UtcNow;
            return new IndexSearcher(context.Reader);
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    /// <summary>
    /// Get analyzer asynchronously
    /// </summary>
    public Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Create a new analyzer instance to avoid sharing issues
        return Task.FromResult<Analyzer>(new StandardAnalyzer(Version));
    }
    
    /// <summary>
    /// Commit index changes asynchronously
    /// </summary>
    public async Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = GetIndexPath(workspacePath);
        
        if (_indexes.TryGetValue(indexPath, out var context))
        {
            await context.Lock.WaitAsync(cancellationToken);
            try
            {
                if (context.Writer != null)
                {
                    try
                    {
                        context.Writer.Commit();
                        _logger.LogInformation("Committed changes to index at {Path}", indexPath);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Writer already disposed
                    }
                }
            }
            finally
            {
                context.Lock.Release();
            }
        }
    }
    
    /// <summary>
    /// Optimize index asynchronously (merge segments for better performance)
    /// </summary>
    public async Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var writer = await GetIndexWriterAsync(workspacePath, cancellationToken);
        
        // ForceMerge is the modern equivalent of Optimize
        writer.ForceMerge(1); // Merge down to a single segment
        await CommitAsync(workspacePath, cancellationToken);
        
        _logger.LogInformation("Optimized index at {Path}", GetIndexPath(workspacePath));
    }
    
    /// <summary>
    /// Clear index asynchronously
    /// </summary>
    public async Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = GetIndexPath(workspacePath);
        
        // Remove from dictionary and dispose
        if (_indexes.TryRemove(indexPath, out var context))
        {
            await context.Lock.WaitAsync(cancellationToken);
            try
            {
                context.Writer?.Dispose();
                context.Reader?.Dispose();
                context.Directory?.Dispose();
            }
            finally
            {
                context.Lock.Release();
                context.Lock.Dispose();
            }
        }
        
        // Clear the index directory
        await Task.Run(() => ClearIndex(indexPath), cancellationToken);
        
        _logger.LogInformation("Cleared index for workspace {WorkspacePath}", workspacePath);
    }
    
    #endregion
}