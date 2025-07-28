using COA.CodeSearch.McpServer.Infrastructure;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Index.Extensions;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Options for index repair operations
/// </summary>
public class IndexRepairOptions
{
    public bool CreateBackup { get; set; } = true;
    public bool RemoveBadSegments { get; set; } = true;
    public bool ValidateAfterRepair { get; set; } = true;
    public string? BackupPath { get; set; }
}

/// <summary>
/// Result of an index repair operation
/// </summary>
public class IndexRepairResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RemovedSegments { get; set; }
    public int LostDocuments { get; set; }
    public string? BackupPath { get; set; }
    public Exception? Exception { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Simple health check result for index service
/// </summary>
public class IndexHealthCheckResult
{
    public enum HealthStatus { Healthy, Degraded, Unhealthy }
    
    public HealthStatus Status { get; }
    public string Description { get; }
    public Dictionary<string, object> Data { get; }
    public Exception? Exception { get; }
    
    public IndexHealthCheckResult(HealthStatus status, string description, Dictionary<string, object>? data = null, Exception? exception = null)
    {
        Status = status;
        Description = description;
        Data = data ?? new Dictionary<string, object>();
        Exception = exception;
    }
}

/// <summary>
/// Thread-safe Lucene index service with proper async/await patterns and deadlock prevention.
/// 
/// Design Decisions:
/// 1. Long-lived writers: We maintain writers for performance in an interactive MCP server context.
///    The docs recommend short-lived writers for batch operations, but our use case benefits from
///    keeping writers open to avoid constant open/close overhead.
/// 2. AsyncLock with timeouts: All locks use AsyncLock with enforced timeouts to prevent deadlocks.
///    No nested synchronous locks. Consistent lock ordering: _writerLock → context.Lock.
/// 3. No automatic recovery: Stuck locks are treated as critical bugs requiring manual intervention.
///    We diagnose and report but never automatically delete write.lock files.
/// 4. IAsyncDisposable: Proper async disposal with timeouts ensures clean shutdown.
/// 
/// Concurrency Model:
/// - Multiple readers via DirectoryReader (thread-safe snapshots)
/// - Single writer per index via IndexWriter (Lucene enforced)
/// - AsyncLock prevents race conditions during writer creation
/// - Memory indexes use CREATE_OR_APPEND to preserve data across sessions
/// 
/// Philosophy: "If you have stuck locks, you have a disposal bug that needs fixing"
/// </summary>
public class LuceneIndexService : ILuceneIndexService, ILuceneWriterManager, IAsyncDisposable
{
    // Constants
    private const int HASH_PREFIX_LENGTH = 8;
    private const int LOCK_TIMEOUT_MINUTES = 1;
    
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPathResolutionService _pathResolution;
    private readonly StandardAnalyzer _standardAnalyzer;
    private readonly MemoryAnalyzer _memoryAnalyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly TimeSpan _lockTimeout;
    private readonly AsyncLock _writerLock = new("writer-lock");  // Using AsyncLock to enforce timeout usage
    
    // Idle cleanup configuration (timer removed - cleanup is done on-demand)
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(15); // Indexes idle for 15 minutes are evicted
    private readonly int _maxIndexCount = 100; // Maximum number of indexes to keep in memory
    private volatile bool _disposed;
    
    private const string WriteLockFilename = "write.lock";
    private const string SegmentsFilename = "segments.gen";
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    private readonly ConcurrentDictionary<string, IndexMetadata> _metadataCache = new();
    private readonly AsyncLock _metadataLock = new("metadata-lock");  // Using AsyncLock to prevent deadlocks
    
    private class IndexMetadata
    {
        public Dictionary<string, IndexEntry> Indexes { get; set; } = new();
    }
    
    private class IndexEntry
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string HashPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
    }
    
    private class IndexContext : IDisposable
    {
        public FSDirectory Directory { get; set; } = null!;
        public IndexWriter? Writer { get; set; }
        public DirectoryReader? Reader { get; set; }
        public IndexSearcher? CachedSearcher { get; set; }
        public DateTime SearcherLastRefresh { get; set; }
        public DateTime LastAccessed { get; set; }
        public DateTime LastCommitted { get; set; }
        public string Path { get; set; } = string.Empty;
        public readonly AsyncLock Lock;  // AsyncLock for thread-safe index operations
        public int PendingChanges { get; set; }

        public IndexContext(string path)
        {
            Path = path;
            Lock = new AsyncLock($"index-{System.IO.Path.GetFileName(path)}");  // Named lock for debugging
        }

        public void Dispose()
        {
            // IndexSearcher doesn't implement IDisposable in Lucene.NET 4.8
            // It's tied to the DirectoryReader lifecycle
            CachedSearcher = null;
            Reader?.Dispose();
            Writer?.Dispose();
            Directory?.Dispose();
        }
    }
    
    public LuceneIndexService(ILogger<LuceneIndexService> logger, IConfiguration configuration, IPathResolutionService pathResolution, MemoryAnalyzer memoryAnalyzer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _standardAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _memoryAnalyzer = memoryAnalyzer ?? throw new ArgumentNullException(nameof(memoryAnalyzer));
        
        // Default 15 minute timeout for stuck locks (same as intranet)
        _lockTimeout = TimeSpan.FromMinutes(configuration.GetValue<int>("Lucene:LockTimeoutMinutes", 15));
        
        // Clean up any memory entries from metadata on startup
        _ = Task.Run(async () => await CleanupMemoryEntriesFromMetadataAsync().ConfigureAwait(false));
    }
    
    /// <summary>
    /// Select appropriate analyzer based on workspace or index path
    /// Memory paths use MemoryAnalyzer (with synonyms), code paths use StandardAnalyzer
    /// </summary>
    private Analyzer GetAnalyzerForWorkspace(string pathToCheck)
    {
        try
        {
            // Check if this is a memory path (works for both workspace paths and index paths since they're the same for memory)
            var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
            var localMemoryPath = _pathResolution.GetLocalMemoryPath();
            
            if (pathToCheck.Equals(projectMemoryPath, StringComparison.OrdinalIgnoreCase) ||
                pathToCheck.Equals(localMemoryPath, StringComparison.OrdinalIgnoreCase) ||
                _pathResolution.IsProtectedPath(pathToCheck))
            {
                _logger.LogDebug("Using MemoryAnalyzer for memory path: {Path}", pathToCheck);
                return _memoryAnalyzer;
            }
            
            _logger.LogDebug("Using StandardAnalyzer for code path: {Path}", pathToCheck);
            return _standardAnalyzer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining analyzer for path {Path}, defaulting to StandardAnalyzer", pathToCheck);
            return _standardAnalyzer;
        }
    }
    
    /// <summary>
    /// Cleanup idle indexes to prevent memory leaks
    /// </summary>
    private async Task CleanupIdleIndexesAsync()
    {
        if (_disposed) return;
        
        try
        {
            var now = DateTime.UtcNow;
            var indexesToRemove = new List<string>();
            
            // First pass: identify idle indexes
            foreach (var kvp in _indexes)
            {
                var context = kvp.Value;
                var idleTime = now - context.LastAccessed;
                
                if (idleTime > _idleTimeout)
                {
                    indexesToRemove.Add(kvp.Key);
                    _logger.LogDebug("Index {Path} has been idle for {IdleTime}, marking for removal", 
                        kvp.Key, idleTime);
                }
            }
            
            // Also check if we need to evict based on count (LRU)
            if (_indexes.Count > _maxIndexCount)
            {
                var lruCandidates = _indexes
                    .OrderBy(kvp => kvp.Value.LastAccessed)
                    .Take(_indexes.Count - _maxIndexCount)
                    .Select(kvp => kvp.Key)
                    .Where(key => !indexesToRemove.Contains(key));
                
                indexesToRemove.AddRange(lruCandidates);
                _logger.LogDebug("Evicting {Count} least recently used indexes to stay under limit", 
                    lruCandidates.Count());
            }
            
            // Second pass: remove and dispose idle indexes
            foreach (var indexPath in indexesToRemove)
            {
                if (_indexes.TryRemove(indexPath, out var context))
                {
                    try
                    {
                        await DisposeContextAsync(context, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        _logger.LogInformation("Removed idle index at {Path}", indexPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing idle index at {Path}", indexPath);
                    }
                }
            }
            
            if (indexesToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} idle indexes. Active indexes: {ActiveCount}", 
                    indexesToRemove.Count, _indexes.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during idle index cleanup");
        }
    }
    
    /// <summary>
    /// Get or create an index writer with proper lock handling (async version)
    /// </summary>
    public async Task<IndexWriter> GetOrCreateWriterAsync(string workspacePath, bool forceRecreate = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        // Use double-check locking pattern for thread safety
        if (!_indexes.TryGetValue(indexPath, out var context))
        {
            using (await _writerLock.LockAsync(cancellationToken).ConfigureAwait(false))  // Using async lock with timeout
            {
                // Check again inside the lock
                if (!_indexes.TryGetValue(indexPath, out context))
                {
                    // Atomic operation: check locks and create context together
                    context = await CreateIndexContextSafelyAsync(indexPath, forceRecreate, cancellationToken).ConfigureAwait(false);
                    
                    // Try to add to dictionary - handle race condition
                    if (!_indexes.TryAdd(indexPath, context))
                    {
                        // Another thread beat us, dispose our resources and use theirs
                        var ourContext = context;
                        context = _indexes[indexPath];
                        await DisposeContextAsync(ourContext).ConfigureAwait(false);
                    }
                    // Success - index created
                }
            }
        }
        
        // Update last accessed time
        context.LastAccessed = DateTime.UtcNow;
        
        // Use context-specific lock for writer operations
        using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            // Ensure writer is still valid
            if (context.Writer == null)
            {
                context.Writer = CreateWriter(context.Directory, forceRecreate, workspacePath, context.Path);
            }
            
            context.LastAccessed = DateTime.UtcNow;
            return context.Writer;
        }
    }
    
    /// <summary>
    /// Safely creates an index context with atomic lock checking and creation
    /// </summary>
    /// <param name="indexPath">Path to the index</param>
    /// <param name="forceRecreate">Whether to force recreation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created index context</returns>
    private async Task<IndexContext> CreateIndexContextSafelyAsync(string indexPath, bool forceRecreate, CancellationToken cancellationToken)
    {
        // Get comprehensive lock diagnostics
        var lockDiagnostics = await GetLockDiagnosticsAsync(indexPath).ConfigureAwait(false);
        
        if (lockDiagnostics.IsLocked)
        {
            // Determine if we should clear the lock
            bool shouldClearLock = lockDiagnostics.IsStuck || IsOrphanedLock(lockDiagnostics);
            
            if (shouldClearLock)
            {
                // Check if this is a protected memory index
                if (IsProtectedMemoryIndex(indexPath))
                {
                    _logger.LogError("CRITICAL: Memory index at {Path} has a stuck/orphaned lock. " +
                                   "Lock Age: {LockAge}, Process Info: {ProcessInfo}, " +
                                   "Access Info: {AccessInfo}. " +
                                   "This indicates improper disposal! The memory index may be corrupted. " +
                                   "Manual intervention required: delete the write.lock file and restart.",
                                   indexPath, lockDiagnostics.LockAge, lockDiagnostics.ProcessInfo, lockDiagnostics.AccessInfo);
                    throw new InvalidOperationException($"Memory index at {indexPath} has a stuck lock. " +
                                                      "This indicates improper disposal. Please manually delete the write.lock file and restart.");
                }
                else
                {
                    // For non-memory indexes, clear orphaned or stuck locks
                    var lockType = lockDiagnostics.IsStuck ? "stuck" : "orphaned";
                    _logger.LogWarning("Index at {Path} has {LockType} lock - clearing for safe access. " +
                                     "Lock Age: {LockAge}, Process Info: {ProcessInfo}, " +
                                     "Access Info: {AccessInfo}",
                                     indexPath, lockType, lockDiagnostics.LockAge, lockDiagnostics.ProcessInfo, lockDiagnostics.AccessInfo);
                    await ClearIndexAsync(indexPath).ConfigureAwait(false);
                }
            }
            else
            {
                // Lock exists and appears to be actively held
                _logger.LogError("Index at {Path} is currently locked by an active process. " +
                               "Lock Age: {LockAge}, Process Info: {ProcessInfo}",
                               indexPath, lockDiagnostics.LockAge, lockDiagnostics.ProcessInfo);
                throw new InvalidOperationException($"Index at {indexPath} is currently locked by another process");
            }
        }
        
        // Re-check after potential clear operation to prevent race condition
        var (isStillLocked, _) = await IsIndexLockedAsync(indexPath).ConfigureAwait(false);
        if (isStillLocked)
        {
            throw new InvalidOperationException($"Index at {indexPath} is still locked after attempting to clear");
        }
        
        // Now safely create the context
        return CreateIndexContext(indexPath, forceRecreate);
    }
    
    /// <summary>
    /// Safely dispose a context with timeout
    /// </summary>
    private async Task DisposeContextAsync(IndexContext context, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var disposeTimeout = timeout ?? TimeSpan.FromSeconds(5);
        
        try
        {
            using (await context.Lock.LockAsync(disposeTimeout).ConfigureAwait(false))
            {
                context.Writer?.Commit();
                context.Writer?.Dispose();
                context.Reader?.Dispose();
                context.Directory?.Dispose();
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout disposing index context at {Path}, forcing disposal", context.Path);
            // Force disposal without lock
            try { context.Writer?.Dispose(); } catch { }
            try { context.Reader?.Dispose(); } catch { }
            try { context.Directory?.Dispose(); } catch { }
        }
        finally
        {
            context.Lock?.Dispose();
        }
    }
    
    /// <summary>
    /// Safely close and commit an index writer (async version)
    /// </summary>
    public async Task CloseWriterAsync(string workspacePath, bool commit = true, CancellationToken cancellationToken = default)
    {
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        if (_indexes.TryGetValue(indexPath, out var context))
        {
            using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
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
        }
    }
    
    /// <summary>
    /// Enhanced lock diagnostics information
    /// </summary>
    public record LockDiagnostics(
        bool IsLocked,
        bool IsStuck,
        TimeSpan LockAge,
        DateTime? LockCreated,
        string? ProcessInfo,
        long? FileSizeBytes,
        string? AccessInfo
    );
    
    /// <summary>
    /// Get comprehensive lock diagnostics for an index
    /// </summary>
    private async Task<LockDiagnostics> GetLockDiagnosticsAsync(string indexPath)
    {
        var lockPath = Path.Combine(indexPath, WriteLockFilename);
        
        if (!await FileExistsAsync(lockPath).ConfigureAwait(false))
        {
            return new LockDiagnostics(false, false, TimeSpan.Zero, null, null, null, null);
        }
        
        try
        {
            var lockFileInfo = new FileInfo(lockPath);
            var lockAge = DateTime.UtcNow - lockFileInfo.LastWriteTimeUtc;
            var isStuck = lockAge > _lockTimeout;
            
            // Get process information if available
            string? processInfo = null;
            try
            {
                // Try to determine if another process is holding the file
                using var stream = File.OpenWrite(lockPath);
                processInfo = "No active lock holder detected";
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process"))
            {
                processInfo = "File is actively locked by another process";
            }
            catch (UnauthorizedAccessException)
            {
                processInfo = "Access denied - possible permission issue";
            }
            catch
            {
                processInfo = "Unable to determine lock holder";
            }
            
            // Additional file information
            var accessInfo = $"Created: {lockFileInfo.CreationTimeUtc:yyyy-MM-dd HH:mm:ss} UTC, " +
                           $"Modified: {lockFileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC, " +
                           $"Machine: {Environment.MachineName}, " +
                           $"Process: {Environment.ProcessId}";
            
            return new LockDiagnostics(
                IsLocked: true,
                IsStuck: isStuck,
                LockAge: lockAge,
                LockCreated: lockFileInfo.CreationTimeUtc,
                ProcessInfo: processInfo,
                FileSizeBytes: lockFileInfo.Length,
                AccessInfo: accessInfo
            );
        }
        catch (Exception ex)
        {
            return new LockDiagnostics(
                IsLocked: true,
                IsStuck: true,
                LockAge: TimeSpan.MaxValue,
                LockCreated: null,
                ProcessInfo: $"Error reading lock file: {ex.Message}",
                FileSizeBytes: null,
                AccessInfo: null
            );
        }
    }
    
    /// <summary>
    /// Check if an index is locked and if the lock is stuck
    /// </summary>
    private async Task<(bool isLocked, bool isStuck)> IsIndexLockedAsync(string indexPath)
    {
        var lockPath = Path.Combine(indexPath, WriteLockFilename);
        
        if (!await FileExistsAsync(lockPath).ConfigureAwait(false))
            return (false, false);
        
        var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
        var isStuck = lockAge > _lockTimeout;
        
        return (true, isStuck);
    }
    
    /// <summary>
    /// Check if a path is a protected memory index
    /// </summary>
    private bool IsProtectedMemoryIndex(string indexPath)
    {
        return _pathResolution.IsProtectedPath(indexPath);
    }
    
    
    /// <summary>
    /// Clear an index directory (nuclear option for stuck locks)
    /// </summary>
    private async Task ClearIndexAsync(string indexPath)
    {
        try
        {
            // CRITICAL: Protect memory indexes from accidental deletion
            if (IsProtectedMemoryIndex(indexPath))
            {
                _logger.LogWarning("Attempted to clear protected memory index at {Path}. Operation blocked.", indexPath);
                throw new InvalidOperationException($"Cannot clear protected memory index at {indexPath}");
            }
            
            if (await DirectoryExistsAsync(indexPath).ConfigureAwait(false))
            {
                await Task.Run(() => System.IO.Directory.Delete(indexPath, recursive: true)).ConfigureAwait(false);
                _logger.LogInformation("Cleared index directory at {Path}", indexPath);
            }
            
            // PathResolutionService already creates the directory
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear index at {Path}", indexPath);
            throw;
        }
    }
    
    private IndexContext CreateIndexContext(string indexPath, bool forceRecreate)
    {
        // Ensure directory exists before creating index
        // This is necessary because Lucene requires the directory to exist
        System.IO.Directory.CreateDirectory(indexPath);
        
        FSDirectory? directory = null;
        IndexWriter? writer = null;
        
        try
        {
            directory = FSDirectory.Open(indexPath);
            writer = CreateWriter(directory, forceRecreate, indexPath, indexPath);
            
            var context = new IndexContext(indexPath)
            {
                Directory = directory,
                Writer = writer,
                LastAccessed = DateTime.UtcNow
            };
            
            // Success - ownership transferred to context
            directory = null;
            writer = null;
            
            return context;
        }
        catch
        {
            // Clean up resources if anything fails
            writer?.Dispose();
            directory?.Dispose();
            throw;
        }
    }
    
    private IndexWriter CreateWriter(FSDirectory directory, bool forceRecreate, string workspacePath, string? indexPath = null)
    {
        var analyzer = GetAnalyzerForWorkspace(workspacePath);
        var config = new IndexWriterConfig(Version, analyzer)
        {
            // Use CREATE_OR_APPEND by default, CREATE if forcing recreate
            OpenMode = forceRecreate ? OpenMode.CREATE : OpenMode.CREATE_OR_APPEND
        };
        
        try
        {
            return new IndexWriter(directory, config);
        }
        catch (LockObtainFailedException ex)
        {
            // If we have an index path and it's a memory index, log critical error
            if (indexPath != null && IsProtectedMemoryIndex(indexPath))
            {
                _logger.LogError(ex, "CRITICAL: Failed to obtain lock for memory index at {Path}. " +
                                   "This indicates the index is in use by another process or has a stuck lock. " +
                                   "Manual intervention may be required.", indexPath);
            }
            
            throw;
        }
    }
    
    private async Task<string> GetIndexPathAsync(string workspacePath)
    {
        _logger.LogDebug("GetIndexPath called with: {WorkspacePath}", workspacePath);
        
        // Check if this is a memory path using PathResolutionService
        var isProtected = _pathResolution.IsProtectedPath(workspacePath);
        _logger.LogDebug("IsProtectedPath result: {IsProtected}", isProtected);
        
        if (isProtected)
        {
            // This is a memory path, use it directly
            _logger.LogDebug("Using memory path directly: {Path}", workspacePath);
            return workspacePath;
        }
        
        // For regular workspace paths, check if this path is contained within an existing workspace
        _logger.LogDebug("Using hashed path for workspace: {WorkspacePath}", workspacePath);
        
        // Check if this path is contained within an existing workspace to prevent duplicate indexes
        var existingWorkspacePath = await FindExistingWorkspaceForPathAsync(workspacePath).ConfigureAwait(false);
        if (existingWorkspacePath != null)
        {
            _logger.LogInformation("Path {RequestedPath} is contained within existing workspace {ExistingWorkspace}, reusing existing index", 
                workspacePath, existingWorkspacePath);
            var existingIndexPath = _pathResolution.GetIndexPath(existingWorkspacePath);
            _logger.LogDebug("Reusing existing index path: {IndexPath}", existingIndexPath);
            return existingIndexPath;
        }
        
        var indexPath = _pathResolution.GetIndexPath(workspacePath);
        _logger.LogDebug("Hashed index path result: {IndexPath}", indexPath);
        
        // Fixed: Duplicate index issue resolved with workspace validation
        
        // Update metadata for code indexes (not memory indexes)
        var workspaceRoot = await NormalizeToWorkspaceRootAsync(workspacePath).ConfigureAwait(false);
        if (workspaceRoot != null)
        {
            var hashPath = Path.GetFileName(indexPath); // Extract just the directory name
            if (!string.IsNullOrEmpty(hashPath))
            {
                // Update metadata asynchronously with error handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await UpdateMetadataAsync(workspaceRoot, hashPath).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update metadata for workspace {Workspace}, hash {Hash}", 
                            workspaceRoot, hashPath);
                    }
                });
            }
        }
        
        return indexPath;
    }
    
    private string GetBasePath()
    {
        return _pathResolution.GetBasePath();
    }
    
    private async Task<string?> FindProjectRootAsync(string startPath)
    {
        var currentPath = startPath;
        
        while (!string.IsNullOrEmpty(currentPath))
        {
            // Check for various project root indicators
            var projectIndicators = new[]
            {
                ".git",
                "*.sln",
                "*.csproj",
                "package.json",
                "tsconfig.json",
                "Cargo.toml",
                "go.mod"
            };
            
            foreach (var indicator in projectIndicators)
            {
                if (indicator.Contains('*'))
                {
                    // It's a pattern, check for files
                    if (await DirectoryExistsAsync(currentPath).ConfigureAwait(false))
                    {
                        var files = await GetFilesAsync(currentPath, indicator).ConfigureAwait(false);
                        if (files.Length > 0)
                        {
                            _logger.LogDebug("Found project indicator {Indicator} at {Path}, using as project root", indicator, currentPath);
                            return currentPath;
                        }
                    }
                }
                else
                {
                    // It's a directory or file name
                    var indicatorPath = Path.Combine(currentPath, indicator);
                    if (await DirectoryExistsAsync(indicatorPath).ConfigureAwait(false) || await FileExistsAsync(indicatorPath).ConfigureAwait(false))
                    {
                        _logger.LogDebug("Found project indicator {Indicator} at {Path}, using as project root", indicator, currentPath);
                        return currentPath;
                    }
                }
            }
            
            var parent = System.IO.Directory.GetParent(currentPath);
            if (parent == null)
                break;
                
            currentPath = parent.FullName;
        }
        
        _logger.LogDebug("No project root indicators found, will use current directory as base");
        return null;
    }
    
    /// <summary>
    /// Normalizes any path (file or directory) to its workspace root.
    /// This ensures we always use the project root for indexing, not individual files or subdirectories.
    /// </summary>
    private async Task<string> NormalizeToWorkspaceRootAsync(string path)
    {
        // Get the absolute path
        var absolutePath = Path.GetFullPath(path);
        
        // If it's a file, get its directory
        string searchPath;
        if (await FileExistsAsync(absolutePath).ConfigureAwait(false))
        {
            searchPath = Path.GetDirectoryName(absolutePath) ?? absolutePath;
            _logger.LogDebug("Path {Path} is a file, using directory {Directory} for root search", absolutePath, searchPath);
        }
        else
        {
            searchPath = absolutePath;
        }
        
        // Find the project root from this path
        var projectRoot = await FindProjectRootAsync(searchPath).ConfigureAwait(false);
        
        if (projectRoot != null)
        {
            _logger.LogDebug("Normalized path {Path} to workspace root {Root}", path, projectRoot);
            return projectRoot;
        }
        
        // If no project root found, use the directory itself (not individual files)
        if (await FileExistsAsync(absolutePath).ConfigureAwait(false))
        {
            var directory = Path.GetDirectoryName(absolutePath) ?? absolutePath;
            _logger.LogDebug("No project root found for file {Path}, using directory {Directory}", absolutePath, directory);
            return directory;
        }
        
        _logger.LogDebug("No project root found for {Path}, using as-is", absolutePath);
        return absolutePath;
    }
    
    private string GenerateHashPath(string workspacePath)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(workspacePath));
        
        // Convert to hex and take first 8 characters for a short hash
        var hash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        return hash.Substring(0, HASH_PREFIX_LENGTH);
    }
    
    private async Task UpdateMetadataAsync(string originalPath, string hashPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(hashPath))
        {
            _logger.LogWarning("Skipping metadata update for null or empty hashPath");
            return;
        }
        
        // Skip memory paths - they should not be tracked in workspace metadata
        if (hashPath.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            originalPath.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping metadata update for memory path: {Path}", hashPath);
            return;
        }
        
        using (await _metadataLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var metadataPath = GetMetadataPath();
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            
            if (!metadata.Indexes.ContainsKey(hashPath))
            {
                metadata.Indexes[hashPath] = new IndexEntry
                {
                    OriginalPath = originalPath,
                    HashPath = hashPath,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow
                };
            }
            else
            {
                metadata.Indexes[hashPath].LastAccessed = DateTime.UtcNow;
            }
            
            await SaveMetadataAsync(metadataPath, metadata).ConfigureAwait(false);
            _metadataCache[hashPath] = metadata;
        }
    }
    
    private string GetMetadataPath()
    {
        return _pathResolution.GetWorkspaceMetadataPath();
    }
    
    /// <summary>
    /// Retry helper for file operations that may encounter transient locking issues
    /// </summary>
    private async Task<T> RetryFileOperationAsync<T>(Func<Task<T>> operation, string path, string operationName)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 100;
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (IOException ex) when (i < maxRetries - 1 && IsTransientFileError(ex))
            {
                var delay = baseDelayMs * (int)Math.Pow(2, i); // Exponential backoff
                _logger.LogWarning("Transient error during {Operation} for {Path}, retrying in {Delay}ms (attempt {Attempt}/{Max}): {Error}",
                    operationName, path, delay, i + 1, maxRetries, ex.Message);
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Operation} from {Path}", operationName, path);
                throw;
            }
        }
        
        throw new InvalidOperationException($"Failed to {operationName} after {maxRetries} attempts");
    }
    
    /// <summary>
    /// Retry helper for file operations that don't return a value
    /// </summary>
    private async Task RetryFileOperationAsync(Func<Task> operation, string path, string operationName)
    {
        await RetryFileOperationAsync<object>(async () => 
        {
            await operation().ConfigureAwait(false);
            return null!;
        }, path, operationName).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Determines if an IOException is transient and should be retried
    /// </summary>
    private bool IsTransientFileError(IOException ex)
    {
        // Check for common transient errors
        var message = ex.Message;
        return message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("access is denied", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Determines if a lock is orphaned (no active holder but not old enough to be stuck)
    /// </summary>
    private bool IsOrphanedLock(LockDiagnostics diagnostics)
    {
        // A lock is considered orphaned if:
        // 1. It's locked
        // 2. No active process is holding it
        // 3. It's older than 1 minute (to avoid race conditions)
        // 4. But not yet considered "stuck" (< 15 minutes)
        return diagnostics.IsLocked && 
               !diagnostics.IsStuck &&
               diagnostics.LockAge > TimeSpan.FromMinutes(1) &&
               diagnostics.ProcessInfo?.Contains("No active lock holder detected", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    private async Task<IndexMetadata> LoadMetadataAsync(string metadataPath)
    {
        if (await FileExistsAsync(metadataPath).ConfigureAwait(false))
        {
            return await RetryFileOperationAsync(async () =>
            {
                // Use FileShare.Read to allow multiple readers
                using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<IndexMetadata>(json) ?? new IndexMetadata();
            }, metadataPath, "load metadata").ConfigureAwait(false);
        }
        
        return new IndexMetadata();
    }
    
    /// <summary>
    /// Find an existing workspace that contains the requested path to prevent duplicate indexes
    /// </summary>
    private async Task<string?> FindExistingWorkspaceForPathAsync(string requestedPath)
    {
        try
        {
            var metadataPath = GetMetadataPath();
            if (!File.Exists(metadataPath))
            {
                return null;
            }
            
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            var normalizedRequestedPath = Path.GetFullPath(requestedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            // Check each existing workspace to see if the requested path is contained within it
            foreach (var indexEntry in metadata.Indexes.Values)
            {
                var existingWorkspacePath = Path.GetFullPath(indexEntry.OriginalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                
                // Skip if this is the exact same path
                if (string.Equals(normalizedRequestedPath, existingWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                // Check if the requested path is contained within this existing workspace
                if (normalizedRequestedPath.StartsWith(existingWorkspacePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedRequestedPath.StartsWith(existingWorkspacePath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found requested path {RequestedPath} is contained within existing workspace {ExistingWorkspace}", 
                        requestedPath, indexEntry.OriginalPath);
                    return indexEntry.OriginalPath;
                }
                
                // Also check the reverse - if an existing workspace is contained within the requested path
                // This handles the case where we indexed a subdirectory first, then try to index the parent
                if (existingWorkspacePath.StartsWith(normalizedRequestedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    existingWorkspacePath.StartsWith(normalizedRequestedPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found existing workspace {ExistingWorkspace} is contained within requested path {RequestedPath}, should use parent", 
                        indexEntry.OriginalPath, requestedPath);
                    // In this case, we want to use the broader (parent) path, so we don't return the existing one
                    // We'll let the normal indexing process create the parent index
                    continue;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for existing workspace containing path {Path}", requestedPath);
            return null;
        }
    }
    
    private async Task SaveMetadataAsync(string metadataPath, IndexMetadata metadata)
    {
        await RetryFileOperationAsync(async () =>
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(metadata, options);
            
            // Write to temp file first to ensure atomic operation
            var tempPath = metadataPath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            
            // Atomic move - this prevents partial writes
            File.Move(tempPath, metadataPath, overwrite: true);
        }, metadataPath, "save metadata").ConfigureAwait(false);
    }
    
    private async Task CleanupMemoryEntriesFromMetadataAsync()
    {
        try
        {
            var metadataPath = GetMetadataPath();
            if (!File.Exists(metadataPath))
            {
                return;
            }
            
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            var memoryKeys = metadata.Indexes
                .Where(kvp => kvp.Key.Contains("memory", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();
                
            if (memoryKeys.Count > 0)
            {
                _logger.LogInformation("Removing {Count} memory entries from workspace metadata", memoryKeys.Count);
                
                foreach (var key in memoryKeys)
                {
                    metadata.Indexes.Remove(key);
                    _logger.LogDebug("Removed memory entry from metadata: {Key}", key);
                }
                
                await SaveMetadataAsync(metadataPath, metadata).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup memory entries from metadata");
        }
    }
    
    /// <summary>
    /// Get the original workspace path from a hash path (for debugging/logging)
    /// </summary>
    public async Task<string?> GetOriginalPathAsync(string hashPath, CancellationToken cancellationToken = default)
    {
        using (await _metadataLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var metadataPath = GetMetadataPath();
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            
            if (metadata.Indexes.TryGetValue(hashPath, out var entry))
            {
                return entry.OriginalPath;
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Get all index mappings (for debugging/maintenance)
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllIndexMappingsAsync()
    {
        using (await _metadataLock.LockAsync().ConfigureAwait(false))
        {
            var metadataPath = GetMetadataPath();
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            
            return metadata.Indexes.ToDictionary(
                kvp => kvp.Value.OriginalPath,
                kvp => kvp.Key
            );
        }
    }
    
    
    /// <summary>
    /// Check if memory indexes exist and are healthy
    /// </summary>
    public async Task<(bool projectMemoryExists, bool localMemoryExists)> CheckMemoryIndexHealthAsync()
    {
        var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
        var localMemoryPath = _pathResolution.GetLocalMemoryPath();
        
        var projectMemoryExists = await DirectoryExistsAsync(projectMemoryPath).ConfigureAwait(false) && 
                                  await FileExistsAsync(Path.Combine(projectMemoryPath, SegmentsFilename)).ConfigureAwait(false);
        var localMemoryExists = await DirectoryExistsAsync(localMemoryPath).ConfigureAwait(false) && 
                               await FileExistsAsync(Path.Combine(localMemoryPath, SegmentsFilename)).ConfigureAwait(false);
        
        _logger.LogInformation("Memory index health check - Project: {ProjectExists}, Local: {LocalExists}", 
            projectMemoryExists, localMemoryExists);
        
        return (projectMemoryExists, localMemoryExists);
    }
    
    /// <summary>
    /// Static method to diagnose stuck write.lock files during startup (before services start).
    /// This method only warns about stuck locks but does NOT remove them automatically.
    /// </summary>
    public static async Task DiagnoseStuckIndexesOnStartupAsync(IPathResolutionService pathResolution, ILogger logger)
    {
        var lockTimeout = TimeSpan.FromMinutes(LOCK_TIMEOUT_MINUTES); // Aggressive timeout for startup cleanup
        const string WriteLockFilename = "write.lock";
        
        logger.LogInformation("STARTUP: Checking for stuck write.lock files");
        
        // Clean up workspace indexes
        var indexRoot = pathResolution.GetIndexRootPath();
        if (await DirectoryExistsAsync(indexRoot).ConfigureAwait(false))
        {
            logger.LogDebug("STARTUP: Checking workspace indexes at {Path}", indexRoot);
            
            foreach (var indexDir in System.IO.Directory.GetDirectories(indexRoot))
            {
                var lockPath = Path.Combine(indexDir, WriteLockFilename);
                
                if (await FileExistsAsync(lockPath).ConfigureAwait(false))
                {
                    var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
                    var hashPath = Path.GetFileName(indexDir);
                    
                    if (lockAge > lockTimeout)
                    {
                        logger.LogError("CRITICAL: Found stuck workspace lock at {Path}, age: {Age}. " +
                                      "This indicates improper disposal of index writers! " +
                                      "Manual intervention required: delete the write.lock file after ensuring no processes are using the index.",
                                      lockPath, lockAge);
                    }
                    else
                    {
                        logger.LogDebug("Found recent workspace lock at {Path}, age: {Age} - likely in use", lockPath, lockAge);
                    }
                }
            }
        }
        
        // Check memory indexes
        var memoryPaths = new[]
        {
            pathResolution.GetProjectMemoryPath(),
            pathResolution.GetLocalMemoryPath()
        };
        
        foreach (var memoryPath in memoryPaths)
        {
            if (!await DirectoryExistsAsync(memoryPath).ConfigureAwait(false))
            {
                continue;
            }
            
            var lockPath = Path.Combine(memoryPath, WriteLockFilename);
            
            if (await FileExistsAsync(lockPath).ConfigureAwait(false))
            {
                var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
                var memoryType = Path.GetFileName(memoryPath);
                
                if (lockAge > lockTimeout)
                {
                    logger.LogError("CRITICAL: Found stuck {MemoryType} memory lock, age: {Age}. " +
                                  "This indicates improper disposal! The memory index may be corrupted. " +
                                  "Manual intervention required: delete the write.lock file after ensuring no processes are using the index.",
                                  memoryType, lockAge);
                }
                else
                {
                    logger.LogDebug("Found recent {MemoryType} memory lock ({Age}) - likely in use", memoryType, lockAge);
                }
            }
        }
        
        logger.LogInformation("STARTUP: Stuck lock diagnostics completed");
    }
    
    /// <summary>
    /// Smart startup cleanup with tiered safety approach.
    /// TIER 1: Auto-clean test artifacts and recent workspace locks (low risk)
    /// TIER 2: Auto-clean older workspace locks with safety checks (medium risk)  
    /// TIER 3: Diagnose-only memory locks (high risk - manual intervention required)
    /// </summary>
    public static async Task SmartStartupCleanupAsync(IPathResolutionService pathResolution, ILogger logger)
    {
        var testArtifactMinAge = TimeSpan.FromMinutes(1);     // Very aggressive for test artifacts
        var workspaceMinAge = TimeSpan.FromMinutes(15);       // Conservative for workspace locks
        var diagnosticMinAge = TimeSpan.FromMinutes(LOCK_TIMEOUT_MINUTES); // Memory locks - diagnose only
        
        logger.LogInformation("STARTUP: Smart cleanup - Tiered approach to stuck write.lock files");
        
        var cleanupStats = new
        {
            TestArtifactsRemoved = 0,
            WorkspaceLocksRemoved = 0,
            MemoryLocksFound = 0,
            Errors = new List<string>()
        };
        
        // TIER 1: SAFE AUTO-CLEANUP - Test artifacts
        try
        {
            var testCleanupCount = await CleanupTestArtifactsAsync(pathResolution, logger, testArtifactMinAge);
            cleanupStats = cleanupStats with { TestArtifactsRemoved = testCleanupCount };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TIER 1 CLEANUP: Failed to clean test artifacts");
            cleanupStats.Errors.Add($"Test artifacts: {ex.Message}");
        }
        
        // TIER 2: CONSERVATIVE AUTO-CLEANUP - Workspace indexes  
        try
        {
            var workspaceCleanupCount = await CleanupWorkspaceIndexesAsync(pathResolution, logger, workspaceMinAge);
            cleanupStats = cleanupStats with { WorkspaceLocksRemoved = workspaceCleanupCount };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TIER 2 CLEANUP: Failed to clean workspace locks");
            cleanupStats.Errors.Add($"Workspace locks: {ex.Message}");
        }
        
        // TIER 3: DIAGNOSTIC ONLY - Memory indexes (no auto-cleanup)
        try
        {
            var memoryDiagnosticCount = await DiagnoseMemoryIndexLocksAsync(pathResolution, logger, diagnosticMinAge);
            cleanupStats = cleanupStats with { MemoryLocksFound = memoryDiagnosticCount };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TIER 3 DIAGNOSTIC: Failed to diagnose memory locks");
            cleanupStats.Errors.Add($"Memory diagnostics: {ex.Message}");
        }
        
        // Summary
        if (cleanupStats.TestArtifactsRemoved > 0 || cleanupStats.WorkspaceLocksRemoved > 0 || cleanupStats.MemoryLocksFound > 0)
        {
            logger.LogInformation("STARTUP CLEANUP SUMMARY: Test artifacts removed: {TestCount}, " +
                                 "Workspace locks removed: {WorkspaceCount}, Memory locks found: {MemoryCount}",
                                 cleanupStats.TestArtifactsRemoved, cleanupStats.WorkspaceLocksRemoved, cleanupStats.MemoryLocksFound);
        }
        else
        {
            logger.LogInformation("STARTUP CLEANUP: No stuck locks found - system is clean");
        }
        
        if (cleanupStats.Errors.Count > 0)
        {
            logger.LogWarning("STARTUP CLEANUP: {ErrorCount} errors during cleanup: {Errors}", 
                             cleanupStats.Errors.Count, string.Join("; ", cleanupStats.Errors));
        }
    }
    
    /// <summary>
    /// TIER 1: Clean up test artifacts - very safe, aggressive cleanup
    /// </summary>
    private static async Task<int> CleanupTestArtifactsAsync(IPathResolutionService pathResolution, ILogger logger, TimeSpan minAge)
    {
        var cleanupCount = 0;
        
        logger.LogDebug("TIER 1: Cleaning test artifacts older than {MinAge}", minAge);
        
        var basePath = Path.GetDirectoryName(pathResolution.GetIndexRootPath()) ?? Environment.CurrentDirectory;
        
        try
        {
            var testLockFiles = await Task.Run(() => 
                System.IO.Directory.GetFiles(basePath, "write.lock", SearchOption.AllDirectories)
                    .Where(f => IsTestArtifact(f))
                    .ToList());
            
            foreach (var lockFile in testLockFiles)
            {
                try
                {
                    var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockFile);
                    if (lockAge > minAge)
                    {
                        File.Delete(lockFile);
                        cleanupCount++;
                        logger.LogDebug("TIER 1: Removed test artifact lock: {Path} (age: {Age})", lockFile, lockAge);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug("TIER 1: Could not remove test lock {Path}: {Error}", lockFile, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("TIER 1: Error scanning for test artifacts: {Error}", ex.Message);
        }
        
        if (cleanupCount > 0)
        {
            logger.LogInformation("TIER 1: Cleaned {Count} test artifact locks", cleanupCount);
        }
        
        return cleanupCount;
    }
    
    /// <summary>
    /// TIER 2: Clean up workspace indexes with safety checks
    /// </summary>
    private static async Task<int> CleanupWorkspaceIndexesAsync(IPathResolutionService pathResolution, ILogger logger, TimeSpan minAge)
    {
        const string WriteLockFilename = "write.lock";
        var cleanupCount = 0;
        
        logger.LogDebug("TIER 2: Cleaning workspace locks older than {MinAge}", minAge);
        
        var indexRoot = pathResolution.GetIndexRootPath();
        if (!await DirectoryExistsAsync(indexRoot).ConfigureAwait(false))
        {
            return 0;
        }
        
        foreach (var indexDir in System.IO.Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WriteLockFilename);
            
            if (!await FileExistsAsync(lockPath).ConfigureAwait(false))
            {
                continue;
            }
            
            try
            {
                var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
                
                if (lockAge > minAge)
                {
                    // Safety checks before deletion
                    if (await IsSafeToRemoveLockAsync(lockPath, logger))
                    {
                        File.Delete(lockPath);
                        cleanupCount++;
                        logger.LogInformation("TIER 2: Removed stuck workspace lock: {Path} (age: {Age})", lockPath, lockAge);
                    }
                    else
                    {
                        logger.LogWarning("TIER 2: Skipped unsafe lock removal: {Path} (age: {Age})", lockPath, lockAge);
                    }
                }
                else
                {
                    logger.LogDebug("TIER 2: Found recent workspace lock: {Path} (age: {Age}) - keeping", lockPath, lockAge);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("TIER 2: Could not process workspace lock {Path}: {Error}", lockPath, ex.Message);
            }
        }
        
        if (cleanupCount > 0)
        {
            logger.LogInformation("TIER 2: Cleaned {Count} workspace locks", cleanupCount);
        }
        
        return cleanupCount;
    }
    
    /// <summary>
    /// TIER 3: Diagnose memory locks only - never auto-remove
    /// This includes both main memory index locks and taxonomy subdirectory locks
    /// </summary>
    private static async Task<int> DiagnoseMemoryIndexLocksAsync(IPathResolutionService pathResolution, ILogger logger, TimeSpan minAge)
    {
        const string WriteLockFilename = "write.lock";
        var foundCount = 0;
        
        logger.LogDebug("TIER 3: Diagnosing memory locks including taxonomy directories (no auto-cleanup)");
        
        var memoryPaths = new[]
        {
            pathResolution.GetProjectMemoryPath(),
            pathResolution.GetLocalMemoryPath()
        };
        
        foreach (var memoryPath in memoryPaths)
        {
            if (!await DirectoryExistsAsync(memoryPath).ConfigureAwait(false))
            {
                continue;
            }
            
            var memoryType = Path.GetFileName(memoryPath);
            
            // Check main memory index lock
            var mainLockPath = Path.Combine(memoryPath, WriteLockFilename);
            foundCount += await DiagnoseStaticMemoryLockFileAsync(mainLockPath, $"{memoryType} main index", logger, minAge);
            
            // Check taxonomy subdirectory lock
            var taxonomyPath = Path.Combine(memoryPath, "taxonomy");
            if (await DirectoryExistsAsync(taxonomyPath).ConfigureAwait(false))
            {
                var taxonomyLockPath = Path.Combine(taxonomyPath, WriteLockFilename);
                foundCount += await DiagnoseStaticMemoryLockFileAsync(taxonomyLockPath, $"{memoryType} taxonomy index", logger, minAge);
            }
        }
        
        return foundCount;
    }
    
    /// <summary>
    /// Diagnose a specific memory lock file (static version for startup)
    /// </summary>
    private static async Task<int> DiagnoseStaticMemoryLockFileAsync(string lockPath, string lockDescription, ILogger logger, TimeSpan minAge)
    {
        if (await FileExistsAsync(lockPath).ConfigureAwait(false))
        {
            var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
            
            if (lockAge > minAge)
            {
                logger.LogError("TIER 3: CRITICAL - Found stuck {LockDescription} lock, age: {Age}. " +
                              "This indicates improper disposal! The index may be corrupted. " +
                              "MANUAL INTERVENTION REQUIRED: Exit Claude Code and delete {LockPath}",
                              lockDescription, lockAge, lockPath);
            }
            else
            {
                logger.LogDebug("TIER 3: Found recent {LockDescription} lock ({Age}) - likely in use", 
                                lockDescription, lockAge);
            }
            
            return 1; // Found one lock file
        }
        
        return 0; // No lock file found
    }
    
    /// <summary>
    /// Helper: Check if a lock file is from test artifacts
    /// </summary>
    private static bool IsTestArtifact(string lockPath)
    {
        var normalizedPath = lockPath.Replace('\\', '/').ToLowerInvariant();
        return normalizedPath.Contains("/bin/debug/") ||
               normalizedPath.Contains("/bin/release/") ||
               normalizedPath.Contains("/testprojects/") ||
               normalizedPath.Contains("test") && normalizedPath.Contains(".codesearch");
    }
    
    /// <summary>
    /// Helper: Safety checks before removing a workspace lock
    /// </summary>
    private static async Task<bool> IsSafeToRemoveLockAsync(string lockPath, ILogger logger)
    {
        try
        {
            // Check 1: File is not currently being written to
            var initialSize = new FileInfo(lockPath).Length;
            await Task.Delay(100); // Brief pause
            var currentSize = new FileInfo(lockPath).Length;
            
            if (initialSize != currentSize)
            {
                logger.LogDebug("SAFETY CHECK: Lock file {Path} is growing - likely in use", lockPath);
                return false;
            }
            
            // Check 2: Try to get exclusive access briefly
            try
            {
                using var fs = File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
                // If we can get exclusive access, it's probably safe
                return true;
            }
            catch (IOException)
            {
                logger.LogDebug("SAFETY CHECK: Lock file {Path} is in use by another process", lockPath);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("SAFETY CHECK: Could not verify safety for {Path}: {Error}", lockPath, ex.Message);
            return false; // Err on the side of caution
        }
    }
    
    /// <summary>
    /// Diagnose stuck write.lock files from workspace indexes and memory indexes.
    /// This method only reports stuck locks but does NOT remove them automatically.
    /// </summary>
    public async Task DiagnoseStuckIndexesAsync()
    {
        var indexRoot = _pathResolution.GetIndexRootPath();
        
        if (!await DirectoryExistsAsync(indexRoot).ConfigureAwait(false))
        {
            _logger.LogInformation("No index directory found at {Path}, nothing to clean", indexRoot);
            return;
        }
        
        _logger.LogInformation("Checking for stuck write.lock files in indexes at {Path}", indexRoot);
        
        // Clean up workspace indexes
        foreach (var indexDir in await Task.Run(() => System.IO.Directory.GetDirectories(indexRoot)).ConfigureAwait(false))
        {
            var lockPath = Path.Combine(indexDir, WriteLockFilename);
            
            if (await FileExistsAsync(lockPath).ConfigureAwait(false))
            {
                var lockDiagnostics = await GetLockDiagnosticsAsync(indexDir).ConfigureAwait(false);
                
                if (lockDiagnostics.IsStuck)
                {
                    // Get the hash from the directory name
                    var hashPath = Path.GetFileName(indexDir);
                    var originalPath = await GetOriginalPathAsync(hashPath).ConfigureAwait(false);
                    var pathInfo = originalPath != null ? $" (original: {originalPath})" : "";
                    
                    _logger.LogError("CRITICAL: Found stuck lock at {Path}{PathInfo}. " +
                                   "Lock Age: {LockAge}, Process Info: {ProcessInfo}, " +
                                   "Access Info: {AccessInfo}, File Size: {FileSize} bytes. " +
                                   "This indicates improper disposal of index writers! " +
                                   "Manual intervention required: delete the write.lock file after ensuring no processes are using the index.",
                                   lockPath, pathInfo, lockDiagnostics.LockAge, lockDiagnostics.ProcessInfo, 
                                   lockDiagnostics.AccessInfo, lockDiagnostics.FileSizeBytes);
                }
            }
        }
        
        // Also check memory indexes
        await DiagnoseMemoryIndexLocksAsync().ConfigureAwait(false);
    }
    
    /// <summary>
    /// Diagnose stuck write.lock files in memory indexes (project-memory and local-memory).
    /// This includes both main memory index locks and taxonomy subdirectory locks.
    /// This method only reports stuck locks but does NOT remove them automatically.
    /// </summary>
    private async Task DiagnoseMemoryIndexLocksAsync()
    {
        var memoryPaths = new[]
        {
            _pathResolution.GetProjectMemoryPath(),
            _pathResolution.GetLocalMemoryPath()
        };
        
        foreach (var memoryPath in memoryPaths)
        {
            if (!await DirectoryExistsAsync(memoryPath).ConfigureAwait(false))
            {
                continue;
            }
            
            var memoryType = Path.GetFileName(memoryPath);
            
            // Check main memory index lock
            var mainLockPath = Path.Combine(memoryPath, WriteLockFilename);
            await DiagnoseMemoryLockFileAsync(mainLockPath, $"{memoryType} main index");
            
            // Check taxonomy subdirectory lock
            var taxonomyPath = Path.Combine(memoryPath, "taxonomy");
            if (await DirectoryExistsAsync(taxonomyPath).ConfigureAwait(false))
            {
                var taxonomyLockPath = Path.Combine(taxonomyPath, WriteLockFilename);
                await DiagnoseMemoryLockFileAsync(taxonomyLockPath, $"{memoryType} taxonomy index");
            }
        }
    }
    
    /// <summary>
    /// Diagnose a specific memory lock file
    /// </summary>
    private async Task DiagnoseMemoryLockFileAsync(string lockPath, string lockDescription)
    {
        if (await FileExistsAsync(lockPath).ConfigureAwait(false))
        {
            var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
            
            if (lockAge > _lockTimeout)
            {
                _logger.LogError("CRITICAL: Found stuck write.lock in {LockDescription}, age: {Age}. " +
                               "This indicates improper disposal! The index may be corrupted. " +
                               "Manual intervention required: delete {Path} after ensuring no processes are using it.",
                               lockDescription, lockAge, lockPath);
            }
            else
            {
                _logger.LogDebug("Found recent write.lock in {LockDescription} ({Age}) - likely in use", 
                                lockDescription, lockAge);
            }
        }
    }
    
    public async Task CleanupDuplicateIndicesAsync()
    {
        var indexRoot = _pathResolution.GetIndexRootPath();
        var metadataPath = _pathResolution.GetWorkspaceMetadataPath();
        
        if (!File.Exists(metadataPath))
        {
            _logger.LogInformation("No metadata file found, cannot cleanup duplicates");
            return;
        }
        
        _logger.LogInformation("Checking for duplicate indices");
        
        try
        {
            var metadata = await LoadMetadataAsync(metadataPath).ConfigureAwait(false);
            var duplicateGroups = metadata.Indexes
                .GroupBy(kvp => kvp.Value.OriginalPath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            
            if (!duplicateGroups.Any())
            {
                _logger.LogInformation("No duplicate indices found");
                return;
            }
            
            foreach (var group in duplicateGroups)
            {
                var originalPath = group.Key;
                var duplicates = group.OrderBy(kvp => kvp.Value.LastAccessed).ToList();
                
                // Keep the most recently accessed index, remove others
                var toKeep = duplicates.Last();
                var toRemove = duplicates.Take(duplicates.Count - 1).ToList();
                
                _logger.LogInformation("Found {Count} duplicate indices for {Path}, keeping {Keep}, removing {Remove}", 
                    duplicates.Count, originalPath, toKeep.Key, 
                    string.Join(", ", toRemove.Select(x => x.Key)));
                
                foreach (var duplicate in toRemove)
                {
                    try
                    {
                        var indexPath = Path.Combine(indexRoot, duplicate.Key);
                        if (await DirectoryExistsAsync(indexPath).ConfigureAwait(false))
                        {
                            // Close any open index context for this path
                            if (_indexes.TryRemove(indexPath, out var context))
                            {
                                // Dispose the context asynchronously
                                await DisposeContextAsync(context).ConfigureAwait(false);
                            }
                            
                            System.IO.Directory.Delete(indexPath, true);
                            _logger.LogInformation("Removed duplicate index directory: {Path}", indexPath);
                        }
                        
                        // Remove from metadata
                        metadata.Indexes.Remove(duplicate.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove duplicate index {HashPath} for {OriginalPath}", 
                            duplicate.Key, originalPath);
                    }
                }
            }
            
            // Save updated metadata
            await SaveMetadataAsync(metadataPath, metadata).ConfigureAwait(false);
            _logger.LogInformation("Cleanup completed, removed {Count} duplicate indices", 
                duplicateGroups.Sum(g => g.Count() - 1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate index cleanup");
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LuceneIndexService));
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
            
        _logger.LogWarning("LuceneIndexService.DisposeAsync() called - beginning shutdown cleanup of {Count} index contexts", _indexes.Count);
        _disposed = true;
        
        // Dispose all contexts in parallel with timeout
        var disposeTasks = _indexes.Select(async kvp =>
        {
            try
            {
                await DisposeContextAsync(kvp.Value, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                _logger.LogDebug("Successfully disposed index context at {Path}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing index context at {Path}", kvp.Key);
            }
        });
        
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        
        _indexes.Clear();
        _standardAnalyzer?.Dispose();
        _memoryAnalyzer?.Dispose();
        _writerLock?.Dispose();
        _metadataLock?.Dispose();
        
        // Idle cleanup timer removed - cleanup is done on-demand
        
        _logger.LogWarning("LuceneIndexService.DisposeAsync() completed - all Lucene resources cleaned up");
    }
    
    // Synchronous Dispose for IDisposable compatibility
    public void Dispose()
    {
        // Prevent deadlock by using a new task context
        Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
    
    #region ILuceneIndexService Implementation
    
    /// <summary>
    /// Get index writer asynchronously
    /// </summary>
    public Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        return GetOrCreateWriterAsync(workspacePath, false, cancellationToken);
    }
    
    /// <summary>
    /// Get index searcher asynchronously with caching for performance
    /// </summary>
    public async Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        // Get or create context
        if (!_indexes.TryGetValue(indexPath, out var context))
        {
            // Check if index exists on disk before auto-creating
            if (!await DirectoryExistsAsync(indexPath).ConfigureAwait(false) || !(await GetFilesAsync(indexPath, "*.cfs").ConfigureAwait(false)).Any())
            {
                throw new InvalidOperationException($"No index found for workspace: {workspacePath}. Please run index_workspace first.");
            }
            
            using (await _writerLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_indexes.TryGetValue(indexPath, out context))
                {
                    // Open existing index (read-only mode)
                    context = CreateIndexContext(indexPath, false);
                    _indexes.TryAdd(indexPath, context);
                }
            }
        }
        
        // Update last accessed time
        context.LastAccessed = DateTime.UtcNow;
        
        using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = DateTime.UtcNow;
            var refreshInterval = TimeSpan.FromMinutes(1); // Refresh searcher every minute for NRT
            
            // Check if we need to refresh the reader
            bool needsRefresh = context.Reader == null || !context.Reader.IsCurrent() ||
                               (now - context.SearcherLastRefresh) > refreshInterval;
            
            if (needsRefresh)
            {
                // Clear old searcher reference (IndexSearcher doesn't implement IDisposable)
                context.CachedSearcher = null;
                
                // Refresh reader if needed
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
                    
                    if (context.Writer == null)
                    {
                        context.Reader = DirectoryReader.Open(context.Directory);
                    }
                }
                
                if (context.Reader == null)
                {
                    throw new InvalidOperationException($"Failed to create or refresh index reader for workspace: {workspacePath}");
                }
                
                // Create new cached searcher
                context.CachedSearcher = new IndexSearcher(context.Reader);
                context.SearcherLastRefresh = now;
                
                _logger.LogDebug("Refreshed searcher for workspace: {WorkspacePath}", workspacePath);
            }
            
            context.LastAccessed = now;
            return context.CachedSearcher!;
        }
    }
    
    /// <summary>
    /// Get analyzer asynchronously - returns shared instance for performance
    /// </summary>
    public Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Return appropriate analyzer based on workspace path
        // Both analyzers are thread-safe for reading operations
        var analyzer = GetAnalyzerForWorkspace(workspacePath);
        return Task.FromResult<Analyzer>(analyzer);
    }
    
    /// <summary>
    /// Commit index changes asynchronously
    /// </summary>
    public async Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        if (_indexes.TryGetValue(indexPath, out var context))
        {
            using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (context.Writer != null)
                {
                    try
                    {
                        context.Writer.Commit();
                        
                        // Invalidate the reader so next search gets fresh data
                        context.Reader?.Dispose();
                        context.Reader = null;
                        
                        // Log extra info for memory indexes
                        if (IsProtectedMemoryIndex(indexPath))
                        {
                            _logger.LogInformation("Successfully committed changes to MEMORY index at {Path}", indexPath);
                        }
                        else
                        {
                            _logger.LogInformation("Committed changes to index at {Path}", indexPath);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Writer already disposed
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Force merge index segments asynchronously (merge segments for better performance)
    /// </summary>
    public async Task ForceMergeAsync(string workspacePath, int maxNumSegments = 1, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var writer = await GetIndexWriterAsync(workspacePath, cancellationToken);
        
        writer.ForceMerge(maxNumSegments); // Merge down to specified number of segments
        await CommitAsync(workspacePath, cancellationToken);
        
        _logger.LogInformation("Force merged index to {Segments} segments at {Path}", 
            maxNumSegments, await GetIndexPathAsync(workspacePath).ConfigureAwait(false));
    }
    
    /// <summary>
    /// Optimize index asynchronously (merge segments for better performance)
    /// </summary>
    [Obsolete("Use ForceMergeAsync instead. 'Optimize' is deprecated terminology in Lucene.")]
    public async Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        await ForceMergeAsync(workspacePath, 1, cancellationToken);
    }
    
    /// <summary>
    /// Comprehensive index defragmentation for long-running installations.
    /// This goes beyond simple ForceMerge to provide full maintenance:
    /// - Analyzes fragmentation levels
    /// - Performs incremental or full defragmentation based on metrics
    /// - Reports space savings and performance improvements
    /// - Safe for production use with progress reporting
    /// </summary>
    public async Task<IndexDefragmentationResult> DefragmentIndexAsync(string workspacePath, 
        IndexDefragmentationOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= new IndexDefragmentationOptions();
        
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        var startTime = DateTime.UtcNow;
        var result = new IndexDefragmentationResult { StartTime = startTime };
        
        _logger.LogInformation("Starting comprehensive index defragmentation for workspace: {WorkspacePath}", workspacePath);
        
        try
        {
            // Phase 1: Analyze current index state
            var analysisResult = await AnalyzeIndexFragmentationAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            result.InitialFragmentationLevel = analysisResult.FragmentationPercentage;
            result.InitialSegmentCount = analysisResult.SegmentCount;
            result.InitialSizeBytes = analysisResult.SizeBytes;
            
            _logger.LogInformation("Index analysis complete - Fragmentation: {Fragmentation:F1}%, Segments: {Segments}, Size: {SizeMB:F2} MB",
                analysisResult.FragmentationPercentage, analysisResult.SegmentCount, analysisResult.SizeBytes / 1024.0 / 1024.0);
            
            // Determine if defragmentation is needed
            if (analysisResult.FragmentationPercentage < options.MinFragmentationThreshold)
            {
                result.ActionTaken = DefragmentationAction.Skipped;
                result.Reason = $"Fragmentation level {analysisResult.FragmentationPercentage:F1}% below threshold {options.MinFragmentationThreshold}%";
                _logger.LogInformation("Defragmentation skipped: {Reason}", result.Reason);
                return result;
            }
            
            // Phase 2: Create backup if requested
            if (options.CreateBackup)
            {
                result.BackupPath = await CreateIndexBackupAsync(workspacePath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Index backup created at: {BackupPath}", result.BackupPath);
            }
            
            // Phase 3: Perform defragmentation based on severity
            if (analysisResult.FragmentationPercentage >= options.FullDefragmentationThreshold)
            {
                // Full defragmentation for severely fragmented indexes
                await PerformFullDefragmentationAsync(workspacePath, options, result, cancellationToken).ConfigureAwait(false);
                result.ActionTaken = DefragmentationAction.FullDefragmentation;
            }
            else
            {
                // Incremental defragmentation for moderately fragmented indexes  
                await PerformIncrementalDefragmentationAsync(workspacePath, options, result, cancellationToken).ConfigureAwait(false);
                result.ActionTaken = DefragmentationAction.IncrementalDefragmentation;
            }
            
            // Phase 4: Post-defragmentation analysis
            var finalAnalysis = await AnalyzeIndexFragmentationAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            result.FinalFragmentationLevel = finalAnalysis.FragmentationPercentage;
            result.FinalSegmentCount = finalAnalysis.SegmentCount;
            result.FinalSizeBytes = finalAnalysis.SizeBytes;
            
            // Calculate improvements
            result.FragmentationReduction = result.InitialFragmentationLevel - result.FinalFragmentationLevel;
            result.SegmentReduction = result.InitialSegmentCount - result.FinalSegmentCount;
            result.SizeReductionBytes = result.InitialSizeBytes - result.FinalSizeBytes;
            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartTime;
            result.Success = true;
            
            _logger.LogInformation("Defragmentation completed successfully - " +
                "Fragmentation: {InitialFrag:F1}% → {FinalFrag:F1}% (-{FragReduction:F1}%), " +
                "Segments: {InitialSegs} → {FinalSegs} (-{SegReduction}), " +
                "Size: {InitialMB:F2} → {FinalMB:F2} MB ({SizeReductionMB:+F2} MB), " +
                "Duration: {Duration}",
                result.InitialFragmentationLevel, result.FinalFragmentationLevel, result.FragmentationReduction,
                result.InitialSegmentCount, result.FinalSegmentCount, result.SegmentReduction,
                result.InitialSizeBytes / 1024.0 / 1024.0, result.FinalSizeBytes / 1024.0 / 1024.0,
                result.SizeReductionBytes / 1024.0 / 1024.0, result.Duration);
                
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartTime;
            
            _logger.LogError(ex, "Index defragmentation failed for workspace: {WorkspacePath}", workspacePath);
            
            // If we created a backup and defragmentation failed, offer to restore
            if (!string.IsNullOrEmpty(result.BackupPath) && options.RestoreOnFailure)
            {
                try
                {
                    await RestoreIndexFromBackupAsync(workspacePath, result.BackupPath, cancellationToken).ConfigureAwait(false);
                    result.BackupRestored = true;
                    _logger.LogInformation("Index restored from backup due to defragmentation failure");
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Failed to restore index from backup");
                    result.RestoreError = restoreEx.Message;
                }
            }
            
            return result;
        }
    }

    /// <summary>
    /// Clear index asynchronously
    /// </summary>
    public async Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        // Remove from dictionary and dispose
        if (_indexes.TryRemove(indexPath, out var context))
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
        }
        
        // Clear the index directory
        await ClearIndexAsync(indexPath).ConfigureAwait(false);
        
        _logger.LogInformation("Cleared index for workspace {WorkspacePath}", workspacePath);
    }
    
    /// <summary>
    /// Get the physical index path for a workspace - single source of truth for path resolution
    /// </summary>
    public async Task<string> GetPhysicalIndexPathAsync(string workspacePath)
    {
        return await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
    }
    
    #endregion
    
    #region Health Check
    
    /// <summary>
    /// Represents the corruption status of a Lucene index
    /// </summary>
    public record IndexCorruptionStatus(bool IsCorrupted, string Details, Exception? Exception = null);
    
    /// <summary>
    /// Checks a specific index for corruption using Lucene.NET's CheckIndex utility
    /// </summary>
    /// <param name="indexPath">Path to the index directory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Corruption status with details</returns>
    /// <summary>
    /// Repairs a corrupted index by removing bad segments
    /// </summary>
    public async Task<IndexRepairResult> RepairCorruptedIndexAsync(string workspacePath, IndexRepairOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = new IndexRepairResult { StartTime = DateTime.UtcNow };
        options ??= new IndexRepairOptions();
        
        try
        {
            ThrowIfDisposed();
            var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
            
            _logger.LogWarning("Starting index repair for workspace: {WorkspacePath} at path: {IndexPath}", workspacePath, indexPath);
            
            // First check if the index is actually corrupted
            var corruptionStatus = await CheckIndexCorruptionAsync(indexPath, cancellationToken).ConfigureAwait(false);
            if (!corruptionStatus.IsCorrupted)
            {
                result.Success = true;
                result.Message = "Index is not corrupted, no repair needed";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
            
            // Close any open contexts for this index
            if (_indexes.TryRemove(indexPath, out var context))
            {
                await DisposeContextAsync(context).ConfigureAwait(false);
            }
            
            // Create backup if requested
            if (options.CreateBackup)
            {
                try
                {
                    var backupPath = options.BackupPath ?? $"{indexPath}_backup_{DateTime.UtcNow:yyyyMMddHHmmss}";
                    // Create backup directory and copy files
                    await Task.Run(() =>
                    {
                        System.IO.Directory.CreateDirectory(backupPath);
                        foreach (var file in System.IO.Directory.GetFiles(indexPath))
                        {
                            var fileName = Path.GetFileName(file);
                            var destFile = Path.Combine(backupPath, fileName);
                            File.Copy(file, destFile, true);
                        }
                    }, cancellationToken).ConfigureAwait(false);
                    result.BackupPath = backupPath;
                    _logger.LogInformation("Created index backup at: {BackupPath}", backupPath);
                }
                catch (Exception backupEx)
                {
                    _logger.LogWarning(backupEx, "Failed to create backup before repair, continuing anyway");
                }
            }
            
            // Perform the repair
            var repairResult = await Task.Run(() =>
            {
                try
                {
                    using var directory = FSDirectory.Open(indexPath);
                    var checkIndex = new CheckIndex(directory);
                    
                    // First get the status
                    var status = checkIndex.DoCheckIndex();
                    
                    if (!status.Clean && options.RemoveBadSegments)
                    {
                        _logger.LogWarning("Index has {BadSegments} bad segments with {LostDocs} lost documents. Attempting repair...",
                            status.NumBadSegments, status.TotLoseDocCount);
                        
                        // FixIndex removes bad segments - data in those segments will be lost
                        checkIndex.FixIndex(status);
                        
                        result.RemovedSegments = status.NumBadSegments;
                        result.LostDocuments = (int)status.TotLoseDocCount;
                        
                        _logger.LogInformation("Repair completed. Removed {RemovedSegments} segments, lost {LostDocs} documents",
                            result.RemovedSegments, result.LostDocuments);
                    }
                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during index repair");
                    result.Exception = ex;
                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);
            
            if (!repairResult)
            {
                result.Success = false;
                result.Message = $"Repair failed: {result.Exception?.Message ?? "Unknown error"}";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
            
            // Validate the repair if requested
            if (options.ValidateAfterRepair)
            {
                var postRepairStatus = await CheckIndexCorruptionAsync(indexPath, cancellationToken).ConfigureAwait(false);
                if (postRepairStatus.IsCorrupted)
                {
                    result.Success = false;
                    result.Message = $"Index still corrupted after repair: {postRepairStatus.Details}";
                    
                    // Restore from backup if available
                    if (!string.IsNullOrEmpty(result.BackupPath))
                    {
                        try
                        {
                            await RestoreIndexFromBackupAsync(workspacePath, result.BackupPath, cancellationToken).ConfigureAwait(false);
                            result.Message += " - Restored from backup";
                        }
                        catch (Exception restoreEx)
                        {
                            _logger.LogError(restoreEx, "Failed to restore from backup after failed repair");
                        }
                    }
                }
                else
                {
                    result.Success = true;
                    result.Message = $"Index successfully repaired. Removed {result.RemovedSegments} segments, lost {result.LostDocuments} documents";
                }
            }
            else
            {
                result.Success = true;
                result.Message = $"Repair completed. Removed {result.RemovedSegments} segments, lost {result.LostDocuments} documents";
            }
            
            result.EndTime = DateTime.UtcNow;
            _logger.LogInformation("Index repair completed in {Duration}ms with result: {Result}", 
                result.Duration.TotalMilliseconds, result.Message);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during index repair");
            result.Success = false;
            result.Message = $"Unexpected error: {ex.Message}";
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }
    
    private async Task<IndexCorruptionStatus> CheckIndexCorruptionAsync(string indexPath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var directory = FSDirectory.Open(indexPath);
                    
                    // Skip corruption check if index doesn't exist
                    if (!DirectoryReader.IndexExists(directory))
                    {
                        return new IndexCorruptionStatus(false, "Index does not exist");
                    }
                    
                    // Use CheckIndex to validate index integrity
                    var checkIndex = new CheckIndex(directory);
                    
                    // Perform the check (this can be expensive for large indexes)
                    var status = checkIndex.DoCheckIndex();
                    
                    // Check if any issues were found
                    if (!status.Clean)
                    {
                        var issues = new List<string>();
                        
                        if (status.MissingSegments) issues.Add("missing segments");
                        if (status.NumBadSegments > 0) issues.Add($"{status.NumBadSegments} bad segments");
                        if (status.TotLoseDocCount > 0) issues.Add($"{status.TotLoseDocCount} lost documents");
                        
                        var details = $"Index corruption detected: {string.Join(", ", issues)}";
                        
                        return new IndexCorruptionStatus(true, details);
                    }
                    
                    return new IndexCorruptionStatus(false, "Index is clean and healthy");
                }
                catch (IndexFormatTooOldException ex)
                {
                    return new IndexCorruptionStatus(true, $"Index format too old: {ex.Message}", ex);
                }
                catch (IndexFormatTooNewException ex)
                {
                    return new IndexCorruptionStatus(true, $"Index format too new: {ex.Message}", ex);
                }
                catch (CorruptIndexException ex)
                {
                    return new IndexCorruptionStatus(true, $"Index corruption exception: {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    // For other exceptions, we can't determine corruption status
                    return new IndexCorruptionStatus(false, $"Unable to check corruption: {ex.Message}", ex);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new IndexCorruptionStatus(false, "Corruption check was cancelled");
        }
        catch (Exception ex)
        {
            return new IndexCorruptionStatus(false, $"Corruption check failed: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Performs a comprehensive health check of the Lucene index service
    /// </summary>
    public async Task<IndexHealthCheckResult> CheckHealthAsync(bool includeAutoRepair = false, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var healthyIndexes = 0;
        var unhealthyIndexes = 0;
        var stuckLocks = 0;
        
        try
        {
            // Check memory indexes
            var (projectMemoryExists, localMemoryExists) = await CheckMemoryIndexHealthAsync().ConfigureAwait(false);
            data["projectMemoryIndex"] = projectMemoryExists ? "healthy" : "missing";
            data["localMemoryIndex"] = localMemoryExists ? "healthy" : "missing";
            
            if (projectMemoryExists) healthyIndexes++;
            else unhealthyIndexes++;
            
            if (localMemoryExists) healthyIndexes++;
            else unhealthyIndexes++;
            
            // Check all registered indexes for locks and corruption
            var corruptedIndexes = 0;
            foreach (var kvp in _indexes)
            {
                var indexName = Path.GetFileName(kvp.Key);
                var (isLocked, isStuck) = await IsIndexLockedAsync(kvp.Key).ConfigureAwait(false);
                
                if (isStuck)
                {
                    stuckLocks++;
                    data[$"index_{indexName}"] = "stuck_lock";
                    unhealthyIndexes++;
                }
                else if (isLocked)
                {
                    data[$"index_{indexName}"] = "locked";
                    // Locked indexes are still considered healthy if not stuck
                    healthyIndexes++;
                }
                else
                {
                    // Check for corruption when index is not locked
                    var corruptionStatus = await CheckIndexCorruptionAsync(kvp.Key, cancellationToken).ConfigureAwait(false);
                    if (corruptionStatus.IsCorrupted)
                    {
                        corruptedIndexes++;
                        data[$"index_{indexName}"] = "corrupted";
                        data[$"index_{indexName}_corruption_details"] = corruptionStatus.Details;
                        
                        // Attempt automatic repair for corrupted indexes
                        if (includeAutoRepair)
                        {
                            try
                            {
                                _logger.LogWarning("Attempting automatic repair for corrupted index: {IndexName}", indexName);
                                
                                // Get workspace path from index path mapping
                                var workspacePath = await GetWorkspaceFromIndexPathAsync(kvp.Key).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(workspacePath))
                                {
                                    var repairOptions = new IndexRepairOptions
                                    {
                                        CreateBackup = true,
                                        RemoveBadSegments = true,
                                        ValidateAfterRepair = true
                                    };
                                    
                                    var repairResult = await RepairCorruptedIndexAsync(workspacePath, repairOptions, cancellationToken).ConfigureAwait(false);
                                    
                                    if (repairResult.Success)
                                    {
                                        data[$"index_{indexName}_repair"] = "success";
                                        data[$"index_{indexName}_repair_message"] = repairResult.Message;
                                        // Index is now healthy after repair
                                        healthyIndexes++;
                                        corruptedIndexes--;
                                    }
                                    else
                                    {
                                        data[$"index_{indexName}_repair"] = "failed";
                                        data[$"index_{indexName}_repair_error"] = repairResult.Message;
                                        unhealthyIndexes++;
                                    }
                                }
                                else
                                {
                                    data[$"index_{indexName}_repair"] = "skipped";
                                    data[$"index_{indexName}_repair_reason"] = "Could not determine workspace path";
                                    unhealthyIndexes++;
                                }
                            }
                            catch (Exception repairEx)
                            {
                                _logger.LogError(repairEx, "Failed to auto-repair corrupted index: {IndexName}", indexName);
                                data[$"index_{indexName}_repair"] = "error";
                                data[$"index_{indexName}_repair_error"] = repairEx.Message;
                                unhealthyIndexes++;
                            }
                        }
                        else
                        {
                            unhealthyIndexes++;
                        }
                    }
                    else
                    {
                        data[$"index_{indexName}"] = "healthy";
                        healthyIndexes++;
                    }
                }
            }
            
            data["corruptedIndexes"] = corruptedIndexes;
            
            data["totalIndexes"] = _indexes.Count + 2; // Include memory indexes
            data["healthyIndexes"] = healthyIndexes;
            data["unhealthyIndexes"] = unhealthyIndexes;
            data["stuckLocks"] = stuckLocks;
            
            if (stuckLocks > 0 || corruptedIndexes > 0)
            {
                var issues = new List<string>();
                if (stuckLocks > 0) issues.Add($"{stuckLocks} stuck locks");
                if (corruptedIndexes > 0) issues.Add($"{corruptedIndexes} corrupted indexes");
                
                return new IndexHealthCheckResult(
                    IndexHealthCheckResult.HealthStatus.Unhealthy, 
                    $"Critical issues found: {string.Join(", ", issues)}", 
                    data: data);
            }
            else if (unhealthyIndexes > 0)
            {
                return new IndexHealthCheckResult(
                    IndexHealthCheckResult.HealthStatus.Degraded, 
                    $"{unhealthyIndexes} indexes are missing or unhealthy", 
                    data: data);
            }
            else
            {
                return new IndexHealthCheckResult(
                    IndexHealthCheckResult.HealthStatus.Healthy, 
                    $"All {healthyIndexes} indexes are healthy", 
                    data: data);
            }
        }
        catch (Exception ex)
        {
            return new IndexHealthCheckResult(
                IndexHealthCheckResult.HealthStatus.Unhealthy, 
                "Health check failed", 
                data, 
                ex);
        }
    }
    
    /// <summary>
    /// Gets the workspace path from an index path by checking metadata
    /// </summary>
    private async Task<string?> GetWorkspaceFromIndexPathAsync(string indexPath)
    {
        try
        {
            var metadataPath = _pathResolution.GetWorkspaceMetadataPath();
            if (File.Exists(metadataPath))
            {
                var json = await File.ReadAllTextAsync(metadataPath).ConfigureAwait(false);
                var metadata = JsonSerializer.Deserialize<WorkspaceMetadata>(json);
                
                if (metadata?.Indexes != null)
                {
                    // Find the workspace that maps to this index path
                    foreach (var kvp in metadata.Indexes)
                    {
                        if (kvp.Value.HashPath == indexPath)
                        {
                            return kvp.Value.OriginalPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving workspace from index path: {IndexPath}", indexPath);
        }
        
        return null;
    }
    
    private class WorkspaceMetadata
    {
        public Dictionary<string, WorkspaceIndexInfo> Indexes { get; set; } = new();
    }
    
    private class WorkspaceIndexInfo
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string HashPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
    }
    
    #endregion
    
    #region Async File System Helpers
    
    private static async Task<bool> FileExistsAsync(string path)
    {
        return await Task.Run(() => File.Exists(path)).ConfigureAwait(false);
    }
    
    private static async Task<bool> DirectoryExistsAsync(string path)
    {
        return await Task.Run(() => System.IO.Directory.Exists(path)).ConfigureAwait(false);
    }
    
    private static async Task<DateTime> GetFileLastWriteTimeUtcAsync(string path)
    {
        return await Task.Run(() => File.GetLastWriteTimeUtc(path)).ConfigureAwait(false);
    }
    
    private static async Task CreateDirectoryAsync(string path)
    {
        await Task.Run(() => System.IO.Directory.CreateDirectory(path)).ConfigureAwait(false);
    }
    
    private static async Task<string[]> GetFilesAsync(string path, string searchPattern)
    {
        return await Task.Run(() => System.IO.Directory.GetFiles(path, searchPattern)).ConfigureAwait(false);
    }
    
    #endregion
    
    #region Index Defragmentation Support Methods
    
    /// <summary>
    /// Analyzes index fragmentation and provides metrics
    /// </summary>
    private async Task<IndexFragmentationAnalysis> AnalyzeIndexFragmentationAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        var analysis = new IndexFragmentationAnalysis();
        
        using (await _writerLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_indexes.TryGetValue(indexPath, out var context))
            {
                using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        var directory = context.Directory;
                        
                        // Get basic index info
                        using var reader = DirectoryReader.Open(directory);
                        analysis.SegmentCount = reader.Leaves.Count;
                        analysis.DocumentCount = reader.NumDocs;
                        analysis.DeletedDocumentCount = reader.NumDeletedDocs;
                        
                        // Calculate size
                        var files = directory.ListAll();
                        long totalSize = 0;
                        foreach (var file in files)
                        {
                            totalSize += directory.FileLength(file);
                        }
                        analysis.SizeBytes = totalSize;
                        
                        // Calculate fragmentation percentage
                        // More segments = more fragmentation
                        // More deleted docs = more fragmentation
                        var segmentFragmentation = Math.Min(100.0, (analysis.SegmentCount - 1) * 10.0); // 1 segment = 0%, 10+ segments = 90%
                        var deletionFragmentation = analysis.DocumentCount > 0 
                            ? (analysis.DeletedDocumentCount / (double)(analysis.DocumentCount + analysis.DeletedDocumentCount)) * 100.0
                            : 0.0;
                        
                        analysis.FragmentationPercentage = Math.Max(segmentFragmentation, deletionFragmentation);
                        
                        _logger.LogDebug("Index fragmentation analysis - Segments: {Segments}, Docs: {Docs}, Deleted: {Deleted}, " +
                            "Size: {SizeKB} KB, Fragmentation: {Fragmentation:F1}%",
                            analysis.SegmentCount, analysis.DocumentCount, analysis.DeletedDocumentCount,
                            analysis.SizeBytes / 1024, analysis.FragmentationPercentage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to analyze index fragmentation for {IndexPath}", indexPath);
                        throw;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"Index not found: {indexPath}");
            }
        }
        
        return analysis;
    }
    
    /// <summary>
    /// Performs full defragmentation (complete rebuild and optimization)
    /// </summary>
    private async Task PerformFullDefragmentationAsync(string workspacePath, IndexDefragmentationOptions options, 
        IndexDefragmentationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing FULL defragmentation for workspace: {WorkspacePath}", workspacePath);
        
        // Force merge to 1 segment for maximum optimization
        await ForceMergeAsync(workspacePath, 1, cancellationToken).ConfigureAwait(false);
        
        // Additional cleanup steps for full defragmentation
        var writer = await GetIndexWriterAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        
        // Force merge deletes (clean up deleted documents)
        writer.ForceMergeDeletes();
        
        // Commit all changes
        await CommitAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        
        result.DefragmentationSteps.Add("Full segment merge to 1 segment");
        result.DefragmentationSteps.Add("Forced merge of deleted documents");
        result.DefragmentationSteps.Add("Index commit");
    }
    
    /// <summary>
    /// Performs incremental defragmentation (targeted optimization)
    /// </summary>
    private async Task PerformIncrementalDefragmentationAsync(string workspacePath, IndexDefragmentationOptions options,
        IndexDefragmentationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing INCREMENTAL defragmentation for workspace: {WorkspacePath}", workspacePath);
        
        // Merge to a reasonable number of segments (not just 1)
        var targetSegments = Math.Max(2, options.TargetSegmentCount);
        await ForceMergeAsync(workspacePath, targetSegments, cancellationToken).ConfigureAwait(false);
        
        result.DefragmentationSteps.Add($"Incremental segment merge to {targetSegments} segments");
        result.DefragmentationSteps.Add("Index commit");
    }
    
    /// <summary>
    /// Creates a backup of the index before defragmentation
    /// </summary>
    private async Task<string> CreateIndexBackupAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        var backupPath = indexPath + "_backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        
        _logger.LogInformation("Creating index backup from {Source} to {Backup}", indexPath, backupPath);
        
        await Task.Run(() =>
        {
            if (System.IO.Directory.Exists(backupPath))
            {
                System.IO.Directory.Delete(backupPath, true);
            }
            
            System.IO.Directory.CreateDirectory(backupPath);
            
            // Copy all index files
            foreach (var file in System.IO.Directory.GetFiles(indexPath))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(backupPath, fileName);
                File.Copy(file, destFile, true);
            }
        }, cancellationToken).ConfigureAwait(false);
        
        return backupPath;
    }
    
    /// <summary>
    /// Restores index from backup if defragmentation fails
    /// </summary>
    private async Task RestoreIndexFromBackupAsync(string workspacePath, string backupPath, CancellationToken cancellationToken)
    {
        var indexPath = await GetIndexPathAsync(workspacePath).ConfigureAwait(false);
        
        _logger.LogWarning("Restoring index from backup {Backup} to {Index}", backupPath, indexPath);
        
        // Close any existing writers/readers for this index
        await CloseWriterAsync(workspacePath, true, cancellationToken).ConfigureAwait(false);
        
        await Task.Run(() =>
        {
            // Remove corrupted index
            if (System.IO.Directory.Exists(indexPath))
            {
                System.IO.Directory.Delete(indexPath, true);
            }
            
            System.IO.Directory.CreateDirectory(indexPath);
            
            // Restore from backup
            foreach (var file in System.IO.Directory.GetFiles(backupPath))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(indexPath, fileName);
                File.Copy(file, destFile, true);
            }
        }, cancellationToken).ConfigureAwait(false);
    }
    
    #endregion
}

/// <summary>
/// Options for controlling index defragmentation behavior
/// </summary>
public class IndexDefragmentationOptions
{
    /// <summary>
    /// Minimum fragmentation percentage to trigger defragmentation (default: 20%)
    /// </summary>
    public double MinFragmentationThreshold { get; set; } = 20.0;
    
    /// <summary>
    /// Fragmentation percentage threshold for full defragmentation (default: 60%)
    /// Above this threshold, perform full defragmentation; below this, perform incremental
    /// </summary>
    public double FullDefragmentationThreshold { get; set; } = 60.0;
    
    /// <summary>
    /// Target number of segments for incremental defragmentation (default: 5)
    /// </summary>
    public int TargetSegmentCount { get; set; } = 5;
    
    /// <summary>
    /// Whether to create a backup before defragmentation (default: true)
    /// </summary>
    public bool CreateBackup { get; set; } = true;
    
    /// <summary>
    /// Whether to restore from backup if defragmentation fails (default: true)
    /// </summary>
    public bool RestoreOnFailure { get; set; } = true;
}

/// <summary>
/// Result of index defragmentation operation
/// </summary>
public class IndexDefragmentationResult
{
    public DateTime StartTime { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Exception? Exception { get; set; }
    
    // Fragmentation metrics
    public double InitialFragmentationLevel { get; set; }
    public double FinalFragmentationLevel { get; set; }
    public double FragmentationReduction { get; set; }
    
    // Segment metrics  
    public int InitialSegmentCount { get; set; }
    public int FinalSegmentCount { get; set; }
    public int SegmentReduction { get; set; }
    
    // Size metrics
    public long InitialSizeBytes { get; set; }
    public long FinalSizeBytes { get; set; }
    public long SizeReductionBytes { get; set; }
    
    // Operation details
    public DefragmentationAction ActionTaken { get; set; }
    public string? Reason { get; set; }
    public List<string> DefragmentationSteps { get; set; } = new();
    
    // Backup/restore info
    public string? BackupPath { get; set; }
    public bool BackupRestored { get; set; }
    public string? RestoreError { get; set; }
}

/// <summary>
/// Type of defragmentation action performed
/// </summary>
public enum DefragmentationAction
{
    Skipped,
    IncrementalDefragmentation,
    FullDefragmentation
}

/// <summary>
/// Index fragmentation analysis results
/// </summary>
public class IndexFragmentationAnalysis
{
    public int SegmentCount { get; set; }
    public int DocumentCount { get; set; }
    public int DeletedDocumentCount { get; set; }
    public long SizeBytes { get; set; }
    public double FragmentationPercentage { get; set; }
}