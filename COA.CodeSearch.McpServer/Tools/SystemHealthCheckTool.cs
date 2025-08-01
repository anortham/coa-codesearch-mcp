using COA.CodeSearch.Contracts;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Comprehensive system health check tool covering all major services and components
/// </summary>
[McpServerToolType]
public class SystemHealthCheckTool : ITool
{
    private readonly ILogger<SystemHealthCheckTool> _logger;
    private readonly IndexHealthCheckTool _indexHealthCheck;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly ICircuitBreakerService _circuitBreakerService;
    private readonly IIndexingMetricsService _metricsService;
    private readonly MemoryLimitsConfiguration _memoryLimits;

    public string ToolName => ToolNames.SystemHealthCheck;
    public string Description => "Perform comprehensive system health check covering all major services and components";
    public ToolCategory Category => ToolCategory.Infrastructure;

    public SystemHealthCheckTool(
        ILogger<SystemHealthCheckTool> logger,
        IndexHealthCheckTool indexHealthCheck,
        IMemoryPressureService memoryPressureService,
        ICircuitBreakerService circuitBreakerService,
        IIndexingMetricsService metricsService,
        IOptions<MemoryLimitsConfiguration> memoryLimits)
    {
        _logger = logger;
        _indexHealthCheck = indexHealthCheck;
        _memoryPressureService = memoryPressureService;
        _circuitBreakerService = circuitBreakerService;
        _metricsService = metricsService;
        _memoryLimits = memoryLimits.Value;
    }

    /// <summary>
    /// Attribute-based ExecuteAsync method for MCP registration
    /// </summary>
    [McpServerTool(Name = "system_health_check")]
    [Description("Perform comprehensive system health check covering all major services and components. Includes memory pressure, index health, circuit breakers, system metrics, and configuration validation with overall assessment and recommendations.")]
    public async Task<object> ExecuteAsync(SystemHealthCheckParams parameters)
    {
        // Call the existing implementation
        return await ExecuteAsync(
            parameters?.IncludeIndexHealth ?? true,
            parameters?.IncludeMemoryPressure ?? true,
            parameters?.IncludeSystemMetrics ?? true,
            parameters?.IncludeConfiguration ?? false,
            CancellationToken.None);
    }

