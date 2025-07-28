using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Hybrid search service that combines Lucene text search with semantic search
/// Provides both keyword-based and concept-based memory discovery
/// </summary>
public class HybridMemorySearch
{
    private readonly FlexibleMemoryService _luceneSearch;
    private readonly SemanticMemoryIndex _semanticIndex;
    private readonly ILogger<HybridMemorySearch> _logger;

    public HybridMemorySearch(
        FlexibleMemoryService luceneSearch,
        SemanticMemoryIndex semanticIndex,
        ILogger<HybridMemorySearch> logger)
    {
        _luceneSearch = luceneSearch;
        _semanticIndex = semanticIndex;
        _logger = logger;
    }

    /// <summary>
    /// Perform hybrid search combining Lucene and semantic results
    /// </summary>
    public async Task<HybridSearchResult> HybridSearchAsync(
        string query,
        HybridSearchOptions? options = null)
    {
        options ??= HybridSearchOptions.Default;
        
        _logger.LogDebug("Starting hybrid search for query: '{Query}' with {Strategy} merge strategy", 
            query, options.MergeStrategy);

        var startTime = DateTime.UtcNow;

        try
        {
            // Run both searches in parallel for better performance
            var luceneTask = SearchLuceneAsync(query, options);
            var semanticTask = SearchSemanticAsync(query, options);

            await Task.WhenAll(luceneTask, semanticTask);

            var luceneResults = luceneTask.Result;
            var semanticResults = semanticTask.Result;

            // Merge and rerank results
            var mergedResults = await MergeResultsAsync(luceneResults, semanticResults, options);

            var searchTime = DateTime.UtcNow - startTime;

            var result = new HybridSearchResult
            {
                Results = mergedResults,
                LuceneCount = luceneResults.Count,
                SemanticCount = semanticResults.Count,
                MergeStrategy = options.MergeStrategy.ToString(),
                SearchTimeMs = (int)searchTime.TotalMilliseconds,
                Query = query
            };

            _logger.LogDebug("Hybrid search completed in {Ms}ms: {LuceneCount} Lucene + {SemanticCount} semantic → {FinalCount} merged", 
                result.SearchTimeMs, result.LuceneCount, result.SemanticCount, result.Results.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform hybrid search for query: {Query}", query);
            
            // Fallback to Lucene-only search
            var fallbackResults = await SearchLuceneAsync(query, options);
            return new HybridSearchResult
            {
                Results = fallbackResults.Select(memory => new RankedMemory
                {
                    Memory = memory,
                    LuceneRank = fallbackResults.IndexOf(memory) + 1,
                    LuceneScore = 1.0f, // Default score since FlexibleMemoryEntry doesn't have Score
                    CombinedScore = 1.0f
                }).ToList(),
                LuceneCount = fallbackResults.Count,
                SemanticCount = 0,
                MergeStrategy = "LuceneOnly-Fallback",
                Query = query
            };
        }
    }

    /// <summary>
    /// Search using Lucene text search
    /// </summary>
    private async Task<List<FlexibleMemoryEntry>> SearchLuceneAsync(
        string query, 
        HybridSearchOptions options)
    {
        var request = new FlexibleMemorySearchRequest
        {
            Query = query,
            MaxResults = options.MaxResults * 2, // Get more results for better merging
            IncludeArchived = false,
            Facets = options.LuceneFilters
        };

        var luceneResult = await _luceneSearch.SearchMemoriesAsync(request);
        return luceneResult.Memories.ToList();
    }

    /// <summary>
    /// Search using semantic similarity
    /// </summary>
    private async Task<List<SemanticSearchResult>> SearchSemanticAsync(
        string query,
        HybridSearchOptions options)
    {
        var filter = CreateSemanticFilter(options.SemanticFilters);
        
        return await _semanticIndex.SemanticSearchAsync(
            query,
            options.MaxResults * 2, // Get more results for better merging
            filter,
            options.SemanticThreshold);
    }

    /// <summary>
    /// Merge and rerank results from both search methods
    /// </summary>
    private async Task<List<RankedMemory>> MergeResultsAsync(
        List<FlexibleMemoryEntry> luceneResults,
        List<SemanticSearchResult> semanticResults,
        HybridSearchOptions options)
    {
        var rankedResults = new Dictionary<string, RankedMemory>();

        // Add Lucene results
        for (int i = 0; i < luceneResults.Count; i++)
        {
            var memory = luceneResults[i];
            rankedResults[memory.Id] = new RankedMemory
            {
                Memory = memory,
                LuceneRank = i + 1,
                LuceneScore = 1.0f, // Default score since FlexibleMemoryEntry doesn't have Score
                CombinedScore = 1.0f * options.LuceneWeight
            };
        }

        // Add/update with semantic results
        for (int i = 0; i < semanticResults.Count; i++)
        {
            var result = semanticResults[i];
            
            if (rankedResults.TryGetValue(result.MemoryId, out var ranked))
            {
                // Memory found by both methods - boost score
                ranked.SemanticRank = i + 1;
                ranked.SemanticScore = result.Similarity;
                ranked.CombinedScore = CalculateCombinedScore(ranked, options);
                ranked.FoundByBoth = true;
            }
            else
            {
                // Only found by semantic search - need to get full memory
                var searchRequest = new FlexibleMemorySearchRequest
                {
                    Query = $"id:{result.MemoryId}",
                    MaxResults = 1
                };
                var searchResult = await _luceneSearch.SearchMemoriesAsync(searchRequest);
                var memory = searchResult.Memories.FirstOrDefault();
                if (memory != null)
                {
                    rankedResults[result.MemoryId] = new RankedMemory
                    {
                        Memory = memory,
                        SemanticRank = i + 1,
                        SemanticScore = result.Similarity,
                        CombinedScore = result.Similarity * options.SemanticWeight,
                        FoundByBoth = false
                    };
                }
            }
        }

        // Sort by combined score and return top results
        return rankedResults.Values
            .OrderByDescending(r => r.CombinedScore)
            .Take(options.MaxResults)
            .ToList();
    }

    /// <summary>
    /// Calculate combined score based on merge strategy
    /// </summary>
    private float CalculateCombinedScore(RankedMemory memory, HybridSearchOptions options)
    {
        return options.MergeStrategy switch
        {
            MergeStrategy.Linear => CalculateLinearScore(memory, options),
            MergeStrategy.Reciprocal => CalculateReciprocalScore(memory, options),
            MergeStrategy.Multiplicative => CalculateMultiplicativeScore(memory, options),
            _ => memory.CombinedScore
        };
    }

    private float CalculateLinearScore(RankedMemory memory, HybridSearchOptions options)
    {
        var luceneComponent = memory.LuceneScore * options.LuceneWeight;
        var semanticComponent = memory.SemanticScore * options.SemanticWeight;
        
        // Boost if found by both methods
        var boost = memory.FoundByBoth ? options.BothFoundBoost : 1.0f;
        
        return (luceneComponent + semanticComponent) * boost;
    }

    private float CalculateReciprocalScore(RankedMemory memory, HybridSearchOptions options)
    {
        // Reciprocal Rank Fusion (RRF) - popular in IR
        const float k = 60; // Standard RRF constant
        
        var luceneComponent = memory.LuceneRank > 0 ? 1.0f / (memory.LuceneRank + k) : 0f;
        var semanticComponent = memory.SemanticRank > 0 ? 1.0f / (memory.SemanticRank + k) : 0f;
        
        return (luceneComponent * options.LuceneWeight) + (semanticComponent * options.SemanticWeight);
    }

    private float CalculateMultiplicativeScore(RankedMemory memory, HybridSearchOptions options)
    {
        var luceneScore = memory.LuceneScore;
        var semanticScore = memory.SemanticScore;
        
        if (memory.FoundByBoth)
        {
            // Multiply scores when found by both (amplifies high matches)
            return luceneScore * semanticScore * options.BothFoundBoost * 2.0f;
        }
        else
        {
            // Use weighted single score
            return Math.Max(
                luceneScore * options.LuceneWeight,
                semanticScore * options.SemanticWeight);
        }
    }

    /// <summary>
    /// Create semantic filter from hybrid search options
    /// </summary>
    private Dictionary<string, object>? CreateSemanticFilter(Dictionary<string, object>? filters)
    {
        if (filters == null || filters.Count == 0)
            return null;

        var semanticFilter = new Dictionary<string, object>();
        
        foreach (var filter in filters)
        {
            // Map common filters to semantic index metadata
            switch (filter.Key.ToLowerInvariant())
            {
                case "type":
                    semanticFilter["type"] = filter.Value;
                    break;
                case "isshared":
                    semanticFilter["isShared"] = filter.Value;
                    break;
                default:
                    // Prefix custom fields
                    semanticFilter[$"field_{filter.Key}"] = filter.Value;
                    break;
            }
        }

        return semanticFilter.Count > 0 ? semanticFilter : null;
    }
}

/// <summary>
/// Options for hybrid search configuration
/// </summary>
public class HybridSearchOptions
{
    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int MaxResults { get; set; } = 50;
    
