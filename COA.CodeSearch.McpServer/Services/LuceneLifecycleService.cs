using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services
{
    /// <summary>
    /// Manages Lucene service lifecycle to ensure proper cleanup on shutdown
    /// </summary>
    public class LuceneLifecycleService : IHostedService, IAsyncDisposable
    {
        private const string ProjectMemoryIndexName = "project-memory";
        private const string LocalMemoryIndexName = "local-memory";
        
        private readonly ILuceneIndexService _luceneService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<LuceneLifecycleService> _logger;
        private readonly IPathResolutionService _pathResolution;
        private bool _disposed;

        public LuceneLifecycleService(
            ILuceneIndexService luceneService,
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
            await InitializeMemoryIndexes(cancellationToken);
            
            // Register for graceful shutdown
            _lifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogWarning("Application stopping - Lucene cleanup will occur in StopAsync");
            });

            // Note: Stuck lock cleanup now happens in LuceneIndexService constructor
            // to ensure it occurs before any index operations
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return;
            }
            
            _logger.LogWarning("Stopping Lucene lifecycle service");
            
            try
            {
                // Dispose the Lucene service properly
                if (_luceneService is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                    _logger.LogWarning("Lucene indexes cleaned up successfully during shutdown (async)");
                }
                else
                {
                    _luceneService.Dispose();
                    _logger.LogWarning("Lucene indexes cleaned up successfully during shutdown (sync)");
                }
                
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up Lucene indexes during shutdown");
            }
        }

        /// <summary>
        /// Initialize memory indexes if they don't exist to ensure they're available on new systems
        /// </summary>
        private async Task InitializeMemoryIndexes(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
                var localMemoryPath = _pathResolution.GetLocalMemoryPath();

                _logger.LogInformation("Initializing memory indexes: project={ProjectPath}, local={LocalPath}", 
                    projectMemoryPath, localMemoryPath);

                // Create a minimal dummy document to initialize each index
                // This ensures the index structure exists even if no memories have been stored yet
                await InitializeMemoryIndex(projectMemoryPath, ProjectMemoryIndexName, cancellationToken);
                await InitializeMemoryIndex(localMemoryPath, LocalMemoryIndexName, cancellationToken);

                _logger.LogInformation("Memory indexes initialization completed");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Memory index initialization was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize memory indexes - memory system may not work on first use");
            }
        }

        private async Task InitializeMemoryIndex(string indexPath, string indexType, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if index already exists by trying to get a writer
                // If it fails, the index doesn't exist and needs initialization
                var writer = await _luceneService.GetIndexWriterAsync(indexPath, cancellationToken);
                
                // If we get here, index exists - no need to initialize
                _logger.LogDebug("Memory index {IndexType} already exists at {Path}", indexType, indexPath);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No index found"))
            {
                _logger.LogInformation("Memory index {IndexType} doesn't exist, creating at {Path}", indexType, indexPath);
                
                // First clear any partial index, then get a writer to create fresh index
                await _luceneService.ClearIndexAsync(indexPath, cancellationToken);
                var writer = await _luceneService.GetIndexWriterAsync(indexPath, cancellationToken);
                
                // Commit to ensure the index structure is persisted
                await _luceneService.CommitAsync(indexPath, cancellationToken);
                
                _logger.LogInformation("Successfully created memory index {IndexType} at {Path}", indexType, indexPath);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Memory index {IndexType} initialization was cancelled", indexType);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not initialize memory index {IndexType} at {Path}", indexType, indexPath);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            
            // No resources to dispose in this service
            // The actual Lucene disposal happens in StopAsync
            await Task.CompletedTask;
        }
    }
}