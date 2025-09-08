using System.ComponentModel;
using System.Text.RegularExpressions;
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
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly ILogger<FindReferencesTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public FindReferencesTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        ILogger<FindReferencesTool> logger) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _logger = logger;
        _responseBuilder = new SearchResponseBuilder(logger as ILogger<SearchResponseBuilder>, storageService);
    }

    public override string Name => ToolNames.FindReferences;
    public override string Description => "CRITICAL FOR REFACTORING - Find ALL usages before making changes. PREVENTS breaking code. Shows: every reference, grouped by file, with context. Always use before renaming/deleting.";
    public override ToolCategory Category => ToolCategory.Query;
    
    // IPrioritizedTool implementation - VERY HIGH priority for refactoring safety
    public int Priority => 95;
    public string[] PreferredScenarios => new[] { "before_refactoring", "symbol_analysis", "impact_assessment", "before_deleting" };

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
            
            // Use SmartQueryPreprocessor for multi-field reference searching
            var searchMode = parameters.IncludePotential ? SearchMode.Standard : SearchMode.Symbol;
            var queryResult = _queryProcessor.Process(symbolName, searchMode);
            
            _logger.LogInformation("Find references: {Symbol} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
                symbolName, queryResult.TargetField, queryResult.ProcessedQuery, queryResult.Reason);
            
            Query query;
            if (parameters.IncludePotential)
            {
                // Broader search including partial matches using Standard analyzer
                var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LUCENE_VERSION);
                var parser = new QueryParser(LUCENE_VERSION, queryResult.TargetField, analyzer);
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
                true, // Include snippets for context
                cancellationToken);
            
            stopwatch.Stop();
            
            // Post-process results to highlight references
            if (searchResult.Hits != null)
            {
                foreach (var hit in searchResult.Hits)
                {
                    // Enhance context lines to show where the symbol is referenced
                    if (hit.ContextLines != null && hit.ContextLines.Count > 0)
                    {
                        hit.ContextLines = HighlightSymbolInContext(hit.ContextLines, symbolName, parameters.CaseSensitive);
                    }
                    
                    // Add reference-specific metadata
                    if (hit.Fields == null)
                        hit.Fields = new Dictionary<string, string>();
                    
                    hit.Fields["referenceType"] = DetermineReferenceType(hit.ContextLines, symbolName);
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
            var parser = new QueryParser(LUCENE_VERSION, "content", new Lucene.Net.Analysis.Standard.StandardAnalyzer(LUCENE_VERSION));
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
            var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LUCENE_VERSION);
            var parser = new QueryParser(LUCENE_VERSION, "content_symbols", analyzer);
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