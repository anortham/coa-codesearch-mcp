using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

namespace COA.CodeSearch.McpServer.Services;

public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileIndexingService _fileIndexingService;
    private readonly IPathResolutionService _pathResolution;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _pendingUpdates = new();
    private readonly BlockingCollection<FileChangeEvent> _changeQueue = new();
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private readonly int _debounceMilliseconds;
    private readonly int _batchSize;
    private readonly bool _enabled;
    private readonly ConcurrentBag<IFileChangeSubscriber> _subscribers = new();
    
    // Track pending deletes - files scheduled for deletion after a quiet period
    private readonly ConcurrentDictionary<string, PendingDelete> _pendingDeletes = new();
    private readonly TimeSpan _deleteQuietPeriod = TimeSpan.FromSeconds(3); // Wait 3 seconds of no activity before deleting

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IConfiguration configuration,
        FileIndexingService fileIndexingService,
        IPathResolutionService pathResolution,
        IEnumerable<IFileChangeSubscriber>? subscribers = null)
    {
        _logger = logger;
        _configuration = configuration;
        _fileIndexingService = fileIndexingService;
        _pathResolution = pathResolution;
        
        if (subscribers != null)
        {
            foreach (var subscriber in subscribers)
            {
                _subscribers.Add(subscriber);
            }
        }

        // Load configuration
        _enabled = configuration.GetValue("FileWatcher:Enabled", true);
        _debounceMilliseconds = configuration.GetValue("FileWatcher:DebounceMilliseconds", 500);
        _batchSize = configuration.GetValue("FileWatcher:BatchSize", 50);

        // Load supported extensions from FileIndexingService configuration
        var extensions = configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>()
            ?? new[] { ".cs", ".razor", ".cshtml", ".json", ".xml", ".md", ".txt", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".html", ".yml", ".yaml", ".csproj", ".sln" };
        _supportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        // Load excluded directories
        var excluded = configuration.GetSection("FileWatcher:ExcludePatterns").Get<string[]>()
            ?? PathConstants.DefaultExcludedDirectories;
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
    }

    public void StartWatching(string workspacePath)
    {
        if (!_enabled)
        {
            _logger.LogInformation("File watching is disabled in configuration");
            return;
        }

        if (_watchers.ContainsKey(workspacePath))
        {
            _logger.LogDebug("Already watching workspace: {WorkspacePath}", workspacePath);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(workspacePath)
            {
                // Use all relevant notify filters for Windows compatibility
                // LastWrite alone is not reliable on Windows - need DirectoryName and Attributes too
                NotifyFilter = NotifyFilters.FileName 
                             | NotifyFilters.LastWrite 
                             | NotifyFilters.Size
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.Attributes
                             | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Set up event handlers
            watcher.Changed += (sender, e) => OnFileChanged(workspacePath, e);
            watcher.Created += (sender, e) => OnFileCreated(workspacePath, e);
            watcher.Deleted += (sender, e) => OnFileDeleted(workspacePath, e);
            watcher.Renamed += (sender, e) => OnFileRenamed(workspacePath, e);
            watcher.Error += OnWatcherError;

            _watchers[workspacePath] = watcher;
            _logger.LogInformation("Started watching workspace: {WorkspacePath}", workspacePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching workspace: {WorkspacePath}", workspacePath);
        }
    }

    public void StopWatching(string workspacePath)
    {
        if (_watchers.TryRemove(workspacePath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger.LogInformation("Stopped watching workspace: {WorkspacePath}", workspacePath);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("File watching is disabled - FileWatcherService will not process changes");
            return Task.CompletedTask;
        }

        _logger.LogInformation("FileWatcherService ExecuteAsync started - file watching is enabled");

        // Start the background processing task
        // Note: Watching of workspaces is initiated by WorkspaceAutoIndexService on startup
        // and by IndexWorkspaceTool when manually indexing
        _ = Task.Run(async () => await ProcessFileChangesAsync(stoppingToken), stoppingToken);
        
        return Task.CompletedTask;
    }


    private async Task ProcessFileChangesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileWatcherService started processing changes");

        // Process changes in batches
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = new List<FileChangeEvent>();
                var timeout = TimeSpan.FromMilliseconds(_debounceMilliseconds);

                // Collect a batch of changes
                while (batch.Count < _batchSize)
                {
                    if (_changeQueue.TryTake(out var change, (int)timeout.TotalMilliseconds, stoppingToken))
                    {
                        // Check if this file has a pending update that's been debounced
                        if (_pendingUpdates.TryGetValue(change.FilePath, out var lastUpdate))
                        {
                            if (DateTime.UtcNow - lastUpdate < TimeSpan.FromMilliseconds(_debounceMilliseconds))
                            {
                                // Skip this update, it's too soon
                                continue;
                            }
                        }

                        batch.Add(change);
                        _pendingUpdates[change.FilePath] = DateTime.UtcNow;
                    }
                    else
                    {
                        // Timeout reached, process what we have
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file change batch");
                await Task.Delay(1000, stoppingToken); // Brief delay before retrying
            }
        }
    }

    private async Task ProcessBatchAsync(List<FileChangeEvent> batch, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing batch of {Count} file changes", batch.Count);

        // Group by workspace for efficient processing
        var workspaceGroups = batch.GroupBy(c => c.WorkspacePath);

        foreach (var group in workspaceGroups)
        {
            var workspacePath = group.Key;
            
            // Apply smart event coalescing to detect atomic writes
            var coalescedEvents = CoalesceAtomicWrites(group.ToList());
            
            // Separate true deletes from updates/creates
            var deletes = coalescedEvents.Where(c => c.ChangeType == FileChangeType.Deleted).ToList();
            var updates = coalescedEvents.Where(c => c.ChangeType != FileChangeType.Deleted).ToList();
            
            // Process deletes only after quiet period
            foreach (var delete in deletes)
            {
                // Check if this delete is pending
                if (_pendingDeletes.TryGetValue(delete.FilePath, out var pendingDelete))
                {
                    // Skip if cancelled (file was recreated/modified)
                    if (pendingDelete.Cancelled)
                    {
                        _logger.LogDebug("Skipping cancelled delete for {FilePath}", delete.FilePath);
                        _pendingDeletes.TryRemove(delete.FilePath, out _);
                        continue;
                    }
                    
                    var now = DateTime.UtcNow;
                    var timeSinceLastActivity = now - pendingDelete.LastActivityTime;
                    
                    // Check if quiet period has passed
                    if (timeSinceLastActivity < _deleteQuietPeriod)
                    {
                        _logger.LogDebug("Deferring delete for {FilePath} - still within quiet period ({Time:F1}s remaining)", 
                            delete.FilePath, (_deleteQuietPeriod - timeSinceLastActivity).TotalSeconds);
                        
                        // Re-add to queue to check again later
                        _changeQueue.TryAdd(delete);
                        continue;
                    }
                    
                    // Quiet period passed - but double-check if file still exists
                    if (File.Exists(delete.FilePath))
                    {
                        _logger.LogWarning("File still exists after delete event: {FilePath} - treating as modification", delete.FilePath);
                        _pendingDeletes.TryRemove(delete.FilePath, out _);
                        
                        // Convert to update event
                        await _fileIndexingService.ReindexFileAsync(workspacePath, delete.FilePath, cancellationToken);
                        _logger.LogInformation("Reindexed file that still exists: {FilePath}", delete.FilePath);
                        continue;
                    }
                    
                    // File really doesn't exist - proceed with delete
                    _pendingDeletes.TryRemove(delete.FilePath, out _);
                }
                
                try
                {
                    await _fileIndexingService.DeleteFileAsync(workspacePath, delete.FilePath, cancellationToken);
                    _logger.LogInformation("Deleted from index: {FilePath} (verified file doesn't exist)", 
                        delete.FilePath);
                    
                    // Notify subscribers
                    await NotifySubscribersAsync(delete, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete file from index: {FilePath}", delete.FilePath);
                }
            }

            // Process updates and creates
            foreach (var update in updates)
            {
                try
                {
                    await _fileIndexingService.ReindexFileAsync(workspacePath, update.FilePath, cancellationToken);
                    _logger.LogInformation("Updated in index: {FilePath}", update.FilePath);
                    
                    // Notify subscribers
                    await NotifySubscribersAsync(update, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update file in index: {FilePath}", update.FilePath);
                }
            }
        }
    }
    
    /// <summary>
    /// Coalesce atomic write operations (delete+create) into modifications
    /// Many editors use atomic writes: delete original, create new with same name
    /// </summary>
    private List<FileChangeEvent> CoalesceAtomicWrites(List<FileChangeEvent> events)
    {
        var result = new List<FileChangeEvent>();
        var eventsByFile = events.GroupBy(e => e.FilePath).ToList();
        
        foreach (var fileGroup in eventsByFile)
        {
            var fileEvents = fileGroup.ToList();
            
            // Look for delete followed by create/modify pattern (atomic write)
            var hasDelete = fileEvents.Any(e => e.ChangeType == FileChangeType.Deleted);
            var hasCreateOrModify = fileEvents.Any(e => e.ChangeType == FileChangeType.Created || e.ChangeType == FileChangeType.Modified);
            
            if (hasDelete && hasCreateOrModify)
            {
                // This looks like an atomic write - convert to a single modification
                _logger.LogDebug("Detected atomic write for {FilePath}, converting delete+create to modification", fileGroup.Key);
                
                result.Add(new FileChangeEvent
                {
                    WorkspacePath = fileEvents.First().WorkspacePath,
                    FilePath = fileGroup.Key,
                    ChangeType = FileChangeType.Modified
                });
            }
            else
            {
                // No atomic write pattern detected, keep all events
                result.AddRange(fileEvents);
            }
        }
        
        return result;
    }
    
    private async Task NotifySubscribersAsync(FileChangeEvent changeEvent, CancellationToken cancellationToken)
    {
        if (_subscribers.IsEmpty)
            return;
        
        try
        {
            // Convert internal FileChangeEvent to public one for subscribers
            var publicEvent = new MemoryLifecycleFileChangeEvent
            {
                FilePath = changeEvent.FilePath,
                ChangeType = ConvertChangeType(changeEvent.ChangeType),
                Timestamp = DateTime.UtcNow
            };
            
            // Notify all subscribers in parallel but with timeout
            var notificationTasks = _subscribers.Select(async subscriber =>
            {
                try
                {
                    // Create a timeout for each subscriber notification
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5)); // 5 second timeout per subscriber
                    
                    await subscriber.OnFileChangedAsync(publicEvent);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Subscriber {Type} timed out processing file change event", 
                        subscriber.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Subscriber {Type} failed to process file change event", 
                        subscriber.GetType().Name);
                }
            });
            
            await Task.WhenAll(notificationTasks);
        }
        catch (Exception ex)
        {
            // Don't let subscriber failures affect file indexing
            _logger.LogError(ex, "Error notifying subscribers of file change");
        }
    }
    
    private MemoryLifecycleFileChangeType ConvertChangeType(FileChangeType internalType)
    {
        return internalType switch
        {
            FileChangeType.Created => MemoryLifecycleFileChangeType.Created,
            FileChangeType.Modified => MemoryLifecycleFileChangeType.Modified,
            FileChangeType.Deleted => MemoryLifecycleFileChangeType.Deleted,
            _ => MemoryLifecycleFileChangeType.Modified
        };
    }

    private void OnFileChanged(string workspacePath, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath))
        {
            _logger.LogTrace("Ignoring change event for file: {FilePath}", e.FullPath);
            return;
        }

        var now = DateTime.UtcNow;
        
        // Cancel any pending delete for this file
        if (_pendingDeletes.TryGetValue(e.FullPath, out var pendingDelete))
        {
            if (!pendingDelete.Cancelled)
            {
                pendingDelete.Cancelled = true;
                pendingDelete.LastActivityTime = now;
                _logger.LogDebug("Cancelled pending delete for {FilePath} due to file change", e.FullPath);
            }
        }

        _logger.LogInformation("File changed detected: {FilePath} in workspace {WorkspacePath}", e.FullPath, workspacePath);
        
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Modified,
            Timestamp = now
        });
        
        if (!added)
        {
            _logger.LogWarning("Failed to add file change event to queue for: {FilePath}", e.FullPath);
        }
    }

    private void OnFileCreated(string workspacePath, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath))
            return;

        var now = DateTime.UtcNow;
        
        // Check if this file has a pending delete (atomic write pattern)
        if (_pendingDeletes.TryGetValue(e.FullPath, out var pendingDelete))
        {
            if (!pendingDelete.Cancelled)
            {
                var timeSinceDelete = now - pendingDelete.DeleteDetectedTime;
                pendingDelete.Cancelled = true;
                pendingDelete.LastActivityTime = now;
                
                _logger.LogInformation("✓ Detected atomic write (delete+create) for {FilePath} after {Time:F1}s - converting to modification", 
                    e.FullPath, timeSinceDelete.TotalSeconds);
                
                var modAdded = _changeQueue.TryAdd(new FileChangeEvent
                {
                    WorkspacePath = workspacePath,
                    FilePath = e.FullPath,
                    ChangeType = FileChangeType.Modified,  // Convert to modification
                    Timestamp = now
                });
                
                if (!modAdded)
                {
                    _logger.LogWarning("Failed to add atomic write modification event to queue for: {FilePath}", e.FullPath);
                }
                return;
            }
        }

        _logger.LogInformation("File created detected: {FilePath} in workspace {WorkspacePath}", e.FullPath, workspacePath);
        
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Created,
            Timestamp = now
        });
        
        if (!added)
        {
            _logger.LogWarning("Failed to add file create event to queue for: {FilePath}", e.FullPath);
        }
    }

    private void OnFileDeleted(string workspacePath, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath))
            return;

        // Add to pending deletes - will only be processed after quiet period
        var now = DateTime.UtcNow;
        _pendingDeletes[e.FullPath] = new PendingDelete
        {
            FilePath = e.FullPath,
            DeleteDetectedTime = now,
            LastActivityTime = now,
            Cancelled = false
        };
        
        // Clean up old entries periodically
        CleanupOldPendingDeletes();

        _logger.LogInformation("File deleted detected: {FilePath} - deferring for {Period:F1}s quiet period", 
            e.FullPath, _deleteQuietPeriod.TotalSeconds);
        
        // Add delete event to queue - batch processor will check if it should be processed
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Deleted,
            Timestamp = now
        });
        
        if (!added)
        {
            _logger.LogWarning("Failed to add file delete event to queue for: {FilePath}", e.FullPath);
        }
    }

    private void OnFileRenamed(string workspacePath, RenamedEventArgs e)
    {
        // Treat rename as delete + create
        if (!ShouldIgnoreFile(e.OldFullPath))
        {
            _changeQueue.TryAdd(new FileChangeEvent
            {
                WorkspacePath = workspacePath,
                FilePath = e.OldFullPath,
                ChangeType = FileChangeType.Deleted,
                Timestamp = DateTime.UtcNow
            });
        }

        if (!ShouldIgnoreFile(e.FullPath))
        {
            _changeQueue.TryAdd(new FileChangeEvent
            {
                WorkspacePath = workspacePath,
                FilePath = e.FullPath,
                ChangeType = FileChangeType.Created,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error");
    }

    private void CleanupOldPendingDeletes()
    {
        // Only cleanup every 100th call to avoid performance impact
        if (Random.Shared.Next(100) != 0)
            return;
            
        var cutoffTime = DateTime.UtcNow - TimeSpan.FromMinutes(5); // Clean up entries older than 5 minutes
        var keysToRemove = _pendingDeletes
            .Where(kvp => kvp.Value.LastActivityTime < cutoffTime || kvp.Value.Cancelled)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            _pendingDeletes.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} old pending delete entries", keysToRemove.Count);
        }
    }

    private bool ShouldIgnoreFile(string filePath)
    {
        // Always ignore anything in .codesearch directory
        if (IsUnderCodeSearchDirectory(filePath))
        {
            _logger.LogTrace("Ignoring file in {BaseDir} directory: {FilePath}", PathConstants.BaseDirectoryName, filePath);
            return true;
        }

        // Check extension
        var extension = GetSafeFileExtension(filePath);
        if (string.IsNullOrEmpty(extension) || !_supportedExtensions.Contains(extension))
            return true;

        // Check excluded directories
        var directory = GetSafeDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            var parts = SplitPath(directory);
            if (parts.Any(part => _excludedDirectories.Contains(part)))
                return true;
        }

        return false;
    }

    private bool IsUnderCodeSearchDirectory(string filePath)
    {
        try
        {
            var normalizedPath = NormalizePath(filePath);
            var baseDir = NormalizePath(_pathResolution.GetBasePath());
            return normalizedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Fallback to simple string check if path normalization fails
            return filePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string GetSafeFileExtension(string filePath)
    {
        try
        {
            return Path.GetExtension(filePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetSafeDirectoryName(string filePath)
    {
        try
        {
            return Path.GetDirectoryName(filePath) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string[] SplitPath(string path)
    {
        try
        {
            return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    public override void Dispose()
    {
        try
        {
            // Signal shutdown first
            _changeQueue?.CompleteAdding();
            
            // Stop all watchers
            foreach (var watcher in _watchers.Values)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing file system watcher");
                }
            }
            _watchers.Clear();

            _changeQueue?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FileWatcherService disposal");
        }
        finally
        {
            base.Dispose();
        }
    }

    private class FileChangeEvent
    {
        public string WorkspacePath { get; set; } = "";
        public string FilePath { get; set; } = "";
        public FileChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    private enum FileChangeType
    {
        Created,
        Modified,
        Deleted
    }
    
    private class PendingDelete
    {
        public string FilePath { get; set; } = "";
        public DateTime DeleteDetectedTime { get; set; }
        public DateTime LastActivityTime { get; set; }
        public bool Cancelled { get; set; }
    }
}

// Public types for IFileChangeSubscriber pattern
public class MemoryLifecycleFileChangeEvent
{
    public string FilePath { get; set; } = string.Empty;
    public MemoryLifecycleFileChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; }
    public string? OldPath { get; set; } // For renames
}

public enum MemoryLifecycleFileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}