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
using COA.CodeSearch.McpServer.Services.Sqlite;
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
    private readonly ICallPathTracerService _callPathTracer;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly TraceCallPathResponseBuilder _responseBuilder;
    private readonly ILogger<TraceCallPathTool> _logger;

    /// <summary>
    /// Initializes a new instance of the TraceCallPathTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="callPathTracer">Call path tracing service</param>
    /// <param name="sqliteService">SQLite symbol service for fast exact matching</param>
    /// <param name="pathResolutionService">Path resolution service for workspace defaults</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="logger">Logger instance</param>
    public TraceCallPathTool(
        IServiceProvider serviceProvider,
        ICallPathTracerService callPathTracer,
        ISQLiteSymbolService sqliteService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<TraceCallPathTool> logger) : base(serviceProvider, logger)
    {
        _callPathTracer = callPathTracer;
        _sqliteService = sqliteService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
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

        // Use provided workspace path or default to current workspace
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
                _logger.LogDebug("Returning cached trace call path result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // TIER 1: Verify symbol exists via SQLite (0-1ms) - fast validation before expensive call tracing
            var symbolExists = await TryVerifySymbolExistsAsync(workspacePath, symbolName, parameters.CaseSensitive, cancellationToken);
            if (symbolExists)
            {
                _logger.LogInformation("✅ Tier 1 HIT: Symbol '{Symbol}' found in SQLite in {Ms}ms",
                    symbolName, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("⏭️ Tier 1 MISS: Symbol '{Symbol}' not found in SQLite, attempting fuzzy call trace",
                    symbolName);
            }

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
            
            // Add specific insights for call path tracing (3-tier architecture showcase!)
            if (response.Insights != null)
            {
                response.Insights.Insert(0, $"⚡ Traced {parameters.Direction} call path for '{symbolName}' - found {searchResult.TotalHits} references");

                if (searchResult.Hits != null)
                {
                    // Count exact vs semantic matches
                    var exactMatches = searchResult.Hits.Where(h => h.Fields?.GetValueOrDefault("is_semantic_match") != "true").Count();
                    var semanticMatches = searchResult.Hits.Where(h => h.Fields?.GetValueOrDefault("is_semantic_match") == "true").Count();

                    if (exactMatches > 0 && semanticMatches > 0)
                    {
                        response.Insights.Add($"🎯 3-Tier Results: {exactMatches} exact (SQL CTE) + {semanticMatches} semantic bridges");
                    }
                    else if (exactMatches > 0)
                    {
                        response.Insights.Add($"🎯 Tier 1 (SQL CTE): {exactMatches} exact call paths");
                    }

                    if (semanticMatches > 0)
                    {
                        response.Insights.Add($"🌉 Cross-language bridges: {semanticMatches} semantic matches (confidence >= 0.7)");
                    }

                    var fileCount = searchResult.Hits.Select(h => h.FilePath).Distinct().Count();
                    var languageCount = searchResult.Hits
                        .Select(h => System.IO.Path.GetExtension(h.FilePath).ToLowerInvariant())
                        .Distinct()
                        .Count();

                    response.Insights.Add($"📂 Call path spans {fileCount} files across {languageCount} languages");

                    var entryPoints = searchResult.Hits.Where(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true").Count();
                    if (entryPoints > 0)
                    {
                        response.Insights.Add($"🚪 Found {entryPoints} entry points (controllers, main methods, etc.)");
                    }

                    // Show max depth reached
                    var maxDepthReached = searchResult.Hits
                        .Where(h => h.Fields?.ContainsKey("call_depth") == true)
                        .Select(h => int.TryParse(h.Fields["call_depth"], out var d) ? d : 0)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (maxDepthReached > 0)
                    {
                        response.Insights.Add($"📊 Call hierarchy depth: {maxDepthReached + 1} levels (0-{maxDepthReached})");
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
    /// Trace calls based on direction parameter using SQLite-based call path tracer
    /// </summary>
    private async Task<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> TraceCallsAsync(
        string symbolName,
        string workspacePath,
        TraceCallPathParameters parameters,
        CancellationToken cancellationToken)
    {
        List<CallPathNode> callPathNodes;

        // Use direction parameter to determine tracing direction
        switch (parameters.Direction.ToLowerInvariant())
        {
            case "up":
            case "upward":
                callPathNodes = await _callPathTracer.TraceUpwardAsync(
                    workspacePath,
                    symbolName,
                    parameters.MaxDepth,
                    parameters.CaseSensitive,
                    cancellationToken);
                break;

            case "down":
            case "downward":
                callPathNodes = await _callPathTracer.TraceDownwardAsync(
                    workspacePath,
                    symbolName,
                    parameters.MaxDepth,
                    parameters.CaseSensitive,
                    cancellationToken);
                break;

            case "both":
                var bothResult = await _callPathTracer.TraceBothDirectionsAsync(
                    workspacePath,
                    symbolName,
                    parameters.MaxDepth,
                    parameters.CaseSensitive,
                    cancellationToken);
                // Combine callers and callees
                callPathNodes = bothResult.Callers.Concat(bothResult.Callees).ToList();
                break;

            default:
                // Default to upward tracing
                callPathNodes = await _callPathTracer.TraceUpwardAsync(
                    workspacePath,
                    symbolName,
                    parameters.MaxDepth,
                    parameters.CaseSensitive,
                    cancellationToken);
                break;
        }

        // Convert CallPathNodes to SearchHits for compatibility with response builder
        var hits = ConvertCallPathNodesToSearchHits(callPathNodes, symbolName, parameters.Direction);

        return new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
        {
            TotalHits = hits.Count,
            Hits = hits,
            Query = $"trace_call_path:{symbolName}",
            SearchTime = TimeSpan.Zero
        };
    }

    /// <summary>
    /// Convert CallPathNodes to SearchHits for response builder compatibility
    /// </summary>
    private List<SearchHit> ConvertCallPathNodesToSearchHits(
        List<CallPathNode> nodes,
        string symbolName,
        string direction)
    {
        var hits = new List<SearchHit>();

        void ProcessNode(CallPathNode node, int hierarchyLevel)
        {
            var contextLines = node.Identifier.CodeContext != null
                ? new List<string> { node.Identifier.CodeContext }
                : new List<string>();

            var hit = new SearchHit
            {
                FilePath = node.Identifier.FilePath,
                LineNumber = node.Identifier.StartLine,
                Snippet = node.Identifier.CodeContext ?? string.Empty,
                ContextLines = contextLines,
                Score = 1.0f - (node.Depth * 0.1f), // Higher score for shallower depth
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = direction,
                    ["trace_symbol"] = symbolName,
                    ["call_depth"] = node.Depth.ToString(),
                    ["hierarchy_level"] = hierarchyLevel.ToString(),
                    ["identifier_kind"] = node.Identifier.Kind,
                    ["is_semantic_match"] = node.IsSemanticMatch.ToString().ToLowerInvariant(),
                    ["confidence"] = node.Confidence.ToString("F2"),
                    ["column_number"] = node.Identifier.StartColumn.ToString()
                }
            };

            if (node.ContainingSymbol != null)
            {
                hit.Fields["containing_symbol"] = node.ContainingSymbol.Name;
                hit.Fields["containing_symbol_kind"] = node.ContainingSymbol.Kind;
                hit.Fields["is_entry_point"] = DetectEntryPoint(node.ContainingSymbol).ToString().ToLowerInvariant();
            }

            if (node.TargetSymbol != null)
            {
                hit.Fields["target_symbol"] = node.TargetSymbol.Name;
                hit.Fields["target_symbol_kind"] = node.TargetSymbol.Kind;
            }

            hits.Add(hit);

            // Recursively process children
            foreach (var child in node.Children)
            {
                ProcessNode(child, hierarchyLevel + 1);
            }
        }

        foreach (var node in nodes)
        {
            ProcessNode(node, 0);
        }

        return hits;
    }

    /// <summary>
    /// Detect if a symbol represents an entry point (controller, main method, etc.)
    /// </summary>
    private bool DetectEntryPoint(Services.Julie.JulieSymbol symbol)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(symbol.FilePath)?.ToLowerInvariant();
            var symbolName = symbol.Name.ToLowerInvariant();
            var symbolKind = symbol.Kind.ToLowerInvariant();

            // Entry point patterns
            var entryPointPatterns = new[] {
                "controller", "handler", "service", "main", "program", "startup", "app"
            };

            // Check symbol name or file name patterns
            if (entryPointPatterns.Any(pattern =>
                symbolName.Contains(pattern) || fileName?.Contains(pattern) == true))
            {
                return true;
            }

            // Check if it's a main method
            if (symbolName == "main" && symbolKind == "method")
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
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

    /// <summary>
    /// TIER 1: Verify symbol exists in SQLite for fast validation (0-1ms)
    /// This prevents expensive call path tracing for non-existent symbols
    /// </summary>
    private async Task<bool> TryVerifySymbolExistsAsync(
        string workspacePath,
        string symbolName,
        bool caseSensitive,
        CancellationToken cancellationToken)
    {
        try
        {
            // Query SQLite for exact symbol name match
            var sqliteSymbols = await _sqliteService.GetSymbolsByNameAsync(
                workspacePath,
                symbolName,
                caseSensitive,
                cancellationToken);

            return sqliteSymbols != null && sqliteSymbols.Any();
        }
        catch (Exception ex)
        {
            // Log but don't fail - fall back to full call tracing
            _logger.LogDebug(ex, "Failed to verify symbol existence in SQLite for '{Symbol}'", symbolName);
            return false; // Assume symbol might exist, proceed with call tracing
        }
    }
}