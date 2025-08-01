using COA.CodeSearch.Contracts;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for checking the health of Lucene indexes and providing detailed metrics
/// </summary>
public class IndexHealthCheckTool
{
    private readonly ILogger<IndexHealthCheckTool> _logger;
    private readonly LuceneIndexService _luceneIndexService;
    private readonly IIndexingMetricsService _metricsService;
    private readonly ICircuitBreakerService _circuitBreakerService;

    public IndexHealthCheckTool(
        ILogger<IndexHealthCheckTool> logger,
        LuceneIndexService luceneIndexService,
        IIndexingMetricsService metricsService,
        ICircuitBreakerService circuitBreakerService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _metricsService = metricsService;
        _circuitBreakerService = circuitBreakerService;
    }

    /// <summary>
    /// Perform comprehensive health check with metrics and circuit breaker status
    /// </summary>
    public async Task<object> ExecuteAsync(bool includeMetrics = true, bool includeCircuitBreakerStatus = true, bool includeAutoRepair = false, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting comprehensive index health check");

            var healthResult = await _luceneIndexService.CheckHealthAsync(includeAutoRepair, cancellationToken);
            
            var result = new
            {
                status = healthResult.Status.ToString(),
                description = healthResult.Description,
                timestamp = DateTime.UtcNow,
                data = healthResult.Data,
                exception = healthResult.Exception?.Message,
                metrics = includeMetrics ? await GetMetricsAsync() : null,
                circuitBreakerStatus = includeCircuitBreakerStatus ? GetCircuitBreakerStatus() : null,
                summary = GenerateHealthSummary(healthResult)
            };

            _logger.LogInformation("Health check completed with status: {Status}", healthResult.Status);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform health check");
            return new
            {
                status = "Error",
                description = "Health check failed",
                timestamp = DateTime.UtcNow,
                error = ex.Message,
                exception = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Get indexing performance metrics
    /// </summary>
    private async Task<object> GetMetricsAsync()
    {
        try
        {
            var metricsSnapshot = await _metricsService.GetCurrentMetricsAsync();
            var metricsReport = await _metricsService.GetMetricsReportAsync(TimeSpan.FromHours(24)); // Last 24 hours

            return new
            {
                current = new
                {
                    totalOperations = metricsSnapshot.TotalOperations,
                    totalFileOperations = metricsSnapshot.TotalFileOperations,
                    totalSearchOperations = metricsSnapshot.TotalSearchOperations,
                    totalBytesIndexed = metricsSnapshot.TotalBytesIndexed,
                    recentFileOperationsCount = metricsSnapshot.RecentFileOperationsCount,
                    recentFileOperationsPerSecond = metricsSnapshot.RecentFileOperationsPerSecond,
                    currentWorkingSetMB = metricsSnapshot.CurrentWorkingSetMB,
                    currentPrivateMemoryMB = metricsSnapshot.CurrentPrivateMemoryMB
                },
                performance = new
                {
                    recentAverageFileIndexingTime = metricsSnapshot.RecentAverageFileIndexingTime,
                    hourlyAverageFileIndexingTime = metricsSnapshot.HourlyAverageFileIndexingTime,
                    averageFileIndexingTime = metricsReport.AverageFileIndexingTime,
                    medianFileIndexingTime = metricsReport.MedianFileIndexingTime,
                    p95FileIndexingTime = metricsReport.P95FileIndexingTime,
                    averageSearchTime = metricsReport.AverageSearchTime,
                    peakMemoryUsageMB = metricsReport.PeakMemoryUsageMB,
                    averageMemoryUsageMB = metricsReport.AverageMemoryUsageMB
                },
                errors = new
                {
                    totalFileOperations = metricsReport.TotalFileOperations,
                    successfulFileOperations = metricsReport.SuccessfulFileOperations,
                    failedFileOperations = metricsReport.FailedFileOperations,
                    recentFileErrorRate = metricsSnapshot.RecentFileErrorRate,
                    topFileErrors = metricsReport.TopFileErrors?.Take(5).ToList()
                },
                trends = new
                {
                    performanceByFileType = metricsReport.PerformanceByFileType?.Take(10).ToList(),
                    topFileExtensionsByCount = metricsSnapshot.TopFileExtensionsByCount?.Take(5).ToList(),
                    topFileExtensionsBySize = metricsSnapshot.TopFileExtensionsBySize?.Take(5).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metrics for health check");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve metrics", 
                details = ex.Message 
            };
        }
    }

    /// <summary>
    /// Get circuit breaker status for all operations
    /// </summary>
    private object GetCircuitBreakerStatus()
    {
        try
        {
            var metrics = _circuitBreakerService.GetMetrics();
            
            return new
            {
                totalCircuits = metrics.Count,
                openCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.Open),
                halfOpenCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.HalfOpen),
                closedCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.Closed),
                disabledCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.Disabled),
                circuits = metrics.Select(kvp => new
                {
                    operation = kvp.Key,
                    state = kvp.Value.State.ToString(),
                    failureCount = kvp.Value.FailureCount,
                    successfulExecutions = kvp.Value.SuccessfulExecutions,
                    totalExecutions = kvp.Value.TotalExecutions,
                    lastFailureTime = kvp.Value.LastFailureTime,
                    lastSuccessTime = kvp.Value.LastSuccessTime,
                    successRate = kvp.Value.TotalExecutions > 0 
                        ? (double)kvp.Value.SuccessfulExecutions / kvp.Value.TotalExecutions * 100 
                        : 0
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get circuit breaker status for health check");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve circuit breaker status", 
                details = ex.Message 
            };
        }
    }

    /// <summary>
    /// Generate a comprehensive health summary with recommendations
    /// </summary>
    private object GenerateHealthSummary(IndexHealthCheckResult healthResult)
    {
        var summary = new
        {
            overallHealth = healthResult.Status.ToString(),
            criticalIssues = GetCriticalIssues(healthResult),
            recommendations = GetRecommendations(healthResult),
            keyMetrics = GetKeyMetrics(healthResult.Data),
            uptime = DateTime.UtcNow - Program.ServerStartTime,
            riskLevel = GetRiskLevel(healthResult)
        };

        return summary;
    }

    /// <summary>
    /// Extract critical issues from health check data
    /// </summary>
    private List<string> GetCriticalIssues(IndexHealthCheckResult healthResult)
    {
        var issues = new List<string>();

        if (healthResult.Data.TryGetValue("stuckLocks", out var stuckLocksObj) && 
            stuckLocksObj is int stuckLocks && stuckLocks > 0)
        {
            issues.Add($"{stuckLocks} stuck write locks detected - manual intervention required");
        }

        if (healthResult.Data.TryGetValue("corruptedIndexes", out var corruptedObj) && 
            corruptedObj is int corrupted && corrupted > 0)
        {
            issues.Add($"{corrupted} corrupted indexes detected - data integrity compromised");
        }

        if (healthResult.Exception != null)
        {
            issues.Add($"Health check failed with exception: {healthResult.Exception.Message}");
        }

        return issues;
    }

    /// <summary>
    /// Generate recommendations based on health check results
    /// </summary>
    private List<string> GetRecommendations(IndexHealthCheckResult healthResult)
    {
        var recommendations = new List<string>();

        if (healthResult.Status == IndexHealthCheckResult.HealthStatus.Unhealthy)
        {
            recommendations.Add("URGENT: Address critical issues immediately");
            
            if (healthResult.Data.TryGetValue("stuckLocks", out var stuckLocksObj) && 
                stuckLocksObj is int stuckLocks && stuckLocks > 0)
            {
                recommendations.Add("Delete stuck write.lock files after ensuring no processes are using the indexes");
            }

            if (healthResult.Data.TryGetValue("corruptedIndexes", out var corruptedObj) && 
                corruptedObj is int corrupted && corrupted > 0)
            {
                recommendations.Add("Rebuild corrupted indexes from source files");
            }
        }
        else if (healthResult.Status == IndexHealthCheckResult.HealthStatus.Degraded)
        {
            recommendations.Add("Address degraded performance when convenient");
            
            if (healthResult.Data.TryGetValue("unhealthyIndexes", out var unhealthyObj) && 
                unhealthyObj is int unhealthy && unhealthy > 0)
            {
                recommendations.Add("Re-index missing or unhealthy indexes");
            }
        }
        else
        {
            recommendations.Add("System is healthy - maintain regular monitoring");
        }

        // Add proactive recommendations
        recommendations.Add("Schedule regular health checks (recommended: daily)");
        recommendations.Add("Monitor memory usage and indexing performance trends");
        recommendations.Add("Keep backups of critical index configurations");

        return recommendations;
    }

    /// <summary>
    /// Extract key metrics from health check data
    /// </summary>
    private object GetKeyMetrics(Dictionary<string, object> data)
    {
        return new
        {
            totalIndexes = data.GetValueOrDefault("totalIndexes", 0),
            healthyIndexes = data.GetValueOrDefault("healthyIndexes", 0),
            unhealthyIndexes = data.GetValueOrDefault("unhealthyIndexes", 0),
            stuckLocks = data.GetValueOrDefault("stuckLocks", 0),
            corruptedIndexes = data.GetValueOrDefault("corruptedIndexes", 0),
            projectMemoryIndex = data.GetValueOrDefault("projectMemoryIndex", "unknown"),
            localMemoryIndex = data.GetValueOrDefault("localMemoryIndex", "unknown")
        };
    }

    /// <summary>
    /// Determine risk level based on health status
    /// </summary>
    private string GetRiskLevel(IndexHealthCheckResult healthResult)
    {
        return healthResult.Status switch
        {
            IndexHealthCheckResult.HealthStatus.Healthy => "Low",
            IndexHealthCheckResult.HealthStatus.Degraded => "Medium",
            IndexHealthCheckResult.HealthStatus.Unhealthy => "High",
            _ => "Unknown"
        };
    }
}