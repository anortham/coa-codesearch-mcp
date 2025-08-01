using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Models;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for semantic search that finds conceptually similar memories
/// </summary>
[McpServerToolType]
public class SemanticSearchTool
{
    private readonly SemanticMemoryIndex _semanticIndex;
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<SemanticSearchTool> _logger;

    public SemanticSearchTool(
        SemanticMemoryIndex semanticIndex,
        FlexibleMemoryService memoryService,
        ILogger<SemanticSearchTool> logger)
    {
        _semanticIndex = semanticIndex;
        _memoryService = memoryService;
        _logger = logger;
    }

    [McpServerTool(Name = "semantic_search")]
    [Description("Perform semantic search to find conceptually similar memories using embeddings. Finds memories based on concepts and meaning, not just exact keyword matches. Ideal for discovering related architectural decisions, similar problems, or concept-based exploration.")]
    public async Task<object> ExecuteAsync(SemanticSearchParams parameters)
    {
        try
        {
            _logger.LogInformation("Executing semantic search for query: {Query}", parameters.Query);

            // Create filter from parameters
            var filter = CreateFilter(parameters);

            // Perform semantic search
            var results = await _semanticIndex.SemanticSearchAsync(
                parameters.Query,
                parameters.MaxResults,
                filter,
                parameters.Threshold);

            if (results.Count == 0)
            {
                return new
                {
                    success = true,
                    query = parameters.Query,
                    results = new object[0],
                    summary = new
                    {
                        totalFound = 0,
                        searchType = "semantic",
                        thresholdUsed = parameters.Threshold
                    },
                    insights = new[]
                    {
                        "No semantically similar memories found",
                        "Try lowering the similarity threshold",
                        "Consider using broader or different terminology",
                        "💡 TIP: Semantic search finds concepts, not exact words"
                    }
                };
            }

            // Get full memory details for results
            var enrichedResults = new List<object>();
            foreach (var result in results)
            {
                // Get memory by searching for its ID
                var searchRequest = new FlexibleMemorySearchRequest
                {
                    Query = $"id:{result.MemoryId}",
                    MaxResults = 1
                };
                var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
                var memory = searchResult.Memories.FirstOrDefault();
                if (memory != null)
                {
                    enrichedResults.Add(new
                    {
                        id = memory.Id,
                        type = memory.Type,
                        content = memory.Content,
                        created = memory.Created,
                        modified = memory.Modified,
                        filesInvolved = memory.FilesInvolved,
                        semanticScore = Math.Round(result.Similarity, 3),
                        distance = Math.Round(result.Distance, 3),
                        isShared = memory.IsShared,
                        accessCount = memory.AccessCount,
                        fields = memory.Fields
                    });
                }
            }

            // Create insights about the results
            var insights = GenerateInsights(results, parameters);

            return new
            {
                success = true,
                query = parameters.Query,
                results = enrichedResults,
                summary = new
                {
                    totalFound = results.Count,
                    searchType = "semantic",
                    thresholdUsed = parameters.Threshold,
                    avgSimilarity = Math.Round(results.Average(r => r.Similarity), 3),
                    topSimilarity = Math.Round(results.Max(r => r.Similarity), 3)
                },
                insights = insights
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing semantic search for query: {Query}", parameters.Query);
            
            return new
            {
                success = false,
                error = "Failed to execute semantic search",
                details = ex.Message,
                query = parameters.Query
            };
        }
    }

    private Dictionary<string, object>? CreateFilter(SemanticSearchParams parameters)
    {
        var filter = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(parameters.MemoryType))
        {
            filter["type"] = parameters.MemoryType;
        }

        if (parameters.IsShared.HasValue)
        {
            filter["isShared"] = parameters.IsShared.Value;
        }

        if (parameters.CustomFilters != null)
        {
            foreach (var kvp in parameters.CustomFilters)
            {
                filter[$"field_{kvp.Key}"] = kvp.Value;
            }
        }

        return filter.Count > 0 ? filter : null;
    }

    private string[] GenerateInsights(List<SemanticSearchResult> results, SemanticSearchParams parameters)
    {
        var insights = new List<string>();

        // Quality insights
        var highQualityResults = results.Count(r => r.Similarity > 0.7f);
        var mediumQualityResults = results.Count(r => r.Similarity > 0.4f && r.Similarity <= 0.7f);
        
        if (highQualityResults > 0)
        {
            insights.Add($"Found {highQualityResults} highly similar memories (>70% similarity)");
        }
        
        if (mediumQualityResults > 0)
        {
            insights.Add($"Found {mediumQualityResults} moderately similar memories (40-70% similarity)");
        }

        // Type distribution
        var typeGroups = results.GroupBy(r => r.Metadata.TryGetValue("type", out var type) ? type.ToString() : "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(3);

        if (typeGroups.Any())
        {
            var typeInsight = "Most common types: " + string.Join(", ", typeGroups.Select(g => $"{g.Key} ({g.Count()})"));
            insights.Add(typeInsight);
        }

        // Similarity distribution insight
        var avgSimilarity = results.Average(r => r.Similarity);
        if (avgSimilarity > 0.6f)
        {
            insights.Add("🎯 High-quality semantic matches found");
        }
        else if (avgSimilarity > 0.3f)
        {
            insights.Add("📊 Mixed quality matches - consider refining query");
        }
        else
        {
            insights.Add("🔍 Low similarity matches - try different concepts");
        }

        // Actionable suggestions
        if (results.Count >= parameters.MaxResults)
        {
            insights.Add($"💡 TIP: Showing top {parameters.MaxResults} results - increase maxResults for more");
        }

        if (parameters.Threshold > 0.1f && results.Count < 5)
        {
            insights.Add("💡 TIP: Lower threshold to find more conceptually related memories");
        }

        return insights.ToArray();
    }
}

/// <summary>
/// Parameters for semantic search
/// </summary>
public class SemanticSearchParams
{
    /// <summary>
    /// Search query (concepts, not just keywords)
    /// </summary>
    [Description("Search query (concepts, not just keywords). Examples: 'authentication issues', 'performance problems', 'database design patterns'")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    [Description("Maximum number of results to return")]
    public int MaxResults { get; set; } = 20;

    /// <summary>
    /// Minimum similarity threshold (0.0 to 1.0)
    /// </summary>
    [Description("Minimum similarity threshold (0.0 to 1.0). Lower values find more results.")]
    public float Threshold { get; set; } = 0.2f;

    /// <summary>
    /// Filter by memory type
    /// </summary>
    [Description("Filter by memory type (TechnicalDebt, ArchitecturalDecision, etc.)")]
    public string? MemoryType { get; set; }

    /// <summary>
    /// Filter by shared status
    /// </summary>
    [Description("Filter by shared status")]
    public bool? IsShared { get; set; }

    /// <summary>
    /// Custom field filters
    /// </summary>
    [Description("Custom field filters as key-value pairs")]
    public Dictionary<string, object>? CustomFilters { get; set; }
}