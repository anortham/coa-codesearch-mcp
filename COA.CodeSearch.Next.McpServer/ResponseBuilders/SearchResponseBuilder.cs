using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for text search operations with token optimization
/// </summary>
public class SearchResponseBuilder : BaseResponseBuilder<SearchResult>
{
    private readonly ITokenEstimator _tokenEstimator;
    
    public SearchResponseBuilder(
        ITokenEstimator tokenEstimator,
        ILogger<SearchResponseBuilder> logger) : base(logger)
    {
        _tokenEstimator = tokenEstimator;
    }
    
    public override async Task<object> BuildResponseAsync(
        SearchResult data,
        ResponseContext context)
    {
        var tokenBudget = CalculateTokenBudget(context);
        var startTime = DateTime.UtcNow;
        
        _logger.LogDebug("Building search response with token budget: {Budget}", tokenBudget);
        
        // Allocate token budget across response components
        var contentBudget = (int)(tokenBudget * 0.60);  // 60% for content
        var insightsBudget = (int)(tokenBudget * 0.20); // 20% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        // 5% reserved for metadata
        
        // Reduce search hits to fit token budget
        var reducedHits = await ReduceSearchHitsAsync(data.Hits, contentBudget);
        
        // Generate insights based on search results
        var insights = GenerateInsights(data, context.ResponseMode);
        var reducedInsights = ReduceInsights(insights, insightsBudget);
        
        // Generate suggested actions
        var actions = GenerateActions(data, actionsBudget);
        var reducedActions = ReduceActions(actions, actionsBudget);
        
        // Build the response
        var response = new AIOptimizedResponse
        {
            Format = "ai-optimized",
            Operation = context.ToolName,
            Success = true,
            Data = new AIResponseData
            {
                Summary = GenerateSummary(data, reducedHits),
                Results = FormatSearchResults(reducedHits),
                Count = reducedHits.Count,
                Meta = new Dictionary<string, object>
                {
                    { "TotalHits", data.TotalHits },
                    { "Query", data.Query ?? "" },
                    { "SearchTime", data.SearchTime.TotalMilliseconds }
                }
            },
            Insights = reducedInsights,
            Actions = reducedActions,
            Meta = CreateMetadata(startTime, reducedHits.Count < data.Hits.Count)
        };
        
        // Update actual token count
        response.Meta.TokenInfo.Estimated = _tokenEstimator.EstimateObject(response);
        response.Meta.TokenInfo.ResponseMode = context.ResponseMode;
        
        _logger.LogInformation(
            "Built search response: {ReducedHits}/{TotalHits} hits, {TokensUsed} tokens",
            reducedHits.Count, data.TotalHits, response.Meta.TokenInfo.Estimated);
        
        return await Task.FromResult(response);
    }
    
    private async Task<List<SearchHit>> ReduceSearchHitsAsync(
        List<SearchHit> hits, 
        int tokenBudget)
    {
        var reducedHits = new List<SearchHit>();
        var currentTokens = 0;
        
        // Sort by score to keep most relevant results
        var sortedHits = hits.OrderByDescending(h => h.Score).ToList();
        
        foreach (var hit in sortedHits)
        {
            // Estimate tokens for this hit with truncated content
            var truncatedHit = TruncateSearchHit(hit);
            var hitTokens = _tokenEstimator.EstimateObject(truncatedHit);
            
            if (currentTokens + hitTokens <= tokenBudget)
            {
                reducedHits.Add(truncatedHit);
                currentTokens += hitTokens;
            }
            else
            {
                // Stop adding hits if we exceed budget
                break;
            }
        }
        
        return await Task.FromResult(reducedHits);
    }
    
    private SearchHit TruncateSearchHit(SearchHit hit)
    {
        const int maxContentLength = 500;
        
        return new SearchHit
        {
            FilePath = hit.FilePath,
            Score = hit.Score,
            Content = TruncateContent(hit.Content, maxContentLength),
            Fields = hit.Fields,
            HighlightedFragments = hit.HighlightedFragments?.Take(3).ToList()
        };
    }
    
    private string? TruncateContent(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        
        var truncated = content.Substring(0, maxLength);
        
        // Try to truncate at a word boundary
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.8)
        {
            truncated = truncated.Substring(0, lastSpace);
        }
        
