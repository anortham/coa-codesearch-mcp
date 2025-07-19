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

        public LuceneLifecycleService(
            LuceneIndexService luceneService,
            IHostApplicationLifetime lifetime,
            ILogger<LuceneLifecycleService> logger)
        {
            _luceneService = luceneService;
            _lifetime = lifetime;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Lucene lifecycle service");
            
            // Register for graceful shutdown
            _lifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogInformation("Application stopping - cleaning up Lucene indexes");
                try
                {
                    _luceneService.Dispose();
                    _logger.LogInformation("Lucene indexes cleaned up successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up Lucene indexes");
                }
            });

            // Clean up any stuck locks from previous runs
            try
            {
                _luceneService.CleanupStuckIndexes();
                _logger.LogInformation("Cleaned up stuck Lucene indexes from previous runs");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up stuck indexes on startup");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Additional cleanup if needed
            // The main cleanup happens in ApplicationStopping event
            return Task.CompletedTask;
        }
    }
}