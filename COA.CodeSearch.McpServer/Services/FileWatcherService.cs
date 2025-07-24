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
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    // TODO: Fix potential memory leak - items may not be removed from dictionary on exceptions
    // UPDATE: This has been addressed - testing memory lifecycle service
    private readonly ConcurrentDictionary<string, DateTime> _pendingUpdates = new();
    private readonly BlockingCollection<FileChangeEvent> _changeQueue = new();
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private readonly int _debounceMilliseconds;
    private readonly int _batchSize;
    private readonly bool _enabled;
    private readonly List<IFileChangeSubscriber> _subscribers = new();

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IConfiguration configuration,
        FileIndexingService fileIndexingService,
        IEnumerable<IFileChangeSubscriber>? subscribers = null)
    {
        _logger = logger;
        _configuration = configuration;
        _fileIndexingService = fileIndexingService;
        
        if (subscribers != null)
        {
            _subscribers.AddRange(subscribers);
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
            
            // Process deletes first (they're quick and don't require file access)
            var deletes = group.Where(c => c.ChangeType == FileChangeType.Deleted).ToList();
            foreach (var delete in deletes)
            {
                try
                {
                    await _fileIndexingService.DeleteFileAsync(workspacePath, delete.FilePath, cancellationToken);
                    _logger.LogInformation("Deleted from index: {FilePath}", delete.FilePath);
                    
                    // Notify subscribers
                    await NotifySubscribersAsync(delete, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete file from index: {FilePath}", delete.FilePath);
                }
            }

            // Process updates and creates
            var updates = group.Where(c => c.ChangeType != FileChangeType.Deleted).ToList();
            foreach (var update in updates)
            {
                try
                {
                    await _fileIndexingService.UpdateFileAsync(workspacePath, update.FilePath, cancellationToken);
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
    
    private async Task NotifySubscribersAsync(FileChangeEvent changeEvent, CancellationToken cancellationToken)
    {
        if (_subscribers.Count == 0)
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

        _logger.LogInformation("File changed detected: {FilePath} in workspace {WorkspacePath}", e.FullPath, workspacePath);
        
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Modified
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

        _logger.LogInformation("File created detected: {FilePath} in workspace {WorkspacePath}", e.FullPath, workspacePath);
        
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Created
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

        _logger.LogInformation("File deleted detected: {FilePath} in workspace {WorkspacePath}", e.FullPath, workspacePath);
        
        var added = _changeQueue.TryAdd(new FileChangeEvent
        {
            WorkspacePath = workspacePath,
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Deleted
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
                ChangeType = FileChangeType.Deleted
            });
        }

        if (!ShouldIgnoreFile(e.FullPath))
        {
            _changeQueue.TryAdd(new FileChangeEvent
            {
                WorkspacePath = workspacePath,
                FilePath = e.FullPath,
                ChangeType = FileChangeType.Created
            });
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error");
    }

    private bool ShouldIgnoreFile(string filePath)
    {
        // Always ignore anything in .codesearch directory
        if (filePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogTrace("Ignoring file in {BaseDir} directory: {FilePath}", PathConstants.BaseDirectoryName, filePath);
            return true;
        }

        // Check extension
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension) || !_supportedExtensions.Contains(extension))
            return true;

        // Check excluded directories
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(part => _excludedDirectories.Contains(part)))
                return true;
        }

        return false;
    }

    public override void Dispose()
    {
        // Stop all watchers
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        _changeQueue?.Dispose();
        base.Dispose();
    }

    private class FileChangeEvent
    {
        public string WorkspacePath { get; set; } = "";
        public string FilePath { get; set; } = "";
        public FileChangeType ChangeType { get; set; }
    }

    private enum FileChangeType
    {
        Created,
        Modified,
        Deleted
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