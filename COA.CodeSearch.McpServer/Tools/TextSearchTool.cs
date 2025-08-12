using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Scoring;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Text search tool using the BaseResponseBuilder pattern for consistent response building
/// </summary>
public class TextSearchTool : McpToolBase<TextSearchParameters, AIOptimizedResponse<SearchResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly ILogger<TextSearchTool> _logger;

    public TextSearchTool(
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        QueryPreprocessor queryPreprocessor,
        ILogger<TextSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryPreprocessor = queryPreprocessor;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(null, storageService);
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "Search for text content using BaseResponseBuilder pattern for consistent responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<SearchResult>> ExecuteInternalAsync(
        TextSearchParameters parameters,
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
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached search results for query: {Query}", query);
                cached.Meta ??= new AIResponseMeta();
                if (cached.Meta.ExtensionData == null)
                    cached.Meta.ExtensionData = new Dictionary<string, object>();
                cached.Meta.ExtensionData["cacheHit"] = true;
                return cached;
            }
        }

        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateNoIndexError(workspacePath);
            }

            // Validate and preprocess query
            var searchType = parameters.SearchType ?? "standard";
            if (!_queryPreprocessor.IsValidQuery(query, searchType, out var errorMessage))
            {
                return CreateQueryParseError(query, errorMessage);
            }

            // Build query with proper preprocessing
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var luceneQuery = _queryPreprocessor.BuildQuery(query, searchType, parameters.CaseSensitive, analyzer);
            
            // Apply scoring factors for better relevance
            var scoringContext = new ScoringContext
            {
                QueryText = query,
                SearchType = searchType,
                WorkspacePath = workspacePath
            };
            
            var multiFactorQuery = new MultiFactorScoreQuery(luceneQuery, scoringContext, _logger);
            
            // Add scoring factors - these dramatically improve search relevance
            multiFactorQuery.AddScoringFactor(new PathRelevanceFactor(_logger)); // Deboosting test files
            multiFactorQuery.AddScoringFactor(new FilenameRelevanceFactor());    // Boosting filename matches
            multiFactorQuery.AddScoringFactor(new FileTypeRelevanceFactor());    // Prioritize code files
            multiFactorQuery.AddScoringFactor(new RecencyBoostFactor());         // Boost recently modified
            multiFactorQuery.AddScoringFactor(new ExactMatchBoostFactor(parameters.CaseSensitive)); // Exact phrase matches
            multiFactorQuery.AddScoringFactor(new InterfaceImplementationFactor(_logger)); // Reduce mock/test noise for interface searches

            // Implement aggressive token-aware limiting like the old system
            // The old system targeted ~1500 tokens with ~5 results for maximum relevance
            var responseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
            
            // Estimate tokens per result (old system used ~300 tokens per result as baseline)
            var hasContext = false; // We don't use context in this tool
            var tokensPerResult = hasContext ? 200 : 100; 
            
            // Calculate token budget (be conservative like old system)
            var tokenBudget = parameters.MaxTokens;
            var safetyBudget = (int)Math.Min(tokenBudget * 0.4, 2000); // Use only 40% of budget, max 2000 tokens for results
            
            // Calculate max results based on token budget
            var budgetBasedMax = Math.Max(1, safetyBudget / tokensPerResult);
            
            // Apply mode-specific limits (but respect budget limits)
            var maxResults = responseMode switch
            {
                "full" => Math.Min(budgetBasedMax, 10),     // Full mode: reduced from 15
                "summary" => Math.Min(budgetBasedMax, 2),   // Summary: ultra-lean - just 2 results
                _ => Math.Min(budgetBasedMax, 3)            // Default: lean - just top 3 results
            };
            
            _logger.LogDebug("Token-aware search limits: budget={Budget}, tokensPerResult={TokensPerResult}, maxResults={MaxResults}, mode={Mode}, Query={Query}", 
                safetyBudget, tokensPerResult, maxResults, responseMode, query);

            // Perform search with scoring
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                multiFactorQuery,  // Use the multi-factor query instead of plain query
                maxResults, 
                cancellationToken);
            
            // Add query to result for insights
            searchResult.Query = query;

            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = responseMode,
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };

            // Use response builder to create optimized response
            var result = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Cache the successful response
            if (!parameters.NoCache && result.Success)
            {
                await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(15),
                    Priority = searchResult.TotalHits > 100 ? CachePriority.High : CachePriority.Normal
                });
                _logger.LogDebug("Cached search results for query: {Query}", query);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query: {Query}", query);
            return new AIOptimizedResponse<SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "SEARCH_ERROR",
                    Message = $"Error performing search: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the query syntax is valid",
                            "Check if the workspace is properly indexed",
                            "Try a simpler query",
                            "Check logs for detailed error information"
                        }
                    }
                }
            };
        }
    }

    private Lucene.Net.Search.Query? ParseQuery(string query)
    {
        try
        {
            var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            var queryParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            queryParser.DefaultOperator = QueryParserBase.AND_OPERATOR; // More precise results
            return queryParser.Parse(query);
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Failed to parse query: {Query}", query);
            return null;
        }
    }

    private AIOptimizedResponse<SearchResult> CreateNoIndexError(string workspacePath)
    {
        var result = new AIOptimizedResponse<SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "NO_INDEX",
                Message = $"No index found for workspace: {workspacePath}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        $"Run index_workspace tool to create the index",
                        "Verify the workspace path is correct",
                        "Check if you have read permissions for the workspace"
                    }
                }
            },
            Insights = new List<string>
            {
                "The workspace needs to be indexed before searching",
                "Indexing creates a searchable database of file contents"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = ToolNames.IndexWorkspace,
                    Description = "Create search index for this workspace",
                    Priority = 100
                }
            }
        };
        return result;
    }

    private AIOptimizedResponse<SearchResult> CreateQueryParseError(string query, string? customMessage = null)
    {
        var result = new AIOptimizedResponse<SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "INVALID_QUERY",
                Message = customMessage ?? $"Could not parse search query: {query}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Use a more specific search pattern (3+ characters)",
                        "For operators, use: =>, ??, ?., ::, ->, +=, -=, ==, !=, >=, <=, &&, ||, <<, >>",
                        "Try different search types: literal, code, wildcard, fuzzy, phrase, regex",
                        "Check for unmatched quotes or parentheses"
                    }
                }
            },
            Insights = new List<string>
            {
                "The query contains invalid syntax",
                "Common issues: unmatched quotes, invalid operators, special characters"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = "simplify_query",
                    Description = "Try a simpler query without special operators",
                    Priority = 90
                },
                new AIAction
                {
                    Action = "quote_phrase",
                    Description = "Put phrases in quotes for exact matching",
                    Priority = 80
                }
            }
        };
        return result;
    }
}