using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for searching text content across indexed files with full token optimization
/// </summary>
public class TextSearchTool_V2 : McpToolBase<TextSearchParameters_V2, AIOptimizedResponse>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly ILogger<TextSearchTool_V2> _logger;

    public TextSearchTool_V2(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        ICacheKeyGenerator keyGenerator,
        ITokenEstimator tokenEstimator,
        ILogger<TextSearchTool_V2> logger,
        ILogger<SearchResponseBuilder> builderLogger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new SearchResponseBuilder(tokenEstimator, builderLogger);
        _logger = logger;
    }

    public override string Name => ToolNames.TextSearch + "_v2";
    public override string Description => "Search for text content across all indexed files with token-optimized responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse> ExecuteInternalAsync(
        TextSearchParameters_V2 parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var query = ValidateRequired(parameters.Query, nameof(parameters.Query));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Validate max results
        var maxResults = parameters.MaxResults ?? 50;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Try cache first if not forcing refresh
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse>(cacheKey, cancellationToken);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached search results for query: {Query}", query);
                cached.Meta ??= new AIResponseMeta();
                cached.Meta.CacheHit = true;
                return cached;
            }
        }
        
        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return new AIOptimizedResponse
                {
                    Operation = Name,
                    Success = false,
                    Error = CreateValidationErrorResult(
                        Name,
                        nameof(parameters.WorkspacePath),
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
            }
            
            // Parse the query string into a Lucene Query
            var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            var queryParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            Lucene.Net.Search.Query luceneQuery;
            
            try
            {
                luceneQuery = queryParser.Parse(query);
            }
            catch (ParseException ex)
            {
                return new AIOptimizedResponse
                {
                    Operation = Name,
                    Success = false,
                    Error = CreateValidationErrorResult(
                        Name,
                        nameof(parameters.Query),
                        $"Invalid query syntax: {ex.Message}"
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
            }
            
            // Perform search
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                luceneQuery,
                maxResults,
                cancellationToken);
            
            // Build response with token optimization
            var context = new ResponseContext
            {
                ToolName = Name,
                ResponseMode = parameters.ResponseMode ?? "summary",
                TokenLimit = parameters.MaxTokens,
                UserQuery = query,
                Parameters = new Dictionary<string, object>
                {
                    { "workspacePath", workspacePath },
                    { "maxResults", maxResults }
                }
            };
            
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);
            
            // Cache the response
            if (!parameters.NoCache && response is AIOptimizedResponse aiResponse && aiResponse.Success)
            {
                await _cacheService.SetAsync(
                    cacheKey, 
                    aiResponse, 
                    TimeSpan.FromMinutes(15), 
                    cancellationToken);
                
                _logger.LogDebug("Cached search results for query: {Query}", query);
            }
            
            return (AIOptimizedResponse)response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query: {Query}", query);
            return new AIOptimizedResponse
            {
                Operation = Name,
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "SEARCH_ERROR",
                    Message = $"Error performing search: {ex.Message}"
                },
                Data = new AIResponseData
                {
                    Summary = "Search failed",
                    Results = new List<object>(),
                    Count = 0
                }
            };
        }
    }
}

/// <summary>
/// Parameters for the enhanced TextSearch tool
/// </summary>
public class TextSearchParameters_V2
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
    /// Maximum number of results to return (default: 50, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 50, max: 500)")]
    public int? MaxResults { get; set; }

    /// <summary>
    /// Response mode: 'summary' (default) or 'full'
    /// </summary>
    [Description("Response mode: 'summary' (default) or 'full'")]
    public string? ResponseMode { get; set; }

    /// <summary>
    /// Maximum tokens for response (for token optimization)
    /// </summary>
    [Description("Maximum tokens for response (default: 5000)")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Skip cache and force fresh search
    /// </summary>
    [Description("Skip cache and force fresh search")]
    public bool NoCache { get; set; }
}