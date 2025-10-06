using System.ComponentModel;
using System.Text.Json;
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
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Models;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Symbol search tool that finds type and method definitions using Tree-sitter extracted data
/// </summary>
public class SymbolSearchTool : CodeSearchToolBase<SymbolSearchParameters, AIOptimizedResponse<SymbolSearchResult>>, IPrioritizedTool
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SymbolSearchResponseBuilder _responseBuilder;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly ILogger<SymbolSearchTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Initializes a new instance of the SymbolSearchTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="sqliteService">SQLite symbol service for fast exact/prefix matching</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="queryProcessor">Smart query preprocessing service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="logger">Logger instance</param>
    public SymbolSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        ISQLiteSymbolService sqliteService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        CodeAnalyzer codeAnalyzer,
        ILogger<SymbolSearchTool> logger) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _sqliteService = sqliteService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _codeAnalyzer = codeAnalyzer;
        _responseBuilder = new SymbolSearchResponseBuilder(logger as ILogger<SymbolSearchResponseBuilder>, storageService);
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.SymbolSearch;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "FIND SYMBOLS FAST - Locate any class/interface/method by name. BETTER than text search for code navigation. Multi-tier parallel search: Tier 1 (SQLite exact 0-1ms) → Tier 2+4 PARALLEL (Lucene fuzzy ~20ms + Semantic similarity ~47ms). Returns: signatures, documentation, inheritance, usage counts with tier breakdown.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 85;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "symbol_discovery", "type_exploration", "api_discovery", "inheritance_analysis" };


    /// <summary>
    /// Executes the symbol search operation to find type and method definitions.
    /// </summary>
    /// <param name="parameters">Symbol search parameters including symbol name and workspace path</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Symbol search results with type information and usage counts</returns>
    protected override async Task<AIOptimizedResponse<SymbolSearchResult>> ExecuteInternalAsync(
        SymbolSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var symbolName = ValidateRequired(parameters.Symbol, nameof(parameters.Symbol));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SymbolSearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached symbol search result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // TIER 1: Try exact match via SQLite (0-1ms) - fastest path for exact symbol names
            var exactMatch = await TryExactMatchAsync(workspacePath, symbolName, parameters, cancellationToken);
            if (exactMatch != null)
            {
                stopwatch.Stop();
                _logger.LogInformation("✅ Tier 1 HIT: Found exact SQLite match for '{Symbol}' in {Ms}ms",
                    symbolName, stopwatch.ElapsedMilliseconds);

                // Build and cache the response
                var tierOneContext = new ResponseContext
                {
                    ResponseMode = "adaptive",
                    TokenLimit = parameters.MaxTokens,
                    StoreFullResults = true,
                    ToolName = Name,
                    CacheKey = cacheKey
                };

                var tierOneResult = await _responseBuilder.BuildResponseAsync(exactMatch, tierOneContext);
                if (!parameters.NoCache)
                {
                    await _cacheService.SetAsync(cacheKey, tierOneResult);
                }
                return tierOneResult;
            }

            _logger.LogDebug("Tier 1 MISS: No exact SQLite match, starting parallel Tier 2 (Lucene) + Tier 4 (Semantic) search");

            // TIER 2: Lucene fuzzy search task
            var luceneTask = Task.Run(async () =>
            {
                var luceneStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var symbols = new List<SymbolDefinition>();

                try
                {
                    // Use SmartQueryPreprocessor to determine optimal field and processing
                    var queryResult = _queryProcessor.Process(symbolName, SearchMode.Symbol);
                    var targetField = queryResult.TargetField;
                    var processedQuery = queryResult.ProcessedQuery;

                    _logger.LogInformation("Tier 2 Lucene: {Symbol} -> Field: {Field}, Query: {Query}",
                        symbolName, targetField, processedQuery);

                    // Build Lucene query
                    var analyzer = _codeAnalyzer;
                    Query query;

                    var parser = new QueryParser(LUCENE_VERSION, targetField, analyzer);
                    query = parser.Parse(processedQuery);

                    // Optionally boost if searching for specific type
                    if (!string.IsNullOrEmpty(parameters.SymbolType))
                    {
                        var typeBoostQuery = new BooleanQuery();
                        typeBoostQuery.Add(query, Occur.MUST);
                        typeBoostQuery.Add(new TermQuery(new Term("type_info", parameters.SymbolType.ToLowerInvariant())), Occur.SHOULD);
                        query = typeBoostQuery;
                    }

                    // Perform the search
                    var searchResult = await _luceneIndexService.SearchAsync(
                        workspacePath,
                        query,
                        parameters.MaxResults * 2,
                        cancellationToken);

                    // Process results to extract symbol definitions
                    if (searchResult.Hits != null)
                    {
                        foreach (var hit in searchResult.Hits)
                        {
                            var typeInfoJson = hit.Fields?.ContainsKey("type_info") == true ? hit.Fields["type_info"] : null;
                            if (string.IsNullOrEmpty(typeInfoJson))
                                continue;

                            try
                            {
                                var typeData = JsonSerializer.Deserialize<TypeExtractionResult>(
                                    typeInfoJson,
                                    TypeExtractionResult.DeserializationOptions);
                                if (typeData == null)
                                    continue;

                                // Extract matching types
                                if (typeData.Types != null)
                                {
                                    foreach (var type in typeData.Types)
                                    {
                                        if (MatchesSymbol(type.Name, symbolName, parameters.CaseSensitive))
                                        {
                                            symbols.Add(new SymbolDefinition
                                            {
                                                Name = type.Name,
                                                Kind = type.Kind,
                                                Signature = type.Signature,
                                                FilePath = hit.FilePath ?? "",
                                                Line = type.Line,
                                                Column = type.Column,
                                                Language = typeData.Language,
                                                Modifiers = type.Modifiers,
                                                BaseType = type.BaseType,
                                                Interfaces = type.Interfaces,
                                                Score = hit.Score,
                                                Snippet = GetSnippet(hit.ContextLines)
                                            });

                                            if (type.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
                                                break;
                                        }
                                    }
                                }

                                // Extract matching methods
                                if (typeData.Methods != null)
                                {
                                    foreach (var method in typeData.Methods)
                                    {
                                        if (MatchesSymbol(method.Name, symbolName, parameters.CaseSensitive))
                                        {
                                            symbols.Add(new SymbolDefinition
                                            {
                                                Name = method.Name,
                                                Kind = "method",
                                                Signature = method.Signature,
                                                FilePath = hit.FilePath ?? "",
                                                Line = method.Line,
                                                Column = method.Column,
                                                Language = typeData.Language,
                                                Modifiers = method.Modifiers,
                                                ContainingType = method.ContainingType,
                                                ReturnType = method.ReturnType,
                                                Parameters = method.Parameters,
                                                Score = hit.Score,
                                                Snippet = GetSnippet(hit.ContextLines)
                                            });
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Failed to parse type_info for {FilePath}", hit.FilePath);
                            }
                        }
                    }

                    luceneStopwatch.Stop();
                    _logger.LogInformation("✅ Tier 2 Lucene: Found {Count} symbols in {Ms}ms",
                        symbols.Count, luceneStopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    luceneStopwatch.Stop();
                    _logger.LogWarning(ex, "❌ Tier 2 Lucene failed in {Ms}ms", luceneStopwatch.ElapsedMilliseconds);
                }

                return symbols;
            }, cancellationToken);

            // TIER 4: Semantic search task (parallel with Lucene)
            var semanticTask = Task.Run(async () =>
            {
                var semanticSymbols = new List<SymbolDefinition>();

                if (!_sqliteService.IsSemanticSearchAvailable())
                {
                    _logger.LogDebug("⏭️ Tier 4 SKIP: Semantic search not available");
                    return semanticSymbols;
                }

                var semanticStopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var semanticResults = await _sqliteService.SearchSymbolsSemanticAsync(
                        workspacePath,
                        symbolName,
                        limit: parameters.MaxResults,
                        cancellationToken);

                    if (semanticResults.Any())
                    {
                        semanticSymbols = semanticResults.Select(sr => new SymbolDefinition
                        {
                            Name = sr.Symbol.Name,
                            Kind = sr.Symbol.Kind,
                            Signature = sr.Symbol.Signature ?? "",
                            FilePath = sr.Symbol.FilePath,
                            Line = sr.Symbol.StartLine,
                            Column = sr.Symbol.StartColumn,
                            Language = sr.Symbol.Language,
                            Score = sr.SimilarityScore, // Semantic similarity score (0-1)
                            Snippet = sr.Symbol.DocComment
                        }).ToList();

                        semanticStopwatch.Stop();
                        _logger.LogInformation("✅ Tier 4 Semantic: Found {Count} matches in {Ms}ms",
                            semanticSymbols.Count, semanticStopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        semanticStopwatch.Stop();
                        _logger.LogDebug("⏭️ Tier 4 MISS: No semantic matches in {Ms}ms", semanticStopwatch.ElapsedMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    semanticStopwatch.Stop();
                    _logger.LogWarning(ex, "❌ Tier 4 Semantic failed in {Ms}ms", semanticStopwatch.ElapsedMilliseconds);
                }

                return semanticSymbols;
            }, cancellationToken);

            // Wait for both tiers to complete
            await Task.WhenAll(luceneTask, semanticTask);

            var luceneSymbols = await luceneTask;
            var semanticSymbols = await semanticTask;

            // Merge results intelligently (deduplicate by file:name, keep highest score)
            var mergedSymbols = new Dictionary<string, SymbolDefinition>();
            var tier2Count = 0;
            var tier4Count = 0;

            // Add Lucene results first (Tier 2)
            foreach (var symbol in luceneSymbols)
            {
                var key = $"{symbol.FilePath}:{symbol.Name}";
                mergedSymbols[key] = symbol;
                tier2Count++;
            }

            // Add semantic results, deduplicating with Lucene (Tier 4)
            foreach (var symbol in semanticSymbols)
            {
                var key = $"{symbol.FilePath}:{symbol.Name}";
                if (!mergedSymbols.ContainsKey(key))
                {
                    mergedSymbols[key] = symbol;
                    tier4Count++;
                }
                else
                {
                    // Symbol exists in both - keep the one with higher score
                    if (symbol.Score > mergedSymbols[key].Score)
                    {
                        mergedSymbols[key] = symbol;
                    }
                }
            }

            // Sort by relevance (exact matches first, then by score)
            var symbols = mergedSymbols.Values
                .OrderByDescending(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.Score)
                .Take(parameters.MaxResults)
                .ToList();

            stopwatch.Stop();
            _logger.LogInformation("🎯 Multi-tier complete: {Total} symbols ({Tier2} Lucene, {Tier4} Semantic unique) in {Ms}ms",
                symbols.Count, tier2Count, tier4Count, stopwatch.ElapsedMilliseconds);

            // Optionally get reference counts
            if (parameters.IncludeReferences)
            {
                await AddReferenceCounts(workspacePath, symbols, cancellationToken);
            }
            
            // Create the result with tier breakdown
            var result = new SymbolSearchResult
            {
                Symbols = symbols,
                TotalCount = symbols.Count,
                SearchTime = stopwatch.Elapsed,
                Query = symbolName,
                Tier1Count = 0, // Tier 1 already returned earlier if it matched
                Tier2Count = tier2Count,
                Tier4Count = tier4Count
            };
            
            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = "adaptive",  // SymbolSearch doesn't have ResponseMode parameter
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };
            
            // Use response builder to create optimized response
            var response = await _responseBuilder.BuildResponseAsync(result, context);
            
            // Cache the response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(5)
                });
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Symbol search failed for {Symbol} in {WorkspacePath}", symbolName, workspacePath);
            
            return new AIOptimizedResponse<SymbolSearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "SYMBOL_SEARCH_ERROR",
                    Message = $"Failed to search for symbol: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the symbol name is correct",
                            "Try a broader search with text_search tool"
                        }
                    }
                }
            };
        }
    }
    
    private bool MatchesSymbol(string symbolName, string searchTerm, bool caseSensitive)
    {
        if (caseSensitive)
            return symbolName.Contains(searchTerm);
        else
            return symbolName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    
    private string? GetSnippet(List<string>? contextLines)
    {
        if (contextLines == null || contextLines.Count == 0)
            return null;
        
        // Return up to 5 lines of context
        var snippetLines = contextLines.Take(5).ToList();
        return string.Join("\n", snippetLines);
    }
    
    private async Task AddReferenceCounts(string workspacePath, List<SymbolDefinition> symbols, CancellationToken cancellationToken)
    {
        // For each symbol, do a quick search to count references
        foreach (var symbol in symbols)
        {
            try
            {
                // Search for the symbol name in content
                var parser = new QueryParser(LUCENE_VERSION, "content", _codeAnalyzer);
                var query = parser.Parse(symbol.Name);

                var result = await _luceneIndexService.SearchAsync(workspacePath, query, 1, cancellationToken);
                symbol.ReferenceCount = result.TotalHits;
            }
            catch
            {
                // Ignore errors in reference counting
                symbol.ReferenceCount = null;
            }
        }
    }

    /// <summary>
    /// Tier 1: Try exact match via SQLite symbols table (0-1ms)
    /// </summary>
    private async Task<SymbolSearchResult?> TryExactMatchAsync(
        string workspacePath,
        string symbolName,
        SymbolSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Query SQLite for exact symbol name match
            var sqliteSymbols = await _sqliteService.GetSymbolsByNameAsync(
                workspacePath,
                symbolName,
                parameters.CaseSensitive,
                cancellationToken);

            if (sqliteSymbols == null || !sqliteSymbols.Any())
            {
                return null; // No exact match found
            }

            // Convert JulieSymbol to SymbolDefinition
            var symbols = new List<SymbolDefinition>();
            foreach (var julieSymbol in sqliteSymbols)
            {
                // Get reference count from identifiers table (optimized COUNT query, not full fetch)
                var referenceCount = await _sqliteService.GetIdentifierCountByNameAsync(
                    workspacePath,
                    julieSymbol.Name,
                    caseSensitive: false, // Count all references regardless of case
                    cancellationToken);

                symbols.Add(new SymbolDefinition
                {
                    Name = julieSymbol.Name,
                    Kind = julieSymbol.Kind,
                    Signature = julieSymbol.Signature ?? $"{julieSymbol.Kind} {julieSymbol.Name}",
                    FilePath = julieSymbol.FilePath,
                    Line = julieSymbol.StartLine,
                    Column = julieSymbol.StartColumn,
                    Language = julieSymbol.Language,
                    Modifiers = julieSymbol.Visibility != null ? new List<string> { julieSymbol.Visibility } : new List<string>(),
                    ReferenceCount = referenceCount,
                    Score = 1.0f // Exact match gets perfect score
                });
            }

            // Apply type filter if specified
            if (!string.IsNullOrEmpty(parameters.SymbolType))
            {
                symbols = symbols
                    .Where(s => s.Kind.Equals(parameters.SymbolType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!symbols.Any())
                {
                    return null; // No symbols match the type filter
                }
            }

            // Limit results
            if (symbols.Count > parameters.MaxResults)
            {
                symbols = symbols
                    .OrderByDescending(s => s.ReferenceCount ?? 0) // Sort by popularity
                    .Take(parameters.MaxResults)
                    .ToList();
            }

            return new SymbolSearchResult
            {
                Symbols = symbols,
                TotalCount = symbols.Count,
                SearchTime = TimeSpan.Zero,
                Query = symbolName,
                Tier1Count = symbols.Count, // All from Tier 1
                Tier2Count = 0,
                Tier4Count = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tier 1 exact match failed for '{Symbol}', will fall back to Lucene", symbolName);
            return null; // Fall back to Lucene on error
        }
    }
}