using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Service for monitoring and managing memory pressure
/// </summary>
public class MemoryPressureService : IMemoryPressureService
{
    private readonly ILogger<MemoryPressureService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, long> _componentMemoryUsage = new();
    
    private readonly long _maxMemoryMB;
    private readonly double _throttleThresholdPercent;
    private readonly double _gcThresholdPercent;
    private DateTime _lastGcTime = DateTime.MinValue;
    private readonly TimeSpan _minGcInterval = TimeSpan.FromSeconds(10);
    
    public MemoryPressureService(ILogger<MemoryPressureService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _maxMemoryMB = configuration.GetValue("CodeSearch:MemoryPressure:MaxMemoryMB", 500L);
        _throttleThresholdPercent = configuration.GetValue("CodeSearch:MemoryPressure:ThrottleThresholdPercent", 80.0) / 100.0;
        _gcThresholdPercent = configuration.GetValue("CodeSearch:MemoryPressure:GCThresholdPercent", 90.0) / 100.0;
    }
    
    public bool ShouldThrottleOperation(string operationName)
    {
        var level = GetCurrentPressureLevel();
        
        if (level >= MemoryPressureLevel.High)
        {
            _logger.LogWarning("Throttling operation {Operation} due to {Level} memory pressure", operationName, level);
            return true;
        }
        
        return false;
    }
    
    public int GetRecommendedConcurrency(int baseConcurrency)
    {
        var level = GetCurrentPressureLevel();
        
        return level switch
        {
            MemoryPressureLevel.Critical => Math.Max(1, baseConcurrency / 4),
            MemoryPressureLevel.High => Math.Max(1, baseConcurrency / 2),
            MemoryPressureLevel.Moderate => Math.Max(1, (int)(baseConcurrency * 0.75)),
            _ => baseConcurrency
        };
    }
    
    public MemoryPressureLevel GetCurrentPressureLevel()
    {
        var memoryUsedMB = GC.GetTotalMemory(false) / (1024 * 1024);
        var percentUsed = (double)memoryUsedMB / _maxMemoryMB;
        
        if (percentUsed >= _gcThresholdPercent)
        {
            return MemoryPressureLevel.Critical;
        }
        else if (percentUsed >= _throttleThresholdPercent)
        {
            return MemoryPressureLevel.High;
        }
        else if (percentUsed >= 0.6)
        {
            return MemoryPressureLevel.Moderate;
        }
        else
        {
            return MemoryPressureLevel.Normal;
        }
    }
    
    public void ReportMemoryUsage(string component, long bytesUsed)
    {
        _componentMemoryUsage.AddOrUpdate(component, bytesUsed, (_, _) => bytesUsed);
        
        // Log if this component is using significant memory
        var mbUsed = bytesUsed / (1024 * 1024);
        if (mbUsed > 50)
        {
            _logger.LogInformation("Component {Component} using {MB}MB of memory", component, mbUsed);
        }
    }
    
    public void ForceGarbageCollectionIfNeeded()
    {
        var level = GetCurrentPressureLevel();
        
        if (level >= MemoryPressureLevel.High && DateTime.UtcNow - _lastGcTime > _minGcInterval)
        {
            _logger.LogInformation("Forcing garbage collection due to {Level} memory pressure", level);
            
            var beforeMB = GC.GetTotalMemory(false) / (1024 * 1024);
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var afterMB = GC.GetTotalMemory(false) / (1024 * 1024);
            
            _logger.LogInformation("Garbage collection freed {MB}MB (from {Before}MB to {After}MB)", 
                beforeMB - afterMB, beforeMB, afterMB);
            
            _lastGcTime = DateTime.UtcNow;
        }
    }
}