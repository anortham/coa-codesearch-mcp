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
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SymbolSearchResponseBuilder _responseBuilder;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly ILogger<SymbolSearchTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public SymbolSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        CodeAnalyzer codeAnalyzer,
        ILogger<SymbolSearchTool> logger) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _codeAnalyzer = codeAnalyzer;
        _responseBuilder = new SymbolSearchResponseBuilder(logger as ILogger<SymbolSearchResponseBuilder>, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.SymbolSearch;
    public override string Description => "FIND SYMBOLS FAST - Locate any class/interface/method by name. BETTER than text search for code navigation. Returns: signatures, documentation, inheritance, usage counts.";
    public override ToolCategory Category => ToolCategory.Query;
    
    // IPrioritizedTool implementation - HIGH priority for symbol discovery
    public int Priority => 85;
    public string[] PreferredScenarios => new[] { "symbol_discovery", "type_exploration", "api_discovery", "inheritance_analysis" };

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
            
            // Use SmartQueryPreprocessor to determine optimal field and processing
            // Symbols benefit from SearchMode.Symbol which targets content_symbols field
            var queryResult = _queryProcessor.Process(symbolName, SearchMode.Symbol);
            var targetField = queryResult.TargetField;
            var processedQuery = queryResult.ProcessedQuery;
            
            _logger.LogInformation("Searching for symbol: {Symbol} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
                symbolName, targetField, processedQuery, queryResult.Reason);
            
            // Build Lucene query using the preprocessor's field selection
            var analyzer = _codeAnalyzer;
            Query query;
            
            if (parameters.CaseSensitive)
            {
                // For case-sensitive, we still need to use the analyzer since fields are analyzed
                // Note: CodeAnalyzer with preserveCase=false always lowercases, so true case-sensitive isn't possible
                var parser = new QueryParser(LUCENE_VERSION, targetField, analyzer);
                query = parser.Parse(processedQuery);
            }
            else
            {
                // Case-insensitive search using QueryParser with CodeAnalyzer
                var parser = new QueryParser(LUCENE_VERSION, targetField, analyzer);
                query = parser.Parse(processedQuery);
            }
            
            _logger.LogInformation("Generated Lucene query: {Query}", query.ToString());
            
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
                parameters.MaxResults * 2, // Get more to filter by exact matches
                cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation("Search returned {HitCount} hits for symbol: {Symbol}", 
                searchResult.Hits?.Count ?? 0, symbolName);
            
            // Process results to extract symbol definitions
            var symbols = new List<SymbolDefinition>();
            
            if (searchResult.Hits != null)
            {
                _logger.LogInformation("Processing {Count} hits to extract symbols", searchResult.Hits.Count);
                
                foreach (var hit in searchResult.Hits)
                {
                    _logger.LogDebug("Processing hit for file: {FilePath}", hit.FilePath);
                    _logger.LogDebug("Hit.Fields is null: {IsNull}, Count: {Count}", 
                        hit.Fields == null, hit.Fields?.Count ?? 0);
                    
                    if (hit.Fields != null)
                    {
                        _logger.LogDebug("Available fields: {Fields}", string.Join(", ", hit.Fields.Keys));
                    }
                    
                    // Get the stored type_info JSON from Fields dictionary
                    var typeInfoJson = hit.Fields?.ContainsKey("type_info") == true ? hit.Fields["type_info"] : null;
                    
                    if (string.IsNullOrEmpty(typeInfoJson))
                    {
                        _logger.LogWarning("No type_info field found for {FilePath}", hit.FilePath);
                        continue;
                    }
                    
                    _logger.LogDebug("Found type_info for {FilePath}, length: {Length}", 
                        hit.FilePath, typeInfoJson.Length);
                    
                    try
                    {
                        // Deserialize using shared options from TypeExtractionResult
                        var typeData = JsonSerializer.Deserialize<TypeExtractionResult>(
                            typeInfoJson, 
                            TypeExtractionResult.DeserializationOptions);
                        if (typeData == null)
                            continue;
                        
                        // Extract matching types
                        if (typeData.Types != null)
                        {
                            _logger.LogDebug("Checking {Count} types for matches", typeData.Types.Count);
                            foreach (var type in typeData.Types)
                            {
                                _logger.LogDebug("Checking type: {TypeName} against symbol: {SymbolName}", 
                                    type.Name, symbolName);
                                    
                                if (MatchesSymbol(type.Name, symbolName, parameters.CaseSensitive))
                                {
                                    _logger.LogInformation("Found matching type: {TypeName} in {FilePath}", 
                                        type.Name, hit.FilePath);
                                    
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
                                    
                                    // If we found an exact match in this file, don't look for methods
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
            
            _logger.LogInformation("Total symbols found before sorting: {Count}", symbols.Count);
            
            // Sort by relevance (exact matches first, then by score)
            symbols = symbols
                .OrderByDescending(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.Score)
                .Take(parameters.MaxResults)
                .ToList();
            
            _logger.LogInformation("Final symbols count after sorting and limiting: {Count}", symbols.Count);
            
            // Optionally get reference counts
            if (parameters.IncludeReferences)
            {
                await AddReferenceCounts(workspacePath, symbols, cancellationToken);
            }
            
            // Create the result
            var result = new SymbolSearchResult
            {
                Symbols = symbols,
                TotalCount = symbols.Count,
                SearchTime = stopwatch.Elapsed,
                Query = symbolName
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
}