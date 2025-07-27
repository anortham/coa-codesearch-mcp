using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for accessing and managing tool usage analytics.
/// Provides insights into tool usage patterns, performance metrics, and workflow optimization.
/// </summary>
public class ToolUsageAnalyticsTool : ClaudeOptimizedToolBase
{
    public override string ToolName => "tool_usage_analytics";
    public override string Description => "View tool usage analytics, performance metrics, and workflow patterns";
    public override ToolCategory Category => ToolCategory.Infrastructure;

    private readonly ToolUsageAnalyticsService _analyticsService;
    private readonly IErrorRecoveryService _errorRecoveryService;

    public ToolUsageAnalyticsTool(
        ILogger<ToolUsageAnalyticsTool> logger,
        ToolUsageAnalyticsService analyticsService,
        IErrorRecoveryService errorRecoveryService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _analyticsService = analyticsService;
        _errorRecoveryService = errorRecoveryService;
    }

    public async Task<object> ExecuteAsync(
        AnalyticsAction action = AnalyticsAction.Summary,
        string? toolName = null,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                var cachedData = DetailCache.GetDetailData<object>(detailRequest.DetailRequestToken!);
                if (cachedData != null)
                {
                    return cachedData;
                }
            }

            Logger.LogInformation("Tool usage analytics requested - Action: {Action}, Tool: {ToolName}", action, toolName);

            var result = action switch
            {
                AnalyticsAction.Summary => await GetAnalyticsSummaryAsync(),
                AnalyticsAction.Detailed => await GetDetailedAnalyticsAsync(),
                AnalyticsAction.ToolSpecific => await GetToolSpecificAnalyticsAsync(toolName),
                AnalyticsAction.Export => await ExportAnalyticsAsync(),
                AnalyticsAction.Reset => await ResetAnalyticsAsync(),
                _ => throw new ArgumentException($"Unknown analytics action: {action}")
            };

            Logger.LogInformation("Tool usage analytics completed for action: {Action}", action);

