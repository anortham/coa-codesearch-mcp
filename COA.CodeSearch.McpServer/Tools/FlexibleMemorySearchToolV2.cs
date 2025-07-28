using System.Text.Json;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of memory search with structured response format
/// </summary>
public class FlexibleMemorySearchToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "flexible_memory_search_v2";
    public override string Description => "AI-optimized memory search";
    public override ToolCategory Category => ToolCategory.Memory;
    private readonly FlexibleMemoryService _memoryService;
    private readonly IConfiguration _configuration;
    private readonly IQueryExpansionService _queryExpansion;
    private readonly IContextAwarenessService _contextAwareness;
    private readonly AIResponseBuilderService _responseBuilder;

    public FlexibleMemorySearchToolV2(
        ILogger<FlexibleMemorySearchToolV2> logger,
        FlexibleMemoryService memoryService,
        IConfiguration configuration,
        IQueryExpansionService queryExpansion,
        IContextAwarenessService contextAwareness,
        AIResponseBuilderService responseBuilder,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memoryService = memoryService;
        _configuration = configuration;
        _queryExpansion = queryExpansion;
        _contextAwareness = contextAwareness;
        _responseBuilder = responseBuilder;
    }

    public async Task<object> ExecuteAsync(
        string? query = null,
        string[]? types = null,
        string? dateRange = null,
        Dictionary<string, string>? facets = null,
        string? orderBy = null,
        bool orderDescending = true,
        int maxResults = 50,
        bool includeArchived = false,
        bool boostRecent = false,
        bool boostFrequent = false,
        // New intelligent features
        bool enableQueryExpansion = true,
        bool enableContextAwareness = true,
        string? currentFile = null,
        string[]? recentFiles = null,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("Memory search request: query={Query}, types={Types}, expansion={Expansion}, context={Context}", 
                query, types, enableQueryExpansion, enableContextAwareness);

            // Process query with intelligence
            var processedQuery = await ProcessIntelligentQueryAsync(query, enableQueryExpansion, enableContextAwareness, currentFile, recentFiles);
            
            var request = new FlexibleMemorySearchRequest
            {
                Query = processedQuery.FinalQuery,
                Types = types,
                Facets = facets,
                OrderBy = orderBy,
                OrderDescending = orderDescending,
                MaxResults = maxResults,
                IncludeArchived = includeArchived,
                BoostRecent = boostRecent,
                BoostFrequent = boostFrequent
            };

            if (!string.IsNullOrEmpty(dateRange))
            {
                request.DateRange = new DateRangeFilter { RelativeTime = dateRange };
            }

            var searchResult = await _memoryService.SearchMemoriesAsync(request);

            // Update search tracking with actual results
            if (enableContextAwareness && !string.IsNullOrEmpty(query))
            {
                await _contextAwareness.TrackSearchQueryAsync(query, searchResult.TotalFound);
            }

            // Create AI-optimized response using response builder
            return _responseBuilder.BuildMemorySearchResponse(searchResult, request, query, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in FlexibleMemorySearchV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }


    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for memory search"));
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is FlexibleMemorySearchResult searchResult)
        {
            return searchResult.TotalFound;
        }
        return 0;
    }

    /// <summary>
    /// Process query with intelligent expansion and context awareness
    /// </summary>
    private async Task<ProcessedQueryResult> ProcessIntelligentQueryAsync(
        string? originalQuery, 
        bool enableExpansion, 
        bool enableContext, 
        string? currentFile, 
        string[]? recentFiles)
    {
        var result = new ProcessedQueryResult
        {
            OriginalQuery = originalQuery ?? "*",
            FinalQuery = originalQuery ?? "*"
        };

        // Skip processing for wildcard queries
        if (string.IsNullOrWhiteSpace(originalQuery) || originalQuery == "*")
        {
            return result;
        }

        try
        {
            // Step 1: Query Expansion
            if (enableExpansion)
            {
                var expandedQuery = await _queryExpansion.ExpandQueryAsync(originalQuery);
                result.ExpandedTerms = expandedQuery.WeightedTerms.Keys.ToArray();
                result.FinalQuery = expandedQuery.ExpandedLuceneQuery;
                
                Logger.LogDebug("Query expanded from '{Original}' to '{Final}' with {TermCount} terms", 
                    originalQuery, result.FinalQuery, expandedQuery.WeightedTerms.Count);
            }

            // Step 2: Context Awareness
            if (enableContext)
            {
                // Update context tracking
                if (!string.IsNullOrEmpty(currentFile))
                {
                    await _contextAwareness.UpdateCurrentFileAsync(currentFile);
                }

                if (recentFiles != null)
                {
                    foreach (var file in recentFiles)
                    {
                        await _contextAwareness.TrackFileAccessAsync(file);
                    }
                }

                // Get current context
                var context = await _contextAwareness.GetCurrentContextAsync();
                result.ContextKeywords = context.ContextKeywords;

                // Apply context boosts (this would integrate with Lucene scoring)
                if (result.ExpandedTerms.Length > 0)
                {
                    var contextBoosts = _contextAwareness.GetContextBoosts(context, result.ExpandedTerms);
                    result.ContextBoosts = contextBoosts;
                    
                    Logger.LogDebug("Applied context boosts to {TermCount} terms, max boost: {MaxBoost:F2}", 
                        contextBoosts.Count, contextBoosts.Values.DefaultIfEmpty(1.0f).Max());
                }
            }

            // Track this search for future context
            // Note: ResultsFound will be updated after the actual search
            await _contextAwareness.TrackSearchQueryAsync(originalQuery, 0);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error processing intelligent query, falling back to original");
            return result; // Return with original query on error
        }
    }

    /// <summary>
    /// Result of intelligent query processing
    /// </summary>
    private class ProcessedQueryResult
    {
        public string OriginalQuery { get; set; } = string.Empty;
        public string FinalQuery { get; set; } = string.Empty;
        public string[] ExpandedTerms { get; set; } = Array.Empty<string>();
        public string[] ContextKeywords { get; set; } = Array.Empty<string>();
        public Dictionary<string, float> ContextBoosts { get; set; } = new();
    }

}