    /// <summary>
    /// Weight for Lucene search results (0.0 to 1.0)
    /// </summary>
    public float LuceneWeight { get; set; } = 0.6f;
    
    /// <summary>
    /// Weight for semantic search results (0.0 to 1.0)
    /// </summary>
    public float SemanticWeight { get; set; } = 0.4f;
    
    /// <summary>
    /// Strategy for merging results from both searches
    /// </summary>
    public MergeStrategy MergeStrategy { get; set; } = MergeStrategy.Linear;
    
    /// <summary>
    /// Minimum similarity threshold for semantic results
    /// </summary>
    public float SemanticThreshold { get; set; } = 0.1f;
    
    /// <summary>
    /// Boost factor when a result is found by both methods
    /// </summary>
    public float BothFoundBoost { get; set; } = 1.2f;
    
    /// <summary>
    /// Filters to apply to Lucene search
    /// </summary>
    public Dictionary<string, string>? LuceneFilters { get; set; }
    
    /// <summary>
    /// Filters to apply to semantic search
    /// </summary>
    public Dictionary<string, object>? SemanticFilters { get; set; }

    /// <summary>
    /// Default configuration optimized for balanced results
    /// </summary>
    public static HybridSearchOptions Default => new HybridSearchOptions();
}

/// <summary>
/// Strategy for merging search results
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Linear combination of scores
    /// </summary>
    Linear,
    
