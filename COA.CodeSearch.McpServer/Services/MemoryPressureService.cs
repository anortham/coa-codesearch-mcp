using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for monitoring and controlling memory usage to prevent OOM conditions
/// </summary>
public class MemoryPressureService : IMemoryPressureService, IDisposable
{
    private readonly ILogger<MemoryPressureService> _logger;
    private readonly MemoryLimitsConfiguration _config;
    private readonly Timer _memoryMonitorTimer;
    private volatile MemoryPressureLevel _currentPressureLevel = MemoryPressureLevel.Normal;
    private DateTime _lastGcTime = DateTime.MinValue;

    // Throttling counters
    private readonly Dictionary<string, DateTime> _lastThrottleLog = new();
    private readonly object _lockObject = new();

    public MemoryPressureService(
        ILogger<MemoryPressureService> logger,
        IOptions<MemoryLimitsConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;

        // Monitor memory every 30 seconds
        _memoryMonitorTimer = new Timer(CheckMemoryPressure, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

        _logger.LogInformation("Memory pressure monitoring started - Max usage: {MaxPercent}%, GC enabled: {GcEnabled}",
            _config.MaxMemoryUsagePercent, _config.EnableMemoryPressureGC);
    }

    public MemoryPressureLevel GetCurrentPressureLevel()
    {
        return _currentPressureLevel;
    }

    public bool ShouldThrottleOperation(string operationType)
    {
        if (!_config.EnableBackpressure)
            return false;

        var pressureLevel = _currentPressureLevel;

        // Throttle based on pressure level and operation type
        var shouldThrottle = pressureLevel switch
        {
            MemoryPressureLevel.Normal => false,
            MemoryPressureLevel.Moderate => operationType == "batch_indexing" || operationType == "large_search",
            MemoryPressureLevel.High => operationType != "memory_search", // Only allow memory searches
            MemoryPressureLevel.Critical => true, // Throttle everything
            _ => false
        };

        if (shouldThrottle)
        {
            LogThrottleEvent(operationType, pressureLevel);
        }

        return shouldThrottle;
    }

    public int GetRecommendedBatchSize(int maxBatchSize)
    {
        return _currentPressureLevel switch
        {
            MemoryPressureLevel.Normal => maxBatchSize,
            MemoryPressureLevel.Moderate => Math.Max(10, maxBatchSize / 2),
            MemoryPressureLevel.High => Math.Max(5, maxBatchSize / 4),
            MemoryPressureLevel.Critical => 1,
            _ => maxBatchSize
        };
    }

    public int GetRecommendedConcurrency(int maxConcurrency)
    {
        return _currentPressureLevel switch
        {
            MemoryPressureLevel.Normal => maxConcurrency,
            MemoryPressureLevel.Moderate => Math.Max(1, maxConcurrency / 2),
            MemoryPressureLevel.High => Math.Max(1, maxConcurrency / 4),
            MemoryPressureLevel.Critical => 1,
            _ => maxConcurrency
        };
    }

    public async Task TriggerMemoryCleanupIfNeededAsync()
    {
        if (!_config.EnableMemoryPressureGC)
            return;

        var pressureLevel = _currentPressureLevel;
        var timeSinceLastGc = DateTime.UtcNow - _lastGcTime;

        // Force GC based on pressure level and time since last GC
        var shouldForceGc = pressureLevel switch
        {
            MemoryPressureLevel.High => timeSinceLastGc > TimeSpan.FromMinutes(2),
            MemoryPressureLevel.Critical => timeSinceLastGc > TimeSpan.FromSeconds(30),
            _ => false
        };

        if (shouldForceGc)
        {
            _logger.LogInformation("Triggering garbage collection due to {PressureLevel} memory pressure", pressureLevel);
            
            await Task.Run(() =>
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            }).ConfigureAwait(false);

            _lastGcTime = DateTime.UtcNow;
            
            // Re-check pressure after GC
            CheckMemoryPressure(null);
        }
    }

    private void CheckMemoryPressure(object? state)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var totalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            var workingSetMB = process.WorkingSet64 / (1024.0 * 1024.0);

            // Get system memory info
            var availableMemoryMB = GetAvailableMemoryMB();
            var totalSystemMemoryMB = GetTotalSystemMemoryMB();
            var systemMemoryUsagePercent = ((totalSystemMemoryMB - availableMemoryMB) / totalSystemMemoryMB) * 100;

