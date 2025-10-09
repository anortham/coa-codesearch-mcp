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
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Scoring;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using System.Text;
using System.Text.Json;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Index;
using Lucene.Net.Util;
using Lucene.Net.QueryParsers.Classic;
using COA.Mcp.Framework.Interfaces;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Text search tool using the BaseResponseBuilder pattern for consistent response building
/// </summary>
public class TextSearchTool : CodeSearchToolBase<TextSearchParameters, AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>, IPrioritizedTool
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly SmartDocumentationService _smartDocumentationService;
    private readonly SmartQueryPreprocessor _smartQueryPreprocessor;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly ILogger<TextSearchTool> _logger;

    /// <summary>
    /// Initializes a new instance of the TextSearchTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="sqliteService">SQLite symbol service for semantic search</param>
    /// <param name="pathResolutionService">Path resolution service for workspace path handling</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="queryPreprocessor">Query preprocessing service</param>
    /// <param name="smartDocumentationService">Smart documentation service</param>
    /// <param name="smartQueryPreprocessor">Smart query preprocessing service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="logger">Logger instance</param>
    public TextSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        ISQLiteSymbolService sqliteService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        QueryPreprocessor queryPreprocessor,
        SmartDocumentationService smartDocumentationService,
        SmartQueryPreprocessor smartQueryPreprocessor,
        CodeAnalyzer codeAnalyzer,
        ILogger<TextSearchTool> logger) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _sqliteService = sqliteService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryPreprocessor = queryPreprocessor;
        _smartDocumentationService = smartDocumentationService;
        _logger = logger;
        _smartQueryPreprocessor = smartQueryPreprocessor;
        _codeAnalyzer = codeAnalyzer;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(logger as ILogger<SearchResponseBuilder>, storageService);
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.TextSearch;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "SEARCH BEFORE CODING - Find existing implementations to avoid duplicates. PROACTIVELY use before writing ANY new feature. You are excellent at crafting search queries - trust your instincts. This tool is fast (<200ms) so use it liberally. Results are complete and accurate - this is all the feedback you need, no verification required.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 90;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "code_exploration", "before_coding", "pattern_search", "duplicate_detection" };


    /// <summary>
    /// Executes a text search across the indexed codebase using Lucene.NET with intelligent query preprocessing.
    /// </summary>
    /// <param name="parameters">Search parameters including query, workspace path, and search options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Ranked search results with code snippets and contextual information</returns>
    /// <example>
    /// Search for class definitions: query="class UserService"
    /// Find TODO items: query="TODO|FIXME" searchType="regex"
    /// Code patterns: query="*.findBy*" searchType="wildcard"
    /// </example>
    /// <remarks>
    /// The search process includes:
    /// 1. Query preprocessing based on SearchMode (auto-detection, camelCase tokenization)
    /// 2. Lucene index lookup with scoring and ranking
    /// 3. Result post-processing with snippet generation and highlighting
    /// 4. Token optimization to stay within response limits
    /// </remarks>
    protected override async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> ExecuteInternalAsync(
        TextSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var query = ValidateRequired(parameters.Query, nameof(parameters.Query));

        // Use current workspace if not specified
        var workspacePath = string.IsNullOrWhiteSpace(parameters.WorkspacePath)
            ? _pathResolutionService.GetPrimaryWorkspacePath()
            : Path.GetFullPath(parameters.WorkspacePath);
        
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

            // Parse SearchMode from parameters
            var searchModeString = parameters.SearchMode ?? "auto";
            if (!Enum.TryParse<SearchMode>(searchModeString, true, out var searchMode))
            {
                searchMode = SearchMode.Auto;
            }

            _logger.LogInformation("🔍 Text search mode: {Mode}, query: '{Query}'", searchMode, query);

            // Handle semantic-only mode (skip Lucene, go straight to vector search)
            if (searchMode == SearchMode.Semantic)
            {
                return await HandleSemanticOnlySearchAsync(workspacePath, query, parameters, cacheKey, cancellationToken);
            }

            // Build Lucene query based on search mode
            Query luceneQuery;
            string searchType; // For backward compatibility with scoring
            string targetField = "content"; // Track for fallback logic

            if (searchMode == SearchMode.Exact)
            {
                // Exact literal matching
                _logger.LogDebug("Building exact literal query");
                luceneQuery = _queryPreprocessor.BuildQuery(query, "literal", parameters.CaseSensitive, _codeAnalyzer);
                searchType = "literal";
            }
            else if (searchMode == SearchMode.Fuzzy)
            {
                // Typo-tolerant fuzzy search
                _logger.LogDebug("Building fuzzy query");
                luceneQuery = _queryPreprocessor.BuildQuery(query, "fuzzy", parameters.CaseSensitive, _codeAnalyzer);
                searchType = "fuzzy";
            }
            else if (searchMode == SearchMode.Regex)
            {
                // Regular expression pattern matching
                _logger.LogDebug("Building regex query");
                luceneQuery = _queryPreprocessor.BuildQuery(query, "regex", parameters.CaseSensitive, _codeAnalyzer);
                searchType = "regex";
            }
            else
            {
                // Auto mode (default): Use SmartQueryPreprocessor for intelligent routing
                var queryResult = _smartQueryPreprocessor.Process(query, searchMode);
                        
                // Log the smart query processing result for debugging
                _logger.LogDebug("Smart query processing: '{OriginalQuery}' -> '{ProcessedQuery}', Field: {TargetField}, Mode: {Mode}, Reason: {Reason}",
                    query, queryResult.ProcessedQuery, queryResult.TargetField, queryResult.DetectedMode, queryResult.Reason);

                // Track target field for fallback logic
                targetField = queryResult.TargetField;

                // Validate the processed query
#pragma warning disable CS0618 // SearchType is obsolete but still used for backward compatibility
                searchType = parameters.SearchType ?? "standard";
#pragma warning restore CS0618
                if (!_queryPreprocessor.IsValidQuery(queryResult.ProcessedQuery, searchType, out var errorMessage))
                {
                    return CreateQueryParseError(queryResult.ProcessedQuery, errorMessage);
                }

                // Build field-specific query using the SmartQueryPreprocessor results
                if (queryResult.TargetField == "content")
                {
                    // Use existing QueryPreprocessor for standard content field
                    luceneQuery = _queryPreprocessor.BuildQuery(queryResult.ProcessedQuery, searchType, parameters.CaseSensitive, _codeAnalyzer);
                }
                else
                {
                    // Create field-specific query for multi-field indexing
                    var queryParser = new QueryParser(LuceneVersion.LUCENE_48, queryResult.TargetField, _codeAnalyzer);
                    queryParser.AllowLeadingWildcard = true;

                    try
                    {
                        luceneQuery = queryParser.Parse(queryResult.ProcessedQuery);
                    }
                    catch (ParseException)
                    {
                        // Fallback to term query for problematic queries
                        luceneQuery = new TermQuery(new Term(queryResult.TargetField, queryResult.ProcessedQuery.ToLowerInvariant()));
                    }
                }
            }

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
            
            // Fallback mechanism: If symbol search returns 0 results, retry with content field
            if (searchResult.TotalHits == 0 && targetField == "content_symbols")
            {
                _logger.LogDebug("Symbol search for '{Query}' returned 0 results, falling back to content field", query);
                
                // Retry with content field
                var fallbackQuery = _queryPreprocessor.BuildQuery(query, searchType, parameters.CaseSensitive, _codeAnalyzer);
                var fallbackMultiFactorQuery = new MultiFactorScoreQuery(fallbackQuery, scoringContext, _logger);
                
                // Add same scoring factors
                fallbackMultiFactorQuery.AddScoringFactor(new PathRelevanceFactor(_logger));
                fallbackMultiFactorQuery.AddScoringFactor(new FilenameRelevanceFactor());
                fallbackMultiFactorQuery.AddScoringFactor(new FileTypeRelevanceFactor());
                fallbackMultiFactorQuery.AddScoringFactor(new RecencyBoostFactor());
                fallbackMultiFactorQuery.AddScoringFactor(new ExactMatchBoostFactor(parameters.CaseSensitive));
                fallbackMultiFactorQuery.AddScoringFactor(new InterfaceImplementationFactor(_logger));
                
                searchResult = await _luceneIndexService.SearchAsync(
                    workspacePath, 
                    fallbackMultiFactorQuery,
                    maxResults,
                    includeSnippets,
                    cancellationToken);
                
                searchResult.Query = query; // Keep original query for display
                
                if (searchResult.TotalHits > 0)
                {
                    _logger.LogInformation("Fallback search found {HitCount} results for '{Query}'", searchResult.TotalHits, query);
                }
            }

            // TIER 3: Semantic search fallback (if few results and semantic search available)
            if (searchResult.TotalHits < 5 && _sqliteService.IsSemanticSearchAvailable())
            {
                _logger.LogDebug("Lucene returned {Count} results, trying Tier 3 semantic search", searchResult.TotalHits);

                try
                {
                    var semanticStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var semanticResults = await _sqliteService.SearchSymbolsSemanticAsync(
                        workspacePath,
                        query,
                        limit: maxResults,
                        cancellationToken);
                    semanticStopwatch.Stop();

                    if (semanticResults.Any())
                    {
                        _logger.LogInformation("✅ Tier 3 HIT: Found {Count} semantic matches in {Ms}ms",
                            semanticResults.Count, semanticStopwatch.ElapsedMilliseconds);

                        // Convert semantic results to SearchHit format
                        var semanticHits = semanticResults.Select(sr => new SearchHit
                        {
                            FilePath = sr.Symbol.FilePath,
                            StartLine = sr.Symbol.StartLine,
                            Score = sr.SimilarityScore, // Use similarity score (0-1, higher is better)
                            Snippet = sr.Symbol.DocComment ?? sr.Symbol.Signature ?? $"{sr.Symbol.Kind} {sr.Symbol.Name}",
                            Fields = new Dictionary<string, string>
                            {
                                ["name"] = sr.Symbol.Name,
                                ["kind"] = sr.Symbol.Kind,
                                ["signature"] = sr.Symbol.Signature ?? "",
                                ["language"] = sr.Symbol.Language,
                                ["similarity_score"] = sr.SimilarityScore.ToString("F3"),
                                ["search_tier"] = "semantic"
                            }
                        }).ToList();

                        // Merge with existing results (semantic as supplement, not replacement)
                        // Only add semantic results that aren't already in the result set
                        if (searchResult.Hits == null)
                        {
                            searchResult.Hits = new List<SearchHit>();
                        }

                        var existingPaths = searchResult.Hits
                            .Select(h => $"{h.FilePath}:{h.LineNumber}")
                            .ToHashSet();

                        var newSemanticHits = semanticHits
                            .Where(h => !existingPaths.Contains($"{h.FilePath}:{h.LineNumber}"))
                            .ToList();

                        if (newSemanticHits.Any())
                        {
                            searchResult.Hits.AddRange(newSemanticHits);
                            searchResult.TotalHits += newSemanticHits.Count;
                            _logger.LogInformation("Added {Count} new semantic results to {Existing} existing results",
                                newSemanticHits.Count, existingPaths.Count);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Tier 3 MISS: No semantic matches found");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 3 semantic search failed, continuing with Lucene results");
                }
            }


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

            // Auto-documentation: store findings in ProjectKnowledge if enabled
            if (parameters.DocumentFindings && result.Success && searchResult.TotalHits > 0)
            {
                DocumentSearchFindings(parameters, searchResult, query);
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
    private void DocumentSearchFindings(TextSearchParameters parameters, COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string query)
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
            metadata["workspace"] = Path.GetFileName(parameters.WorkspacePath ?? string.Empty);  // Project name, not full path
            metadata["searchQuery"] = query;
            metadata["resultCount"] = searchResult.TotalHits.ToString();  // Convert to string
            metadata["workspacePath"] = parameters.WorkspacePath ?? string.Empty;
#pragma warning disable CS0618 // SearchType is obsolete but still used for backward compatibility
            metadata["searchType"] = parameters.SearchType ?? "standard";
#pragma warning restore CS0618
            metadata["caseSensitive"] = parameters.CaseSensitive.ToString();  // Convert to string

            // ProjectKnowledge integration removed - service retired
            _logger.LogDebug("Search findings documented locally: Query='{Query}', Type={Type}",
                query, knowledgeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-documenting search findings for query: {Query}", query);
            // Don't throw - documentation failure shouldn't break search
        }
    }

    /// <summary>
    /// Handle semantic-only search mode (Tier 3 vector search, no Lucene)
    /// </summary>
    private async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> HandleSemanticOnlySearchAsync(
        string workspacePath,
        string query,
        TextSearchParameters parameters,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔮 Semantic-only mode: Skipping Lucene, using vector search");

        // Check if semantic search is available
        if (!_sqliteService.IsSemanticSearchAvailable())
        {
            return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "SEMANTIC_UNAVAILABLE",
                    Message = "Semantic search is not available for this workspace",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Semantic search requires embeddings to be generated",
                            "Run index_workspace to build semantic index",
                            "Try a different search mode (auto, exact, fuzzy, regex)"
                        }
                    }
                },
                Insights = new List<string>
                {
                    "Semantic search uses vector embeddings for conceptual similarity",
                    "The workspace needs to be indexed with semantic support enabled"
                }
            };
        }

        var semanticStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var semanticResults = await _sqliteService.SearchSymbolsSemanticAsync(
            workspacePath,
            query,
            limit: 20, // More results for semantic-only mode
            cancellationToken);
        semanticStopwatch.Stop();

        if (!semanticResults.Any())
        {
            _logger.LogInformation("Semantic search returned no results for '{Query}'", query);

            var emptyResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                TotalHits = 0,
                SearchTime = semanticStopwatch.Elapsed,
                Query = query,
                Hits = new List<SearchHit>()
            };

            var emptyResponseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
            var emptyContext = new ResponseContext
            {
                ResponseMode = emptyResponseMode,
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };

            var response = await _responseBuilder.BuildResponseAsync(emptyResult, emptyContext);
            response.Insights = new List<string>
            {
                $"No semantic matches found for '{query}'",
                "Try using 'auto' mode for standard Lucene search",
                "Semantic search finds conceptually similar code, not exact matches"
            };

            return response;
        }

        // Convert semantic results to SearchResult format
        var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
        {
            TotalHits = semanticResults.Count,
            SearchTime = semanticStopwatch.Elapsed,
            Query = query,
            Hits = semanticResults.Select(sr => new SearchHit
            {
                FilePath = sr.Symbol.FilePath,
                StartLine = sr.Symbol.StartLine,
                Score = sr.SimilarityScore,
                Snippet = sr.Symbol.DocComment ?? sr.Symbol.Signature ?? $"{sr.Symbol.Kind} {sr.Symbol.Name}",
                Fields = new Dictionary<string, string>
                {
                    ["name"] = sr.Symbol.Name,
                    ["kind"] = sr.Symbol.Kind,
                    ["signature"] = sr.Symbol.Signature ?? "",
                    ["language"] = sr.Symbol.Language,
                    ["similarity_score"] = sr.SimilarityScore.ToString("F3"),
                    ["search_tier"] = "semantic"
                }
            }).ToList()
        };

        _logger.LogInformation("✅ Semantic search: Found {Count} matches in {Ms}ms",
            semanticResults.Count, semanticStopwatch.ElapsedMilliseconds);

        var responseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
        var context = new ResponseContext
        {
            ResponseMode = responseMode,
            TokenLimit = parameters.MaxTokens,
            StoreFullResults = true,
            ToolName = Name,
            CacheKey = cacheKey
        };

        var result = await _responseBuilder.BuildResponseAsync(searchResult, context);

        // Cache semantic results
        if (!parameters.NoCache && result.Success)
        {
            await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
            {
                AbsoluteExpiration = TimeSpan.FromMinutes(15),
                Priority = CachePriority.Normal
            });
        }

        return result;
    }


}