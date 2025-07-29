using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace COA.CodeSearch.McpServer.Services.ResponseBuilders;

/// <summary>
/// Base class for AI-optimized response builders with common functionality
/// </summary>
public abstract class BaseResponseBuilder : IResponseBuilder
{
    protected readonly ILogger Logger;
    
    // Token budgets for different response modes
    protected const int SummaryTokenBudget = 5000;
    protected const int FullTokenBudget = 50000;
    
    protected BaseResponseBuilder(ILogger logger)
    {
        Logger = logger;
    }

    public abstract string ResponseType { get; }
    
    /// <summary>
    /// Generate insights based on the specific operation type
    /// </summary>
    protected abstract List<string> GenerateInsights(dynamic data, ResponseMode mode);
    
    /// <summary>
    /// Generate contextual actions for the response
    /// </summary>
    protected abstract List<dynamic> GenerateActions(dynamic data, int tokenBudget);
    
    /// <summary>
    /// Common method to format file sizes
    /// </summary>
    protected string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int index = 0;
        
        while (size >= 1024 && index < sizes.Length - 1)
        {
            size /= 1024;
            index++;
        }
        
        return $"{size:0.##} {sizes[index]}";
    }
    
    /// <summary>
    /// Common method to format time ago
    /// </summary>
    protected string FormatTimeAgo(DateTime dateTime)
    {
        var timeSpan = DateTime.UtcNow - dateTime;
        
        if (timeSpan.TotalMinutes < 1) return "just now";
        if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes != 1 ? "s" : "")} ago";
        if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago";
        if (timeSpan.TotalDays < 30) return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago";
        if (timeSpan.TotalDays < 365) return $"{(int)(timeSpan.TotalDays / 30)} month{((int)(timeSpan.TotalDays / 30) != 1 ? "s" : "")} ago";
        
        return $"{(int)(timeSpan.TotalDays / 365)} year{((int)(timeSpan.TotalDays / 365) != 1 ? "s" : "")} ago";
    }
    
    /// <summary>
    /// Check if response should switch to summary mode based on token estimate
    /// </summary>
    protected bool ShouldUseSummaryMode(int resultCount, int estimatedTokensPerResult, ResponseMode requestedMode)
    {
        if (requestedMode == ResponseMode.Summary) return true;
        
        var estimatedTokens = resultCount * estimatedTokensPerResult;
        return estimatedTokens > SummaryTokenBudget;
    }
}