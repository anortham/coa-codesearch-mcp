using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services
{
    /// <summary>
    /// Manages Lucene service lifecycle to ensure proper cleanup on shutdown
    /// </summary>
    public class LuceneLifecycleService : IHostedService
    {
        private readonly LuceneIndexService _luceneService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<LuceneLifecycleService> _logger;
        private readonly IPathResolutionService _pathResolution;

        public LuceneLifecycleService(
            LuceneIndexService luceneService,
            IHostApplicationLifetime lifetime,
            ILogger<LuceneLifecycleService> logger,
            IPathResolutionService pathResolution)
        {
            _luceneService = luceneService;
            _lifetime = lifetime;
            _logger = logger;
            _pathResolution = pathResolution;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Lucene lifecycle service");
            
            // Initialize memory indexes if they don't exist
            await InitializeMemoryIndexes();
            
            // Register for graceful shutdown
            _lifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogWarning("Application stopping - beginning Lucene index cleanup");
                try
                {
                    _luceneService.Dispose();
                    _logger.LogWarning("Lucene indexes cleaned up successfully during shutdown");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up Lucene indexes during shutdown");
                }
            });

            // Note: Stuck lock cleanup now happens in LuceneIndexService constructor
            // to ensure it occurs before any index operations
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Additional cleanup if needed
            // The main cleanup happens in ApplicationStopping event
            return Task.CompletedTask;
        }

        /// <summary>
        /// Initialize memory indexes if they don't exist to ensure they're available on new systems
        /// </summary>
        private async Task InitializeMemoryIndexes()
        {
            try
            {
                var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
                var localMemoryPath = _pathResolution.GetLocalMemoryPath();

                _logger.LogInformation("Initializing memory indexes: project={ProjectPath}, local={LocalPath}", 
                    projectMemoryPath, localMemoryPath);

                // Create a minimal dummy document to initialize each index
                // This ensures the index structure exists even if no memories have been stored yet
                await InitializeMemoryIndex(projectMemoryPath, "project-memory");
                await InitializeMemoryIndex(localMemoryPath, "local-memory");

                _logger.LogInformation("Memory indexes initialization completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize memory indexes - memory system may not work on first use");
            }
        }

        private async Task InitializeMemoryIndex(string indexPath, string indexType)
        {
            try
            {
                // Check if index already exists by trying to get a writer
                // If it fails, the index doesn't exist and needs initialization
                var writer = await _luceneService.GetIndexWriterAsync(indexPath);
                
                // If we get here, index exists - no need to initialize
                _logger.LogDebug("Memory index {IndexType} already exists at {Path}", indexType, indexPath);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No index found"))
            {
                _logger.LogInformation("Memory index {IndexType} doesn't exist, creating at {Path}", indexType, indexPath);
                
                // First clear any partial index, then get a writer to create fresh index
                await _luceneService.ClearIndexAsync(indexPath);
                var writer = await _luceneService.GetIndexWriterAsync(indexPath);
                
                // Commit to ensure the index structure is persisted
                await _luceneService.CommitAsync(indexPath);
                
                _logger.LogInformation("Successfully created memory index {IndexType} at {Path}", indexType, indexPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not initialize memory index {IndexType} at {Path}", indexType, indexPath);
            }
        }
    }
}