        return truncated + "...";
    }
    
    private string GenerateSummary(SearchResult data, List<SearchHit> reducedHits)
    {
        if (data.TotalHits == 0)
            return "No results found.";
        
        if (reducedHits.Count == data.TotalHits)
            return $"Found {data.TotalHits} result{(data.TotalHits == 1 ? "" : "s")}.";
        
        return $"Showing top {reducedHits.Count} of {data.TotalHits} results.";
    }
    
    private List<object> FormatSearchResults(List<SearchHit> hits)
    {
        return hits.Select(hit => new
        {
            FilePath = hit.FilePath,
            Score = Math.Round(hit.Score, 4),
            Content = hit.Content,
            Highlights = hit.HighlightedFragments
        }).Cast<object>().ToList();
    }
    
    protected override List<string> GenerateInsights(SearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        // No results insight
        if (data.TotalHits == 0)
        {
            insights.Add("No results found. Try broadening your search criteria.");
            insights.Add("Check if the workspace is properly indexed.");
            return insights;
        }
        
        // Result count insights
        if (data.TotalHits == 1)
        {
            insights.Add("Found exactly one match.");
        }
        else if (data.TotalHits > 100)
        {
            insights.Add($"Large result set ({data.TotalHits} matches). Consider refining your search.");
        }
        else if (data.TotalHits < 5)
        {
            insights.Add($"Found {data.TotalHits} matches. Consider broadening search if needed.");
        }
        else
        {
            insights.Add($"Found {data.TotalHits} matches across the codebase.");
        }
        
        // File type distribution insight
        var fileTypes = data.Hits
            .Select(h => Path.GetExtension(h.FilePath))
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .ToList();
        
        if (fileTypes.Any())
        {
            var topType = fileTypes.First();
            insights.Add($"Most matches in {topType.Key} files ({topType.Count()} matches).");
        }
        
        // Score distribution insight
        if (data.Hits.Count > 0)
        {
            var topScore = data.Hits.Max(h => h.Score);
            var avgScore = data.Hits.Average(h => h.Score);
            
            if (topScore > 0.8)
            {
                insights.Add("Found highly relevant matches (score > 0.8).");
            }
            else if (avgScore < 0.3)
            {
                insights.Add("Match relevance is low. Consider more specific search terms.");
            }
        }
        
        // Response mode insight
        if (responseMode == "summary")
        {
            insights.Add("Showing summary view. Use 'full' mode for complete content.");
        }
        
        // Search performance insight
        if (data.SearchTime.TotalMilliseconds > 1000)
        {
            insights.Add($"Search took {data.SearchTime.TotalSeconds:F1} seconds. Consider optimizing the index.");
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(SearchResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.TotalHits == 0)
        {
            // No results - suggest broadening search
            actions.Add(new AIAction
            {
                Action = "text_search",
                Description = "Try a broader search term",
                Rationale = "No results found with current query",
                Category = "search",
                Priority = 10,
                Parameters = new Dictionary<string, object>
                {
                    { "query", SimplifyQuery(data.Query) }
                }
            });
        }
        else if (data.TotalHits > 50)
        {
            // Too many results - suggest refinement
            actions.Add(new AIAction
            {
                Action = "text_search",
                Description = "Refine search with more specific terms",
                Rationale = $"Found {data.TotalHits} results - refinement could help",
                Category = "search",
                Priority = 10,
                Parameters = new Dictionary<string, object>
                {
                    { "query", data.Query + " AND specific_term" }
                }
            });
        }
        
        // Suggest reading top result
        if (data.Hits.Count > 0)
        {
            var topHit = data.Hits.OrderByDescending(h => h.Score).First();
            actions.Add(new AIAction
            {
                Action = "read_file",
                Description = $"Read the most relevant file: {Path.GetFileName(topHit.FilePath)}",
                Rationale = $"This file has the highest relevance score ({topHit.Score:F2})",
                Category = "navigate",
                Priority = 8,
                Parameters = new Dictionary<string, object>
                {
                    { "path", topHit.FilePath }
                }
            });
        }
        
        // Suggest file search for related files
        if (!string.IsNullOrEmpty(data.Query) && data.TotalHits > 0)
        {
            actions.Add(new AIAction
            {
                Action = "file_search",
                Description = $"Find files with names matching '{data.Query}'",
                Rationale = "File names might contain relevant code",
                Category = "search",
                Priority = 6,
                Parameters = new Dictionary<string, object>
                {
                    { "pattern", $"*{data.Query}*" }
                }
            });
        }
        
        return actions;
    }
    
    private string SimplifyQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return "*";
        
        // Remove complex operators for a simpler search
        return query
            .Replace(" AND ", " ")
            .Replace(" OR ", " ")
            .Replace(" NOT ", " ")
            .Split(' ')
            .FirstOrDefault() ?? "*";
    }
}