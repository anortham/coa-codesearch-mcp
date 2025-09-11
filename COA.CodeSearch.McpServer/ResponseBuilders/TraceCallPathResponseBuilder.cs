using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for trace call path operations with hierarchical formatting.
/// Optimizes token usage while preserving call hierarchy structure.
/// </summary>
public class TraceCallPathResponseBuilder : BaseResponseBuilder<SearchResult, AIOptimizedResponse<SearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public TraceCallPathResponseBuilder(
        ILogger<TraceCallPathResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SearchResult>> BuildResponseAsync(SearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building trace call path response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
            
        // Adaptive token budget allocation based on result count and mode
        var (dataBudget, insightsBudget, actionsBudget) = CalculateAdaptiveBudget(tokenBudget, data.TotalHits, context.ResponseMode);
        
        _logger?.LogDebug("Adaptive token optimization: Data={DataBudget}, Insights={InsightsBudget}, Actions={ActionsBudget}, Total={TotalBudget}, ResultCount={ResultCount}", 
            dataBudget, insightsBudget, actionsBudget, tokenBudget, data.TotalHits);
        
        // Reduce search hits to fit budget - preserve call hierarchy information
        var reducedHits = ReduceSearchHitsPreservingCallHierarchy(data.Hits, dataBudget, context.ResponseMode);
        var wasTruncated = reducedHits.Count < data.Hits.Count;
        
        // Store full results if truncated and storage is available
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Hits,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(24),
                        Compress = true,
                        Category = "trace-call-path-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["query"] = data.Query ?? "",
                            ["totalHits"] = data.TotalHits.ToString(),
                            ["tool"] = context.ToolName ?? "trace_call_path"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full call path results");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        // Create a copy of the data for AI response with reduced hits
        var aiData = new SearchResult
        {
            TotalHits = data.TotalHits,
            Hits = reducedHits,
            SearchTime = data.SearchTime,
            Query = data.Query
        };
        
        var response = new AIOptimizedResponse<SearchResult>
        {
            Success = true,
            Data = new AIResponseData<SearchResult>
            {
                Summary = BuildCallPathSummary(data, reducedHits.Count, context.ResponseMode),
                Results = aiData, // Pass the copy with reduced hits for AI
                Count = data.TotalHits,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = data.TotalHits,
                    ["query"] = data.Query ?? "",
                    ["processingTime"] = data.ProcessingTimeMs,
                    ["callHierarchy"] = "hierarchical_tracing"
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built trace call path response: {Hits} of {Total} hits, {Insights} insights, {Actions} actions, {Tokens} tokens (Budget: {Budget}, Truncated: {Truncated}, Stored: {HasResource})",
            reducedHits.Count, data.TotalHits, response.Insights.Count, response.Actions.Count, 
            response.Meta.TokenInfo.Estimated, tokenBudget, wasTruncated, resourceUri != null);
        
        return response;
    }
    
    /// <summary>
    /// Override to use TokenLimit from context instead of default budget
    /// </summary>
    protected override int CalculateTokenBudget(ResponseContext context)
    {
        return context.TokenLimit ?? 8000; // Use TokenLimit from context or default to 8000
    }
    
    /// <summary>
    /// Calculates adaptive token budget allocation optimized for call path scenarios.
    /// </summary>
    private (int dataBudget, int insightsBudget, int actionsBudget) CalculateAdaptiveBudget(int totalBudget, int resultCount, string responseMode)
    {
        // Base allocation percentages for call path tracing
        double dataPercent = 0.75; // More data for hierarchical structure
        double insightsPercent = 0.15;
        double actionsPercent = 0.10;
        
        // Adjust based on result count
        if (resultCount == 0)
        {
            // No results: more budget for insights and actions to guide user
            dataPercent = 0.30;
            insightsPercent = 0.40;
            actionsPercent = 0.30;
        }
        else if (resultCount == 1)
        {
            // Single result: less need for insights, more for data
            dataPercent = 0.80;
            insightsPercent = 0.10;
            actionsPercent = 0.10;
        }
        else if (resultCount > 20)
        {
            // Many results: prioritize insights over raw data
            dataPercent = 0.70;
            insightsPercent = 0.20;
            actionsPercent = 0.10;
        }
        
        // Adjust based on response mode
        switch (responseMode?.ToLowerInvariant())
        {
            case "summary":
                dataPercent = 0.60;
                insightsPercent = 0.25;
                actionsPercent = 0.15;
                break;
            case "full":
                dataPercent = 0.80;
                insightsPercent = 0.12;
                actionsPercent = 0.08;
                break;
        }
        
        return (
            (int)(totalBudget * dataPercent),
            (int)(totalBudget * insightsPercent),
            (int)(totalBudget * actionsPercent)
        );
    }
    
    /// <summary>
    /// Reduce search hits while preserving call hierarchy information
    /// </summary>
    private List<SearchHit> ReduceSearchHitsPreservingCallHierarchy(List<SearchHit> hits, int tokenBudget, string responseMode)
    {
        if (hits == null || hits.Count == 0)
            return new List<SearchHit>();
        
        // Estimate tokens per hit (including call hierarchy metadata)
        var tokensPerHit = EstimateTokensPerCallPathHit(hits.FirstOrDefault());
        var maxHits = Math.Max(1, tokenBudget / tokensPerHit);
        
        // Apply mode-specific limits
        var modeLimit = responseMode?.ToLowerInvariant() switch
        {
            "summary" => Math.Min(maxHits, 3),
            "full" => Math.Min(maxHits, 15),
            _ => Math.Min(maxHits, 8) // adaptive default
        };
        
        if (hits.Count <= modeLimit)
            return hits;
        
        // Prioritize hits with call hierarchy information
        var prioritizedHits = hits
            .OrderByDescending(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true" ? 2 : 0) // Entry points first
            .ThenByDescending(h => h.Score) // Then by relevance score
            .ThenBy(h => h.Fields?.GetValueOrDefault("call_depth", "1")) // Then by call depth
            .Take(modeLimit)
            .ToList();
        
        _logger?.LogDebug("Reduced call path hits from {Original} to {Reduced} (budget: {Budget} tokens, mode: {Mode})", 
            hits.Count, prioritizedHits.Count, tokenBudget, responseMode);
        
        return prioritizedHits;
    }
    
    /// <summary>
    /// Estimate tokens for a call path hit including metadata
    /// </summary>
    private int EstimateTokensPerCallPathHit(SearchHit? hit)
    {
        if (hit == null) return 100;
        
        var baseTokens = 80; // Base hit structure
        
        // Add tokens for file path
        baseTokens += (hit.FilePath?.Length ?? 0) / 4;
        
        // Add tokens for context lines
        if (hit.ContextLines != null)
        {
            baseTokens += hit.ContextLines.Sum(line => line.Length / 4);
        }
        
        // Add tokens for call hierarchy metadata
        if (hit.Fields != null)
        {
            baseTokens += hit.Fields.Sum(kvp => (kvp.Key.Length + kvp.Value.Length) / 4);
        }
        
        return Math.Max(baseTokens, 50); // Minimum 50 tokens per hit
    }
    
    /// <summary>
    /// Build summary for call path results
    /// </summary>
    private string BuildCallPathSummary(SearchResult data, int reducedCount, string responseMode)
    {
        if (data.TotalHits == 0)
        {
            return "No call path found for the specified symbol";
        }
        
        var summary = $"Call path trace: {data.TotalHits} references found";
        
        if (reducedCount < data.TotalHits)
        {
            summary += $" (showing top {reducedCount})";
        }
        
        if (data.Hits != null)
        {
            var fileCount = data.Hits.Take(reducedCount).Select(h => h.FilePath).Distinct().Count();
            summary += $" across {fileCount} files";
        }
        
        return summary;
    }
    
    /// <summary>
    /// Generate insights for the base response builder pattern
    /// </summary>
    protected override List<string> GenerateInsights(SearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalHits == 0)
        {
            insights.Add("No call path found - symbol may not exist or have references");
            insights.Add("Try checking spelling or using a different symbol name");
            return insights;
        }
        
        // Analyze call hierarchy patterns
        if (data.Hits != null)
        {
            var entryPoints = data.Hits.Where(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true").ToList();
            if (entryPoints.Any())
            {
                insights.Add($"Found {entryPoints.Count} entry points (controllers, main methods) - these are likely starting points");
            }
            
            var fileCount = data.Hits.Select(h => h.FilePath).Distinct().Count();
            insights.Add($"Call path spans {fileCount} files");
            
            // Analyze call depth distribution
            var depths = data.Hits
                .Select(h => int.TryParse(h.Fields?.GetValueOrDefault("call_depth", "1"), out var d) ? d : 1)
                .GroupBy(d => d)
                .OrderBy(g => g.Key)
                .ToList();
            
            if (depths.Count > 1)
            {
                insights.Add($"Call hierarchy: {string.Join(", ", depths.Select(g => $"depth {g.Key}: {g.Count()} calls"))}");
            }
        }
        
        // Performance insights
        insights.Add($"Search completed in {data.ProcessingTimeMs}ms");
        
        return insights;
    }
    
    /// <summary>
    /// Generate actions for the base response builder pattern
    /// </summary>
    protected override List<COA.Mcp.Framework.Models.AIAction> GenerateActions(SearchResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.TotalHits == 0)
        {
            actions.Add(new AIAction
            {
                Action = "symbol_search",
                Description = "Search for the symbol definition first",
                Priority = 100
            });
            actions.Add(new AIAction
            {
                Action = "text_search",
                Description = "Try broader text search for the symbol",
                Priority = 90
            });
        }
        else
        {
            // Actions for successful call path results
            if (data.Hits?.Any(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true") == true)
            {
                actions.Add(new AIAction
                {
                    Action = "trace_entry_points",
                    Description = "Focus on entry points to understand request flow",
                    Priority = 95
                });
            }
            
            actions.Add(new AIAction
            {
                Action = "find_references",
                Description = "Get complete reference list for refactoring safety",
                Priority = 85
            });
            
            actions.Add(new AIAction
            {
                Action = "goto_definition",
                Description = "Navigate to symbol definition for implementation details",
                Priority = 80
            });
        }
        
        // Apply token budget to actions
        var estimatedTokens = actions.Sum(a => (a.Description?.Length ?? 0) / 4 + 20);
        if (estimatedTokens > tokenBudget && actions.Count > 2)
        {
            actions = actions.Take(2).ToList();
        }
        
        return actions;
    }
}