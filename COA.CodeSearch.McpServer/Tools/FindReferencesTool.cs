using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Find references tool that locates all usages of a symbol in the codebase
/// </summary>
public class FindReferencesTool : CodeSearchToolBase<FindReferencesParameters, AIOptimizedResponse<SearchResult>>, IPrioritizedTool
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly FindReferencesResponseBuilder _responseBuilder;
    private readonly ILogger<FindReferencesTool> _logger;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly IReferenceResolverService? _referenceResolver;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Initializes a new instance of the FindReferencesTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="queryProcessor">Smart query preprocessing service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="logger">Logger instance</param>
    public FindReferencesTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        CodeAnalyzer codeAnalyzer,
        IReferenceResolverService referenceResolver,
        ILogger<FindReferencesTool> logger) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _codeAnalyzer = codeAnalyzer;
        _referenceResolver = referenceResolver;
        _logger = logger;
        _responseBuilder = new FindReferencesResponseBuilder(logger as ILogger<FindReferencesResponseBuilder>, storageService);
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.FindReferences;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "CRITICAL FOR REFACTORING - Find ALL usages before making changes. PREVENTS breaking code. Shows: every reference, grouped by file, with context. Always use before renaming/deleting.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 95;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "before_refactoring", "symbol_analysis", "impact_assessment", "before_deleting" };


    /// <summary>
    /// Executes the find references operation to locate all symbol usages.
    /// </summary>
    /// <param name="parameters">Find references parameters including symbol name and search options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Search results containing all references to the specified symbol</returns>
    protected override async Task<AIOptimizedResponse<SearchResult>> ExecuteInternalAsync(
        FindReferencesParameters parameters,
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
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached find references result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // FAST-PATH: Try identifier-based reference finding first (LSP-quality, <10ms)
            if (_referenceResolver != null)
            {
                try
                {
                    var resolvedRefs = await _referenceResolver.FindReferencesAsync(
                        workspacePath,
                        symbolName,
                        caseSensitive: parameters.CaseSensitive,
                        cancellationToken);

                    if (resolvedRefs.Any())
                    {
                        _logger.LogInformation("✅ Found {Count} references using identifier fast-path ({Ms}ms)",
                            resolvedRefs.Count, stopwatch.ElapsedMilliseconds);

                        // Convert ResolvedReferences to SearchHits
                        var hits = resolvedRefs.Select(rr => new SearchHit
                        {
                            FilePath = rr.Identifier.FilePath,
                            LineNumber = rr.Identifier.StartLine,
                            Score = rr.Identifier.Confidence,
                            Fields = new Dictionary<string, string>
                            {
                                ["kind"] = rr.Identifier.Kind,
                                ["language"] = rr.Identifier.Language,
                                ["referenceType"] = rr.Identifier.Kind, // call, member_access, etc.
                                ["containedIn"] = rr.ContainingSymbol?.Name ?? "unknown",
                                ["containedInKind"] = rr.ContainingSymbol?.Kind ?? "unknown",
                                ["resolved"] = rr.IsResolved.ToString()
                            },
                            ContextLines = rr.Identifier.CodeContext != null
                                ? rr.Identifier.CodeContext.Split('\n').ToList()
                                : null
                        }).ToList();

                        var identifierSearchResult = new SearchResult
                        {
                            Hits = hits,
                            TotalHits = hits.Count,
                            Query = symbolName
                        };

                        // Build response using identifier data
                        var responseContext = new ResponseContext
                        {
                            TokenLimit = parameters.MaxTokens,
                            ResponseMode = "adaptive",
                            ToolName = Name,
                            StoreFullResults = false
                        };

                        var identifierResponse = await _responseBuilder.BuildResponseAsync(
                            identifierSearchResult,
                            responseContext);

                        // Cache the result
                        if (!parameters.NoCache && identifierResponse != null)
                        {
                            await _cacheService.SetAsync(cacheKey, identifierResponse);
                        }

                        return identifierResponse;
                    }
                    else
                    {
                        _logger.LogDebug("No identifier references found, falling back to Lucene search");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Identifier fast-path failed, falling back to Lucene search");
                }
            }

            // FALLBACK: Use Lucene full-text search (original implementation)
            _logger.LogInformation("Using Lucene fallback for find_references");

            // Use SmartQueryPreprocessor for multi-field reference searching
            var searchMode = parameters.IncludePotential ? SearchMode.Standard : SearchMode.Symbol;
            var queryResult = _queryProcessor.Process(symbolName, searchMode);
            
            _logger.LogInformation("Find references: {Symbol} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
                symbolName, queryResult.TargetField, queryResult.ProcessedQuery, queryResult.Reason);
            
            Query query;
            if (parameters.IncludePotential)
            {
                // Broader search including partial matches using CodeAnalyzer
                var parser = new QueryParser(LUCENE_VERSION, queryResult.TargetField, _codeAnalyzer);
                query = parser.Parse(queryResult.ProcessedQuery);
            }
            else
            {
                // Stricter search for exact symbol references - combine processed query with existing logic
                var processedQuery = BuildStrictReferenceQueryFromProcessed(queryResult.ProcessedQuery, parameters.CaseSensitive);
                query = processedQuery;
            }
            
            // Perform the search  
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                query, 
                parameters.MaxResults,
                false, // TEMPORARILY DISABLE snippets to test type_info retrieval
                cancellationToken);
            
            stopwatch.Stop();
            
            // Post-process results with type-aware analysis
            if (searchResult.Hits != null)
            {
                foreach (var hit in searchResult.Hits)
                {
                    // Enhance context lines to show where the symbol is referenced
                    if (hit.ContextLines != null && hit.ContextLines.Count > 0)
                    {
                        hit.ContextLines = HighlightSymbolInContext(hit.ContextLines, symbolName, parameters.CaseSensitive);
                    }
                    
                    // Add reference-specific metadata using type information
                    if (hit.Fields == null)
                        hit.Fields = new Dictionary<string, string>();
                    
                    // Enhanced reference type detection using indexed type info
                    hit.Fields["referenceType"] = await DetermineReferenceTypeWithTypeInfoAsync(
                        hit, symbolName, parameters.CaseSensitive, cancellationToken);
                }
                
                // Group by file if requested
                if (parameters.GroupByFile)
                {
                    searchResult.Hits = searchResult.Hits
                        .GroupBy(h => h.FilePath)
                        .SelectMany(g => g.OrderBy(h => h.LineNumber ?? 0))
                        .ToList();
                }
            }
            
            // Build the response using the existing SearchResponseBuilder
            var context = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens,
                ResponseMode = "adaptive",  // FindReferences doesn't have ResponseMode parameter
                ToolName = Name,
                StoreFullResults = false
            };
            
            var response = await _responseBuilder.BuildResponseAsync(
                searchResult,
                context);
            
            // Add find-references specific metadata
            if (response.Data != null)
            {
                response.Data.Summary = GenerateReferencesSummary(searchResult, symbolName);
                // Store query in ExtensionData since AIResponseData doesn't have Query property
                if (response.Data.ExtensionData == null)
                    response.Data.ExtensionData = new Dictionary<string, object>();
                response.Data.ExtensionData["query"] = symbolName;
            }
            
            // Add specific insights for references
            if (response.Insights != null)
            {
                response.Insights.Insert(0, $"Found {searchResult.TotalHits} references to '{symbolName}'");
                
                if (searchResult.Hits != null && parameters.GroupByFile)
                {
                    var fileCount = searchResult.Hits.Select(h => h.FilePath).Distinct().Count();
                    response.Insights.Add($"References found in {fileCount} files");
                }
            }
            
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
            _logger.LogError(ex, "Find references failed for {Symbol} in {WorkspacePath}", symbolName, workspacePath);
            
            return new AIOptimizedResponse<SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "FIND_REFERENCES_ERROR",
                    Message = $"Failed to find references: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the symbol name is correct",
                            "Try with IncludePotential=true for broader search"
                        }
                    }
                }
            };
        }
    }
    
    private string BuildReferenceQueryString(string symbolName)
    {
        // Build a query that looks for various usage patterns
        var patterns = new List<string>
        {
            symbolName,                                    // Direct reference
            $"new {symbolName}",                          // Instantiation
            $": {symbolName}",                            // Inheritance (C#)
            $"extends {symbolName}",                      // Inheritance (TS/Java)
            $"implements {symbolName}",                   // Interface implementation
            $"{symbolName}.",                             // Static member access
            $"<{symbolName}>",                            // Generic type parameter
            $"{symbolName}[]",                            // Array type
            $"typeof {symbolName}",                       // Type checking
            $"is {symbolName}",                           // Type checking (C#)
            $"as {symbolName}",                           // Type casting
            $"({symbolName})",                            // Type casting or grouping
        };
        
        // Join with OR operators
        return string.Join(" OR ", patterns.Select(p => $"\"{p}\""));
    }
    
    private Query BuildStrictReferenceQuery(string symbolName, bool caseSensitive)
    {
        // Build a more precise query using phrase queries
        var booleanQuery = new BooleanQuery();
        
        // Main term query
        if (caseSensitive)
        {
            booleanQuery.Add(new TermQuery(new Term("content", symbolName)), Occur.MUST);
        }
        else
        {
            // Use wildcard for case-insensitive
            var parser = new QueryParser(LUCENE_VERSION, "content", _codeAnalyzer);
            booleanQuery.Add(parser.Parse(symbolName.ToLowerInvariant()), Occur.MUST);
        }
        
        // Exclude definition files (those that have the symbol in type_names)
        booleanQuery.Add(new TermQuery(new Term("type_names", symbolName)), Occur.MUST_NOT);
        
        return booleanQuery;
    }

    private Query BuildStrictReferenceQueryFromProcessed(string processedQuery, bool caseSensitive)
    {
        // Adapt the strict reference logic to work with SmartQueryPreprocessor output
        var booleanQuery = new BooleanQuery();
        
        // Use the processed query from SmartQueryPreprocessor 
        if (caseSensitive)
        {
            // For case-sensitive, create a term query with the processed query
            booleanQuery.Add(new TermQuery(new Term("content_symbols", processedQuery)), Occur.MUST);
        }
        else
        {
            // Use QueryParser with the processed query for case-insensitive search
            var parser = new QueryParser(LUCENE_VERSION, "content_symbols", _codeAnalyzer);
            booleanQuery.Add(parser.Parse(processedQuery.ToLowerInvariant()), Occur.MUST);
        }
        
        // Exclude definition files (those that have the symbol in type_names)
        // Use original symbol name for type_names exclusion
        var originalSymbol = processedQuery; // For now, assume processed query is close to original
        booleanQuery.Add(new TermQuery(new Term("type_names", originalSymbol)), Occur.MUST_NOT);
        
        return booleanQuery;
    }
    
    private List<string> HighlightSymbolInContext(List<string> contextLines, string symbolName, bool caseSensitive)
    {
        var highlighted = new List<string>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        foreach (var line in contextLines)
        {
            // Simple highlighting with markers
            var index = line.IndexOf(symbolName, comparison);
            if (index >= 0)
            {
                var before = line.Substring(0, index);
                var match = line.Substring(index, symbolName.Length);
                var after = line.Substring(index + symbolName.Length);
                highlighted.Add($"{before}«{match}»{after}");
            }
            else
            {
                highlighted.Add(line);
            }
        }
        
        return highlighted;
    }
    
    private string DetermineReferenceType(List<string>? contextLines, string symbolName)
    {
        if (contextLines == null || contextLines.Count == 0)
            return "usage";
        
        var context = string.Join(" ", contextLines);
        
        // Detect reference type based on patterns
        if (Regex.IsMatch(context, $@"new\s+{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "instantiation";
        if (Regex.IsMatch(context, $@":\s*{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "inheritance";
        if (Regex.IsMatch(context, $@"extends\s+{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "inheritance";
        if (Regex.IsMatch(context, $@"implements\s+{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "implementation";
        if (Regex.IsMatch(context, $@"{Regex.Escape(symbolName)}\.", RegexOptions.IgnoreCase))
            return "static-access";
        if (Regex.IsMatch(context, $@"<{Regex.Escape(symbolName)}>", RegexOptions.IgnoreCase))
            return "generic-type";
        if (Regex.IsMatch(context, $@"import.*{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "import";
        if (Regex.IsMatch(context, $@"using.*{Regex.Escape(symbolName)}", RegexOptions.IgnoreCase))
            return "using";
        
        return "usage";
    }

    /// <summary>
    /// Enhanced reference type detection using both indexed type information and context analysis.
    /// This provides more accurate classification by understanding the symbol's semantic meaning.
    /// </summary>
    private Task<string> DetermineReferenceTypeWithTypeInfoAsync(
        SearchHit hit, string symbolName, bool caseSensitive, CancellationToken cancellationToken)
    {
        try
        {
            // Start with the basic regex-based detection
            var baseType = DetermineReferenceType(hit.ContextLines, symbolName);
            
            // Get type information from the index for this file
            var typeInfoJson = hit.Fields?.ContainsKey("type_info") == true ? hit.Fields["type_info"] : null;
            
            if (string.IsNullOrEmpty(typeInfoJson))
            {
                // No type info available, return the regex-based result
                return Task.FromResult(baseType);
            }
            
            // Deserialize the type information
            TypeExtractionResult? typeData = null;
            try
            {
                typeData = JsonSerializer.Deserialize<TypeExtractionResult>(
                    typeInfoJson, 
                    TypeExtractionResult.DeserializationOptions);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to deserialize type_info for {FilePath}", hit.FilePath);
                return Task.FromResult(baseType);
            }
            
            if (typeData?.Success != true)
                return Task.FromResult(baseType);
            
            // Check if this symbol is defined in this file (definition vs reference)
            var isDefinition = IsSymbolDefinedInFile(typeData, symbolName, caseSensitive);
            if (isDefinition.isDefined)
            {
                return Task.FromResult(isDefinition.symbolType == "type" ? "type-definition" : 
                       isDefinition.symbolType == "method" ? "method-definition" : 
                       "definition");
            }
            
            // Enhance the reference type based on what we know about the symbol
            var symbolInfo = GetSymbolInfo(typeData, symbolName, caseSensitive);
            if (symbolInfo != null)
            {
                // If we know this symbol is a type, enhance the reference classification
                if (symbolInfo.IsType)
                {
                    return Task.FromResult(EnhanceTypeReference(baseType, hit.ContextLines, symbolName));
                }
                else if (symbolInfo.IsMethod)
                {
                    return Task.FromResult(EnhanceMethodReference(baseType, hit.ContextLines, symbolName));
                }
            }
            
            return Task.FromResult(baseType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in enhanced reference type detection for {Symbol} in {FilePath}", 
                symbolName, hit.FilePath);
            return Task.FromResult(DetermineReferenceType(hit.ContextLines, symbolName));
        }
    }

    /// <summary>
    /// Checks if a symbol is defined in the given type data (indicating this is the declaration file)
    /// </summary>
    private (bool isDefined, string symbolType) IsSymbolDefinedInFile(
        TypeExtractionResult typeData, string symbolName, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        // Check types first
        if (typeData.Types?.Any(t => t.Name.Equals(symbolName, comparison)) == true)
        {
            return (true, "type");
        }
        
        // Check methods
        if (typeData.Methods?.Any(m => m.Name.Equals(symbolName, comparison)) == true)
        {
            return (true, "method");
        }
        
        return (false, "");
    }

    /// <summary>
    /// Gets information about a symbol from type data across the solution
    /// </summary>
    private SymbolInfo? GetSymbolInfo(TypeExtractionResult typeData, string symbolName, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        // Check if it's a type
        var matchingType = typeData.Types?.FirstOrDefault(t => t.Name.Equals(symbolName, comparison));
        if (matchingType != null)
        {
            return new SymbolInfo { IsType = true, Kind = matchingType.Kind };
        }
        
        // Check if it's a method
        var matchingMethod = typeData.Methods?.FirstOrDefault(m => m.Name.Equals(symbolName, comparison));
        if (matchingMethod != null)
        {
            return new SymbolInfo { IsMethod = true, ReturnType = matchingMethod.ReturnType };
        }
        
        return null;
    }

    /// <summary>
    /// Enhances type reference classification with semantic understanding
    /// </summary>
    private string EnhanceTypeReference(string baseType, List<string>? contextLines, string symbolName)
    {
        if (contextLines == null || contextLines.Count == 0)
            return baseType;
        
        var context = string.Join(" ", contextLines).ToLowerInvariant();
        
        // More specific type usage patterns
        if (context.Contains($"new {symbolName.ToLowerInvariant()}"))
            return "type-instantiation";
        if (context.Contains($": {symbolName.ToLowerInvariant()}") || context.Contains($"extends {symbolName.ToLowerInvariant()}"))
            return "type-inheritance";
        if (context.Contains($"implements {symbolName.ToLowerInvariant()}"))
            return "interface-implementation";
        if (context.Contains($"<{symbolName.ToLowerInvariant()}>") || context.Contains($"<{symbolName.ToLowerInvariant()},"))
            return "generic-type-parameter";
        if (context.Contains($"typeof({symbolName.ToLowerInvariant()})") || context.Contains($"{symbolName.ToLowerInvariant()}.class"))
            return "type-reflection";
        
        return baseType == "usage" ? "type-reference" : baseType;
    }

    /// <summary>
    /// Enhances method reference classification with semantic understanding
    /// </summary>
    private string EnhanceMethodReference(string baseType, List<string>? contextLines, string symbolName)
    {
        if (contextLines == null || contextLines.Count == 0)
            return baseType;
        
        var context = string.Join(" ", contextLines).ToLowerInvariant();
        
        // Method-specific patterns
        if (context.Contains($"{symbolName.ToLowerInvariant()}("))
            return "method-call";
        if (context.Contains($"override {symbolName.ToLowerInvariant()}") || context.Contains($"overrides {symbolName.ToLowerInvariant()}"))
            return "method-override";
        if (context.Contains($"=> {symbolName.ToLowerInvariant()}") || context.Contains($"= {symbolName.ToLowerInvariant()}"))
            return "method-reference";
        
        return baseType == "usage" ? "method-usage" : baseType;
    }

    /// <summary>
    /// Helper class to store symbol information
    /// </summary>
    private class SymbolInfo
    {
        public bool IsType { get; set; }
        public bool IsMethod { get; set; }
        public string? Kind { get; set; }
        public string? ReturnType { get; set; }
    }
    
    
    private string GenerateReferencesSummary(SearchResult result, string symbolName)
    {
        if (result.TotalHits == 0)
            return $"No references found for '{symbolName}'";
        
        if (result.Hits == null || result.Hits.Count == 0)
            return $"Found {result.TotalHits} references to '{symbolName}' (results truncated)";
        
        var fileCount = result.Hits.Select(h => h.FilePath).Distinct().Count();
        var referenceTypes = result.Hits
            .Where(h => h.Fields != null && h.Fields.ContainsKey("referenceType"))
            .GroupBy(h => h.Fields["referenceType"])
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Count()} {g.Key}");
        
        var summary = $"Found {result.TotalHits} references to '{symbolName}' in {fileCount} files";
        
        if (referenceTypes.Any())
        {
            summary += $" ({string.Join(", ", referenceTypes)})";
        }
        
        return summary;
    }
}