    /// <summary>
    /// Perform comprehensive system health check
    /// </summary>
    public async Task<object> ExecuteAsync(
        bool includeIndexHealth = true,
        bool includeMemoryPressure = true,
        bool includeSystemMetrics = true,
        bool includeConfiguration = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting comprehensive system health check");
            var startTime = DateTime.UtcNow;

            var result = new
            {
                status = "Healthy",
                timestamp = startTime,
                serverUptime = DateTime.UtcNow - Program.ServerStartTime,
                version = GetVersionInfo(),
                systemInfo = GetSystemInfo(),
                memoryPressure = includeMemoryPressure ? GetMemoryPressureStatus() : null,
                indexHealth = includeIndexHealth ? await GetIndexHealthAsync(cancellationToken) : null,
                circuitBreakers = GetCircuitBreakerSummary(),
                systemMetrics = includeSystemMetrics ? await GetSystemMetricsAsync() : null,
                configuration = includeConfiguration ? GetConfigurationStatus() : null,
                overallAssessment = new { }, // Will be filled after gathering data
                recommendations = new List<string>(),
                warnings = new List<string>(),
                errors = new List<string>()
            };

            // Calculate overall health status
            var assessment = CalculateOverallHealth(result);

            var finalResult = new
            {
                result.status,
                result.timestamp,
                result.serverUptime,
                result.version,
                result.systemInfo,
                result.memoryPressure,
                result.indexHealth,
                result.circuitBreakers,
                result.systemMetrics,
                result.configuration,
                overallAssessment = assessment.assessment,
                recommendations = assessment.recommendations,
                warnings = assessment.warnings,
                errors = assessment.errors,
                checkDuration = DateTime.UtcNow - startTime
            };

            _logger.LogInformation("System health check completed in {Duration}ms",
                (DateTime.UtcNow - startTime).TotalMilliseconds);

            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System health check failed");
            return new
            {
                status = "Error",
                timestamp = DateTime.UtcNow,
                error = ex.Message,
                exception = ex.ToString()
            };
        }
    }

    private object GetVersionInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var buildDate = new FileInfo(assembly.Location).LastWriteTime;

        return new
        {
            version = version?.ToString() ?? "Unknown",
            buildDate = buildDate,
            framework = Environment.Version.ToString(),
            platform = Environment.OSVersion.ToString(),
            architecture = Environment.Is64BitProcess ? "x64" : "x86"
        };
    }

    private object GetSystemInfo()
    {
        var process = Process.GetCurrentProcess();

        return new
        {
            processId = process.Id,
            processName = process.ProcessName,
            startTime = process.StartTime,
            threadCount = process.Threads.Count,
            handleCount = process.HandleCount,
            machineName = Environment.MachineName,
            userDomainName = Environment.UserDomainName,
            osVersion = Environment.OSVersion.VersionString,
            processorCount = Environment.ProcessorCount,
            systemPageSize = Environment.SystemPageSize,
            workingDirectory = Environment.CurrentDirectory
        };
    }

    private object GetMemoryPressureStatus()
    {
        try
        {
            var currentLevel = _memoryPressureService.GetCurrentPressureLevel();
            var process = Process.GetCurrentProcess();
            var gcMemory = GC.GetTotalMemory(false);

            return new
            {
                pressureLevel = currentLevel.ToString(),
                workingSetMB = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2),
                privateMemoryMB = Math.Round(process.PrivateMemorySize64 / (1024.0 * 1024.0), 2),
                gcMemoryMB = Math.Round(gcMemory / (1024.0 * 1024.0), 2),
                maxWorkingSetMB = Math.Round(process.MaxWorkingSet.ToInt64() / (1024.0 * 1024.0), 2),
                generation0Collections = GC.CollectionCount(0),
                generation1Collections = GC.CollectionCount(1),
                generation2Collections = GC.CollectionCount(2),
                backpressureEnabled = _memoryLimits.EnableBackpressure,
                recommendedConcurrency = _memoryPressureService.GetRecommendedConcurrency(_memoryLimits.MaxIndexingConcurrency),
                recommendedBatchSize = _memoryPressureService.GetRecommendedBatchSize(_memoryLimits.MaxBatchSize),
                isThrottling = _memoryPressureService.ShouldThrottleOperation("health_check")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memory pressure status");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve memory pressure status", 
                details = ex.Message 
            };
        }
    }

    private async Task<object> GetIndexHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var healthResult = await _indexHealthCheck.ExecuteAsync(
                includeMetrics: false,
                includeCircuitBreakerStatus: false,
                includeAutoRepair: false,
                cancellationToken);

            return healthResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get index health status");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve index health", 
                details = ex.Message 
            };
        }
    }

    private object GetCircuitBreakerSummary()
    {
        try
        {
            var metrics = _circuitBreakerService.GetMetrics();
            var totalCircuits = metrics.Count;
            var openCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.Open);
            var halfOpenCircuits = metrics.Count(m => m.Value.State == CircuitBreakerState.HalfOpen);

            return new
            {
                totalCircuits,
                healthyCircuits = totalCircuits - openCircuits - halfOpenCircuits,
                openCircuits,
                halfOpenCircuits,
                overallHealth = openCircuits == 0 && halfOpenCircuits == 0 ? "Healthy" : 
                              openCircuits > totalCircuits / 2 ? "Critical" : "Degraded",
                criticalOperations = metrics
                    .Where(m => m.Value.State == CircuitBreakerState.Open)
                    .Select(m => m.Key)
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get circuit breaker summary");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve circuit breaker status", 
                details = ex.Message 
            };
        }
    }

    private async Task<object> GetSystemMetricsAsync()
    {
        try
        {
            var metricsSnapshot = await _metricsService.GetCurrentMetricsAsync();

            return new
            {
                performance = new
                {
                    totalOperations = metricsSnapshot.TotalOperations,
                    operationsPerSecond = metricsSnapshot.RecentFileOperationsPerSecond,
                    averageIndexingTime = metricsSnapshot.RecentAverageFileIndexingTime,
                    errorRate = metricsSnapshot.RecentFileErrorRate
                },
                throughput = new
                {
                    totalFileOperations = metricsSnapshot.TotalFileOperations,
                    totalSearchOperations = metricsSnapshot.TotalSearchOperations,
                    totalBytesIndexed = metricsSnapshot.TotalBytesIndexed,
                    recentFileOperationsCount = metricsSnapshot.RecentFileOperationsCount
                },
                memory = new
                {
                    currentWorkingSetMB = metricsSnapshot.CurrentWorkingSetMB,
                    currentPrivateMemoryMB = metricsSnapshot.CurrentPrivateMemoryMB
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get system metrics");
            return new ErrorResponse 
            { 
                error = "Failed to retrieve system metrics", 
                details = ex.Message 
            };
        }
    }

    private object GetConfigurationStatus()
    {
        return new
        {
            memoryLimits = new
            {
                maxFileSize = _memoryLimits.MaxFileSize,
                largeFileThreshold = _memoryLimits.LargeFileThreshold,
                maxAllowedResults = _memoryLimits.MaxAllowedResults,
                maxIndexingConcurrency = _memoryLimits.MaxIndexingConcurrency,
                maxMemoryUsagePercent = _memoryLimits.MaxMemoryUsagePercent,
                enableBackpressure = _memoryLimits.EnableBackpressure,
                enableMemoryPressureGC = _memoryLimits.EnableMemoryPressureGC
            }
        };
    }

    private (object assessment, List<string> recommendations, List<string> warnings, List<string> errors) CalculateOverallHealth(dynamic result)
    {
        var recommendations = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var issues = new List<string>();

        string overallStatus = "Healthy";
        int riskScore = 0;

        // Check memory pressure
        if (result.memoryPressure != null)
        {
            string pressureLevel = result.memoryPressure.pressureLevel?.ToString() ?? "Unknown";
            switch (pressureLevel)
            {
                case "Critical":
                    errors.Add("Critical memory pressure detected");
                    riskScore += 50;
                    overallStatus = "Critical";
                    break;
                case "High":
                    warnings.Add("High memory pressure detected");
                    riskScore += 30;
                    if (overallStatus == "Healthy") overallStatus = "Degraded";
                    break;
                case "Moderate":
                    warnings.Add("Moderate memory pressure detected");
                    riskScore += 10;
                    break;
            }
        }

        // Check circuit breakers
        if (result.circuitBreakers != null)
        {
            int openCircuits = result.circuitBreakers.openCircuits ?? 0;
            if (openCircuits > 0)
            {
                if (openCircuits >= 3)
                {
                    errors.Add($"{openCircuits} circuit breakers are open - critical systems failing");
                    riskScore += 40;
                    overallStatus = "Critical";
                }
                else
                {
                    warnings.Add($"{openCircuits} circuit breakers are open");
                    riskScore += 20;
                    if (overallStatus == "Healthy") overallStatus = "Degraded";
                }
            }
        }

        // Check index health
        if (result.indexHealth != null && result.indexHealth.status != null)
        {
            string indexStatus = result.indexHealth.status.ToString();
            if (indexStatus == "Unhealthy")
            {
                errors.Add("Index system is unhealthy");
                riskScore += 30;
                overallStatus = "Critical";
            }
            else if (indexStatus == "Degraded")
            {
                warnings.Add("Index system performance is degraded");
                riskScore += 15;
                if (overallStatus == "Healthy") overallStatus = "Degraded";
            }
        }

        // Generate recommendations
        if (riskScore >= 50)
        {
            recommendations.Add("IMMEDIATE ACTION REQUIRED: Critical issues detected");
            recommendations.Add("Review error logs and address critical failures");
            recommendations.Add("Consider restarting services if safe to do so");
        }
        else if (riskScore >= 20)
        {
            recommendations.Add("Monitor system closely and address warnings");
            recommendations.Add("Review performance trends and capacity planning");
        }
        else
        {
            recommendations.Add("System is operating normally");
            recommendations.Add("Continue routine monitoring and maintenance");
        }

        // Add proactive recommendations
        recommendations.Add("Schedule regular health checks (recommended: every 15 minutes)");
        recommendations.Add("Set up alerting for critical health status changes");
        recommendations.Add("Monitor memory usage trends and adjust limits as needed");

        var assessment = new
        {
            overallStatus,
            riskScore,
            riskLevel = riskScore >= 50 ? "Critical" : riskScore >= 20 ? "Medium" : "Low",
            issueCount = errors.Count + warnings.Count,
            lastChecked = DateTime.UtcNow
        };

        return (assessment, recommendations, warnings, errors);
    }
}

/// <summary>
/// Parameters for the SystemHealthCheckTool
/// </summary>
public class SystemHealthCheckParams
{
    [Description("Include index health status in the response")]
    public bool? IncludeIndexHealth { get; set; } = true;
    
    [Description("Include memory pressure monitoring data")]
    public bool? IncludeMemoryPressure { get; set; } = true;
    
    [Description("Include system performance metrics")]
    public bool? IncludeSystemMetrics { get; set; } = true;
    
    [Description("Include configuration status and validation")]
    public bool? IncludeConfiguration { get; set; } = false;
}