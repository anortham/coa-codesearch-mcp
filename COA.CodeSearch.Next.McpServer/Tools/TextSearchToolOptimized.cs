using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using COA.CodeSearch.Next.McpServer.Models;
using Microsoft.Extensions.Logging;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Optimized text search tool with full framework feature utilization
/// </summary>
public class TextSearchToolOptimized : McpToolBase<TextSearchParametersOptimized, TokenOptimizedResult>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger<TextSearchToolOptimized> _logger;

    // Token budget constants
    private const int SUMMARY_TOKEN_BUDGET = 5000;
    private const int FULL_TOKEN_BUDGET = 50000;
    private const int BASE_RESPONSE_OVERHEAD = 500;
    private const int TYPICAL_ITEM_TOKENS = 100;

    public TextSearchToolOptimized(
        ILuceneIndexService luceneIndexService,
        ITokenEstimator tokenEstimator,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<TextSearchToolOptimized> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _tokenEstimator = tokenEstimator;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _logger = logger;
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "Search for text content with intelligent token optimization and caching";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<TokenOptimizedResult> ExecuteInternalAsync(
        TextSearchParametersOptimized parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var query = ValidateRequired(parameters.Query, nameof(parameters.Query));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<TokenOptimizedResult>(cacheKey);
            if (cached != null && cached is TokenOptimizedResult cachedResult)
            {
                _logger.LogDebug("Returning cached search results for query: {Query}", query);
                cachedResult.Meta ??= new AIResponseMeta();
                if (cachedResult.Meta.ExtensionData == null)
                    cachedResult.Meta.ExtensionData = new Dictionary<string, object>();
                cachedResult.Meta.ExtensionData["cacheHit"] = true;
                return cachedResult;
            }
        }

        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateNoIndexError(workspacePath);
            }

            // Parse query
            var luceneQuery = ParseQuery(query);
            if (luceneQuery == null)
            {
                return CreateQueryParseError(query);
            }

            // Determine response mode and token budget
            var responseMode = DetermineResponseMode(parameters);
            var tokenBudget = GetTokenBudget(responseMode, parameters.MaxTokens);

            // Perform search with appropriate limit
            var maxResults = CalculateMaxResults(tokenBudget);
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                luceneQuery, 
                maxResults, 
                cancellationToken);

            // Build optimized response
            var response = await BuildOptimizedResponseAsync(
                searchResult,
                query,
                workspacePath,
                responseMode,
                tokenBudget,
                cancellationToken);

            // Cache the successful response
            if (!parameters.NoCache && response.Success)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(15)
                });
                _logger.LogDebug("Cached search results for query: {Query}", query);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query: {Query}", query);
            return TokenOptimizedResult.CreateError(Name, new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "SEARCH_ERROR",
                Message = $"Error performing search: {ex.Message}"
            });
        }
    }

    private Lucene.Net.Search.Query? ParseQuery(string query)
    {
        try
        {
            var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            var queryParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            return queryParser.Parse(query);
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Failed to parse query: {Query}", query);
            return null;
        }
    }

    private ResponseMode DetermineResponseMode(TextSearchParametersOptimized parameters)
    {
        // Honor explicit mode if specified
        if (!string.IsNullOrEmpty(parameters.ResponseMode))
        {
            return parameters.ResponseMode.ToLowerInvariant() == "full" 
                ? ResponseMode.Full 
                : ResponseMode.Summary;
        }

        // Default to summary for better token efficiency
        return ResponseMode.Summary;
    }

    private int GetTokenBudget(ResponseMode mode, int? requestedLimit)
    {
        if (requestedLimit.HasValue)
        {
            // Use requested limit but cap at framework maximums
            return Math.Min(requestedLimit.Value, 
                mode == ResponseMode.Full ? FULL_TOKEN_BUDGET : SUMMARY_TOKEN_BUDGET);
        }

        return mode == ResponseMode.Full ? FULL_TOKEN_BUDGET : SUMMARY_TOKEN_BUDGET;
    }

    private int CalculateMaxResults(int tokenBudget)
    {
        // Estimate how many results we can fit
        // Reserve tokens for response structure, insights, actions
        var availableForResults = (int)(tokenBudget * 0.7); // 70% for results
        var estimatedResultsCount = availableForResults / TYPICAL_ITEM_TOKENS;
        
        // Cap at reasonable limits
        return Math.Min(estimatedResultsCount, 500);
    }

    private async Task<TokenOptimizedResult> BuildOptimizedResponseAsync(
        SearchResult searchResult,
        string query,
        string workspacePath,
        ResponseMode mode,
        int tokenBudget,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // Apply progressive reduction if needed
        var (reducedHits, wasTruncated, resourceUri) = await ApplyProgressiveReductionAsync(
            searchResult.Hits,
            tokenBudget,
            cancellationToken);

        // Generate insights based on results
        var insights = GenerateInsights(
            searchResult.TotalHits,
            reducedHits.Count,
            wasTruncated,
            mode);

        // Generate suggested actions
        var actions = GenerateActions(
            query,
            searchResult.TotalHits,
            reducedHits.Count,
            wasTruncated,
            resourceUri);

        // Build the response
        var response = new TokenOptimizedResult
        {
            Success = true,
            Format = "ai-optimized",
            Data = new AIResponseData
            {
                Summary = BuildSummary(searchResult.TotalHits, reducedHits.Count, wasTruncated),
                Results = FormatSearchResults(reducedHits),
                Count = reducedHits.Count
            },
            Insights = insights,
            Actions = actions,
            Meta = new AIResponseMeta
            {
                ExecutionTime = (DateTime.UtcNow - startTime).TotalMilliseconds + "ms",
                Truncated = wasTruncated,
                ResourceUri = resourceUri,
                TokenInfo = new TokenInfo
                {
                    Estimated = 0, // Will be calculated below
                    Limit = tokenBudget,
                    ReductionStrategy = mode.ToString().ToLowerInvariant()
                }
            }
        };

        // Calculate actual token usage
        response.Meta.TokenInfo.Estimated = _tokenEstimator.EstimateObject(response);

        _logger.LogInformation(
            "Built optimized search response: {Hits}/{Total} results, {Tokens} tokens, Mode: {Mode}",
            reducedHits.Count, searchResult.TotalHits, response.Meta.TokenInfo.Estimated, mode);

        return response;
    }

    private async Task<(List<SearchHit> hits, bool wasTruncated, string? resourceUri)> 
        ApplyProgressiveReductionAsync(
            List<SearchHit> allHits,
            int tokenBudget,
            CancellationToken cancellationToken)
    {
        // First, estimate token usage for all hits
        var fullEstimate = _tokenEstimator.EstimateCollection(
            allHits,
            hit => EstimateSearchHit(hit));

        // If within budget, return all
        if (fullEstimate <= tokenBudget)
        {
            return (allHits, false, null);
        }

        // Apply progressive reduction
        var reducedHits = new List<SearchHit>();
        var currentTokens = BASE_RESPONSE_OVERHEAD;
        
        // Sort by relevance score to keep best results
        var sortedHits = allHits.OrderByDescending(h => h.Score).ToList();

        foreach (var hit in sortedHits)
        {
            var hitTokens = EstimateSearchHit(hit);
            if (currentTokens + hitTokens <= tokenBudget * 0.7) // Reserve 30% for metadata
            {
                reducedHits.Add(TruncateSearchHit(hit));
                currentTokens += hitTokens;
            }
            else
            {
                break;
            }
        }

        // Store full results if significantly truncated
        string? resourceUri = null;
        if (reducedHits.Count < allHits.Count / 2) // Less than half included
        {
            var storageUri = await _storageService.StoreAsync(
                allHits,
                new ResourceStorageOptions
                {
                    Expiration = TimeSpan.FromHours(1),
                    Compress = true
                });
            resourceUri = storageUri.ToString();
            
            _logger.LogDebug("Stored {Count} full results at {Uri}", allHits.Count, resourceUri);
        }

        return (reducedHits, true, resourceUri);
    }

    private int EstimateSearchHit(SearchHit hit)
    {
        var tokens = 50; // Base structure
        tokens += _tokenEstimator.EstimateString(hit.FilePath);
        tokens += _tokenEstimator.EstimateString(hit.Content ?? "");
        tokens += hit.HighlightedFragments?.Count * 20 ?? 0;
        return tokens;
    }

    private SearchHit TruncateSearchHit(SearchHit hit)
    {
        const int maxContentLength = 300;
        
        return new SearchHit
        {
            FilePath = hit.FilePath,
            Score = hit.Score,
            Content = TruncateContent(hit.Content, maxContentLength),
            Fields = hit.Fields,
            HighlightedFragments = hit.HighlightedFragments?.Take(2).ToList()
        };
    }

    private string? TruncateContent(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        var truncated = content.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.8)
        {
            truncated = truncated.Substring(0, lastSpace);
        }
        return truncated + "...";
    }

    private string BuildSummary(int totalHits, int displayedHits, bool wasTruncated)
    {
        if (totalHits == 0)
            return "No results found.";

        if (!wasTruncated)
            return $"Found {totalHits} result{(totalHits == 1 ? "" : "s")}.";

        return $"Showing top {displayedHits} of {totalHits} results (limited for token efficiency).";
    }

    private List<object> FormatSearchResults(List<SearchHit> hits)
    {
        return hits.Select(hit => new
        {
            file = hit.FilePath,
            score = Math.Round(hit.Score, 3),
            content = hit.Content,
            highlights = hit.HighlightedFragments
        }).Cast<object>().ToList();
    }

    private List<string> GenerateInsights(int totalHits, int displayedHits, bool wasTruncated, ResponseMode mode)
    {
        var insights = new List<string>();

        if (totalHits == 0)
        {
            insights.Add("No results found. Try broadening your search criteria.");
            insights.Add("Ensure the workspace is properly indexed.");
        }
        else if (totalHits == 1)
        {
            insights.Add("Found exactly one match.");
        }
        else if (totalHits > 100)
        {
            insights.Add($"Large result set ({totalHits} matches). Consider refining your search.");
        }

        if (wasTruncated)
        {
            insights.Add($"Results limited to {displayedHits} for token efficiency. Use resource URI for full results.");
        }

        if (mode == ResponseMode.Summary)
        {
            insights.Add("Summary mode active. Use 'full' mode for more detailed results.");
        }

        return insights.Take(5).ToList(); // Limit insights
    }

    private List<AIAction> GenerateActions(
        string query, 
        int totalHits, 
        int displayedHits, 
        bool wasTruncated,
        string? resourceUri)
    {
        var actions = new List<AIAction>();

        if (wasTruncated && !string.IsNullOrEmpty(resourceUri))
        {
            actions.Add(new AIAction
            {
                Action = "retrieve_full_results",
                Description = $"Get all {totalHits} results from storage",
                Rationale = "Full results are available in resource storage",
                Priority = 10,
                Parameters = new Dictionary<string, object>
                {
                    { "resourceUri", resourceUri }
                }
            });
        }

        if (totalHits > 50)
        {
            actions.Add(new AIAction
            {
                Action = Name,
                Description = "Refine search with more specific terms",
                Rationale = $"Current search returned {totalHits} results",
                Priority = 8,
                Parameters = new Dictionary<string, object>
                {
                    { "query", query + " AND specific_term" }
                }
            });
        }

        if (totalHits == 0)
        {
            actions.Add(new AIAction
            {
                Action = Name,
                Description = "Try a broader search",
                Rationale = "No results found with current query",
                Priority = 9,
                Parameters = new Dictionary<string, object>
                {
                    { "query", query.Split(' ').FirstOrDefault() ?? "*" }
                }
            });
        }

        return actions.Take(3).ToList(); // Limit actions
    }

    private TokenOptimizedResult CreateNoIndexError(string workspacePath)
    {
        var result = new TokenOptimizedResult
        {
            Success = false,
            Error = CreateValidationErrorResult(
                Name,
                "WorkspacePath",
                $"No index found for workspace: {workspacePath}. Run index_workspace first."
            ),
            Data = new AIResponseData
            {
                Summary = "No index found for workspace",
                Results = new List<object>(),
                Count = 0
            },
            Insights = new List<string> { "Workspace needs to be indexed before searching." },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = ToolNames.IndexWorkspace,
                    Description = "Index the workspace to enable search",
                    Rationale = "Search requires an indexed workspace",
                    Priority = 10,
                    Parameters = new Dictionary<string, object>
                    {
                        { "workspacePath", workspacePath }
                    }
                }
            }
        };
        result.SetOperation(Name);
        return result;
    }

    private TokenOptimizedResult CreateQueryParseError(string query)
    {
        var result = new TokenOptimizedResult
        {
            Success = false,
            Error = CreateValidationErrorResult(
                Name,
                "Query",
                "Invalid query syntax. Check for unmatched quotes or invalid operators."
            ),
            Data = new AIResponseData
            {
                Summary = "Invalid query syntax",
                Results = new List<object>(),
                Count = 0
            },
            Insights = new List<string> 
            { 
                "Query contains invalid Lucene syntax.",
                "Try escaping special characters or simplifying the query."
            }
        };
        result.SetOperation(Name);
        return result;
    }

    private enum ResponseMode
    {
        Summary,
        Full
    }
}

/// <summary>
/// Parameters for the optimized text search tool
/// </summary>
public class TextSearchParametersOptimized
{
    /// <summary>
    /// The search query string
    /// </summary>
    [Required]
    [Description("The search query string")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Response mode: 'summary' (default) or 'full'
    /// </summary>
    [Description("Response mode: 'summary' (default) or 'full'")]
    public string? ResponseMode { get; set; }

    /// <summary>
    /// Maximum tokens for response (will be capped at framework limits)
    /// </summary>
    [Description("Maximum tokens for response")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Skip cache and force fresh search
    /// </summary>
    [Description("Skip cache and force fresh search")]
    public bool NoCache { get; set; }
}