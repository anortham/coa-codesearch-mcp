using COA.CodeSearch.Next.McpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.Next.McpServer.Services;

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
    // Timing configuration
    private readonly TimeSpan _debounceInterval;
    private readonly TimeSpan _deleteQuietPeriod;
    private readonly TimeSpan _atomicWriteWindow;
    private readonly int _batchSize;
    
    // Supported extensions
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IConfiguration configuration,
        IFileIndexingService fileIndexingService,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _configuration = configuration;
        _fileIndexingService = fileIndexingService;
        _pathResolution = pathResolution;
        
        // Configure timing based on lessons learned
        _debounceInterval = TimeSpan.FromMilliseconds(configuration.GetValue("CodeSearch:FileWatcher:DebounceMilliseconds", 500));
        _deleteQuietPeriod = TimeSpan.FromSeconds(configuration.GetValue("CodeSearch:FileWatcher:DeleteQuietPeriodSeconds", 5));
        _atomicWriteWindow = TimeSpan.FromMilliseconds(configuration.GetValue("CodeSearch:FileWatcher:AtomicWriteWindowMs", 100));
        _batchSize = configuration.GetValue("CodeSearch:FileWatcher:BatchSize", 50);
        
        // Load supported extensions
        var extensions = configuration.GetSection("CodeSearch:Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs" };
        _supportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        
        // Load excluded directories
        var excluded = configuration.GetSection("CodeSearch:Lucene:ExcludedDirectories").Get<string[]>()
            ?? new[] { "node_modules", ".git", "bin", "obj", "dist", "build", ".vs", ".vscode" };
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        
        _logger.LogInformation("FileWatcher configured - Debounce: {Debounce}ms, Delete quiet: {DeleteQuiet}s, Atomic window: {AtomicWindow}ms",
            _debounceInterval.TotalMilliseconds, _deleteQuietPeriod.TotalSeconds, _atomicWriteWindow.TotalMilliseconds);
    }

    public void StartWatching(string workspacePath)
    {
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
        
        _logger.LogDebug("FileWatcher event: {ChangeType} for {FilePath}", changeType, filePath);

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

            // Add to queue
            if (_changeQueue.TryAdd(changeEvent))
            {
                _logger.LogInformation("Queued {ChangeType} event for: {FilePath}", changeType, filePath);
            }
            else
            {
                _logger.LogWarning("Failed to queue {ChangeType} event for: {FilePath}", changeType, filePath);
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

        // Check extension
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            return false; // Skip files without extensions
        }
        
        return _supportedExtensions.Contains(extension);
    }

    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileWatcher service ExecuteAsync started");

        // Start the background processing task
        // IMPORTANT: We must return the actual task, not Task.CompletedTask
        // Otherwise BackgroundService thinks the work is done immediately
        return Task.Run(async () => 
        {
            _logger.LogInformation("FileWatcher Task.Run started");
            try
            {
                await ProcessFileChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                _logger.LogInformation("FileWatcher processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in FileWatcher processing loop");
                throw; // Re-throw to let BackgroundService handle it
            }
            finally
            {
                _logger.LogInformation("FileWatcher Task.Run completed");
            }
        }, stoppingToken);
    }

    private async Task ProcessFileChangesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileWatcher started processing changes");
        _logger.LogInformation("Debounce interval: {Debounce}ms, Batch size: {BatchSize}", 
            _debounceInterval.TotalMilliseconds, _batchSize);

        // Process changes in batches - EXACTLY like the old code
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = new List<FileChangeEvent>();
                var timeout = TimeSpan.FromMilliseconds(_debounceInterval.TotalMilliseconds);
                
                _logger.LogDebug("Waiting for file changes (timeout: {Timeout}ms)...", timeout.TotalMilliseconds);

                // Collect a batch of changes - EXACTLY like old code
                while (batch.Count < _batchSize)
                {
                    if (_changeQueue.TryTake(out var change, (int)timeout.TotalMilliseconds, stoppingToken))
                    {
                        batch.Add(change);
                        _logger.LogDebug("Collected change {Count}/{BatchSize}: {FilePath}", 
                            batch.Count, _batchSize, change.FilePath);
                        // Reduce timeout for subsequent items in batch
                        timeout = TimeSpan.FromMilliseconds(10);
                    }
                    else
                    {
                        // Timeout reached, process what we have
                        _logger.LogDebug("Timeout reached after collecting {Count} changes", batch.Count);
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    _logger.LogInformation("Processing batch of {Count} file changes", batch.Count);
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

        _logger.LogInformation("FileWatcher stopped processing changes");
    }

    private async Task ProcessBatchAsync(List<FileChangeEvent> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        _logger.LogDebug("Processing batch of {Count} file changes", batch.Count);

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

                    var indexed = await _fileIndexingService.IndexFileAsync(workspacePath, update.FilePath, cancellationToken);
                    if (indexed)
                    {
                        _logger.LogInformation("Successfully updated in index: {FilePath} ({ChangeType})", 
                            update.FilePath, update.ChangeType);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to index file: {FilePath} ({ChangeType})", 
                            update.FilePath, update.ChangeType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update file in index: {FilePath}", update.FilePath);
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
                        _logger.LogInformation("Deleted from index: {FilePath} (verified after {Seconds:F1}s quiet period)", 
                            pending.FilePath, (now - pending.FirstSeenTime).TotalSeconds);
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
}