            var previousLevel = _currentPressureLevel;

            // Determine pressure level based on multiple factors
            _currentPressureLevel = CalculatePressureLevel(
                systemMemoryUsagePercent, 
                totalMemoryMB, 
                workingSetMB);

            // Log pressure level changes
            if (_currentPressureLevel != previousLevel)
            {
                _logger.LogWarning(
                    "Memory pressure level changed: {Previous} -> {Current}. " +
                    "System: {SystemPercent:F1}%, Working Set: {WorkingSetMB:F1}MB, GC Memory: {GcMemoryMB:F1}MB",
                    previousLevel, _currentPressureLevel, systemMemoryUsagePercent, workingSetMB, totalMemoryMB);
            }
            else if (_currentPressureLevel != MemoryPressureLevel.Normal)
            {
                // Log current pressure stats every few cycles when not normal
                _logger.LogDebug(
                    "Memory pressure: {Level}. System: {SystemPercent:F1}%, Working Set: {WorkingSetMB:F1}MB, GC Memory: {GcMemoryMB:F1}MB",
                    _currentPressureLevel, systemMemoryUsagePercent, workingSetMB, totalMemoryMB);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking memory pressure");
        }
    }

    private MemoryPressureLevel CalculatePressureLevel(double systemMemoryUsagePercent, double gcMemoryMB, double workingSetMB)
    {
        var maxUsagePercent = _config.MaxMemoryUsagePercent;

        // Critical: System memory usage very high OR working set extremely large
        if (systemMemoryUsagePercent > maxUsagePercent + 10 || workingSetMB > 2000)
        {
            return MemoryPressureLevel.Critical;
        }

        // High: System memory usage high OR working set large
        if (systemMemoryUsagePercent > maxUsagePercent + 5 || workingSetMB > 1000)
        {
            return MemoryPressureLevel.High;
        }

        // Moderate: System memory usage approaching limit OR GC memory high
        if (systemMemoryUsagePercent > maxUsagePercent || gcMemoryMB > 500)
        {
            return MemoryPressureLevel.Moderate;
        }

        return MemoryPressureLevel.Normal;
    }

    private double GetAvailableMemoryMB()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "wmic";
            process.StartInfo.Arguments = "OS get TotalVisibleMemorySize,FreePhysicalMemory /value";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("FreePhysicalMemory="))
                {
                    var value = line.Substring("FreePhysicalMemory=".Length).Trim();
                    if (long.TryParse(value, out var freeKB))
                    {
                        return freeKB / 1024.0; // Convert KB to MB
                    }
                }
            }
        }
        catch
        {
            // Fallback: Use GC memory as approximation
        }

        // Fallback calculation if wmic fails
        return Math.Max(0, 4096 - (GC.GetTotalMemory(false) / (1024.0 * 1024.0)));
    }

    private double GetTotalSystemMemoryMB()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "wmic";
            process.StartInfo.Arguments = "computersystem get TotalPhysicalMemory /value";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("TotalPhysicalMemory="))
                {
                    var value = line.Substring("TotalPhysicalMemory=".Length).Trim();
                    if (long.TryParse(value, out var totalBytes))
                    {
                        return totalBytes / (1024.0 * 1024.0); // Convert bytes to MB
                    }
                }
            }
        }
        catch
        {
            // Fallback: assume reasonable default
        }

        return 8192; // 8GB default assumption
    }

    private void LogThrottleEvent(string operationType, MemoryPressureLevel pressureLevel)
    {
        lock (_lockObject)
        {
            var now = DateTime.UtcNow;
            var logKey = $"{operationType}:{pressureLevel}";

            // Only log throttle events once per minute to avoid spam
            if (!_lastThrottleLog.TryGetValue(logKey, out var lastLog) || 
                now - lastLog > TimeSpan.FromMinutes(1))
            {
                _logger.LogWarning("Throttling {OperationType} due to {PressureLevel} memory pressure", 
                    operationType, pressureLevel);
                _lastThrottleLog[logKey] = now;
            }
        }
    }

    public void Dispose()
    {
        _memoryMonitorTimer?.Dispose();
    }
}