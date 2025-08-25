using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using COA.CodeSearch.McpServer.Models.Api;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Controllers;

/// <summary>
/// API controller for search operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly ILuceneIndexService _luceneService;
    private readonly LineAwareSearchService _lineSearchService;
    private readonly ConfidenceCalculatorService _confidenceCalculator;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        ILuceneIndexService luceneService,
        LineAwareSearchService lineSearchService,
        ConfidenceCalculatorService confidenceCalculator,
        QueryPreprocessor queryPreprocessor,
        ILogger<SearchController> logger)
    {
        _luceneService = luceneService;
        _lineSearchService = lineSearchService;
        _confidenceCalculator = confidenceCalculator;
        _queryPreprocessor = queryPreprocessor;
        _logger = logger;
    }

    /// <summary>
    /// Search for symbols (classes, interfaces, methods, properties, etc.)
    /// </summary>
    /// <param name="name">Symbol name to search for</param>
    /// <param name="type">Symbol type filter (class, interface, method, property, enum)</param>
    /// <param name="workspace">Workspace path to search in</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>Search results with confidence scores</returns>
    [HttpGet("symbol")]
    public async Task<ActionResult<SearchResponse>> SearchSymbol(
        [FromQuery, Required] string name,
        [FromQuery] string? type = null,
        [FromQuery] string? workspace = null,
        [FromQuery] int limit = 10)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Searching for symbol: {Name}, Type: {Type}, Workspace: {Workspace}", 
                name, type, workspace);

            if (limit <= 0 || limit > 100)
                limit = 10;

            // Build search query based on symbol type
            var queryText = BuildSymbolQuery(name, type);
            
            // Use standard analyzer and query preprocessor
            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var query = _queryPreprocessor.BuildQuery(queryText, "standard", false, analyzer);
            
            var searchResults = await _luceneService.SearchAsync(workspace, query, limit * 2);
            
            var apiResults = new List<Models.Api.SearchResult>();
            
            foreach (var hit in searchResults.Hits)
            {
                // Line number is ALREADY calculated by LuceneIndexService!
                if (!hit.LineNumber.HasValue)
                {
                    _logger.LogWarning("No line number for hit in {File}", hit.FilePath);
                    continue;
                }
                
                // Get line content for preview (from context or content field)
                string preview = "";
                int column = 1;
                
                if (hit.ContextLines?.Any() == true)
                {
                    // Use context lines if available
                    preview = hit.ContextLines.First();
                    column = CalculateColumnPosition(preview, name);
                }
                else if (hit.Fields.TryGetValue("content", out var content))
                {
                    // Fall back to parsing content
                    var lines = content.Split('\n');
                    if (hit.LineNumber.Value <= lines.Length)
                    {
                        preview = lines[hit.LineNumber.Value - 1].Trim();
                        column = CalculateColumnPosition(preview, name);
                    }
                }
                
                var apiResult = new Models.Api.SearchResult
                {
                    FilePath = hit.FilePath,
                    Line = hit.LineNumber.Value,
                    Column = column,
                    Confidence = _confidenceCalculator.CalculateConfidence(
                        preview, name, type, Path.GetFileName(hit.FilePath)),
                    Preview = preview,
                    SymbolType = DetectSymbolType(preview, name),
                    Metadata = new Dictionary<string, object>
                    {
                        ["score"] = hit.Score,
                        ["hasContext"] = hit.ContextLines?.Any() == true
                    }
                };
                    
                if (apiResult.Confidence >= 0.3) // Filter out very low confidence results
                {
                    apiResults.Add(apiResult);
                }
            }
            
            apiResults = apiResults
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => (double)(r.Metadata?["score"] ?? 0))
                .Take(limit)
                .ToList();

            var response = new SearchResponse
            {
                Results = apiResults,
                TotalCount = searchResults.TotalHits,
                SearchTimeMs = stopwatch.ElapsedMilliseconds,
                Query = queryText,
                Workspace = workspace
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for symbol: {Name}", name);
            return StatusCode(500, new { error = "Internal server error during symbol search" });
        }
    }

    /// <summary>
    /// Search for text content
    /// </summary>
    /// <param name="query">Text to search for</param>
    /// <param name="exact">Whether to perform exact match</param>
    /// <param name="workspace">Workspace path to search in</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>Search results with confidence scores</returns>
    [HttpGet("text")]
    public async Task<ActionResult<SearchResponse>> SearchText(
        [FromQuery, Required] string query,
        [FromQuery] bool exact = false,
        [FromQuery] string? workspace = null,
        [FromQuery] int limit = 20)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Searching for text: {Query}, Exact: {Exact}, Workspace: {Workspace}", 
                query, exact, workspace);

            if (limit <= 0 || limit > 100)
                limit = 20;

            var searchQuery = exact ? $"\"{query}\"" : query;
            
            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var luceneQuery = _queryPreprocessor.BuildQuery(searchQuery, "standard", false, analyzer);
            
            var searchResults = await _luceneService.SearchAsync(workspace, luceneQuery, limit * 2);
            
            var apiResults = ProcessSearchHits(searchResults.Hits, query, null, limit);

            var response = new SearchResponse
            {
                Results = apiResults,
                TotalCount = searchResults.TotalHits,
                SearchTimeMs = stopwatch.ElapsedMilliseconds,
                Query = searchQuery,
                Workspace = workspace
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for text: {Query}", query);
            return StatusCode(500, new { error = "Internal server error during text search" });
        }
    }

    /// <summary>
    /// Quick check if a symbol exists in the workspace
    /// </summary>
    /// <param name="name">Symbol name to check</param>
    /// <param name="workspace">Workspace path to check in</param>
    /// <returns>Existence information</returns>
    [HttpGet("../check/exists")]
    public async Task<ActionResult<ExistsResponse>> CheckExists(
        [FromQuery, Required] string name,
        [FromQuery] string? workspace = null)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Checking existence of: {Name}, Workspace: {Workspace}", name, workspace);

            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var query = _queryPreprocessor.BuildQuery(name, "standard", false, analyzer);
            
            var searchResults = await _luceneService.SearchAsync(workspace, query, 10);
            
            var symbolTypes = new List<string>();
            foreach (var hit in searchResults.Hits.Take(5))
            {
                // Use already calculated line number
                if (!hit.LineNumber.HasValue)
                    continue;
                    
                // Get line content for type detection
                string lineContent = "";
                if (hit.ContextLines?.Any() == true)
                {
                    lineContent = hit.ContextLines.First();
                }
                else if (hit.Fields.TryGetValue("content", out var content))
                {
                    var lines = content.Split('\n');
                    if (hit.LineNumber.Value <= lines.Length)
                    {
                        lineContent = lines[hit.LineNumber.Value - 1];
                    }
                }
                
                var symbolType = DetectSymbolType(lineContent, name);
                if (!string.IsNullOrEmpty(symbolType) && !symbolTypes.Contains(symbolType))
                {
                    symbolTypes.Add(symbolType);
                }
            }

            var response = new ExistsResponse
            {
                Exists = searchResults.TotalHits > 0,
                Count = searchResults.TotalHits,
                Types = symbolTypes,
                SearchTimeMs = stopwatch.ElapsedMilliseconds
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of: {Name}", name);
            return StatusCode(500, new { error = "Internal server error during existence check" });
        }
    }

    /// <summary>
    /// Perform multiple searches in a single request
    /// </summary>
    /// <param name="request">Batch search request</param>
    /// <returns>Results for all searches</returns>
    [HttpPost("batch")]
    public async Task<ActionResult<BatchSearchResponse>> BatchSearch([FromBody] BatchSearchRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<SearchResponse>();
        var errors = new List<string>();
        var successCount = 0;

        try
        {
            _logger.LogDebug("Performing batch search with {Count} items", request.Searches.Count);

            foreach (var searchItem in request.Searches)
            {
                try
                {
                    var searchResult = searchItem.Type.ToLower() switch
                    {
                        "symbol" => await PerformSymbolSearch(searchItem, request.Workspace),
                        "text" => await PerformTextSearch(searchItem, request.Workspace),
                        _ => throw new ArgumentException($"Unknown search type: {searchItem.Type}")
                    };
                    
                    results.Add(searchResult);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch search item: {Type} {Query}", 
                        searchItem.Type, searchItem.Query);
                    errors.Add($"Error in {searchItem.Type} search for '{searchItem.Query}': {ex.Message}");
                    
                    // Add empty result to maintain order
                    results.Add(new SearchResponse
                    {
                        Results = new List<Models.Api.SearchResult>(),
                        TotalCount = 0,
                        SearchTimeMs = 0,
                        Query = searchItem.Query,
                        Workspace = request.Workspace
                    });
                }
            }

            var response = new BatchSearchResponse
            {
                Results = results,
                TotalTimeMs = stopwatch.ElapsedMilliseconds,
                SuccessfulSearches = successCount,
                Errors = errors.Count > 0 ? errors : null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch search");
            return StatusCode(500, new { error = "Internal server error during batch search" });
        }
    }

    #region Private Helper Methods

    private string BuildSymbolQuery(string name, string? type)
    {
        // Build a query that looks for symbol definitions
        return type?.ToLower() switch
        {
            "class" => $"class {name}",
            "interface" => $"interface {name}",
            "method" => name, // Methods are harder to identify without context
            "property" => name,
            "enum" => $"enum {name}",
            _ => name
        };
    }

    private int CalculateColumnPosition(string lineContent, string searchTerm)
    {
        var index = lineContent.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? index + 1 : 1; // 1-based indexing for Roslyn
    }

    private string? DetectSymbolType(string lineContent, string symbolName)
    {
        var line = lineContent.Trim().ToLower();
        
        if (line.Contains($"class {symbolName.ToLower()}"))
            return "class";
        if (line.Contains($"interface {symbolName.ToLower()}"))
            return "interface";
        if (line.Contains($"enum {symbolName.ToLower()}"))
            return "enum";
        if (line.Contains($"struct {symbolName.ToLower()}"))
            return "struct";
        if (line.Contains($"{symbolName.ToLower()}("))
            return "method";
        if (line.Contains($"{symbolName.ToLower()} {{") || line.Contains($"{symbolName.ToLower()} => "))
            return "property";
        
        return null;
    }

    private string GetLineFromContent(string content, int lineNumber)
    {
        if (string.IsNullOrEmpty(content))
            return "";
            
        var lines = content.Split('\n');
        if (lineNumber > 0 && lineNumber <= lines.Length)
        {
            return lines[lineNumber - 1]; // Convert to 0-based indexing
        }
        
        return "";
    }

    private List<Models.Api.SearchResult> ProcessSearchHits(IEnumerable<SearchHit> hits, string searchTerm, string? symbolType, int limit)
    {
        var results = new List<Models.Api.SearchResult>();
        
        foreach (var hit in hits)
        {
            // Line number is ALREADY calculated by LuceneIndexService!
            if (!hit.LineNumber.HasValue)
            {
                _logger.LogWarning("No line number for hit in {File}", hit.FilePath);
                continue;
            }
            
            // Get line content for preview
            string preview = "";
            int column = 1;
            
            if (hit.ContextLines?.Any() == true)
            {
                // Use context lines if available
                preview = hit.ContextLines.First();
                column = CalculateColumnPosition(preview, searchTerm);
            }
            else if (hit.Fields.TryGetValue("content", out var content))
            {
                // Fall back to parsing content
                var lines = content.Split('\n');
                if (hit.LineNumber.Value <= lines.Length)
                {
                    preview = lines[hit.LineNumber.Value - 1].Trim();
                    column = CalculateColumnPosition(preview, searchTerm);
                }
            }
            
            var result = new Models.Api.SearchResult
            {
                FilePath = hit.FilePath,
                Line = hit.LineNumber.Value,
                Column = column,
                Confidence = _confidenceCalculator.CalculateConfidence(
                    preview, searchTerm, symbolType, Path.GetFileName(hit.FilePath)),
                Preview = preview,
                SymbolType = DetectSymbolType(preview, searchTerm),
                Metadata = new Dictionary<string, object>
                {
                    ["score"] = hit.Score,
                    ["hasContext"] = hit.ContextLines?.Any() == true
                }
            };
            
            if (result.Confidence >= 0.3)
            {
                results.Add(result);
            }
        }
        
        return results
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => (double)(r.Metadata?["score"] ?? 0))
            .Take(limit)
            .ToList();
    }

    private async Task<SearchResponse> PerformSymbolSearch(SearchItem item, string workspace)
    {
        var type = item.Options?.GetValueOrDefault("type");
        var limit = int.TryParse(item.Options?.GetValueOrDefault("limit"), out var l) ? l : 10;
        
        var actionResult = await SearchSymbol(item.Query, type, workspace, limit);
        return actionResult.Value ?? new SearchResponse
        {
            Results = new List<Models.Api.SearchResult>(),
            TotalCount = 0,
            SearchTimeMs = 0,
            Query = item.Query,
            Workspace = workspace
        };
    }

    private async Task<SearchResponse> PerformTextSearch(SearchItem item, string workspace)
    {
        var exact = bool.TryParse(item.Options?.GetValueOrDefault("exact"), out var e) && e;
        var limit = int.TryParse(item.Options?.GetValueOrDefault("limit"), out var l) ? l : 20;
        
        var actionResult = await SearchText(item.Query, exact, workspace, limit);
        return actionResult.Value ?? new SearchResponse
        {
            Results = new List<Models.Api.SearchResult>(),
            TotalCount = 0,
            SearchTimeMs = 0,
            Query = item.Query,
            Workspace = workspace
        };
    }

    #endregion
}