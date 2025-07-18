using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class IndexWorkspaceTool
{
    private readonly ILogger<IndexWorkspaceTool> _logger;
    private readonly FileIndexingService _fileIndexingService;
    private readonly LuceneIndexService _luceneIndexService;

    public IndexWorkspaceTool(
        ILogger<IndexWorkspaceTool> logger,
        FileIndexingService fileIndexingService,
        LuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _fileIndexingService = fileIndexingService;
        _luceneIndexService = luceneIndexService;
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
                var indexPath = Path.Combine(workspacePath, ".codesearch");
                if (Directory.Exists(indexPath))
                {
                    try
                    {
                        // Close the index first
                        _luceneIndexService.Dispose();
                        await Task.Delay(100); // Brief delay to ensure file handles are released
                        
                        Directory.Delete(indexPath, true);
                        _logger.LogInformation("Cleared existing index at {IndexPath}", indexPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clear existing index, will overwrite");
                    }
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

            return new
            {
                success = true,
                message = $"Successfully indexed {indexedCount} files",
                workspacePath = workspacePath,
                filesIndexed = indexedCount,
                duration = $"{duration.TotalSeconds:F2} seconds",
                action = forceRebuild ? "rebuilt" : "created"
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