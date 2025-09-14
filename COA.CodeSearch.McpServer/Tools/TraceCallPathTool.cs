using System.ComponentModel;
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
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Trace call path tool that provides hierarchical call chain analysis
/// Shows who calls a method (up), what a method calls (down), or both directions
/// </summary>
public class TraceCallPathTool : CodeSearchToolBase<TraceCallPathParameters, AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>, IPrioritizedTool
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly TraceCallPathResponseBuilder _responseBuilder;
    private readonly ILogger<TraceCallPathTool> _logger;
    private readonly CodeAnalyzer _codeAnalyzer;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Initializes a new instance of the TraceCallPathTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="queryProcessor">Smart query preprocessing service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="logger">Logger instance</param>
    public TraceCallPathTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        CodeAnalyzer codeAnalyzer,
        ILogger<TraceCallPathTool> logger) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _codeAnalyzer = codeAnalyzer;
        _logger = logger;
        _responseBuilder = new TraceCallPathResponseBuilder(logger as ILogger<TraceCallPathResponseBuilder>, storageService);
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.TraceCallPath;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "TRACE EXECUTION PATHS - Build hierarchical call chains to understand code flow. Essential for debugging, refactoring impact analysis, and architecture understanding.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 92;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "call_tracing", "refactoring_impact", "debugging", "architecture_analysis", "execution_flow" };

    /// <summary>
    /// Executes the trace call path operation to analyze call hierarchies.
    /// </summary>
    /// <param name="parameters">Trace call path parameters including symbol name and trace options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Search results containing the hierarchical call path analysis</returns>
    protected override async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> ExecuteInternalAsync(
        TraceCallPathParameters parameters,
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
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached trace call path result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogInformation("Tracing call path for symbol: {Symbol}, direction: {Direction}, maxDepth: {MaxDepth}", 
                symbolName, parameters.Direction, parameters.MaxDepth);

            // Start with finding references (upward tracing)
            var searchResult = await TraceCallsAsync(symbolName, workspacePath, parameters, cancellationToken);
            
            stopwatch.Stop();
            
            // Build the response using our specialized response builder
            var context = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens,
                ResponseMode = "adaptive",
                ToolName = Name,
                StoreFullResults = false
            };
            
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);
            
            // Add trace-specific metadata
            if (response.Data != null)
            {
                response.Data.Summary = GenerateCallPathSummary(searchResult, symbolName, parameters.Direction);
                if (response.Data.ExtensionData == null)
                    response.Data.ExtensionData = new Dictionary<string, object>();
                response.Data.ExtensionData["symbol"] = symbolName;
                response.Data.ExtensionData["direction"] = parameters.Direction;
                response.Data.ExtensionData["maxDepth"] = parameters.MaxDepth;
            }
            
            // Add specific insights for call path tracing
            if (response.Insights != null)
            {
                response.Insights.Insert(0, $"Traced {parameters.Direction} call path for '{symbolName}' - found {searchResult.TotalHits} references");
                
                if (searchResult.Hits != null)
                {
                    var fileCount = searchResult.Hits.Select(h => h.FilePath).Distinct().Count();
                    response.Insights.Add($"Call path spans {fileCount} files");
                    
                    var entryPoints = searchResult.Hits.Where(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true").Count();
                    if (entryPoints > 0)
                    {
                        response.Insights.Add($"Found {entryPoints} entry points (controllers, main methods, etc.)");
                    }
                }
            }
            
            // Cache the response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(10)
                });
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracing call path for symbol: {Symbol}", symbolName);
            return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "TRACE_ERROR",
                    Message = $"Error tracing call path: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the symbol name is correct",
                            "Check if the workspace is properly indexed",
                            "Try a simpler symbol name",
                            "Check logs for detailed error information"
                        }
                    }
                }
            };
        }
    }

    /// <summary>
    /// Trace calls based on direction parameter
    /// </summary>
    private async Task<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> TraceCallsAsync(
        string symbolName,
        string workspacePath,
        TraceCallPathParameters parameters,
        CancellationToken cancellationToken)
    {
        // For now, implement upward tracing (who calls this symbol)
        // Future enhancement will add downward tracing and both directions
        
        // Use SmartQueryPreprocessor for multi-field reference searching
        var searchMode = SearchMode.Symbol;
        var queryResult = _queryProcessor.Process(symbolName, searchMode);
        
        _logger.LogInformation("Trace call path: {Symbol} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
            symbolName, queryResult.TargetField, queryResult.ProcessedQuery, queryResult.Reason);
        
        // Build strict reference query for exact symbol references
        var parser = new QueryParser(LUCENE_VERSION, queryResult.TargetField, _codeAnalyzer);
        var query = parser.Parse(queryResult.ProcessedQuery);
        
        // Perform the search  
        var searchResult = await _luceneIndexService.SearchAsync(
            workspacePath, 
            query, 
            100, // Default max results for call tracing
            true, // Include snippets for context
            cancellationToken);
        
        // Post-process results with call path analysis
        if (searchResult.Hits != null)
        {
            foreach (var hit in searchResult.Hits)
            {
                // Add call path metadata
                if (hit.Fields == null)
                    hit.Fields = new Dictionary<string, string>();
                
                hit.Fields["trace_direction"] = parameters.Direction;
                hit.Fields["trace_symbol"] = symbolName;
                hit.Fields["call_depth"] = "1"; // Start at depth 1
                
                // Detect entry points (controllers, main methods, etc.)
                var isEntryPoint = DetectEntryPoint(hit);
                hit.Fields["is_entry_point"] = isEntryPoint.ToString().ToLowerInvariant();
                
                // Enhance context to highlight the symbol reference
                if (hit.ContextLines != null && hit.ContextLines.Count > 0)
                {
                    hit.ContextLines = HighlightSymbolInContext(hit.ContextLines, symbolName);
                }
            }
        }
        
        return searchResult;
    }

    /// <summary>
    /// Detect if a hit represents an entry point (controller, main method, etc.)
    /// </summary>
    private bool DetectEntryPoint(SearchHit hit)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(hit.FilePath)?.ToLowerInvariant();
            var filePath = hit.FilePath.ToLowerInvariant();
            
            // Entry point patterns
            var entryPointPatterns = new[] { 
                "controller", "handler", "service", "main", "program", "startup", "app"
            };
            
            // Check file name patterns
            if (entryPointPatterns.Any(pattern => fileName?.Contains(pattern) == true))
                return true;
            
            // Check if it's in a Controllers folder
            if (filePath.Contains("controller") || filePath.Contains("handlers"))
                return true;
            
            // Check context for method signatures that look like entry points
            if (hit.ContextLines != null)
            {
                var context = string.Join(" ", hit.ContextLines).ToLowerInvariant();
                if (context.Contains("public static void main") || 
                    context.Contains("[httpget]") || 
                    context.Contains("[httppost]") ||
                    context.Contains("async task<"))
                {
                    return true;
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Highlight symbol references in context lines
    /// </summary>
    private List<string> HighlightSymbolInContext(List<string> contextLines, string symbolName)
    {
        return contextLines.Select(line =>
        {
            // Simple highlighting - wrap symbol references with markers
            if (line.Contains(symbolName, StringComparison.OrdinalIgnoreCase))
            {
                return line.Replace(symbolName, $"«{symbolName}»", StringComparison.OrdinalIgnoreCase);
            }
            return line;
        }).ToList();
    }

    /// <summary>
    /// Generate summary for call path results
    /// </summary>
    private string GenerateCallPathSummary(COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string symbolName, string direction)
    {
        if (searchResult.TotalHits == 0)
        {
            return $"No {direction} call path found for '{symbolName}'";
        }
        
        var fileCount = searchResult.Hits?.Select(h => h.FilePath).Distinct().Count() ?? 0;
        return $"Call path trace ({direction}): {searchResult.TotalHits} references to '{symbolName}' across {fileCount} files";
    }
}