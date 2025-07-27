using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for tracking tool usage analytics to help understand patterns and optimize workflows.
/// Designed to support AI agents in understanding tool effectiveness and usage patterns.
/// </summary>
public class ToolUsageAnalyticsService
{
    private readonly ILogger<ToolUsageAnalyticsService> _logger;
    private readonly ConcurrentDictionary<string, ToolUsageStats> _toolStats = new();
    private readonly ConcurrentQueue<ToolUsageEvent> _recentEvents = new();
    private readonly object _lockObject = new();
    private const int MaxRecentEvents = 1000;

    public ToolUsageAnalyticsService(ILogger<ToolUsageAnalyticsService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records that a tool was used
    /// </summary>
    public void RecordToolUsage(string toolName, TimeSpan executionTime, bool wasSuccessful, string? errorType = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return;

        var now = DateTime.UtcNow;
        
        // Update tool statistics
        _toolStats.AddOrUpdate(toolName, 
            new ToolUsageStats
            {
                ToolName = toolName,
                TotalUses = 1,
                SuccessfulUses = wasSuccessful ? 1 : 0,
                FailedUses = wasSuccessful ? 0 : 1,
                TotalExecutionTime = executionTime,
                AverageExecutionTime = executionTime,
                LastUsed = now,
                FirstUsed = now
            },
            (key, existing) => 
            {
                existing.TotalUses++;
                if (wasSuccessful)
                    existing.SuccessfulUses++;
                else
                    existing.FailedUses++;
                
                existing.TotalExecutionTime = existing.TotalExecutionTime.Add(executionTime);
                existing.AverageExecutionTime = TimeSpan.FromTicks(existing.TotalExecutionTime.Ticks / existing.TotalUses);
                existing.LastUsed = now;
                
                if (!string.IsNullOrEmpty(errorType))
                {
                    existing.ErrorTypes.TryGetValue(errorType, out var count);
                    existing.ErrorTypes[errorType] = count + 1;
                }
                
                return existing;
            });

        // Add to recent events
        var usageEvent = new ToolUsageEvent
        {
            ToolName = toolName,
            Timestamp = now,
            ExecutionTime = executionTime,
            WasSuccessful = wasSuccessful,
            ErrorType = errorType
        };

        _recentEvents.Enqueue(usageEvent);

        // Maintain queue size
        lock (_lockObject)
        {
            while (_recentEvents.Count > MaxRecentEvents)
            {
                _recentEvents.TryDequeue(out _);
            }
        }

        _logger.LogDebug("Recorded tool usage: {ToolName} ({ExecutionTime}ms, Success: {Success})", 
            toolName, executionTime.TotalMilliseconds, wasSuccessful);
    }

    /// <summary>
    /// Gets comprehensive analytics data
    /// </summary>
    public ToolUsageAnalytics GetAnalytics()
    {
        var stats = _toolStats.Values.ToList();
        var events = _recentEvents.ToList();

        return new ToolUsageAnalytics
        {
            TotalToolInvocations = stats.Sum(s => s.TotalUses),
            UniqueToolsUsed = stats.Count,
            OverallSuccessRate = CalculateOverallSuccessRate(stats),
            MostUsedTools = stats.OrderByDescending(s => s.TotalUses).Take(10).ToList(),
            FastestTools = stats.Where(s => s.TotalUses > 0).OrderBy(s => s.AverageExecutionTime).Take(10).ToList(),
            SlowestTools = stats.Where(s => s.TotalUses > 0).OrderByDescending(s => s.AverageExecutionTime).Take(10).ToList(),
            RecentActivity = events.OrderByDescending(e => e.Timestamp).Take(50).ToList(),
            ToolStats = stats.OrderByDescending(s => s.TotalUses).ToList(),
            UsagePatterns = AnalyzeUsagePatterns(events),
            PerformanceInsights = GeneratePerformanceInsights(stats),
            ErrorAnalysis = AnalyzeErrors(stats),
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets analytics for a specific tool
    /// </summary>
    public ToolUsageStats? GetToolStats(string toolName)
    {
        return _toolStats.TryGetValue(toolName, out var stats) ? stats : null;
    }

    /// <summary>
    /// Exports analytics data as JSON for storage or sharing
    /// </summary>
    public string ExportAnalytics()
    {
        var analytics = GetAnalytics();
        return JsonSerializer.Serialize(analytics, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Clears all analytics data
    /// </summary>
    public void Reset()
    {
        _toolStats.Clear();
        _recentEvents.Clear();
        _logger.LogInformation("Tool usage analytics reset");
    }

    private double CalculateOverallSuccessRate(List<ToolUsageStats> stats)
    {
        var totalUses = stats.Sum(s => s.TotalUses);
        var totalSuccesses = stats.Sum(s => s.SuccessfulUses);
        return totalUses > 0 ? (double)totalSuccesses / totalUses * 100 : 0;
    }

    private List<UsagePattern> AnalyzeUsagePatterns(List<ToolUsageEvent> events)
    {
        var patterns = new List<UsagePattern>();

        // Analyze hourly patterns
        var hourlyUsage = events
            .GroupBy(e => e.Timestamp.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3);

        foreach (var hour in hourlyUsage)
        {
            patterns.Add(new UsagePattern
            {
                Type = "Peak Hour",
                Pattern = $"Hour {hour.Hour}:00",
                Frequency = hour.Count,
                Description = $"High activity at {hour.Hour}:00 with {hour.Count} tool uses"
            });
        }

        // Analyze tool sequences (tools often used together)
        var sequences = events
            .OrderBy(e => e.Timestamp)
            .Select(e => e.ToolName)
            .ToList();

        var sequencePairs = new Dictionary<string, int>();
        for (int i = 0; i < sequences.Count - 1; i++)
        {
            var pair = $"{sequences[i]} → {sequences[i + 1]}";
            sequencePairs.TryGetValue(pair, out var count);
            sequencePairs[pair] = count + 1;
        }

        foreach (var commonSequence in sequencePairs.OrderByDescending(kvp => kvp.Value).Take(5))
        {
            patterns.Add(new UsagePattern
            {
                Type = "Tool Sequence",
                Pattern = commonSequence.Key,
                Frequency = commonSequence.Value,
                Description = $"Common workflow: {commonSequence.Key}"
            });
        }

        return patterns;
    }

    private List<string> GeneratePerformanceInsights(List<ToolUsageStats> stats)
    {
        var insights = new List<string>();

        if (stats.Any())
        {
            var fastestTool = stats.Where(s => s.TotalUses > 0).OrderBy(s => s.AverageExecutionTime).FirstOrDefault();
            var slowestTool = stats.Where(s => s.TotalUses > 0).OrderByDescending(s => s.AverageExecutionTime).FirstOrDefault();

            if (fastestTool != null)
                insights.Add($"Fastest tool: {fastestTool.ToolName} (avg {fastestTool.AverageExecutionTime.TotalMilliseconds:F1}ms)");

            if (slowestTool != null)
                insights.Add($"Slowest tool: {slowestTool.ToolName} (avg {slowestTool.AverageExecutionTime.TotalMilliseconds:F1}ms)");

            var lowSuccessRateTools = stats.Where(s => s.TotalUses > 5 && s.SuccessRate < 80).ToList();
            if (lowSuccessRateTools.Any())
            {
                insights.Add($"Tools with low success rates: {string.Join(", ", lowSuccessRateTools.Select(t => $"{t.ToolName} ({t.SuccessRate:F1}%)"))}");
            }

            var heavilyUsedTools = stats.Where(s => s.TotalUses > 10).OrderByDescending(s => s.TotalUses).Take(3).ToList();
            if (heavilyUsedTools.Any())
            {
                insights.Add($"Most used tools: {string.Join(", ", heavilyUsedTools.Select(t => $"{t.ToolName} ({t.TotalUses} uses)"))}");
            }
        }

        return insights;
    }

    private ErrorAnalysis AnalyzeErrors(List<ToolUsageStats> stats)
    {
        var allErrors = stats.SelectMany(s => s.ErrorTypes).ToList();
        var totalErrors = allErrors.Sum(kvp => kvp.Value);
        
        return new ErrorAnalysis
        {
            TotalErrors = totalErrors,
            MostCommonErrors = allErrors
                .GroupBy(kvp => kvp.Key)
                .Select(g => new { ErrorType = g.Key, Count = g.Sum(x => x.Value) })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToDictionary(x => x.ErrorType, x => x.Count),
            ToolsWithMostErrors = stats
                .Where(s => s.FailedUses > 0)
                .OrderByDescending(s => s.FailedUses)
                .Take(5)
                .Select(s => (object)new { s.ToolName, s.FailedUses, s.SuccessRate })
                .ToList()
        };
    }
}

/// <summary>
/// Complete analytics data structure
/// </summary>
public class ToolUsageAnalytics
{
    public int TotalToolInvocations { get; set; }
    public int UniqueToolsUsed { get; set; }
    public double OverallSuccessRate { get; set; }
    public List<ToolUsageStats> MostUsedTools { get; set; } = new();
    public List<ToolUsageStats> FastestTools { get; set; } = new();
    public List<ToolUsageStats> SlowestTools { get; set; } = new();
    public List<ToolUsageEvent> RecentActivity { get; set; } = new();
    public List<ToolUsageStats> ToolStats { get; set; } = new();
    public List<UsagePattern> UsagePatterns { get; set; } = new();
    public List<string> PerformanceInsights { get; set; } = new();
    public ErrorAnalysis ErrorAnalysis { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Statistics for a specific tool
/// </summary>
public class ToolUsageStats
{
    public string ToolName { get; set; } = string.Empty;
    public int TotalUses { get; set; }
    public int SuccessfulUses { get; set; }
    public int FailedUses { get; set; }
    public double SuccessRate => TotalUses > 0 ? (double)SuccessfulUses / TotalUses * 100 : 0;
    public TimeSpan TotalExecutionTime { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public DateTime FirstUsed { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> ErrorTypes { get; set; } = new();
}

/// <summary>
/// Individual tool usage event
/// </summary>
public class ToolUsageEvent
{
    public string ToolName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool WasSuccessful { get; set; }
    public string? ErrorType { get; set; }
}

/// <summary>
/// Usage pattern analysis
/// </summary>
public class UsagePattern
{
    public string Type { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Error analysis data
/// </summary>
public class ErrorAnalysis
{
    public int TotalErrors { get; set; }
    public Dictionary<string, int> MostCommonErrors { get; set; } = new();
    public List<object> ToolsWithMostErrors { get; set; } = new();
}