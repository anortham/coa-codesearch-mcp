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
/// Improved Lucene index service with better lock handling and thread safety.
/// 
/// Design Decisions (based on Lucene.NET 4.8 docs review):
/// 1. Long-lived writers: We maintain writers for performance in an interactive MCP server context.
///    The docs recommend short-lived writers for batch operations, but our use case benefits from
///    keeping writers open to avoid constant open/close overhead.
/// 2. Lock recovery: Implements automatic recovery for stuck locks, especially critical for memory indexes.
/// 3. Thread safety: Uses SemaphoreSlim for synchronization. IndexWriter itself is thread-safe.
/// 4. Memory protection: Special handling ensures memory indexes are never cleared, only locks removed.
/// 
/// Trade-offs:
/// - Long-lived writers may hold resources but provide better interactive performance
/// - Risk of stuck locks is mitigated by our recovery mechanism
/// - Memory indexes use CREATE_OR_APPEND to preserve data across sessions
/// </summary>
public class LuceneIndexService : ILuceneIndexService, ILuceneWriterManager
{
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPathResolutionService _pathResolution;
    private readonly StandardAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly TimeSpan _lockTimeout;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private volatile bool _disposed;
    
    private const string WriteLockFilename = "write.lock";
    private const string SegmentsFilename = "segments.gen";
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    private readonly ConcurrentDictionary<string, IndexMetadata> _metadataCache = new();
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _recoveryAttempts = new();
    private const int MaxRecoveryAttempts = 3;
    
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
        public readonly SemaphoreSlim Lock = new(1, 1);
        public int PendingChanges { get; set; }
    }
    
    public LuceneIndexService(ILogger<LuceneIndexService> logger, IConfiguration configuration, IPathResolutionService pathResolution)
    {
        _logger = logger;
        _configuration = configuration;
        _pathResolution = pathResolution;
        _analyzer = new StandardAnalyzer(Version);
        
        // Default 15 minute timeout for stuck locks (same as intranet)
        _lockTimeout = TimeSpan.FromMinutes(configuration.GetValue<int>("Lucene:LockTimeoutMinutes", 15));
        
        // Clean up any memory entries from metadata on startup
        CleanupMemoryEntriesFromMetadata();
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
                            // Check if this is a protected memory index
                            if (IsProtectedMemoryIndex(indexPath))
                            {
                                // Check recovery attempts
                                var attempts = _recoveryAttempts.GetOrAdd(indexPath, 0);
                                if (attempts >= MaxRecoveryAttempts)
                                {
                                    _logger.LogError("Memory index at {Path} has stuck lock and exceeded max recovery attempts ({Max}). Manual intervention required.", 
                                        indexPath, MaxRecoveryAttempts);
                                    throw new InvalidOperationException($"Memory index at {indexPath} has a stuck lock after {MaxRecoveryAttempts} recovery attempts. Please manually remove the write.lock file.");
                                }
                                
                                _logger.LogWarning("Memory index at {Path} has stuck lock. Attempting automatic recovery.", indexPath);
                                
                                // Try to remove only the lock file
                                if (TryRemoveWriteLock(indexPath))
                                {
                                    _logger.LogInformation("Successfully recovered memory index at {Path} by removing stuck lock", indexPath);
                                    // Continue with normal index creation
                                }
                                else
                                {
                                    _logger.LogError("Failed to remove stuck lock from memory index at {Path}", indexPath);
                                    throw new InvalidOperationException($"Memory index at {indexPath} has a stuck lock that could not be removed automatically.");
                                }
                            }
                            else
                            {
                                // For non-memory indexes, use the existing clear strategy
                                _logger.LogWarning("Index at {Path} has stuck lock. Clearing index for rebuild.", indexPath);
                                ClearIndex(indexPath);
                            }
                        }
                        else
                        {
                            // Lock exists but is recent - another process may be using it
                            throw new InvalidOperationException($"Index at {indexPath} is currently locked by another process");
                        }
                    }
                    
                    context = CreateIndexContext(indexPath, forceRecreate);
                    _indexes.TryAdd(indexPath, context);
                    
                    // Reset recovery attempts on successful creation
                    ResetRecoveryAttempts(indexPath);
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
                context.Writer = CreateWriter(context.Directory, forceRecreate, context.Path);
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
    /// Check if a path is a protected memory index
    /// </summary>
    private bool IsProtectedMemoryIndex(string indexPath)
    {
        return _pathResolution.IsProtectedPath(indexPath);
    }
    
    /// <summary>
    /// Safely remove only the write.lock file from an index without deleting any data
    /// </summary>
    private bool TryRemoveWriteLock(string indexPath)
    {
        var lockPath = Path.Combine(indexPath, WriteLockFilename);
        
        if (!File.Exists(lockPath))
        {
            _logger.LogDebug("No write.lock file found at {Path}", lockPath);
            return true;
        }
        
        try
        {
            // Log the recovery attempt
            var recoveryCount = _recoveryAttempts.AddOrUpdate(indexPath, 1, (key, count) => count + 1);
            _logger.LogWarning("Attempting to remove write.lock from {Path} (attempt {Count}/{Max})", 
                indexPath, recoveryCount, MaxRecoveryAttempts);
            
            // Verify this is indeed a stuck lock before removing
            var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
            if (lockAge < _lockTimeout)
            {
                _logger.LogInformation("Lock at {Path} is not stuck (age: {Age}), skipping removal", lockPath, lockAge);
                return false;
            }
            
            // For memory indexes, log additional information
            if (IsProtectedMemoryIndex(indexPath))
            {
                _logger.LogWarning("Removing stuck lock from PROTECTED memory index at {Path}. Lock age: {Age}", 
                    indexPath, lockAge);
            }
            
            File.Delete(lockPath);
            _logger.LogInformation("Successfully removed stuck write.lock from {Path} (age: {Age})", lockPath, lockAge);
            
            // Verify index data is still intact
            var segmentsPath = Path.Combine(indexPath, SegmentsFilename);
            if (File.Exists(segmentsPath))
            {
                _logger.LogInformation("Index segments verified intact at {Path}", indexPath);
            }
            else
            {
                _logger.LogWarning("No segments file found at {Path} after lock removal", indexPath);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove write.lock from {Path}", indexPath);
            return false;
        }
    }
    
    /// <summary>
    /// Clear an index directory (nuclear option for stuck locks)
    /// </summary>
    private void ClearIndex(string indexPath)
    {
        try
        {
            // CRITICAL: Protect memory indexes from accidental deletion
            if (IsProtectedMemoryIndex(indexPath))
            {
                _logger.LogWarning("Attempted to clear protected memory index at {Path}. Operation blocked.", indexPath);
                throw new InvalidOperationException($"Cannot clear protected memory index at {indexPath}");
            }
            
            if (System.IO.Directory.Exists(indexPath))
            {
                System.IO.Directory.Delete(indexPath, recursive: true);
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
        System.IO.Directory.CreateDirectory(indexPath);
        
        var directory = FSDirectory.Open(indexPath);
        var writer = CreateWriter(directory, forceRecreate, indexPath);
        
        return new IndexContext
        {
            Directory = directory,
            Writer = writer,
            Path = indexPath,
            LastAccessed = DateTime.UtcNow
        };
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
            // If we have an index path and it's a memory index, try one more recovery attempt
            if (indexPath != null && IsProtectedMemoryIndex(indexPath))
            {
                _logger.LogWarning(ex, "Failed to obtain lock for memory index at {Path}, attempting final recovery", indexPath);
                
                if (TryRemoveWriteLock(indexPath))
                {
                    // Try once more after removing the lock
                    try
                    {
                        var writer = new IndexWriter(directory, config);
                        // Reset recovery attempts on successful recovery
                        ResetRecoveryAttempts(indexPath);
                        _logger.LogInformation("Successfully created writer for memory index at {Path} after lock recovery", indexPath);
                        return writer;
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "Failed to create writer even after removing lock for memory index at {Path}", indexPath);
                        throw;
                    }
                }
            }
            
            throw;
        }
    }
    
    private string GetIndexPath(string workspacePath)
    {
        _logger.LogWarning("GetIndexPath called with: {WorkspacePath}", workspacePath);
        
        // Check if this is a memory path using PathResolutionService
        var isProtected = _pathResolution.IsProtectedPath(workspacePath);
        _logger.LogWarning("IsProtectedPath result: {IsProtected}", isProtected);
        
        if (isProtected)
        {
            // This is a memory path, use it directly
            _logger.LogWarning("Using memory path directly: {Path}", workspacePath);
            return workspacePath;
        }
        
        // For regular workspace paths, check if this path is contained within an existing workspace
        _logger.LogWarning("Using hashed path for workspace: {WorkspacePath}", workspacePath);
        
        // Check if this path is contained within an existing workspace to prevent duplicate indexes
        var existingWorkspacePath = FindExistingWorkspaceForPath(workspacePath);
        if (existingWorkspacePath != null)
        {
            _logger.LogInformation("Path {RequestedPath} is contained within existing workspace {ExistingWorkspace}, reusing existing index", 
                workspacePath, existingWorkspacePath);
            var existingIndexPath = _pathResolution.GetIndexPath(existingWorkspacePath);
            _logger.LogWarning("Reusing existing index path: {IndexPath}", existingIndexPath);
            return existingIndexPath;
        }
        
        var indexPath = _pathResolution.GetIndexPath(workspacePath);
        _logger.LogWarning("Hashed index path result: {IndexPath}", indexPath);
        
        // Fixed: Duplicate index issue resolved with workspace validation
        
        // Update metadata for code indexes (not memory indexes)
        var workspaceRoot = NormalizeToWorkspaceRoot(workspacePath);
        if (workspaceRoot != null)
        {
            var hashPath = Path.GetFileName(indexPath); // Extract just the directory name
            if (!string.IsNullOrEmpty(hashPath))
            {
                UpdateMetadata(workspaceRoot, hashPath);
            }
        }
        
        return indexPath;
    }
    
    private string GetBasePath()
    {
        return _pathResolution.GetBasePath();
    }
    
    private string? FindProjectRoot(string startPath)
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
                    if (System.IO.Directory.Exists(currentPath))
                    {
                        var files = System.IO.Directory.GetFiles(currentPath, indicator);
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
                    if (System.IO.Directory.Exists(indicatorPath) || System.IO.File.Exists(indicatorPath))
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
    private string NormalizeToWorkspaceRoot(string path)
    {
        // Get the absolute path
        var absolutePath = Path.GetFullPath(path);
        
        // If it's a file, get its directory
        string searchPath;
        if (File.Exists(absolutePath))
        {
            searchPath = Path.GetDirectoryName(absolutePath) ?? absolutePath;
            _logger.LogDebug("Path {Path} is a file, using directory {Directory} for root search", absolutePath, searchPath);
        }
        else
        {
            searchPath = absolutePath;
        }
        
        // Find the project root from this path
        var projectRoot = FindProjectRoot(searchPath);
        
        if (projectRoot != null)
        {
            _logger.LogDebug("Normalized path {Path} to workspace root {Root}", path, projectRoot);
            return projectRoot;
        }
        
        // If no project root found, use the directory itself (not individual files)
        if (File.Exists(absolutePath))
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
        return hash.Substring(0, 8);
    }
    
    private void UpdateMetadata(string originalPath, string hashPath)
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
        
        _metadataLock.Wait();
        try
        {
            var metadataPath = GetMetadataPath();
            var metadata = LoadMetadata(metadataPath);
            
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
            
            SaveMetadata(metadataPath, metadata);
            _metadataCache[hashPath] = metadata;
        }
        finally
        {
            _metadataLock.Release();
        }
    }
    
    private string GetMetadataPath()
    {
        return _pathResolution.GetWorkspaceMetadataPath();
    }
    
    private IndexMetadata LoadMetadata(string metadataPath)
    {
        if (File.Exists(metadataPath))
        {
            try
            {
                var json = File.ReadAllText(metadataPath);
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
    private string? FindExistingWorkspaceForPath(string requestedPath)
    {
        try
        {
            var metadataPath = GetMetadataPath();
            if (!File.Exists(metadataPath))
            {
                return null;
            }
            
            var metadata = LoadMetadata(metadataPath);
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
    
    private void SaveMetadata(string metadataPath, IndexMetadata metadata)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(metadata, options);
            File.WriteAllText(metadataPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata to {Path}", metadataPath);
        }
    }
    
    private void CleanupMemoryEntriesFromMetadata()
    {
        try
        {
            var metadataPath = GetMetadataPath();
            if (!File.Exists(metadataPath))
            {
                return;
            }
            
            var metadata = LoadMetadata(metadataPath);
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
                
                SaveMetadata(metadataPath, metadata);
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
    public string? GetOriginalPath(string hashPath)
    {
        _metadataLock.Wait();
        try
        {
            var metadataPath = GetMetadataPath();
            var metadata = LoadMetadata(metadataPath);
            
            if (metadata.Indexes.TryGetValue(hashPath, out var entry))
            {
                return entry.OriginalPath;
            }
            
            return null;
        }
        finally
        {
            _metadataLock.Release();
        }
    }
    
    /// <summary>
    /// Get all index mappings (for debugging/maintenance)
    /// </summary>
    public Dictionary<string, string> GetAllIndexMappings()
    {
        _metadataLock.Wait();
        try
        {
            var metadataPath = GetMetadataPath();
            var metadata = LoadMetadata(metadataPath);
            
            return metadata.Indexes.ToDictionary(
                kvp => kvp.Value.OriginalPath,
                kvp => kvp.Key
            );
        }
        finally
        {
            _metadataLock.Release();
        }
    }
    
    /// <summary>
    /// Reset recovery attempts for a given index path (called after successful operations)
    /// </summary>
    private void ResetRecoveryAttempts(string indexPath)
    {
        if (_recoveryAttempts.TryRemove(indexPath, out var attempts))
        {
            if (attempts > 0)
            {
                _logger.LogInformation("Reset recovery attempt counter for {Path} (was {Count})", indexPath, attempts);
            }
        }
    }
    
    /// <summary>
    /// Get recovery status for all indexes
    /// </summary>
    public Dictionary<string, int> GetRecoveryStatus()
    {
        return new Dictionary<string, int>(_recoveryAttempts);
    }
    
    /// <summary>
    /// Check if memory indexes exist and are healthy
    /// </summary>
    public (bool projectMemoryExists, bool localMemoryExists) CheckMemoryIndexHealth()
    {
        var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
        var localMemoryPath = _pathResolution.GetLocalMemoryPath();
        
        var projectMemoryExists = System.IO.Directory.Exists(projectMemoryPath) && 
                                  File.Exists(Path.Combine(projectMemoryPath, SegmentsFilename));
        var localMemoryExists = System.IO.Directory.Exists(localMemoryPath) && 
                               File.Exists(Path.Combine(localMemoryPath, SegmentsFilename));
        
        _logger.LogInformation("Memory index health check - Project: {ProjectExists}, Local: {LocalExists}", 
            projectMemoryExists, localMemoryExists);
        
        return (projectMemoryExists, localMemoryExists);
    }
    
    /// <summary>
    /// Clean up old or stuck indexes (only in the index subdirectory, never memory indexes)
    /// </summary>
    public void CleanupStuckIndexes()
    {
        var indexRoot = _pathResolution.GetIndexRootPath();
        
        if (!System.IO.Directory.Exists(indexRoot))
        {
            _logger.LogInformation("No index directory found at {Path}, nothing to clean", indexRoot);
            return;
        }
        
        _logger.LogInformation("Checking for stuck locks in code search indexes at {Path}", indexRoot);
        
        foreach (var indexDir in System.IO.Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WriteLockFilename);
            
            if (File.Exists(lockPath))
            {
                var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                
                if (lockAge > _lockTimeout)
                {
                    // Get the hash from the directory name
                    var hashPath = Path.GetFileName(indexDir);
                    var originalPath = GetOriginalPath(hashPath);
                    var pathInfo = originalPath != null ? $" (original: {originalPath})" : "";
                    
                    _logger.LogWarning("Found stuck lock at {Path}{PathInfo}, age: {Age}", lockPath, pathInfo, lockAge);
                    
                    try
                    {
                        File.Delete(lockPath);
                        _logger.LogInformation("Removed stuck lock at {Path}{PathInfo}", lockPath, pathInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove stuck lock at {Path}{PathInfo}", lockPath, pathInfo);
                    }
                }
            }
        }
    }
    
    public void CleanupDuplicateIndices()
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
            var metadata = LoadMetadata(metadataPath);
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
                        if (System.IO.Directory.Exists(indexPath))
                        {
                            // Close any open index context for this path
                            if (_indexes.ContainsKey(indexPath))
                            {
                                var context = _indexes[indexPath];
                                context.Lock.Wait();
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
                                _indexes.Remove(indexPath, out _);
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
            SaveMetadata(metadataPath, metadata);
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
        _metadataLock?.Dispose();
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
            // Check if index exists on disk before auto-creating
            if (!System.IO.Directory.Exists(indexPath) || !System.IO.Directory.GetFiles(indexPath, "*.cfs").Any())
            {
                throw new InvalidOperationException($"No index found for workspace: {workspacePath}. Please run index_workspace first.");
            }
            
            await _writerLock.WaitAsync(cancellationToken);
            try
            {
                if (!_indexes.TryGetValue(indexPath, out context))
                {
                    // Open existing index (read-only mode)
                    context = CreateIndexContext(indexPath, false);
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
            if (context.Reader == null)
            {
                throw new InvalidOperationException($"Failed to create or refresh index reader for workspace: {workspacePath}");
            }
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
                        
                        // Reset recovery attempts on successful commit
                        ResetRecoveryAttempts(indexPath);
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
    
    /// <summary>
    /// Get the physical index path for a workspace - single source of truth for path resolution
    /// </summary>
    public string GetPhysicalIndexPath(string workspacePath)
    {
        return GetIndexPath(workspacePath);
    }
    
    #endregion
}