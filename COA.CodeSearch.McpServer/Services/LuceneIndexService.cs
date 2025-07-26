using COA.CodeSearch.McpServer.Infrastructure;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
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
    private readonly StandardAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly TimeSpan _lockTimeout;
    private readonly AsyncLock _writerLock = new("writer-lock");  // Using AsyncLock to enforce timeout usage
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
    
    private class IndexContext
    {
        public FSDirectory Directory { get; set; } = null!;
        public IndexWriter? Writer { get; set; }
        public DirectoryReader? Reader { get; set; }
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
    }
    
    public LuceneIndexService(ILogger<LuceneIndexService> logger, IConfiguration configuration, IPathResolutionService pathResolution)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _analyzer = new StandardAnalyzer(Version);
        
        // Default 15 minute timeout for stuck locks (same as intranet)
        _lockTimeout = TimeSpan.FromMinutes(configuration.GetValue<int>("Lucene:LockTimeoutMinutes", 15));
        
        // Note: Stuck lock cleanup now happens early in Program.cs before services start
        
        // Clean up any memory entries from metadata on startup
        _ = Task.Run(async () => await CleanupMemoryEntriesFromMetadataAsync().ConfigureAwait(false));
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
                    // Check for stuck locks first
                    var (isLocked, isStuck) = await IsIndexLockedAsync(indexPath).ConfigureAwait(false);
                    if (isLocked)
                    {
                        if (isStuck)
                        {
                            // Check if this is a protected memory index
                            if (IsProtectedMemoryIndex(indexPath))
                            {
                                _logger.LogError("CRITICAL: Memory index at {Path} has a stuck lock. " +
                                               "This indicates improper disposal! The memory index may be corrupted. " +
                                               "Manual intervention required: delete the write.lock file and restart.",
                                               indexPath);
                                throw new InvalidOperationException($"Memory index at {indexPath} has a stuck lock. " +
                                                                  "This indicates improper disposal. Please manually delete the write.lock file and restart.");
                            }
                            else
                            {
                                // For non-memory indexes, use the existing clear strategy
                                _logger.LogWarning("Index at {Path} has stuck lock. Clearing index for rebuild.", indexPath);
                                await ClearIndexAsync(indexPath).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // Lock exists but is recent - another process may be using it
                            throw new InvalidOperationException($"Index at {indexPath} is currently locked by another process");
                        }
                    }
                    
                    context = CreateIndexContext(indexPath, forceRecreate);
                    
                    // Try to add to dictionary - handle race condition
                    if (!_indexes.TryAdd(indexPath, context))
                    {
                        // Another thread beat us, dispose our resources
                        var ourContext = context;
                        context = _indexes[indexPath];
                        await DisposeContextAsync(ourContext);
                    }
                    // Success - index created
                }
            }
        }
        
        // Use context-specific lock for writer operations
        using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            // Ensure writer is still valid
            if (context.Writer == null)
            {
                context.Writer = CreateWriter(context.Directory, forceRecreate, context.Path);
            }
            
            context.LastAccessed = DateTime.UtcNow;
            return context.Writer;
        }
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
            writer = CreateWriter(directory, forceRecreate, indexPath);
            
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
    
    private IndexWriter CreateWriter(FSDirectory directory, bool forceRecreate, string? indexPath = null)
    {
        var config = new IndexWriterConfig(Version, _analyzer)
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
    
    private async Task<IndexMetadata> LoadMetadataAsync(string metadataPath)
    {
        if (await FileExistsAsync(metadataPath).ConfigureAwait(false))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath).ConfigureAwait(false);
                return JsonSerializer.Deserialize<IndexMetadata>(json) ?? new IndexMetadata();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load metadata from {Path}", metadataPath);
            }
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
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(metadata, options);
            await File.WriteAllTextAsync(metadataPath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata to {Path}", metadataPath);
        }
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
                var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
                
                if (lockAge > _lockTimeout)
                {
                    // Get the hash from the directory name
                    var hashPath = Path.GetFileName(indexDir);
                    var originalPath = await GetOriginalPathAsync(hashPath).ConfigureAwait(false);
                    var pathInfo = originalPath != null ? $" (original: {originalPath})" : "";
                    
                    _logger.LogError("CRITICAL: Found stuck lock at {Path}{PathInfo}, age: {Age}. " +
                                   "This indicates improper disposal of index writers! " +
                                   "Manual intervention required: delete the write.lock file after ensuring no processes are using the index.",
                                   lockPath, pathInfo, lockAge);
                }
            }
        }
        
        // Also check memory indexes
        await DiagnoseMemoryIndexLocksAsync().ConfigureAwait(false);
    }
    
    /// <summary>
    /// Diagnose stuck write.lock files in memory indexes (project-memory and local-memory).
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
            
            var lockPath = Path.Combine(memoryPath, WriteLockFilename);
            
            if (await FileExistsAsync(lockPath).ConfigureAwait(false))
            {
                var lockAge = DateTime.UtcNow - await GetFileLastWriteTimeUtcAsync(lockPath).ConfigureAwait(false);
                var memoryType = Path.GetFileName(memoryPath);
                
                if (lockAge > _lockTimeout)
                {
                    _logger.LogError("CRITICAL: Found stuck write.lock in {MemoryType} memory index, age: {Age}. " +
                                   "This indicates improper disposal! The memory index may be corrupted. " +
                                   "Manual intervention required: delete {Path} after ensuring no processes are using it.",
                                   memoryType, lockAge, lockPath);
                }
                else
                {
                    _logger.LogDebug("Found recent write.lock in {MemoryType} memory index ({Age}) - likely in use", 
                                    memoryType, lockAge);
                }
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
        _analyzer?.Dispose();
        _writerLock?.Dispose();
        _metadataLock?.Dispose();
        
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
    /// Get index searcher asynchronously
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
        
        using (await context.Lock.LockAsync(cancellationToken).ConfigureAwait(false))
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
            if (context.Reader == null)
            {
                throw new InvalidOperationException($"Failed to create or refresh index reader for workspace: {workspacePath}");
            }
            return new IndexSearcher(context.Reader);
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
    /// Optimize index asynchronously (merge segments for better performance)
    /// </summary>
    public async Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var writer = await GetIndexWriterAsync(workspacePath, cancellationToken);
        
        // ForceMerge is the modern equivalent of Optimize
        writer.ForceMerge(1); // Merge down to a single segment
        await CommitAsync(workspacePath, cancellationToken);
        
        _logger.LogInformation("Optimized index at {Path}", await GetIndexPathAsync(workspacePath).ConfigureAwait(false));
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
    /// Performs a comprehensive health check of the Lucene index service
    /// </summary>
    public async Task<IndexHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
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
            
            // Check all registered indexes
            foreach (var kvp in _indexes)
            {
                var (isLocked, isStuck) = await IsIndexLockedAsync(kvp.Key).ConfigureAwait(false);
                if (isStuck)
                {
                    stuckLocks++;
                    data[$"index_{Path.GetFileName(kvp.Key)}"] = "stuck_lock";
                }
                else if (isLocked)
                {
                    data[$"index_{Path.GetFileName(kvp.Key)}"] = "locked";
                }
                else
                {
                    data[$"index_{Path.GetFileName(kvp.Key)}"] = "healthy";
                    healthyIndexes++;
                }
            }
            
            data["totalIndexes"] = _indexes.Count + 2; // Include memory indexes
            data["healthyIndexes"] = healthyIndexes;
            data["unhealthyIndexes"] = unhealthyIndexes;
            data["stuckLocks"] = stuckLocks;
            
            if (stuckLocks > 0)
            {
                return new IndexHealthCheckResult(
                    IndexHealthCheckResult.HealthStatus.Unhealthy, 
                    $"Found {stuckLocks} stuck locks", 
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
}