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
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Text search tool using the BaseResponseBuilder pattern for consistent response building
/// </summary>
public class TextSearchTool : McpToolBase<TextSearchParameters, TokenOptimizedResult>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly ILogger<TextSearchTool> _logger;

    public TextSearchTool(
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<TextSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(null, storageService);
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "Search for text content using BaseResponseBuilder pattern for consistent responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<TokenOptimizedResult> ExecuteInternalAsync(
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
            var cached = await _cacheService.GetAsync<TokenOptimizedResult>(cacheKey);
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

            // Parse query
            var luceneQuery = ParseQuery(query);
            if (luceneQuery == null)
            {
                return CreateQueryParseError(query);
            }

            // Determine max results based on response mode to protect token limits
            // We intentionally don't let users control this directly to prevent token blowouts
            var responseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
            var maxResults = responseMode switch
            {
                "full" => 100,     // Even in full mode, cap at 100 for safety
                "summary" => 20,   // Summary mode gets fewer results
                _ => 50            // Adaptive/default: moderate amount
            };
            
            _logger.LogDebug("Text search using ResponseMode-based MaxResults: {MaxResults} (mode: {ResponseMode}), Query: {Query}", 
                maxResults, responseMode, query);

            // Perform search
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                luceneQuery, 
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
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);
            
            // Convert to TokenOptimizedResult
            var result = response as TokenOptimizedResult;
            if (result == null)
            {
                // This shouldn't happen, but handle gracefully
                result = new TokenOptimizedResult
                {
                    Success = true,
                    Data = new AIResponseData
                    {
                        Summary = $"Found {searchResult.TotalHits} results",
                        Results = searchResult.Hits,
                        Count = searchResult.Hits.Count
                    }
                };
                result.SetOperation(Name);
            }

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
            return TokenOptimizedResult.CreateError(Name, new COA.Mcp.Framework.Models.ErrorInfo
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
            });
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

    private TokenOptimizedResult CreateNoIndexError(string workspacePath)
    {
        var result = new TokenOptimizedResult
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
        result.SetOperation(Name);
        return result;
    }

    private TokenOptimizedResult CreateQueryParseError(string query)
    {
        var result = new TokenOptimizedResult
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "INVALID_QUERY",
                Message = $"Could not parse search query: {query}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Check for unmatched quotes or parentheses",
                        "Escape special characters with backslash",
                        "Use simpler query syntax",
                        "Refer to Lucene query syntax documentation"
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
        result.SetOperation(Name);
        return result;
    }
}