            // Return Claude-optimized response
            return await CreateClaudeResponseAsync(
                result,
                mode,
                GenerateAnalyticsSummary,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Tool usage analytics failed for action: {Action}", action);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                ex.Message,
                _errorRecoveryService.GetValidationErrorRecovery("tool_usage_analytics", "Check parameters and try again"));
        }
    }

    private async Task<ToolUsageAnalyticsResult> GetAnalyticsSummaryAsync()
    {
        await Task.CompletedTask;
        
        var analytics = _analyticsService.GetAnalytics();
        
        return new ToolUsageAnalyticsResult
        {
            Action = "summary",
            Success = true,
            Summary = new AnalyticsSummary
            {
                TotalInvocations = analytics.TotalToolInvocations,
                UniqueTools = analytics.UniqueToolsUsed,
                OverallSuccessRate = analytics.OverallSuccessRate,
                TopTools = analytics.MostUsedTools.Take(5).Select(t => $"{t.ToolName} ({t.TotalUses} uses, {t.SuccessRate:F1}% success)").ToList(),
                KeyInsights = analytics.PerformanceInsights.Take(3).ToList(),
                RecentActivity = analytics.RecentActivity.Take(10).Select(e => $"{e.ToolName} at {e.Timestamp:HH:mm:ss} ({(e.WasSuccessful ? "✓" : "✗")})").ToList()
            },
            GeneratedAt = analytics.GeneratedAt
        };
    }

    private async Task<ToolUsageAnalyticsResult> GetDetailedAnalyticsAsync()
    {
        await Task.CompletedTask;
        
        var analytics = _analyticsService.GetAnalytics();
        
        return new ToolUsageAnalyticsResult
        {
            Action = "detailed",
            Success = true,
            DetailedAnalytics = analytics,
            GeneratedAt = analytics.GeneratedAt
        };
    }

    private async Task<ToolUsageAnalyticsResult> GetToolSpecificAnalyticsAsync(string? toolName)
    {
        await Task.CompletedTask;
        
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required for tool-specific analytics");
        }

        var toolStats = _analyticsService.GetToolStats(toolName);
        if (toolStats == null)
        {
            return new ToolUsageAnalyticsResult
            {
                Action = "tool_specific",
                Success = false,
                Error = $"No analytics data found for tool: {toolName}"
            };
        }

        return new ToolUsageAnalyticsResult
        {
            Action = "tool_specific",
            Success = true,
            ToolSpecific = new ToolSpecificAnalytics
            {
                ToolName = toolStats.ToolName,
                Stats = toolStats,
                Recommendations = GenerateToolRecommendations(toolStats)
            },
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<ToolUsageAnalyticsResult> ExportAnalyticsAsync()
    {
        await Task.CompletedTask;
        
        var exportData = _analyticsService.ExportAnalytics();
        
        return new ToolUsageAnalyticsResult
        {
            Action = "export",
            Success = true,
            ExportData = exportData,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<ToolUsageAnalyticsResult> ResetAnalyticsAsync()
    {
        await Task.CompletedTask;
        
        _analyticsService.Reset();
        
        return new ToolUsageAnalyticsResult
        {
            Action = "reset",
            Success = true,
            Message = "Analytics data has been reset",
            GeneratedAt = DateTime.UtcNow
        };
    }

    private List<string> GenerateToolRecommendations(ToolUsageStats stats)
    {
        var recommendations = new List<string>();

        if (stats.SuccessRate < 80)
        {
            recommendations.Add($"Low success rate ({stats.SuccessRate:F1}%) - review parameters and error patterns");
        }

        if (stats.AverageExecutionTime.TotalSeconds > 10)
        {
            recommendations.Add($"Slow execution (avg {stats.AverageExecutionTime.TotalSeconds:F1}s) - consider optimizing or using batch operations");
        }

        if (stats.TotalUses > 50 && stats.SuccessRate > 95)
        {
            recommendations.Add("High-performing tool - consider using as a workflow building block");
        }

        if (stats.ErrorTypes.Any())
        {
            var mostCommonError = stats.ErrorTypes.OrderByDescending(kvp => kvp.Value).First();
            recommendations.Add($"Most common error: {mostCommonError.Key} ({mostCommonError.Value} occurrences)");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Tool is performing well with no specific recommendations");
        }

        return recommendations;
    }

    // Override required base class methods
    protected override int GetTotalResults<T>(T data)
    {
        if (data is ToolUsageAnalyticsResult result)
        {
            return result.DetailedAnalytics?.TotalToolInvocations ?? 1;
        }
        return 1;
    }

    protected override List<string> GenerateKeyInsights<T>(T data)
    {
        var insights = base.GenerateKeyInsights(data);

        if (data is ToolUsageAnalyticsResult result)
        {
            if (result.Summary != null)
            {
                insights.AddRange(result.Summary.KeyInsights.Take(3));
            }
            else if (result.DetailedAnalytics != null)
            {
                insights.AddRange(result.DetailedAnalytics.PerformanceInsights.Take(3));
            }
        }

        return insights;
    }

    private ClaudeSummaryData GenerateAnalyticsSummary(ToolUsageAnalyticsResult result)
    {
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = result.DetailedAnalytics?.TotalToolInvocations ?? result.Summary?.TotalInvocations ?? 0,
                AffectedFiles = 0, // Not applicable for analytics
                EstimatedFullResponseTokens = 2000, // Rough estimate
                KeyInsights = result.Summary?.KeyInsights ?? result.DetailedAnalytics?.PerformanceInsights.Take(3).ToList() ?? new List<string>()
            },
            ByCategory = new Dictionary<string, CategorySummary>
            {
                ["analytics"] = new CategorySummary
                {
                    Files = result.Summary?.UniqueTools ?? result.DetailedAnalytics?.UniqueToolsUsed ?? 0,
                    Occurrences = result.Summary?.TotalInvocations ?? result.DetailedAnalytics?.TotalToolInvocations ?? 0,
                    PrimaryPattern = $"Success Rate: {result.Summary?.OverallSuccessRate ?? result.DetailedAnalytics?.OverallSuccessRate ?? 0:F1}%"
                }
            },
            Hotspots = result.Summary?.TopTools?.Take(3).Select((tool, index) => new Hotspot
            {
                File = tool,
                Occurrences = index + 1, // Position in ranking
                Complexity = "medium",
                Reason = "Frequently used tool"
            }).ToList() ?? new List<Hotspot>()
        };
    }
}

/// <summary>
/// Analytics action types
/// </summary>
public enum AnalyticsAction
{
    Summary,
    Detailed,
    ToolSpecific,
    Export,
    Reset
}

/// <summary>
/// Result structure for tool usage analytics
/// </summary>
public class ToolUsageAnalyticsResult
{
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public AnalyticsSummary? Summary { get; set; }
    public ToolUsageAnalytics? DetailedAnalytics { get; set; }
    public ToolSpecificAnalytics? ToolSpecific { get; set; }
    public string? ExportData { get; set; }
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Summary analytics data
/// </summary>
public class AnalyticsSummary
{
    public int TotalInvocations { get; set; }
    public int UniqueTools { get; set; }
    public double OverallSuccessRate { get; set; }
    public List<string> TopTools { get; set; } = new();
    public List<string> KeyInsights { get; set; } = new();
    public List<string> RecentActivity { get; set; } = new();
}

/// <summary>
/// Tool-specific analytics data
/// </summary>
public class ToolSpecificAnalytics
{
    public string ToolName { get; set; } = string.Empty;
    public ToolUsageStats Stats { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}