    /// <summary>
    /// Reciprocal Rank Fusion
    /// </summary>
    Reciprocal,
    
    /// <summary>
    /// Multiplicative score combination
    /// </summary>
    Multiplicative
}

/// <summary>
/// Memory with ranking information from hybrid search
/// </summary>
public class RankedMemory
{
    /// <summary>
    /// The memory entry
    /// </summary>
    public FlexibleMemoryEntry Memory { get; set; } = new();
    
    /// <summary>
    /// Rank from Lucene search (1-based, 0 = not found)
    /// </summary>
    public int LuceneRank { get; set; }
    
    /// <summary>
    /// Score from Lucene search
    /// </summary>
    public float LuceneScore { get; set; }
    
    /// <summary>
    /// Rank from semantic search (1-based, 0 = not found)
    /// </summary>
    public int SemanticRank { get; set; }
    
    /// <summary>
    /// Score from semantic search
    /// </summary>
    public float SemanticScore { get; set; }
    
    /// <summary>
    /// Final combined score
    /// </summary>
    public float CombinedScore { get; set; }
    
    /// <summary>
    /// Whether this memory was found by both search methods
    /// </summary>
    public bool FoundByBoth { get; set; }
}

/// <summary>
/// Result from hybrid search
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// Ranked and merged search results
    /// </summary>
    public List<RankedMemory> Results { get; set; } = new();
    
    /// <summary>
    /// Number of results from Lucene search
    /// </summary>
    public int LuceneCount { get; set; }
    
    /// <summary>
    /// Number of results from semantic search
    /// </summary>
    public int SemanticCount { get; set; }
    
    /// <summary>
    /// Merge strategy used
    /// </summary>
    public string MergeStrategy { get; set; } = string.Empty;
    
    /// <summary>
    /// Total search time in milliseconds
    /// </summary>
    public int SearchTimeMs { get; set; }
    
    /// <summary>
    /// Original search query
    /// </summary>
    public string Query { get; set; } = string.Empty;
}