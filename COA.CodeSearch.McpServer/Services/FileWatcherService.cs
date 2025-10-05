using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Background service that watches for file changes and automatically updates indexes.
/// Includes sophisticated handling for atomic writes and delete coalescing learned from production use.
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IPathResolutionService _pathResolution;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, FileChangeEvent> _pendingChanges = new();
    private readonly ConcurrentDictionary<string, PendingDelete> _pendingDeletes = new();
    private readonly BlockingCollection<FileChangeEvent> _changeQueue = new();

    // Phoenix integration: SQLite canonical storage + Julie extraction
    private readonly Sqlite.ISQLiteSymbolService? _sqliteService;
    private readonly Julie.IJulieExtractionService? _julieExtractionService;
    private readonly Julie.IJulieCodeSearchService? _julieCodeSearchService;
    private readonly Julie.ISemanticIntelligenceService? _semanticIntelligenceService;
    // Timing configuration
    private readonly TimeSpan _debounceInterval;
    private readonly TimeSpan _deleteQuietPeriod;
    private readonly TimeSpan _atomicWriteWindow;
    private readonly int _batchSize;
    
    // Blacklisted extensions (changed from whitelist to blacklist to match FileIndexingService)
    private readonly HashSet<string> _blacklistedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    
    // Self-starting background task
    private bool _backgroundTaskStarted = false;
    private readonly object _startLock = new object();
    private Task? _executeTask;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IConfiguration configuration,
        IFileIndexingService fileIndexingService,
        IPathResolutionService pathResolution,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _fileIndexingService = fileIndexingService;
        _pathResolution = pathResolution;

        // Phoenix integration: Optional services (graceful degradation if not available)
        _sqliteService = serviceProvider.GetService<Sqlite.ISQLiteSymbolService>();
        _julieExtractionService = serviceProvider.GetService<Julie.IJulieExtractionService>();
        _julieCodeSearchService = serviceProvider.GetService<Julie.IJulieCodeSearchService>();
        _semanticIntelligenceService = serviceProvider.GetService<Julie.ISemanticIntelligenceService>();

        // Configure timing based on lessons learned
        _debounceInterval = TimeSpan.FromMilliseconds(configuration.GetValue("CodeSearch:FileWatcher:DebounceMilliseconds", 500));
        _deleteQuietPeriod = TimeSpan.FromSeconds(configuration.GetValue("CodeSearch:FileWatcher:DeleteQuietPeriodSeconds", 5));
        _atomicWriteWindow = TimeSpan.FromMilliseconds(configuration.GetValue("CodeSearch:FileWatcher:AtomicWriteWindowMs", 100));
        _batchSize = configuration.GetValue("CodeSearch:FileWatcher:BatchSize", 50);
        
        // Load blacklisted extensions (using same source as FileIndexingService)
        var blacklistedExts = configuration.GetSection("CodeSearch:Indexing:BlacklistedExtensions").Get<string[]>() 
            ?? PathConstants.DefaultBlacklistedExtensions;
        _blacklistedExtensions = new HashSet<string>(blacklistedExts, StringComparer.OrdinalIgnoreCase);
        
        // Load excluded directories
        var excluded = configuration.GetSection("CodeSearch:Lucene:ExcludedDirectories").Get<string[]>()
            ?? new[] { "node_modules", ".git", "bin", "obj", "dist", "build", ".vs", ".vscode" };
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        
        _logger.LogInformation("FileWatcher configured - Debounce: {Debounce}ms, Delete quiet: {DeleteQuiet}s, Atomic window: {AtomicWindow}ms",
            _debounceInterval.TotalMilliseconds, _deleteQuietPeriod.TotalSeconds, _atomicWriteWindow.TotalMilliseconds);
    }

    public void StartWatching(string workspacePath)
    {
        // CRITICAL FIX: Ensure ExecuteAsync is running when first workspace is watched
        // This solves the dual ServiceProvider issue - the background task runs on the same instance
        EnsureBackgroundTaskStarted();
        
        if (_watchers.ContainsKey(workspacePath))
        {
            _logger.LogDebug("Already watching workspace: {Workspace}", workspacePath);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(workspacePath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                InternalBufferSize = 64 * 1024 // Increase buffer to prevent event loss
            };

            // We'll filter in event handlers for more control
            watcher.Filter = "*.*";

            // Attach event handlers
            watcher.Changed += (sender, e) => HandleFileEvent(workspacePath, e.FullPath, FileChangeType.Modified);
            watcher.Created += (sender, e) => HandleFileEvent(workspacePath, e.FullPath, FileChangeType.Created);
            watcher.Deleted += (sender, e) => HandleFileEvent(workspacePath, e.FullPath, FileChangeType.Deleted);
            watcher.Renamed += (sender, e) => HandleRename(workspacePath, e);
            watcher.Error += OnWatcherError;

            if (_watchers.TryAdd(workspacePath, watcher))
            {
                _logger.LogInformation("Started watching workspace: {Workspace}", workspacePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching workspace: {Workspace}", workspacePath);
        }
    }

    
    public void StopWatching(string workspacePath)
    {
        if (_watchers.TryRemove(workspacePath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger.LogInformation("Stopped watching workspace: {Workspace}", workspacePath);
        }
    }

    private void HandleFileEvent(string workspacePath, string filePath, FileChangeType changeType)
    {
        // Filter out unsupported files
        if (!IsFileSupported(filePath))
        {
            return;
        }
        

        var changeEvent = new FileChangeEvent
        {
            FilePath = filePath,
            WorkspacePath = workspacePath,
            ChangeType = changeType,
            Timestamp = DateTime.UtcNow
        };

        // Handle deletes specially
        if (changeType == FileChangeType.Deleted)
        {
            HandleDeleteEvent(changeEvent);
        }
        else
        {
            // For creates/modifies, cancel any pending delete for this file
            if (_pendingDeletes.TryGetValue(filePath, out var pendingDelete))
            {
                pendingDelete.Cancelled = true;
                _logger.LogDebug("Cancelled pending delete for {FilePath} due to {ChangeType}", filePath, changeType);
            }

            // Deduplicate using _pendingChanges to prevent duplicate processing
            if (_pendingChanges.TryGetValue(filePath, out var existingEvent))
            {
                // Update existing event with latest timestamp and change type
                existingEvent.Timestamp = changeEvent.Timestamp;
                existingEvent.ChangeType = changeEvent.ChangeType;
                _logger.LogDebug("Updated pending {ChangeType} event for: {FilePath} (deduped)", changeType, filePath);
            }
            else
            {
                // New file event - add to pending dictionary and queue
                if (_pendingChanges.TryAdd(filePath, changeEvent))
                {
                    // Add to queue to signal batch processor
                    if (_changeQueue.TryAdd(changeEvent))
                    {
                        _logger.LogDebug("Queued {ChangeType} event for: {FilePath}", changeType, filePath);
                    }
                    else
                    {
                        // Failed to queue - remove from pending
                        _pendingChanges.TryRemove(filePath, out _);
                        _logger.LogWarning("Failed to queue {ChangeType} event for: {FilePath}", changeType, filePath);
                    }
                }
            }
        }
    }

    private void HandleDeleteEvent(FileChangeEvent deleteEvent)
    {
        // Track pending delete
        _pendingDeletes.AddOrUpdate(deleteEvent.FilePath,
            new PendingDelete 
            { 
                FilePath = deleteEvent.FilePath,
                FirstSeenTime = DateTime.UtcNow,
                LastActivityTime = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.LastActivityTime = DateTime.UtcNow;
                return existing;
            });

        // Add to queue for later processing
        _changeQueue.TryAdd(deleteEvent);
        
        _logger.LogDebug("Queued delete for {FilePath} - will verify after quiet period", deleteEvent.FilePath);
    }

    private void HandleRename(string workspacePath, RenamedEventArgs e)
    {
        // Treat rename as delete + create
        HandleFileEvent(workspacePath, e.OldFullPath, FileChangeType.Deleted);
        HandleFileEvent(workspacePath, e.FullPath, FileChangeType.Created);
    }

    private bool IsFileSupported(string filePath)
    {
        // Check if the file path itself is an excluded directory
        var fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName) && _excludedDirectories.Contains(fileName))
        {
            return false;
        }

        // Check if file is in excluded directory
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
        {
            // Split path into segments and check each one
            var segments = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var segment in segments)
            {
                // Check if this segment is an excluded directory (case-insensitive)
                if (_excludedDirectories.Contains(segment))
                {
                    return false;
                }
            }
        }

        // Enhanced temp file detection for Claude Code settings files
        // Files like "settings.local.json.tmp.54492.1757682376336" have multi-part extensions
        if (!string.IsNullOrEmpty(fileName))
        {
            // Check for temp file patterns that Claude Code creates
            if (fileName.Contains(".tmp.") && char.IsDigit(fileName[fileName.LastIndexOf(".tmp.") + 5]))
            {
                _logger.LogTrace("Excluding temp file with numeric suffix: {FilePath}", filePath);
                return false;
            }
        }

        // Check extension - allow all extensions except blacklisted ones
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            return true; // Include files without extensions (like Web.config, Dockerfile, etc.)
        }
        
        // Use blacklist logic: include file unless it's blacklisted
        return !_blacklistedExtensions.Contains(extension);
    }

    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("FileWatcher ExecuteAsync starting");

        // Start the background processing task
        // IMPORTANT: We must return the actual task, not Task.CompletedTask
        // Otherwise BackgroundService thinks the work is done immediately
        return Task.Run(async () => 
        {
            try
            {
                // Auto-discover and start watching existing indexed workspaces
                // Start watching primary workspace
                AutoStartExistingWorkspaces(stoppingToken);
                
                await ProcessFileChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                _logger.LogDebug("FileWatcher processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in FileWatcher processing loop");
                throw; // Re-throw to let BackgroundService handle it
            }
        }, stoppingToken);
    }

    private async Task ProcessFileChangesAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("FileWatcher processing loop started - Debounce: {Debounce}ms, Batch size: {BatchSize}", 
            _debounceInterval.TotalMilliseconds, _batchSize);

        // Process changes in batches
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = new List<FileChangeEvent>();
                var timeout = TimeSpan.FromMilliseconds(_debounceInterval.TotalMilliseconds);

                // Collect a batch of changes
                while (batch.Count < _batchSize)
                {
                    if (_changeQueue.TryTake(out var change, (int)timeout.TotalMilliseconds, stoppingToken))
                    {
                        batch.Add(change);
                        // Reduce timeout for subsequent items in batch
                        timeout = TimeSpan.FromMilliseconds(10);
                    }
                    else
                    {
                        // Timeout reached, process what we have
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    _logger.LogDebug("Processing batch of {Count} file changes", batch.Count);
                    await ProcessBatchAsync(batch, stoppingToken);
                }

                // Check for expired pending deletes
                await ProcessPendingDeletesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file changes");
                await Task.Delay(1000, stoppingToken); // Brief delay before retry
            }
        }

        _logger.LogDebug("FileWatcher stopped processing changes");
    }

    private async Task ProcessBatchAsync(List<FileChangeEvent> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        // Group by workspace
        var workspaceGroups = batch.GroupBy(c => c.WorkspacePath);

        foreach (var group in workspaceGroups)
        {
            var workspacePath = group.Key;
            
            // Apply atomic write coalescing
            var coalescedEvents = CoalesceAtomicWrites(group.ToList());
            
            // Separate deletes from other operations
            var deletes = coalescedEvents.Where(c => c.ChangeType == FileChangeType.Deleted).ToList();
            var updates = coalescedEvents.Where(c => c.ChangeType != FileChangeType.Deleted).ToList();

            // Process updates first
            foreach (var update in updates)
            {
                try
                {
                    // Cancel any pending delete for this file
                    if (_pendingDeletes.TryGetValue(update.FilePath, out var pendingDelete))
                    {
                        pendingDelete.Cancelled = true;
                    }

                    // Phoenix: Update SQLite FIRST (source of truth)
                    await UpdateSQLiteForFileAsync(workspacePath, update.FilePath, cancellationToken);

                    // Then update Lucene (reads from SQLite)
                    var indexed = await _fileIndexingService.IndexFileAsync(workspacePath, update.FilePath, cancellationToken);
                    if (indexed)
                    {
                        _logger.LogDebug("Successfully updated in index: {FilePath} ({ChangeType})",
                            update.FilePath, update.ChangeType);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to index file: {FilePath} ({ChangeType})",
                            update.FilePath, update.ChangeType);
                    }

                    // Clear from pending changes after successful processing
                    _pendingChanges.TryRemove(update.FilePath, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update file in index: {FilePath}", update.FilePath);
                    // Don't clear from pending on error - allow retry
                }
            }

            // Process deletes (will be verified in ProcessPendingDeletesAsync)
            foreach (var delete in deletes)
            {
                _logger.LogDebug("Delete queued for verification: {FilePath}", delete.FilePath);
            }
        }
    }

    private async Task ProcessPendingDeletesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var toProcess = new List<PendingDelete>();

        foreach (var kvp in _pendingDeletes)
        {
            var pending = kvp.Value;
            
            // Skip if cancelled
            if (pending.Cancelled)
            {
                _pendingDeletes.TryRemove(kvp.Key, out _);
                continue;
            }

            // Check if quiet period has passed
            var timeSinceLastActivity = now - pending.LastActivityTime;
            if (timeSinceLastActivity >= _deleteQuietPeriod)
            {
                toProcess.Add(pending);
            }
        }

        foreach (var pending in toProcess)
        {
            // Double-check file existence before deleting from index
            if (File.Exists(pending.FilePath))
            {
                _logger.LogWarning("File still exists after delete event: {FilePath} - treating as modification", 
                    pending.FilePath);
                _pendingDeletes.TryRemove(pending.FilePath, out _);
                
                // Get workspace for this file
                var workspace = _watchers.Keys.FirstOrDefault(w => pending.FilePath.StartsWith(w));
                if (workspace != null)
                {
                    try
                    {
                        await _fileIndexingService.IndexFileAsync(workspace, pending.FilePath, cancellationToken);
                        _logger.LogInformation("Reindexed file that still exists: {FilePath}", pending.FilePath);

                        // Phoenix: Update SQLite
                        await UpdateSQLiteForFileAsync(workspace, pending.FilePath, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to reindex file: {FilePath}", pending.FilePath);
                    }
                }
            }
            else
            {
                // File really is deleted - remove from index
                _pendingDeletes.TryRemove(pending.FilePath, out _);
                
                var workspace = _watchers.Keys.FirstOrDefault(w => pending.FilePath.StartsWith(w));
                if (workspace != null)
                {
                    try
                    {
                        await _fileIndexingService.RemoveFileAsync(workspace, pending.FilePath, cancellationToken);
                        _logger.LogDebug("Deleted from index: {FilePath} (verified after {Seconds:F1}s quiet period)",
                            pending.FilePath, (now - pending.FirstSeenTime).TotalSeconds);

                        // Phoenix: Delete from SQLite
                        await DeleteFromSQLiteAsync(workspace, pending.FilePath, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file from index: {FilePath}", pending.FilePath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Coalesce atomic write operations (delete+create within small window) into modifications.
    /// Many editors (VS Code, Claude Code, etc.) use atomic writes for saving files.
    /// </summary>
    private List<FileChangeEvent> CoalesceAtomicWrites(List<FileChangeEvent> events)
    {
        var result = new List<FileChangeEvent>();
        var eventsByFile = events.GroupBy(e => e.FilePath).ToList();

        foreach (var fileGroup in eventsByFile)
        {
            var fileEvents = fileGroup.OrderBy(e => e.Timestamp).ToList();
            
            if (fileEvents.Count == 1)
            {
                // Single event - pass through
                result.Add(fileEvents[0]);
            }
            else
            {
                // Multiple events for same file - check for atomic write pattern
                var hasDelete = fileEvents.Any(e => e.ChangeType == FileChangeType.Deleted);
                var hasCreate = fileEvents.Any(e => e.ChangeType == FileChangeType.Created);
                
                if (hasDelete && hasCreate)
                {
                    // Check if delete and create happened within atomic write window
                    var deleteTime = fileEvents.First(e => e.ChangeType == FileChangeType.Deleted).Timestamp;
                    var createTime = fileEvents.First(e => e.ChangeType == FileChangeType.Created).Timestamp;
                    
                    if (Math.Abs((createTime - deleteTime).TotalMilliseconds) <= _atomicWriteWindow.TotalMilliseconds)
                    {
                        // Atomic write detected - treat as modification
                        _logger.LogDebug("Atomic write detected for {FilePath} - treating as modification", fileGroup.Key);
                        result.Add(new FileChangeEvent
                        {
                            FilePath = fileGroup.Key,
                            WorkspacePath = fileEvents[0].WorkspacePath,
                            ChangeType = FileChangeType.Modified,
                            Timestamp = createTime
                        });
                    }
                    else
                    {
                        // Not atomic - keep original events
                        result.AddRange(fileEvents);
                    }
                }
                else
                {
                    // Multiple modifications or other patterns - take the last one
                    result.Add(fileEvents.Last());
                }
            }
        }

        return result;
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error");
        
        // Try to recover by recreating the watcher
        if (sender is FileSystemWatcher watcher)
        {
            var workspace = _watchers.FirstOrDefault(x => x.Value == watcher).Key;
            if (workspace != null)
            {
                _logger.LogInformation("Attempting to recover watcher for {Workspace}", workspace);
                StopWatching(workspace);
                Task.Delay(1000).ContinueWith(_ => StartWatching(workspace));
            }
        }
    }

    private void EnsureBackgroundTaskStarted()
    {
        if (_backgroundTaskStarted) return;
        
        lock (_startLock)
        {
            if (_backgroundTaskStarted) return;
            
            _logger.LogDebug("Self-starting FileWatcher background task");
            
            // Start ExecuteAsync if it's not already running
            // This ensures we use the same instance that receives events
            var cts = new CancellationTokenSource();
            _executeTask = ExecuteAsync(cts.Token);
            _backgroundTaskStarted = true;
        }
    }
    
    public override void Dispose()
    {
        // Stop all watchers
        foreach (var workspace in _watchers.Keys.ToList())
        {
            StopWatching(workspace);
        }
        
        _changeQueue?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Auto-discover and start watching existing indexed workspaces on startup
    /// In hybrid model, we only watch workspaces that have indexes in the primary workspace
    /// </summary>
    private void AutoStartExistingWorkspaces(CancellationToken cancellationToken)
    {
        try
        {
            var indexRoot = _pathResolution.GetIndexRootPath();
            if (!Directory.Exists(indexRoot))
            {
                _logger.LogDebug("Index root directory does not exist: {IndexRoot}", indexRoot);
                return;
            }

            // In hybrid model, just start watching the current primary workspace
            var primaryWorkspace = Environment.CurrentDirectory;
            if (Directory.Exists(primaryWorkspace))
            {
                _logger.LogInformation("Auto-starting FileWatcher for primary workspace: {WorkspacePath}", primaryWorkspace);
                StartWatching(primaryWorkspace);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-start FileWatcher for primary workspace");
        }
    }

    /// <summary>
    /// Attempt to resolve the original workspace path from a hash directory
    /// Not needed in hybrid model since we don't use registry
    /// </summary>
    private string? TryResolveWorkspaceFromHash(string indexDirectory)
    {
        // In hybrid model, we don't maintain a registry
        // This method is no longer needed but kept for compatibility
        try
        {
            var directoryName = Path.GetFileName(indexDirectory);

            // Simply return null - we don't resolve hashes anymore
            // Each workspace manages its own indexes
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving workspace path from: {IndexDir}", indexDirectory);
            return null;
        }
    }

    /// <summary>
    /// Phoenix: Update SQLite database with symbols from a changed file using julie-codesearch
    /// </summary>
    private async Task UpdateSQLiteForFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken)
    {
        // Prefer julie-codesearch (handles everything in one CLI call)
        if (_julieCodeSearchService?.IsAvailable() == true)
        {
            try
            {
                // Get SQLite database path
                var indexPath = _pathResolution.GetIndexPath(workspacePath);
                var dbDirectory = Path.Combine(indexPath, "db");
                var sqlitePath = Path.Combine(dbDirectory, "workspace.db");

                // Ensure db/ directory exists
                Directory.CreateDirectory(dbDirectory);

                // Enable detailed logging for debugging
                var logFilePath = Path.Combine(dbDirectory, "julie-codesearch.log");

                // Update file in SQLite database using julie-codesearch
                var result = await _julieCodeSearchService.UpdateFileAsync(
                    filePath,
                    sqlitePath,
                    logFilePath: logFilePath,
                    cancellationToken);

                if (result.Success)
                {
                    _logger.LogDebug("Phoenix: julie-codesearch {Action} {FilePath} ({SymbolCount} symbols in {ElapsedMs:F1}ms)",
                        result.Action,
                        filePath,
                        result.SymbolCount,
                        result.ElapsedMs);

                    // Update embeddings incrementally if semantic service is available
                    if (_semanticIntelligenceService?.IsAvailable() == true)
                    {
                        try
                        {
                            var vectorsPath = Path.Combine(indexPath, "vectors");
                            if (Directory.Exists(vectorsPath)) // Only update if embeddings exist
                            {
                                _logger.LogDebug("Phoenix: Updating embeddings for {FilePath}...", filePath);
                                var embStats = await _semanticIntelligenceService.UpdateFileAsync(
                                    filePath,
                                    sqlitePath,
                                    vectorsPath,
                                    model: "bge-small",
                                    cancellationToken);

                                if (embStats.Success)
                                {
                                    _logger.LogDebug("Phoenix: Updated {Embeddings} embeddings for {FilePath}",
                                        embStats.EmbeddingsGenerated, filePath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Phoenix: Embedding update failed for {FilePath} - continuing", filePath);
                        }
                    }
                }
                else
                {
                    // Database locked is expected with concurrent file changes - not an error
                    if (result.ErrorMessage?.Contains("database is locked") == true)
                    {
                        _logger.LogDebug("Phoenix: julie-codesearch update skipped for {FilePath} (database locked by concurrent process)",
                            filePath);
                    }
                    else
                    {
                        _logger.LogWarning("Phoenix: julie-codesearch update failed for {FilePath}: {Error}",
                            filePath, result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phoenix: julie-codesearch update failed for {FilePath} - continuing with Lucene only", filePath);
            }
        }
        // Fallback to old approach if julie-codesearch not available
        else if (_sqliteService != null && _julieExtractionService != null && _julieExtractionService.IsAvailable())
        {
            try
            {
                // Extract symbols from the single file using julie-extract
                var symbols = await _julieExtractionService.ExtractSingleFileAsync(filePath, cancellationToken);

                if (symbols.Count == 0)
                {
                    _logger.LogTrace("Phoenix: No symbols extracted from {FilePath}", filePath);
                    return;
                }

                // Read file content and metadata
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    _logger.LogWarning("Phoenix: File disappeared during symbol extraction: {FilePath}", filePath);
                    return;
                }

                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var hash = ComputeFileHash(content);
                var language = DetectLanguage(filePath);

                // Upsert to SQLite (transaction-safe)
                await _sqliteService.UpsertFileSymbolsAsync(
                    workspacePath: workspacePath,
                    filePath: filePath,
                    symbols: symbols,
                    fileContent: content,
                    language: language,
                    hash: hash,
                    size: fileInfo.Length,
                    lastModified: new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Phoenix: Updated SQLite with {SymbolCount} symbols from {FilePath}",
                    symbols.Count, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phoenix: Failed to update SQLite for {FilePath} - continuing with Lucene only", filePath);
            }
        }
    }

    /// <summary>
    /// Phoenix: Delete file and symbols from SQLite database
    /// </summary>
    private async Task DeleteFromSQLiteAsync(string workspacePath, string filePath, CancellationToken cancellationToken)
    {
        if (_sqliteService == null)
        {
            return; // Graceful degradation - SQLite not available
        }

        try
        {
            await _sqliteService.DeleteFileAsync(workspacePath, filePath, cancellationToken);
            _logger.LogDebug("Phoenix: Deleted from SQLite: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phoenix: Failed to delete from SQLite: {FilePath} - continuing with Lucene only", filePath);
        }
    }

    /// <summary>
    /// Compute SHA256 hash of file content for change detection
    /// </summary>
    private static string ComputeFileHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Detect language from file extension
    /// </summary>
    private static string DetectLanguage(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".py" => "python",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" => "c",
            ".rb" => "ruby",
            ".php" => "php",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".scala" => "scala",
            _ => "unknown"
        };
    }
}