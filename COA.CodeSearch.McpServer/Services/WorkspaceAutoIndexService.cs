using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Automatically re-indexes all previously indexed workspaces on startup
/// to catch changes made between sessions
/// </summary>
public class WorkspaceAutoIndexService : BackgroundService
{
    private readonly ILogger<WorkspaceAutoIndexService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public WorkspaceAutoIndexService(
        ILogger<WorkspaceAutoIndexService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("WorkspaceAutoIndex:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Workspace auto-indexing is disabled in configuration");
            return;
        }

        try
        {
            // Wait a moment for all services to fully initialize
            var startupDelay = _configuration.GetValue("WorkspaceAutoIndex:StartupDelayMilliseconds", 3000);
            await Task.Delay(startupDelay, stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var luceneService = scope.ServiceProvider.GetService<ILuceneIndexService>();
            var fileIndexingService = scope.ServiceProvider.GetService<FileIndexingService>();
            var fileWatcherService = scope.ServiceProvider.GetService<FileWatcherService>();
            
            if (luceneService == null || fileIndexingService == null)
            {
                _logger.LogWarning("Required services not available for auto-indexing workspaces");
                return;
            }

            // Get all previously indexed workspaces
            var indexMappings = luceneService.GetAllIndexMappings();
            
            if (indexMappings.Count == 0)
            {
                _logger.LogInformation("No previously indexed workspaces found for auto-indexing");
                return;
            }

            _logger.LogInformation("Starting auto-reindex of {Count} previously indexed workspaces", indexMappings.Count);

            // Re-index each workspace to catch changes made between sessions
            foreach (var mapping in indexMappings)
            {
                var workspacePath = mapping.Key;
                
                // CRITICAL: Skip memory indexes - they should NEVER be re-indexed as code directories
                if (workspacePath.Contains("memory", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping memory index path from auto-indexing: {WorkspacePath}", workspacePath);
                    continue;
                }
                
                // Verify the workspace still exists
                if (!Directory.Exists(workspacePath))
                {
                    _logger.LogWarning("Previously indexed workspace no longer exists: {WorkspacePath}", workspacePath);
                    continue;
                }

                try
                {
                    _logger.LogInformation("Auto-reindexing workspace: {WorkspacePath}", workspacePath);
                    
                    var startTime = DateTime.UtcNow;
                    var filesIndexed = await fileIndexingService.IndexDirectoryAsync(workspacePath, workspacePath, stoppingToken);
                    var duration = DateTime.UtcNow - startTime;
                    
                    _logger.LogInformation(
                        "Successfully auto-reindexed workspace: {WorkspacePath} - {FilesIndexed} files in {Duration:F2} seconds",
                        workspacePath, filesIndexed, duration.TotalSeconds);
                    
                    // Start file watching for this workspace
                    if (fileWatcherService != null)
                    {
                        try
                        {
                            fileWatcherService.StartWatching(workspacePath);
                            _logger.LogInformation("Started file watching for workspace: {WorkspacePath}", workspacePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to start file watching for workspace: {WorkspacePath}", workspacePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-reindex workspace: {WorkspacePath}", workspacePath);
                }
            }

            _logger.LogInformation("Completed auto-reindex of all previously indexed workspaces");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during workspace auto-indexing");
        }
    }
}