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
    private readonly IScoringService? _scoringService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastDirectorySearchTool(
        ILogger<FastDirectorySearchTool> logger,
        ILuceneIndexService luceneIndexService,
        IFieldSelectorService fieldSelectorService,
        IErrorRecoveryService errorRecoveryService,
        IScoringService? scoringService = null)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _fieldSelectorService = fieldSelectorService;
        _errorRecoveryService = errorRecoveryService;
        _scoringService = scoringService;
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

                return new
                {
                    success = true,
                    query = query,
                    searchType = searchType,
                    workspacePath = workspacePath,
                    totalResults = results.Count,
                    searchDurationMs = searchDuration,
                    results = results,
                    performance = searchDuration < 20 ? "excellent" : "very fast"
                };
            }
            else
            {
                // Return individual file results
                var results = new List<object>();
                foreach (var scoreDoc in topDocs.ScoreDocs.Take(maxResults))
                {
                    // Use field selector to load only directory-related fields for better performance
                    var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, FieldSetType.DirectoryListing);
                    
                    results.Add(new
                    {
                        path = doc.Get("path"),
                        filename = doc.Get("filename"),
                        directory = doc.Get("directory"),
                        relativeDirectory = doc.Get("relativeDirectory"),
                        directoryName = doc.Get("directoryName"),
                        extension = doc.Get("extension"),
                        score = scoreDoc.Score
                    });
                }

                return new
                {
                    success = true,
                    query = query,
                    searchType = searchType,
                    workspacePath = workspacePath,
                    totalResults = results.Count,
                    searchDurationMs = searchDuration,
                    results = results,
                    performance = searchDuration < 20 ? "excellent" : "very fast"
                };
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