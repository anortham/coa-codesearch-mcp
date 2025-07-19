using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Improved Lucene index service with better lock handling based on CoA Intranet patterns
/// </summary>
public class ImprovedLuceneIndexService : IDisposable
{
    private readonly ILogger<ImprovedLuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly StandardAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly TimeSpan _lockTimeout;
    
    private const string WriteLockFilename = "write.lock";
    private const string SegmentsFilename = "segments.gen";
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    private class IndexContext
    {
        public FSDirectory Directory { get; set; } = null!;
        public IndexWriter? Writer { get; set; }
        public DateTime LastAccessed { get; set; }
        public string Path { get; set; } = string.Empty;
    }
    
    public ImprovedLuceneIndexService(ILogger<ImprovedLuceneIndexService> logger, IConfiguration configuration)
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
        var indexPath = GetIndexPath(workspacePath);
        
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
        
        if (!_indexes.TryGetValue(indexPath, out var context))
        {
            context = CreateIndexContext(indexPath, forceRecreate);
            _indexes.TryAdd(indexPath, context);
        }
        
        // Ensure writer is still valid
        if (context.Writer == null)
        {
            context.Writer = CreateWriter(context.Directory, forceRecreate);
        }
        
        context.LastAccessed = DateTime.UtcNow;
        return context.Writer;
    }
    
    /// <summary>
    /// Safely close and commit an index writer
    /// </summary>
    public void CloseWriter(string workspacePath, bool commit = true)
    {
        var indexPath = GetIndexPath(workspacePath);
        
        if (_indexes.TryRemove(indexPath, out var context))
        {
            try
            {
                if (context.Writer != null)
                {
                    if (commit)
                    {
                        context.Writer.Commit();
                        _logger.LogInformation("Committed changes to index at {Path}", indexPath);
                    }
                    
                    context.Writer.Dispose();
                }
                
                context.Directory?.Dispose();
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
    
    public void Dispose()
    {
        foreach (var context in _indexes.Values)
        {
            try
            {
                context.Writer?.Dispose();
                context.Directory?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing index context for {Path}", context.Path);
            }
        }
        
        _indexes.Clear();
        _analyzer?.Dispose();
    }
}