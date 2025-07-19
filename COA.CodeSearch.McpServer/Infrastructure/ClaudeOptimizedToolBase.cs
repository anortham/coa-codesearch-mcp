using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Base class for tools optimized for Claude AI consumption
/// Features automatic mode switching, smart summaries, and progressive disclosure
/// </summary>
public abstract class ClaudeOptimizedToolBase : McpToolBase
{
    private const int AutoSummaryThreshold = 5000; // Auto-switch to summary if response > 5k tokens
    protected readonly IDetailRequestCache? DetailCache;
    
    protected ClaudeOptimizedToolBase(
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        ILogger logger,
        IDetailRequestCache? detailCache = null)
        : base(sizeEstimator, truncator, options, logger)
    {
        DetailCache = detailCache;
    }
    
    /// <summary>
    /// Creates a Claude-optimized response with automatic mode selection
    /// </summary>
    protected async Task<object> CreateClaudeResponseAsync<T>(
        T data,
        ResponseMode requestedMode,
        Func<T, ClaudeSummaryData>? summaryGenerator = null,
        CancellationToken cancellationToken = default)
    {
        // Estimate the full response size
        var fullResponseTokens = SizeEstimator.EstimateTokens(data);
        var autoSwitched = false;
        var actualMode = requestedMode;
        
        // Auto-switch to summary mode if response is large
        if (fullResponseTokens > AutoSummaryThreshold && requestedMode == ResponseMode.Full)
        {
            Logger.LogInformation(
                "Auto-switching to summary mode: {FullTokens} tokens exceeds threshold of {Threshold}",
                fullResponseTokens,
                AutoSummaryThreshold);
            actualMode = ResponseMode.Summary;
            autoSwitched = true;
        }
        
        // Create the response based on mode
        object responseData;
        ResponseMetadata metadata;
        
        if (actualMode == ResponseMode.Summary && summaryGenerator != null)
        {
            var summary = summaryGenerator(data);
            responseData = summary;
            
            // Store full data for detail requests if we have a cache
            string? detailToken = null;
            if (DetailCache != null)
            {
                detailToken = DetailCache.StoreDetailData(data);
            }
            
            metadata = new ResponseMetadata
            {
                TotalResults = GetTotalResults(data),
                ReturnedResults = 0, // Summary doesn't return raw results
                EstimatedTokens = SizeEstimator.EstimateTokens(summary),
                DetailRequestToken = detailToken,
                AvailableDetailLevels = GetAvailableDetailLevels(data)
            };
        }
        else
        {
            // Full or compact mode - may need truncation
            var truncated = TruncateIfNeeded(data);
            responseData = truncated.Data ?? data;
            metadata = truncated.Metadata;
        }
        
        // Create the Claude-optimized response
        var response = new ClaudeOptimizedResponse<object>
        {
            Success = true,
            Mode = actualMode.ToString().ToLower(),
            AutoModeSwitch = autoSwitched,
            Data = responseData,
            Metadata = metadata
        };
        
        // Add next actions and context
        response.NextActions = GenerateNextActions(data, actualMode, metadata);
        response.Context = AnalyzeResultContext(data);
        
        // Return object response directly - tools should return object, not typed responses
        return response;
    }
    
    /// <summary>
    /// Analyzes results to identify hotspots (files/areas with high concentration of results)
    /// </summary>
    protected List<Hotspot> IdentifyHotspots<T>(
        IEnumerable<T> items,
        Func<T, string> fileSelector,
        Func<IGrouping<string, T>, int> countSelector,
        int maxHotspots = 5)
    {
        return items
            .GroupBy(fileSelector)
            .Select(g => new
            {
                File = g.Key,
                Count = countSelector(g)
            })
            .OrderByDescending(x => x.Count)
            .Take(maxHotspots)
            .Select(x => new Hotspot
            {
                File = x.File,
                Occurrences = x.Count,
                Complexity = x.Count > 10 ? "high" : x.Count > 5 ? "medium" : "low",
                Reason = x.Count > 10 
                    ? "Very high concentration of occurrences" 
                    : x.Count > 5 
                        ? "Moderate concentration of occurrences"
                        : null
            })
            .ToList();
    }
    
