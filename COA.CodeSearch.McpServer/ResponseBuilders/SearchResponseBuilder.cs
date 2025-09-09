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
/// Response builder for search operations with token-aware optimization.
/// </summary>
public class SearchResponseBuilder : BaseResponseBuilder<SearchResult, AIOptimizedResponse<SearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public SearchResponseBuilder(
        ILogger<SearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SearchResult>> BuildResponseAsync(SearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        _logger?.LogDebug("Token optimization: Data={DataBudget}, Insights={InsightsBudget}, Actions={ActionsBudget}, Total={TotalBudget}", 
            dataBudget, insightsBudget, actionsBudget, tokenBudget);
        
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
                        Expiration = TimeSpan.FromHours(24),
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
        // Create a copy of the data for AI response with reduced hits
        var aiData = new SearchResult
        {
            TotalHits = data.TotalHits,
            Hits = reducedHits,
            SearchTime = data.SearchTime,
            Query = data.Query
            // ProcessingTimeMs is a computed property, no need to set it
        };
        
        var response = new AIOptimizedResponse<SearchResult>
        {
            Success = true,
            Data = new AIResponseData<SearchResult>
            {
                Summary = BuildSummary(data, reducedHits.Count, context.ResponseMode),
                Results = aiData, // Pass the copy with reduced hits for AI
                Count = data.TotalHits,
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
        
        // Set operation name if there's a way to do it
        if (response.Operation != null)
        {
            // Operation is read-only, might be set via constructor or base class
        }
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built search response: {Hits} of {Total} hits, {Insights} insights, {Actions} actions, {Tokens} tokens (Budget: {Budget}, Truncated: {Truncated}, Stored: {HasResource})",
            reducedHits.Count, data.TotalHits, response.Insights.Count, response.Actions.Count, 
            response.Meta.TokenInfo.Estimated, tokenBudget, wasTruncated, resourceUri != null);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(SearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalHits == 0)
        {
            insights.Add("No results");
            return insights;
        }
        
        // Distribution insights - keep ultra-lean
        if (data.Hits.Count > 0 && responseMode != "summary") // Skip insights in summary mode
        {
            var fileExtensions = data.Hits
                .Select(h => Path.GetExtension(h.FilePath))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .GroupBy(ext => ext)
                .OrderByDescending(g => g.Count())
                .Take(2) // Just top 2
                .ToList();
            
            if (fileExtensions.Any())
            {
                var topTypes = string.Join(", ", fileExtensions.Select(g => $"{g.Key}:{g.Count()}"));
                insights.Add($"Types: {topTypes}");
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
                Action = "broaden",
                Description = "Use wildcards (*)",
                Priority = 100
            });
        }
        else
        {
            // Refinement actions - only if really needed
            if (data.TotalHits > 500) // Only suggest refinement for huge result sets
            {
                actions.Add(new AIAction
                {
                    Action = "refine",
                    Description = "Add terms",
                    Priority = 80
                });
            }
            
            // File type filtering disabled to save tokens
            /*
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
            */
            
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
            return CleanupHits(hits); // Clean up even if we're not reducing
        
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
        
        return CleanupHits(result.Items);
    }
    
    private List<SearchHit> CleanupHits(List<SearchHit> hits)
    {
        // Remove redundant fields to minimize tokens
        return hits.Select(hit => {
            // Only keep essential fields, remove duplicates
            var minimalFields = new Dictionary<string, string>();
            
            // Keep only non-duplicated essential fields
            if (hit.Fields.ContainsKey("size"))
                minimalFields["size"] = hit.Fields["size"];
                
                
            // Round score to 2 decimal places
            hit.Score = (float)Math.Round(hit.Score, 2);
            
            // Clear empty collections
            if (hit.HighlightedFragments?.Count == 0)
                hit.HighlightedFragments = null;
            
            // Use cleaner hit structure - PRESERVE line numbers (2 tokens, high value)
            return new SearchHit
            {
                FilePath = hit.FilePath,
                Score = hit.Score,
                Fields = minimalFields, // Minimal fields only
                HighlightedFragments = hit.HighlightedFragments,
                LastModified = hit.LastModified,
                LineNumber = hit.LineNumber, // PRESERVE: Essential for VS Code navigation
                ContextLines = hit.ContextLines, // PRESERVE: Context for AI analysis
                StartLine = hit.StartLine, // PRESERVE: Context bounds
                EndLine = hit.EndLine, // PRESERVE: Context bounds
                Snippet = hit.Snippet // PRESERVE: Original snippet for context
            };
        }).ToList();
    }
    
    private int EstimateHitTokens(SearchHit hit, string responseMode)
    {
        var tokens = TokenEstimator.EstimateString(hit.FilePath);
        
        if (responseMode == "full")
        {
            // In full mode, include snippet and fields
            tokens += TokenEstimator.EstimateString(hit.Snippet ?? "");
            tokens += EstimateFieldTokens(hit.Fields);
            tokens += 20; // Metadata overhead
        }
        else
        {
            // Summary mode - just snippet
            tokens += TokenEstimator.EstimateString(hit.Snippet ?? "");
            tokens += 10; // Reduced metadata
        }
        
        return tokens;
    }
    
    private int EstimateFieldTokens(Dictionary<string, string> fields)
    {
        var tokens = 0;
        foreach (var field in fields)
        {
            tokens += TokenEstimator.EstimateString(field.Key);
            tokens += TokenEstimator.EstimateString(field.Value);
        }
        return tokens;
    }
    
    private string BuildSummary(SearchResult data, int includedCount, string responseMode)
    {
        if (data.TotalHits == 0)
            return "No results";
        
        // Ultra-lean summary
        var summary = $"{data.TotalHits} hits";
        
        if (includedCount < data.TotalHits)
            summary += $" (top {includedCount})";
        
        // Skip query echo and timing in summary mode to save tokens
        if (responseMode != "summary" && data.ProcessingTimeMs > 100) // Only show if slow
            summary += $" {data.ProcessingTimeMs}ms";
        
        return summary;
    }
}