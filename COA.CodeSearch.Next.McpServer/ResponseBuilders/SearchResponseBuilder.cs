using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for search operations with token-aware optimization.
/// </summary>
public class SearchResponseBuilder : BaseResponseBuilder<SearchResult>
{
    private readonly IResourceStorageService? _storageService;
    
    public SearchResponseBuilder(
        ILogger<SearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<object> BuildResponseAsync(SearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce search hits to fit budget
        var reducedHits = ReduceSearchHits(data.Hits, dataBudget, context.ResponseMode);
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
                        Expiration = TimeSpan.FromHours(1),
                        Compress = true,
                        Category = "search-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["query"] = data.Query ?? "",
                            ["totalHits"] = data.TotalHits.ToString(),
                            ["tool"] = context.ToolName ?? "search"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full search results");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        var response = new TokenOptimizedResult
        {
            Success = true,
            Data = new AIResponseData
            {
                Summary = BuildSummary(data, reducedHits.Count, context.ResponseMode),
                Results = reducedHits,
                Count = reducedHits.Count,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = data.TotalHits,
                    ["query"] = data.Query ?? "",
                    ["processingTime"] = data.ProcessingTimeMs
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Set operation name
        response.SetOperation(context.ToolName ?? "search");
        
        // Update token estimate
        response.Meta.TokenInfo.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built search response: {Hits} of {Total} hits, {Insights} insights, {Actions} actions, {Tokens} tokens",
            reducedHits.Count, data.TotalHits, response.Insights.Count, response.Actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(SearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalHits == 0)
        {
            insights.Add("No results found. Consider broadening your search terms or checking if the workspace is indexed.");
            return insights;
        }
        
        // Distribution insights
        if (data.Hits.Count > 0)
        {
            var fileExtensions = data.Hits
                .Select(h => Path.GetExtension(h.FilePath))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .GroupBy(ext => ext)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();
            
            if (fileExtensions.Any())
            {
                var topTypes = string.Join(", ", fileExtensions.Select(g => $"{g.Key} ({g.Count()})"));
                insights.Add($"Results primarily in: {topTypes}");
            }
            
            // Score distribution
            var maxScore = data.Hits.Max(h => h.Score);
            var minScore = data.Hits.Min(h => h.Score);
            if (maxScore - minScore > 0.5f)
            {
                insights.Add($"Wide relevance range (scores: {minScore:F2} to {maxScore:F2}). Top results are significantly more relevant.");
            }
            else
            {
                insights.Add($"Uniform relevance (scores: {minScore:F2} to {maxScore:F2}). All results are similarly relevant.");
            }
            
            // Pattern insights
            if (responseMode == "full")
            {
                // Directory concentration
                var directories = data.Hits
                    .Select(h => Path.GetDirectoryName(h.FilePath) ?? "")
                    .GroupBy(dir => dir)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                
                if (directories != null && directories.Count() > data.Hits.Count * 0.5)
                {
                    insights.Add($"High concentration in: {Path.GetFileName(directories.Key) ?? "root"} ({directories.Count()} results)");
                }
                
                // Recent activity
                if (data.Hits.Any(h => h.LastModified.HasValue))
                {
                    var recentHits = data.Hits
                        .Where(h => h.LastModified.HasValue && h.LastModified.Value > DateTime.Now.AddDays(-7))
                        .Count();
                    
                    if (recentHits > 0)
                    {
                        insights.Add($"{recentHits} result(s) modified in the last week - indicating active development areas");
                    }
                }
            }
        }
        
        // Search effectiveness
        if (data.TotalHits > data.Hits.Count * 10)
        {
            insights.Add($"Many results found ({data.TotalHits}). Consider refining your search for more targeted results.");
        }
        
        return insights;
    }
    
    protected override List<COA.Mcp.Framework.Models.AIAction> GenerateActions(SearchResult data, int tokenBudget)
    {
        var actions = new List<COA.Mcp.Framework.Models.AIAction>();
        
        if (data.TotalHits == 0)
        {
            actions.Add(new AIAction
            {
                Action = "broaden_search",
                Description = "Try removing quotes or using wildcards (*) for partial matches",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = ToolNames.IndexWorkspace,
                Description = "Ensure the workspace is indexed",
                Priority = 90
            });
        }
        else
        {
            // Refinement actions
            if (data.TotalHits > 100)
            {
                actions.Add(new AIAction
                {
                    Action = "refine_search",
                    Description = "Add more specific terms or use quotes for exact phrases",
                    Priority = 80
                });
            }
            
            // File type filtering
            if (data.Hits.Count > 10)
            {
                var topExtension = data.Hits
                    .Select(h => Path.GetExtension(h.FilePath))
                    .Where(ext => !string.IsNullOrEmpty(ext))
                    .GroupBy(ext => ext)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                
                if (topExtension != null)
                {
                    actions.Add(new AIAction
                    {
                        Action = "filter_by_type",
                        Description = $"Focus on {topExtension.Key} files which contain most results",
                        Priority = 70
                    });
                }
            }
            
            // Explore high-scoring results
            if (data.Hits.Any(h => h.Score > 0.8f))
            {
                var topFile = data.Hits.OrderByDescending(h => h.Score).First();
                actions.Add(new AIAction
                {
                    Action = "explore_top_result",
                    Description = $"Examine {Path.GetFileName(topFile.FilePath)} (score: {topFile.Score:F2})",
                    Priority = 90
                });
            }
            
            // Related searches
            if (!string.IsNullOrEmpty(data.Query))
            {
                actions.Add(new AIAction
                {
                    Action = "find_references",
                    Description = $"Search for references to symbols found in '{data.Query}'",
                    Priority = 60
                });
            }
        }
        
        return actions;
    }
    
    private List<SearchHit> ReduceSearchHits(List<SearchHit> hits, int tokenBudget, string responseMode)
    {
        if (hits.Count == 0)
            return hits;
        
        // Estimate tokens per hit
        var sampleHit = hits.First();
        var tokensPerHit = EstimateHitTokens(sampleHit, responseMode);
        
        // Calculate how many hits we can include
        var maxHits = Math.Max(1, tokenBudget / tokensPerHit);
        
        if (hits.Count <= maxHits)
            return hits;
        
        // Use progressive reduction with score-based priority
        var context = new ReductionContext
        {
            PriorityFunction = obj => obj is SearchHit hit ? hit.Score : 0
        };
        
        var result = _reductionEngine.Reduce(
            hits,
            hit => EstimateHitTokens(hit, responseMode),
            tokenBudget,
            "priority",
            context);
        
        return result.Items;
    }
    
    private int EstimateHitTokens(SearchHit hit, string responseMode)
    {
        var tokens = TokenEstimator.EstimateString(hit.FilePath);
        
        if (responseMode == "full")
        {
            tokens += TokenEstimator.EstimateString(hit.Content ?? "");
            tokens += TokenEstimator.EstimateString(hit.Snippet ?? "");
            tokens += 20; // Metadata overhead
        }
        else
        {
            // Summary mode - just snippet
            tokens += TokenEstimator.EstimateString(hit.Snippet ?? hit.Content?.Substring(0, 200) ?? "");
            tokens += 10; // Reduced metadata
        }
        
        return tokens;
    }
    
    private string BuildSummary(SearchResult data, int includedCount, string responseMode)
    {
        if (data.TotalHits == 0)
        {
            return "No results found";
        }
        
        var summary = $"Found {data.TotalHits} result{(data.TotalHits != 1 ? "s" : "")}";
        
        if (includedCount < data.TotalHits)
        {
            summary += $" (showing top {includedCount})";
        }
        
        if (!string.IsNullOrEmpty(data.Query))
        {
            summary += $" for '{data.Query}'";
        }
        
        if (data.ProcessingTimeMs > 0)
        {
            summary += $" in {data.ProcessingTimeMs}ms";
        }
        
        return summary;
    }
}