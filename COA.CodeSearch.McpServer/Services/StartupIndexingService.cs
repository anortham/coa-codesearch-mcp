using COA.CodeSearch.McpServer.Services.Lucene;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Background service that automatically indexes the current workspace on startup
/// Runs asynchronously to avoid blocking Claude Code loading
/// </summary>
public class StartupIndexingService : BackgroundService
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IWorkspaceRegistryService _workspaceRegistryService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StartupIndexingService> _logger;
    private readonly bool _enabled;
    private readonly int _startupDelaySeconds;
    private readonly List<string> _excludedPaths;

    public StartupIndexingService(
        ILuceneIndexService luceneIndexService,
        IFileIndexingService fileIndexingService,
        IPathResolutionService pathResolutionService,
        IWorkspaceRegistryService workspaceRegistryService,
        IConfiguration configuration,
        ILogger<StartupIndexingService> logger)
    {
        _luceneIndexService = luceneIndexService;
        _fileIndexingService = fileIndexingService;
        _pathResolutionService = pathResolutionService;
        _workspaceRegistryService = workspaceRegistryService;
        _configuration = configuration;
        _logger = logger;
        
        // Read configuration
        _enabled = configuration.GetValue<bool>("CodeSearch:StartupIndexing:Enabled", true);
        _startupDelaySeconds = configuration.GetValue<int>("CodeSearch:StartupIndexing:DelaySeconds", 3);
        
        // Read excluded paths and expand environment variables
        var excludedPaths = configuration.GetSection("CodeSearch:StartupIndexing:ExcludedPaths").Get<List<string>>() ?? new List<string>();
        _excludedPaths = excludedPaths.Select(path => Environment.ExpandEnvironmentVariables(path)).ToList();
        
        // Add fallback defaults if no configuration provided
        if (_excludedPaths.Count == 0)
        {
            _excludedPaths = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Startup indexing is disabled in configuration");
            return;
        }

        // Delay startup to let Claude Code fully initialize
        _logger.LogInformation("Waiting {Delay} seconds before starting workspace indexing", _startupDelaySeconds);
        await Task.Delay(TimeSpan.FromSeconds(_startupDelaySeconds), stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return;

        try
        {
            // Get current working directory as the workspace to index
            var workspacePath = Environment.CurrentDirectory;
            
            // Check if it's a valid workspace directory
            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("Current directory does not exist: {Path}", workspacePath);
                return;
            }

            // Skip if it's a system directory or temp directory
            if (_excludedPaths.Any(p => !string.IsNullOrEmpty(p) && workspacePath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Skipping auto-index for excluded directory: {Path}", workspacePath);
                return;
            }

            _logger.LogInformation("Starting background indexing for workspace: {Path}", workspacePath);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check if index already exists and is recent
                var indexExists = await _luceneIndexService.IndexExistsAsync(workspacePath, stoppingToken);
                if (indexExists)
                {
                    var stats = await _luceneIndexService.GetStatisticsAsync(workspacePath, stoppingToken);
                    
                    // Skip if index was updated in the last hour
                    if (DateTimeOffset.UtcNow - new DateTimeOffset(stats.LastModified) < TimeSpan.FromHours(1))
                    {
                        _logger.LogInformation(
                            "Index for {Path} is up-to-date (last modified: {LastModified}). Skipping auto-index.",
                            workspacePath, stats.LastModified);
                        return;
                    }
                }

                // Initialize the index
                var initResult = await _luceneIndexService.InitializeIndexAsync(workspacePath, stoppingToken);
                if (!initResult.Success)
                {
                    _logger.LogWarning("Failed to initialize index for {Path}", workspacePath);
                    return;
                }

                // Register workspace
                await _workspaceRegistryService.RegisterWorkspaceAsync(workspacePath);

                // Index all files
                var indexingResult = await _fileIndexingService.IndexWorkspaceAsync(
                    workspacePath, 
                    stoppingToken);

                stopwatch.Stop();

                if (indexingResult.Success)
                {
                    _logger.LogInformation(
                        "Successfully indexed workspace {Path}: {FileCount} files in {Duration:N2} seconds",
                        workspacePath, indexingResult.IndexedFileCount, stopwatch.Elapsed.TotalSeconds);
                }
                else
                {
                    _logger.LogWarning(
                        "Partial indexing for workspace {Path}. Indexed {FileCount} files with {ErrorCount} errors.",
                        workspacePath, indexingResult.IndexedFileCount, indexingResult.ErrorCount);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Workspace indexing cancelled for: {Path}", workspacePath);
                throw;
            }
            catch (Exception ex)
            {
                // Log error but don't throw - we don't want to crash the server
                _logger.LogError(ex, "Error during startup indexing of workspace: {Path}", workspacePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in startup indexing service");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Startup indexing service is stopping");
        await base.StopAsync(cancellationToken);
    }
}