using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for hybrid search that combines Lucene text search with semantic search
/// </summary>
public class HybridSearchTool
{
    private readonly HybridMemorySearch _hybridSearch;
    private readonly ILogger<HybridSearchTool> _logger;

    public HybridSearchTool(
        HybridMemorySearch hybridSearch,
        ILogger<HybridSearchTool> logger)
    {
        _hybridSearch = hybridSearch;
        _logger = logger;
    }

    public async Task<object> ExecuteAsync(HybridSearchParams parameters)
    {
        try
        {
            _logger.LogInformation("Executing hybrid search for query: {Query} with strategy: {Strategy}", 
                parameters.Query, parameters.MergeStrategy);

            var options = new HybridSearchOptions
            {
                MaxResults = parameters.MaxResults,
                LuceneWeight = parameters.LuceneWeight,
                SemanticWeight = parameters.SemanticWeight,
                MergeStrategy = parameters.MergeStrategy,
                SemanticThreshold = parameters.SemanticThreshold,
                BothFoundBoost = parameters.BothFoundBoost,
                LuceneFilters = parameters.LuceneFilters,
                SemanticFilters = parameters.SemanticFilters
            };

            var result = await _hybridSearch.HybridSearchAsync(parameters.Query, options);

            if (result.Results.Count == 0)
            {
                return new
                {
                    success = true,
                    query = parameters.Query,
                    results = new object[0],
                    summary = new
                    {
                        totalFound = 0,
                        luceneCount = result.LuceneCount,
                        semanticCount = result.SemanticCount,
                        mergeStrategy = result.MergeStrategy,
                        searchTimeMs = result.SearchTimeMs
                    },
                    insights = new[]
                    {
                        "No results found with hybrid search",
                        $"Lucene found {result.LuceneCount} results, semantic found {result.SemanticCount}",
                        "Try different search terms or lower semantic threshold",
                        "💡 TIP: Hybrid search combines exact matching with concept understanding"
                    }
                };
            }

            // Convert results to output format
            var outputResults = result.Results.Select(r => new
            {
                id = r.Memory.Id,
                type = r.Memory.Type,
                content = r.Memory.Content,
                created = r.Memory.Created,
                modified = r.Memory.Modified,
                filesInvolved = r.Memory.FilesInvolved,
                isShared = r.Memory.IsShared,
                accessCount = r.Memory.AccessCount,
                fields = r.Memory.Fields,
                ranking = new
                {
                    combinedScore = Math.Round(r.CombinedScore, 3),
                    luceneRank = r.LuceneRank,
                    luceneScore = Math.Round(r.LuceneScore, 3),
                    semanticRank = r.SemanticRank,
                    semanticScore = Math.Round(r.SemanticScore, 3),
                    foundByBoth = r.FoundByBoth
                }
            }).ToList();

            var insights = GenerateInsights(result, parameters);

            return new
            {
                success = true,
                query = parameters.Query,
                results = outputResults,
                summary = new
                {
                    totalFound = result.Results.Count,
                    luceneCount = result.LuceneCount,
                    semanticCount = result.SemanticCount,
                    mergeStrategy = result.MergeStrategy,
                    searchTimeMs = result.SearchTimeMs,
                    foundByBoth = result.Results.Count(r => r.FoundByBoth),
                    weights = new
                    {
                        lucene = parameters.LuceneWeight,
                        semantic = parameters.SemanticWeight
                    }
                },
                insights = insights
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing hybrid search for query: {Query}", parameters.Query);
            
            return new
            {
                success = false,
                error = "Failed to execute hybrid search",
                details = ex.Message,
                query = parameters.Query
            };
        }
    }

    private string[] GenerateInsights(HybridSearchResult result, HybridSearchParams parameters)
    {
        var insights = new List<string>();

        // Performance insight
        if (result.SearchTimeMs < 100)
        {
            insights.Add($"⚡ Fast search completed in {result.SearchTimeMs}ms");
        }
        else if (result.SearchTimeMs > 500)
        {
            insights.Add($"🐌 Slower search ({result.SearchTimeMs}ms) - consider index optimization");
        }

        // Coverage insight
        var bothFoundCount = result.Results.Count(r => r.FoundByBoth);
        if (bothFoundCount > 0)
        {
            insights.Add($"🎯 {bothFoundCount} memories found by both text and semantic search (high confidence)");
        }

        var luceneOnlyCount = result.Results.Count(r => r.LuceneRank > 0 && r.SemanticRank == 0);
        var semanticOnlyCount = result.Results.Count(r => r.SemanticRank > 0 && r.LuceneRank == 0);

        if (luceneOnlyCount > 0)
        {
            insights.Add($"📝 {luceneOnlyCount} found only by text search (exact word matches)");
        }

        if (semanticOnlyCount > 0)
        {
            insights.Add($"🧠 {semanticOnlyCount} found only by semantic search (concept matches)");
        }

        // Search effectiveness
        if (result.LuceneCount == 0 && result.SemanticCount > 0)
        {
            insights.Add("💡 Text search found nothing, but concepts were discovered semantically");
        }
        else if (result.LuceneCount > 0 && result.SemanticCount == 0)
        {
            insights.Add("📄 Only text matches found - no semantic relationships discovered");
        }

        // Strategy insights
        switch (parameters.MergeStrategy)
        {
            case MergeStrategy.Linear:
                insights.Add($"📊 Linear merge used (Lucene: {parameters.LuceneWeight:P0}, Semantic: {parameters.SemanticWeight:P0})");
                break;
            case MergeStrategy.Reciprocal:
                insights.Add("🔄 Reciprocal Rank Fusion used - ranks matter more than scores");
                break;
            case MergeStrategy.Multiplicative:
                insights.Add("✖️ Multiplicative merge - amplifies high-confidence matches");
                break;
        }

        // Quality assessment
        var avgCombinedScore = result.Results.Any() ? result.Results.Average(r => r.CombinedScore) : 0;
        if (avgCombinedScore > 0.7f)
        {
            insights.Add("⭐ High-quality results with strong relevance");
        }
        else if (avgCombinedScore > 0.4f)
        {
            insights.Add("📈 Mixed quality results - consider refining search terms");
        }

        // Actionable suggestions
        if (result.Results.Count >= parameters.MaxResults)
        {
            insights.Add("📋 More results may be available - increase maxResults to see them");
        }

        if (bothFoundCount == 0 && result.Results.Count > 0)
        {
            insights.Add("💡 TIP: No overlap between text and semantic - try different search terms");
        }

        return insights.ToArray();
    }
}

/// <summary>
/// Parameters for hybrid search
/// </summary>
public class HybridSearchParams
{
    /// <summary>
    /// Search query
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>
    /// Weight for Lucene search results (0.0 to 1.0)
    /// </summary>
    public float LuceneWeight { get; set; } = 0.6f;

    /// <summary>
    /// Weight for semantic search results (0.0 to 1.0)
    /// </summary>
    public float SemanticWeight { get; set; } = 0.4f;

    /// <summary>
    /// Strategy for merging results
    /// </summary>
    public MergeStrategy MergeStrategy { get; set; } = MergeStrategy.Linear;

    /// <summary>
    /// Minimum similarity threshold for semantic results
    /// </summary>
    public float SemanticThreshold { get; set; } = 0.2f;

    /// <summary>
    /// Boost factor when result found by both methods
    /// </summary>
    public float BothFoundBoost { get; set; } = 1.2f;

    /// <summary>
    /// Filters for Lucene search
    /// </summary>
    public Dictionary<string, string>? LuceneFilters { get; set; }

    /// <summary>
    /// Filters for semantic search
    /// </summary>
    public Dictionary<string, object>? SemanticFilters { get; set; }
}