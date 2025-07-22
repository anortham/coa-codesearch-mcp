using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class IndexWorkspaceTool
{
    private readonly ILogger<IndexWorkspaceTool> _logger;
    private readonly FileIndexingService _fileIndexingService;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly FileWatcherService? _fileWatcherService;

    public IndexWorkspaceTool(
        ILogger<IndexWorkspaceTool> logger,
        FileIndexingService fileIndexingService,
        ILuceneIndexService luceneIndexService,
        FileWatcherService? fileWatcherService = null)
    {
        _logger = logger;
        _fileIndexingService = fileIndexingService;
        _luceneIndexService = luceneIndexService;
        _fileWatcherService = fileWatcherService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        bool forceRebuild = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Index workspace request for: {WorkspacePath}, Force: {Force}", workspacePath, forceRebuild);

            // Validate input
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = "Workspace path cannot be empty"
                };
            }

            // CRITICAL: Protect memory indexes from being indexed as code
            if (workspacePath.Contains("memory", StringComparison.OrdinalIgnoreCase) || 
                workspacePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted to index protected path as workspace: {WorkspacePath}", workspacePath);
                return new
                {
                    success = false,
                    error = $"Cannot index {PathConstants.BaseDirectoryName} directories or memory paths. These are managed internally by the indexing system."
                };
            }

            if (!Directory.Exists(workspacePath))
            {
                return new
                {
                    success = false,
                    error = $"Workspace path does not exist: {workspacePath}"
                };
            }

            // Check if index exists and get document count
            var indexExists = false;
            var documentCount = 0;
            
            try
            {
                var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
                var indexReader = searcher.IndexReader;
                documentCount = indexReader.NumDocs;
                indexExists = documentCount > 0;
            }
            catch
            {
                // Index doesn't exist yet
                indexExists = false;
            }

            // Determine if we should index
            var shouldIndex = !indexExists || forceRebuild;
            
            if (!shouldIndex)
            {
                _logger.LogInformation("Index already exists with {Count} documents, skipping", documentCount);
                return new
                {
                    success = true,
                    message = $"Index already exists with {documentCount} documents. Use forceRebuild=true to rebuild.",
                    documentCount = documentCount,
                    action = "skipped"
                };
            }

            // Clear existing index if force rebuild
            if (forceRebuild && indexExists)
            {
                _logger.LogInformation("Force rebuild requested, clearing existing index");
                try
                {
                    // Use the service's built-in method to clear the index safely
                    // This will ONLY clear the specific index directory, not memories or backups
                    await _luceneIndexService.ClearIndexAsync(workspacePath);
                    _logger.LogInformation("Cleared existing index for workspace {WorkspacePath}", workspacePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clear existing index, will overwrite");
                }
            }

            // Perform indexing
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Starting index build for {WorkspacePath}", workspacePath);
            
            var indexedCount = await _fileIndexingService.IndexDirectoryAsync(
                workspacePath, 
                workspacePath, 
                cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Indexed {Count} files in {Duration:F2} seconds", indexedCount, duration.TotalSeconds);

            // Start file watching for this workspace
            if (_fileWatcherService != null)
            {
                try
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watching for workspace: {WorkspacePath}", workspacePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start file watching for workspace: {WorkspacePath}", workspacePath);
                    // Don't fail the entire operation if file watching fails
                }
            }

            return new
            {
                success = true,
                message = $"Successfully indexed {indexedCount} files",
                workspacePath = workspacePath,
                filesIndexed = indexedCount,
                duration = $"{duration.TotalSeconds:F2} seconds",
                action = forceRebuild ? "rebuilt" : "created",
                fileWatching = _fileWatcherService != null ? "enabled" : "disabled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing workspace: {WorkspacePath}", workspacePath);
            return new
            {
                success = false,
                error = $"Failed to index workspace: {ex.Message}"
            };
        }
    }
}