using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Background service that watches for file changes and automatically updates indexes
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IPathResolutionService _pathResolution;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _pendingChanges = new();
    private readonly TimeSpan _debounceInterval;
    private Timer? _processTimer;

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
        
        // Configure debounce interval to batch rapid file changes
        _debounceInterval = TimeSpan.FromSeconds(configuration.GetValue("FileWatcher:DebounceSeconds", 2));
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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Configure filters based on supported extensions
            var extensions = _configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>();
            if (extensions != null && extensions.Length > 0)
            {
                // FileSystemWatcher only supports one filter at a time
                // We'll filter in the event handlers instead
                watcher.Filter = "*.*";
            }

            watcher.Changed += (sender, e) => OnFileChanged(workspacePath, e);
            watcher.Created += (sender, e) => OnFileChanged(workspacePath, e);
            watcher.Deleted += (sender, e) => OnFileDeleted(workspacePath, e);
            watcher.Renamed += (sender, e) => OnFileRenamed(workspacePath, e);
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the timer to process pending changes
        _processTimer = new Timer(
            ProcessPendingChanges,
            null,
            _debounceInterval,
            _debounceInterval);

        // Watch configured workspaces
        var workspaces = _configuration.GetSection("FileWatcher:AutoWatchWorkspaces").Get<string[]>();
        if (workspaces != null)
        {
            foreach (var workspace in workspaces)
            {
                StartWatching(workspace);
            }
        }

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _processTimer?.Dispose();
        
        // Stop all watchers
        foreach (var workspace in _watchers.Keys.ToList())
        {
            StopWatching(workspace);
        }

        return base.StopAsync(cancellationToken);
    }

    private void OnFileChanged(string workspacePath, FileSystemEventArgs e)
    {
        // Debounce rapid changes to the same file
        var key = $"{workspacePath}|{e.FullPath}|change";
        _pendingChanges.AddOrUpdate(key, DateTime.UtcNow, (k, v) => DateTime.UtcNow);
        
        _logger.LogTrace("File changed: {FilePath}", e.FullPath);
    }

    private void OnFileDeleted(string workspacePath, FileSystemEventArgs e)
    {
        var key = $"{workspacePath}|{e.FullPath}|delete";
        _pendingChanges.AddOrUpdate(key, DateTime.UtcNow, (k, v) => DateTime.UtcNow);
        
        _logger.LogTrace("File deleted: {FilePath}", e.FullPath);
    }

    private void OnFileRenamed(string workspacePath, RenamedEventArgs e)
    {
        // Treat rename as delete old + create new
        var deleteKey = $"{workspacePath}|{e.OldFullPath}|delete";
        var createKey = $"{workspacePath}|{e.FullPath}|change";
        
        _pendingChanges.AddOrUpdate(deleteKey, DateTime.UtcNow, (k, v) => DateTime.UtcNow);
        _pendingChanges.AddOrUpdate(createKey, DateTime.UtcNow, (k, v) => DateTime.UtcNow);
        
        _logger.LogTrace("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error");
    }

    private async void ProcessPendingChanges(object? state)
    {
        var now = DateTime.UtcNow;
        var changesToProcess = new List<KeyValuePair<string, DateTime>>();

        // Find changes that have been pending long enough
        foreach (var change in _pendingChanges)
        {
            if (now - change.Value > _debounceInterval)
            {
                changesToProcess.Add(change);
            }
        }

        if (changesToProcess.Count == 0)
            return;

        // Process changes
        foreach (var change in changesToProcess)
        {
            // Remove from pending
            _pendingChanges.TryRemove(change.Key, out _);

            // Parse the change key
            var parts = change.Key.Split('|');
            if (parts.Length != 3)
                continue;

            var workspacePath = parts[0];
            var filePath = parts[1];
            var changeType = parts[2];

            try
            {
                if (changeType == "delete")
                {
                    await _fileIndexingService.RemoveFileAsync(workspacePath, filePath);
                    _logger.LogDebug("Removed file from index: {FilePath}", filePath);
                }
                else if (changeType == "change")
                {
                    if (File.Exists(filePath))
                    {
                        await _fileIndexingService.IndexFileAsync(workspacePath, filePath);
                        _logger.LogDebug("Updated file in index: {FilePath}", filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file change: {FilePath}", filePath);
            }
        }
    }
}