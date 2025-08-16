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
using COA.VSCodeBridge.Extensions;
using COA.VSCodeBridge.Models;
using COA.Mcp.Framework.Interfaces;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Text search tool using the BaseResponseBuilder pattern for consistent response building
/// </summary>
public class TextSearchTool : McpToolBase<TextSearchParameters, AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>, IVisualizationCapable
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly IProjectKnowledgeService _projectKnowledgeService;
    private readonly SmartDocumentationService _smartDocumentationService;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<TextSearchTool> _logger;

    public TextSearchTool(
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        QueryPreprocessor queryPreprocessor,
        IProjectKnowledgeService projectKnowledgeService,
        SmartDocumentationService smartDocumentationService,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<TextSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryPreprocessor = queryPreprocessor;
        _projectKnowledgeService = projectKnowledgeService;
        _smartDocumentationService = smartDocumentationService;
        _vscode = vscode;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(null, storageService);
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "Search for text content using BaseResponseBuilder pattern for consistent responses";
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the default visualization configuration for text search
    /// Text search is an opt-in visualization tool since it's called frequently
    /// </summary>
    public VisualizationConfig GetDefaultVisualizationConfig() => new()
    {
        ShowByDefault = false,              // Opt-in only - too frequent otherwise
        PreferredView = "grid",             // Interactive grid with clickable files
        Priority = VisualizationPriority.OnRequest,
        ConsolidateTabs = true,             // Replace previous search results
        MaxConcurrentTabs = 1,              // One search visualization at a time
        NavigateToFirstResult = false       // Don't auto-navigate, let user choose
    };

    protected override async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> ExecuteInternalAsync(
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
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>(cacheKey);
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
            // Always include snippets for better context in results
            var includeSnippets = true;  // Always generate snippets for rich results
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                multiFactorQuery,  // Use the multi-factor query instead of plain query
                maxResults,
                includeSnippets,
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

            // NEW: Smart visualization using IVisualizationCapable interface
            _logger.LogInformation("VS Code Bridge Status - IsConnected: {IsConnected}, Success: {Success}, TotalHits: {TotalHits}", 
                _vscode.IsConnected, result.Success, searchResult.TotalHits);
            
            // Show in VS Code only when explicitly requested
            if (result.Success && searchResult.TotalHits > 0 && (parameters.ShowInVSCode ?? false))
            {
                // Fire and forget - don't block the main response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _vscode.HandleSmartVisualizationAsync(
                            this,           // IVisualizationCapable tool
                            parameters,     // VisualizableParameters with user overrides
                            searchResult,   // Result to visualize
                            _logger,        // Logger for diagnostics
                            cancellationToken);
                        _logger.LogInformation("Successfully handled smart visualization for query: {Query}", query);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to handle smart visualization for query: {Query}", query);
                    }
                }, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Skipping visualization - ShowInVSCode={ShowInVSCode}, IsConnected={IsConnected}, Success={Success}, TotalHits={TotalHits}", 
                    parameters.ShowInVSCode, _vscode.IsConnected, result.Success, searchResult.TotalHits);
            }

            // Auto-documentation: store findings in ProjectKnowledge if enabled
            if (parameters.DocumentFindings && result.Success && searchResult.TotalHits > 0)
            {
                await DocumentSearchFindingsAsync(parameters, searchResult, query);
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
            return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
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

    /// <summary>
    /// Send search results to VS Code as interactive data grid visualization
    /// </summary>
    private async Task SendSearchVisualizationsAsync(TextSearchParameters parameters, COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string query)
    {
        try
        {
            // Convert search results to SearchResult format for VS Code Bridge
            var searchResults = searchResult.Hits?.Select(hit => new COA.VSCodeBridge.Extensions.SearchResult(
                FilePath: hit.FilePath,
                Line: hit.LineNumber ?? 1, // Will be calculated from term vectors soon
                Score: hit.Score,
                Preview: hit.Snippet ?? ""
            )).ToList() ?? new List<COA.VSCodeBridge.Extensions.SearchResult>();

            // 1. Show search results as markdown with code context and clickable links
            await _vscode.ShowSearchResultsAsMarkdownAsync(
                searchResults,
                query,
                $"Search: \"{query}\" ({searchResult.TotalHits} found)"
            );

            _logger.LogDebug("Successfully sent search markdown visualization to VS Code for query: {Query}", query);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send search visualizations for query: {Query}", query);
            // Don't throw - visualization failure shouldn't break the main search functionality
        }
    }

    /// <summary>
    /// Calculate search metrics for visualization
    /// </summary>
    private Dictionary<string, double> CalculateSearchMetrics(COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult)
    {
        var fileTypes = new Dictionary<string, int>();
        
        if (searchResult.Hits != null)
        {
            foreach (var hit in searchResult.Hits)
            {
                var extension = Path.GetExtension(hit.FilePath)?.ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                    extension = "no-ext";
                else
                    extension = extension.TrimStart('.');
                
                fileTypes[extension] = fileTypes.TryGetValue(extension, out var count) ? count + 1 : 1;
            }
        }
        
        return fileTypes.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value);
    }

    /// <summary>
    /// Generate search insights for markdown display
    /// </summary>
    private string GenerateSearchInsights(COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, TextSearchParameters parameters)
    {
        var insights = new System.Text.StringBuilder();
        
        insights.AppendLine($"# Search Analysis");
        insights.AppendLine();
        insights.AppendLine($"**Query:** `{searchResult.Query}`");
        insights.AppendLine($"**Total Results:** {searchResult.TotalHits}");
        insights.AppendLine($"**Search Type:** {parameters.SearchType ?? "standard"}");
        insights.AppendLine($"**Case Sensitive:** {parameters.CaseSensitive}");
        insights.AppendLine();
        
        if (searchResult.Hits?.Any() == true)
        {
            insights.AppendLine("## Top Results");
            insights.AppendLine();
            
            var topResults = searchResult.Hits.Take(5);
            foreach (var hit in topResults)
            {
                insights.AppendLine($"- **{Path.GetFileName(hit.FilePath)}** (Score: {hit.Score:F2})");
                if (!string.IsNullOrEmpty(hit.Snippet))
                {
                    insights.AppendLine($"  ```");
                    insights.AppendLine($"  {hit.Snippet.Trim()}");
                    insights.AppendLine($"  ```");
                }
                insights.AppendLine();
            }
        }
        
        return insights.ToString();
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

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateNoIndexError(string workspacePath)
    {
        var result = new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
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

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateQueryParseError(string query, string? customMessage = null)
    {
        var result = new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
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

    /// <summary>
    /// Document search findings in ProjectKnowledge based on intelligent pattern detection
    /// </summary>
    private async Task DocumentSearchFindingsAsync(TextSearchParameters parameters, COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string query)
    {
        try
        {
            // Extract file paths from search results
            var filePaths = searchResult.Hits?.Select(h => h.FilePath).ToArray();
            
            // Get documentation recommendation from smart service
            var recommendation = _smartDocumentationService.AnalyzeSearchResults(
                query, 
                searchResult.TotalHits, 
                filePaths
            );

            if (!recommendation.ShouldDocument)
            {
                _logger.LogDebug("No documentation recommended for query: {Query}", query);
                return;
            }

            // Use explicit FindingType if provided, otherwise use recommendation
            var knowledgeType = parameters.FindingType ?? recommendation.KnowledgeType ?? "TechnicalDebt";
            var content = recommendation.Content ?? $"Found {searchResult.TotalHits} instances of '{query}'";
            var tags = recommendation.Tags ?? new[] { "codesearch-auto", "investigation" };
            var priority = recommendation.Priority ?? "medium";

            // Add search context to metadata - all values must be strings for ProjectKnowledge
            var metadata = recommendation.Metadata ?? new Dictionary<string, object>();
            metadata["workspace"] = Path.GetFileName(parameters.WorkspacePath);  // Project name, not full path
            metadata["searchQuery"] = query;
            metadata["resultCount"] = searchResult.TotalHits.ToString();  // Convert to string
            metadata["workspacePath"] = parameters.WorkspacePath;
            metadata["searchType"] = parameters.SearchType;
            metadata["caseSensitive"] = parameters.CaseSensitive.ToString();  // Convert to string

            // Store in ProjectKnowledge
            var knowledgeId = await _projectKnowledgeService.StoreKnowledgeAsync(
                content,
                knowledgeType,
                metadata,
                tags,
                priority
            );

            if (knowledgeId != null)
            {
                _logger.LogInformation("Auto-documented search findings: Query='{Query}', Type={Type}, KnowledgeId={Id}", 
                    query, knowledgeType, knowledgeId);
            }
            else
            {
                _logger.LogWarning("Failed to auto-document search findings for query: {Query}", query);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-documenting search findings for query: {Query}", query);
            // Don't throw - documentation failure shouldn't break search
        }
    }
}