    /// <summary>
    /// Categorizes files into logical groups
    /// </summary>
    protected Dictionary<string, CategorySummary> CategorizeFiles<T>(
        IEnumerable<T> items,
        Func<T, string> fileSelector)
    {
        var categories = new Dictionary<string, List<string>>
        {
            ["controllers"] = new(),
            ["services"] = new(),
            ["pages"] = new(),
            ["components"] = new(),
            ["tests"] = new(),
            ["models"] = new(),
            ["other"] = new()
        };
        
        var fileGroups = items.GroupBy(fileSelector).ToList();
        
        foreach (var group in fileGroups)
        {
            var file = group.Key.ToLowerInvariant();
            var categorized = false;
            
            if (file.Contains("controller"))
            {
                categories["controllers"].Add(group.Key);
                categorized = true;
            }
            else if (file.Contains("service"))
            {
                categories["services"].Add(group.Key);
                categorized = true;
            }
            else if (file.EndsWith(".razor") || file.Contains("/pages/"))
            {
                categories["pages"].Add(group.Key);
                categorized = true;
            }
            else if (file.Contains("/components/"))
            {
                categories["components"].Add(group.Key);
                categorized = true;
            }
            else if (file.Contains("test") || file.Contains("spec"))
            {
                categories["tests"].Add(group.Key);
                categorized = true;
            }
            else if (file.Contains("model") || file.Contains("dto"))
            {
                categories["models"].Add(group.Key);
                categorized = true;
            }
            
            if (!categorized)
            {
                categories["other"].Add(group.Key);
            }
        }
        
        return categories
            .Where(kvp => kvp.Value.Any())
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new CategorySummary
                {
                    Files = kvp.Value.Count,
                    Occurrences = fileGroups
                        .Where(g => kvp.Value.Contains(g.Key))
                        .Sum(g => g.Count())
                });
    }
    
    /// <summary>
    /// Generates key insights from the results
    /// </summary>
    protected virtual List<string> GenerateKeyInsights<T>(T data)
    {
        var insights = new List<string>();
        
        // Override in derived classes to provide tool-specific insights
        var totalItems = GetTotalResults(data);
        if (totalItems > 100)
        {
            insights.Add($"Large result set with {totalItems} items - consider using filters");
        }
        
        return insights;
    }
    
    /// <summary>
    /// Override to provide tool-specific next actions
    /// </summary>
    protected virtual NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = new NextActions();
        
        if (currentMode == ResponseMode.Summary && metadata.DetailRequestToken != null)
        {
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "get_hotspot_details",
                Description = "Get detailed information for the most affected files",
                EstimatedTokens = 3500,
                Priority = "high",
                Command = new { detailLevel = "hotspots", detailRequestToken = metadata.DetailRequestToken }
            });
            
            actions.Available.Add(new AvailableAction
            {
                Action = "get_all_details",
                Description = "Get complete details (may be truncated)",
                EstimatedTokens = metadata.EstimatedTokens ?? 20000,
                Warning = "Response may be truncated due to size"
            });
        }
        
        return actions;
    }
    
    /// <summary>
    /// Override to provide tool-specific context analysis
    /// </summary>
    protected virtual ResultContext AnalyzeResultContext<T>(T data)
    {
        var context = new ResultContext();
        var totalResults = GetTotalResults(data);
        
        // Determine impact level
        context.Impact = totalResults switch
        {
            > 100 => "high",
            > 20 => "medium",
            _ => "low"
        };
        
        // Add generic suggestions
        if (totalResults > 50)
        {
            context.Suggestions.Add("Consider reviewing results in smaller batches");
        }
        
        return context;
    }
    
    /// <summary>
    /// Override to specify available detail levels for the tool
    /// </summary>
    protected virtual List<DetailLevel> GetAvailableDetailLevels<T>(T data)
    {
        return new List<DetailLevel>
        {
            new DetailLevel
            {
                Id = "full",
                Name = "Full Details",
                Description = "Complete information for all results",
                EstimatedTokens = SizeEstimator.EstimateTokens(data)
            }
        };
    }
    
    /// <summary>
    /// Override to extract total result count from data
    /// </summary>
    protected abstract int GetTotalResults<T>(T data);
    
    /// <summary>
    /// Truncates data if needed based on token limits
    /// </summary>
    private (object? Data, ResponseMetadata Metadata) TruncateIfNeeded<T>(T data)
    {
        if (data is IEnumerable<object> enumerable)
        {
            var truncated = Truncator.TruncateResults(enumerable, GetMaxTokens());
            return (truncated.Results, new ResponseMetadata
            {
                TotalResults = truncated.TotalCount,
                ReturnedResults = truncated.ReturnedCount,
                IsTruncated = truncated.IsTruncated,
                TruncationReason = truncated.TruncationReason,
                EstimatedTokens = truncated.EstimatedReturnedTokens
            });
        }
        
        // For non-enumerable data, just return as-is
        return (data, new ResponseMetadata
        {
            TotalResults = 1,
            ReturnedResults = 1,
            EstimatedTokens = SizeEstimator.EstimateTokens(data)
        });
    }
}