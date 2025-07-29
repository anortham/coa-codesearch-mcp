using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Scoring;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// High-performance directory search using Lucene index - find folders by name with fuzzy matching
/// </summary>
public class FastDirectorySearchTool : ITool
{
    public string ToolName => "fast_directory_search";
    public string Description => "Search for directories with fuzzy matching";
    public ToolCategory Category => ToolCategory.Search;
    private readonly ILogger<FastDirectorySearchTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IFieldSelectorService _fieldSelectorService;
    private readonly IErrorRecoveryService _errorRecoveryService;
    private readonly AIResponseBuilderService _aiResponseBuilder;
    private readonly IScoringService? _scoringService;
    private readonly SearchResultResourceProvider? _searchResultResourceProvider;
    private readonly IResultConfidenceService? _resultConfidenceService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastDirectorySearchTool(
        ILogger<FastDirectorySearchTool> logger,
        ILuceneIndexService luceneIndexService,
        IFieldSelectorService fieldSelectorService,
        IErrorRecoveryService errorRecoveryService,
        AIResponseBuilderService aiResponseBuilder,
        IScoringService? scoringService = null,
        SearchResultResourceProvider? searchResultResourceProvider = null,
        IResultConfidenceService? resultConfidenceService = null)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _fieldSelectorService = fieldSelectorService;
        _errorRecoveryService = errorRecoveryService;
        _aiResponseBuilder = aiResponseBuilder;
        _scoringService = scoringService;
        _searchResultResourceProvider = searchResultResourceProvider;
        _resultConfidenceService = resultConfidenceService;
    }

    public async Task<object> ExecuteAsync(
        string query,
        string workspacePath,
        string? searchType = "standard",
        int maxResults = 30,
        bool includeFileCount = true,
        bool groupByDirectory = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fast directory search for '{Query}' in {WorkspacePath}, Type: {SearchType}", 
                query, workspacePath, searchType);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(query))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Directory search query cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("directoryQuery", "non-empty string"));
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Workspace path cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path"));
            }

            // Get index searcher
            IndexSearcher searcher;
            try
            {
                searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            }
            catch (DirectoryNotFoundException)
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.INDEX_NOT_FOUND,
                    $"No search index exists for {workspacePath}",
                    _errorRecoveryService.GetIndexNotFoundRecovery(workspacePath));
            }
            
            // Build the query based on search type
            Query luceneQuery = searchType?.ToLower() switch
            {
                "fuzzy" => BuildFuzzyQuery(query),
                "wildcard" => BuildWildcardQuery(query),
                "exact" => BuildExactQuery(query),
                "regex" => BuildRegexQuery(query),
                _ => BuildStandardQuery(query)
            };

            // Apply multi-factor scoring if service is available
            if (_scoringService != null)
            {
                var searchContext = new ScoringContext
                {
                    QueryText = query,
                    SearchType = searchType ?? "standard",
                    WorkspacePath = workspacePath
                };
                
                // For directory search, emphasize path relevance
                var enabledFactors = new HashSet<string> 
                { 
                    "PathRelevance", // Most important for directory search
                    "FilenameRelevance", // Directory names matter too
                    "RecencyBoost" // Recent directories might be more relevant
                };
                
                // Wrap query with multi-factor scoring
                luceneQuery = _scoringService.CreateScoredQuery(luceneQuery, searchContext, enabledFactors);
                _logger.LogDebug("Applied multi-factor scoring to directory search query");
            }

            // Execute search
            var startTime = DateTime.UtcNow;
            var topDocs = searcher.Search(luceneQuery, groupByDirectory ? 1000 : maxResults); // Get more if grouping
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Apply confidence-based result limiting if service is available
            int effectiveMaxResults = groupByDirectory ? 1000 : maxResults; // Don't limit if grouping
            string? confidenceInsight = null;
            if (_resultConfidenceService != null && !groupByDirectory)
            {
                var confidence = _resultConfidenceService.AnalyzeResults(topDocs, maxResults, false);
                effectiveMaxResults = confidence.RecommendedCount;
                confidenceInsight = confidence.Insight;
                _logger.LogDebug("Directory search confidence: level={Level}, recommended={Count}, topScore={Score:F2}", 
                    confidence.ConfidenceLevel, confidence.RecommendedCount, confidence.TopScore);
            }

            // Process results
            if (groupByDirectory)
            {
                var directoryGroups = new Dictionary<string, DirectoryInfo>();
                
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, "relativeDirectory", "directory");
                    var relativeDir = doc.Get("relativeDirectory") ?? "";
                    var directory = doc.Get("directory") ?? "";
                    
                    if (!directoryGroups.ContainsKey(relativeDir))
                    {
                        directoryGroups[relativeDir] = new DirectoryInfo
                        {
                            Path = directory,
                            RelativePath = relativeDir,
                            DirectoryName = doc.Get("directoryName") ?? "",
                            FileCount = 0,
                            Extensions = new HashSet<string>(),
                            Score = scoreDoc.Score
                        };
                    }
                    
                    var info = directoryGroups[relativeDir];
                    info.FileCount++;
                    var ext = doc.Get("extension");
                    if (!string.IsNullOrEmpty(ext))
                        info.Extensions.Add(ext);
                }
                
                var results = directoryGroups.Values
                    .OrderByDescending(d => d.Score)
                    .Take(maxResults)
                    .Select(d => new
                    {
                        path = d.Path,
                        relativePath = d.RelativePath,
                        directoryName = d.DirectoryName,
                        fileCount = includeFileCount ? d.FileCount : (int?)null,
                        fileTypes = includeFileCount ? d.Extensions.OrderBy(e => e).ToList() : null,
                        score = d.Score,
                        depth = d.RelativePath.Count(c => c == Path.DirectorySeparatorChar)
                    })
                    .ToList();

                _logger.LogInformation("Found {Count} directories in {Duration}ms - high performance search!", 
                    results.Count, searchDuration);

                // Use AIResponseBuilderService to build the response
                var mode = results.Count > 20 ? ResponseMode.Summary : ResponseMode.Full;
                var response = _aiResponseBuilder.BuildDirectorySearchResponse(
                    query,
                    searchType ?? "standard",
                    workspacePath,
                    results.Cast<dynamic>().ToList(),
                    searchDuration,
                    mode,
                    groupByDirectory,
                    topDocs.TotalHits);

                // Store search results as a resource if provider is available
                if (_searchResultResourceProvider != null && results.Count > 0)
                {
                    var resourceUri = _searchResultResourceProvider.StoreSearchResult(
                        query,
                        new
                        {
                            results = results,
                            query = ((dynamic)response).query,
                            summary = ((dynamic)response).summary,
                            searchType = searchType,
                            workspacePath = workspacePath,
                            groupByDirectory = groupByDirectory
                        },
                        new { tool = "directory_search", timestamp = DateTime.UtcNow }
                    );

                    // Add resourceUri to meta
                    var responseWithResource = new
                    {
                        success = ((dynamic)response).success,
                        operation = ((dynamic)response).operation,
                        query = ((dynamic)response).query,
                        summary = ((dynamic)response).summary,
                        analysis = ((dynamic)response).analysis,
                        results = ((dynamic)response).results,
                        resultsSummary = ((dynamic)response).resultsSummary,
                        insights = ((dynamic)response).insights,
                        actions = ((dynamic)response).actions,
                        meta = new
                        {
                            mode = ((dynamic)response).meta.mode,
                            truncated = ((dynamic)response).meta.truncated,
                            tokens = ((dynamic)response).meta.tokens,
                            cached = ((dynamic)response).meta.cached,
                            resourceUri = resourceUri
                        }
                    };

                    return responseWithResource;
                }

                return response;
            }
            else
            {
                // Return individual file results
                var results = new List<dynamic>();
                foreach (var scoreDoc in topDocs.ScoreDocs.Take(effectiveMaxResults))
                {
                    // Use field selector to load only directory-related fields for better performance
                    var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, FieldSetType.DirectoryListing);
                    
                    var relativePath = doc.Get("relativeDirectory") ?? doc.Get("directory") ?? "";
                    results.Add(new
                    {
                        path = doc.Get("directory") ?? "",
                        relativePath = relativePath,
                        directoryName = Path.GetFileName(relativePath.TrimEnd(Path.DirectorySeparatorChar)) ?? "",
                        fileCount = 1, // Individual file entries
                        fileTypes = new List<string> { doc.Get("extension") ?? "" }.Where(e => !string.IsNullOrEmpty(e)).ToList(),
                        score = scoreDoc.Score,
                        depth = relativePath.Count(c => c == Path.DirectorySeparatorChar)
                    });
                }

                // Use AIResponseBuilderService to build the response
                var mode = results.Count > 20 ? ResponseMode.Summary : ResponseMode.Full;
                var response = _aiResponseBuilder.BuildDirectorySearchResponse(
                    query,
                    searchType ?? "standard",
                    workspacePath,
                    results,
                    searchDuration,
                    mode,
                    groupByDirectory,
                    topDocs.TotalHits);

                // Store search results as a resource if provider is available
                if (_searchResultResourceProvider != null && results.Count > 0)
                {
                    var resourceUri = _searchResultResourceProvider.StoreSearchResult(
                        query,
                        new
                        {
                            results = results,
                            query = ((dynamic)response).query,
                            summary = ((dynamic)response).summary,
                            searchType = searchType,
                            workspacePath = workspacePath,
                            groupByDirectory = groupByDirectory
                        },
                        new { tool = "directory_search", timestamp = DateTime.UtcNow }
                    );

                    // Add resourceUri to meta
                    var responseWithResource = new
                    {
                        success = ((dynamic)response).success,
                        operation = ((dynamic)response).operation,
                        query = ((dynamic)response).query,
                        summary = ((dynamic)response).summary,
                        analysis = ((dynamic)response).analysis,
                        results = ((dynamic)response).results,
                        resultsSummary = ((dynamic)response).resultsSummary,
                        insights = ((dynamic)response).insights,
                        actions = ((dynamic)response).actions,
                        meta = new
                        {
                            mode = ((dynamic)response).meta.mode,
                            truncated = ((dynamic)response).meta.truncated,
                            tokens = ((dynamic)response).meta.tokens,
                            cached = ((dynamic)response).meta.cached,
                            resourceUri = resourceUri
                        }
                    };

                    return responseWithResource;
                }

                return response;
            }
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            _logger.LogWarning(cbEx, "Circuit breaker is open for directory search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.CIRCUIT_BREAKER_OPEN,
                cbEx.Message,
                _errorRecoveryService.GetCircuitBreakerOpenRecovery(cbEx.OperationName));
        }
        catch (DirectoryNotFoundException dnfEx)
        {
            _logger.LogError(dnfEx, "Directory not found for directory search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.DIRECTORY_NOT_FOUND,
                dnfEx.Message,
                _errorRecoveryService.GetDirectoryNotFoundRecovery(workspacePath));
        }
        catch (UnauthorizedAccessException uaEx)
        {
            _logger.LogError(uaEx, "Permission denied for directory search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.PERMISSION_DENIED,
                $"Permission denied accessing {workspacePath}: {uaEx.Message}",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fast directory search for query: {Query}", query);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                $"Search failed: {ex.Message}",
                null);
        }
    }

    private Query BuildStandardQuery(string query)
    {
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Version);
        var parser = new QueryParser(Version, "directory_text", analyzer);
        parser.AllowLeadingWildcard = true;
        
        // If query doesn't contain special operators, add wildcards
        if (!query.Contains('*') && !query.Contains('?') && !query.Contains('~'))
        {
            query = $"*{query}*";
        }
        
        return parser.Parse(query);
    }

    private Query BuildFuzzyQuery(string query)
    {
        if (!query.Contains('~'))
        {
            query = $"{query}~";
        }
        
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Version);
        var parser = new QueryParser(Version, "directory_text", analyzer);
        return parser.Parse(query);
    }

    private Query BuildWildcardQuery(string query)
    {
        if (!query.Contains('*') && !query.Contains('?'))
        {
            query = $"*{query}*";
        }
        
        return new WildcardQuery(new Term("directory_text", query.ToLower()));
    }

    private Query BuildExactQuery(string query)
    {
        return new TermQuery(new Term("directoryName", query));
    }

    private Query BuildRegexQuery(string query)
    {
        try
        {
            _ = new Regex(query);
            return new RegexpQuery(new Term("relativeDirectory", query));
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Invalid regex pattern: {Query}, falling back to standard search", query);
            return BuildStandardQuery(query);
        }
    }

    private class DirectoryInfo
    {
        public string Path { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string DirectoryName { get; set; } = "";
        public int FileCount { get; set; }
        public HashSet<string> Extensions { get; set; } = new();
        public float Score { get; set; }
    }
}