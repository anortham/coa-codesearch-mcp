using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Scoring;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of FastFileSearchTool with structured response format
/// </summary>
public class FastFileSearchToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "fast_file_search_v2";
    public override string Description => "AI-optimized file search with hotspots";
    public override ToolCategory Category => ToolCategory.Search;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IConfiguration _configuration;
    private readonly IFieldSelectorService _fieldSelectorService;
    private readonly IErrorRecoveryService _errorRecoveryService;
    private readonly SearchResultResourceProvider? _searchResultResourceProvider;
    private readonly IScoringService? _scoringService;
    private readonly IResultConfidenceService? _resultConfidenceService;
    private readonly AIResponseBuilderService _aiResponseBuilder;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastFileSearchToolV2(
        ILogger<FastFileSearchToolV2> logger,
        ILuceneIndexService luceneIndexService,
        IConfiguration configuration,
        IFieldSelectorService fieldSelectorService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache,
        IErrorRecoveryService errorRecoveryService,
        AIResponseBuilderService aiResponseBuilder,
        SearchResultResourceProvider? searchResultResourceProvider = null,
        IScoringService? scoringService = null,
        IResultConfidenceService? resultConfidenceService = null)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _luceneIndexService = luceneIndexService;
        _configuration = configuration;
        _fieldSelectorService = fieldSelectorService;
        _errorRecoveryService = errorRecoveryService;
        _aiResponseBuilder = aiResponseBuilder;
        _searchResultResourceProvider = searchResultResourceProvider;
        _scoringService = scoringService;
        _resultConfidenceService = resultConfidenceService;
    }

    public async Task<object> ExecuteAsync(
        string query,
        string workspacePath,
        string? searchType = "standard",
        int maxResults = 50,
        bool includeDirectories = false,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("FastFileSearchV2 for '{Query}' in {WorkspacePath}, Type: {SearchType}", 
                query, workspacePath, searchType);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(query))
            {
                return new
                {
                    success = false,
                    error = new
                    {
                        code = ErrorCodes.VALIDATION_ERROR,
                        message = "Search query cannot be empty",
                        recovery = _errorRecoveryService.GetValidationErrorRecovery("nameQuery", "non-empty string")
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = new
                    {
                        code = ErrorCodes.VALIDATION_ERROR,
                        message = "Workspace path cannot be empty",
                        recovery = _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path")
                    }
                };
            }

            // Get index searcher
            IndexSearcher searcher;
            try
            {
                searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            }
            catch (DirectoryNotFoundException)
            {
                return new
                {
                    success = false,
                    error = new
                    {
                        code = ErrorCodes.INDEX_NOT_FOUND,
                        message = $"No search index exists for {workspacePath}",
                        recovery = _errorRecoveryService.GetIndexNotFoundRecovery(workspacePath)
                    }
                };
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
                
                // For file search, we might want to emphasize filename relevance more
                var enabledFactors = new HashSet<string> 
                { 
                    "FilenameRelevance", // Most important for file search
                    "PathRelevance", 
                    "RecencyBoost", 
                    "FileTypeRelevance" 
                };
                
                // Wrap query with multi-factor scoring
                luceneQuery = _scoringService.CreateScoredQuery(luceneQuery, searchContext, enabledFactors);
                Logger.LogDebug("Applied multi-factor scoring to file search query");
            }

            // Execute search
            var startTime = DateTime.UtcNow;
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Apply confidence-based result limiting if service is available
            int effectiveMaxResults = maxResults;
            string? confidenceInsight = null;
            if (_resultConfidenceService != null)
            {
                var confidence = _resultConfidenceService.AnalyzeResults(topDocs, maxResults, false); // No context for file search
                effectiveMaxResults = confidence.RecommendedCount;
                confidenceInsight = confidence.Insight;
                Logger.LogDebug("File search confidence: level={Level}, recommended={Count}, topScore={Score:F2}", 
                    confidence.ConfidenceLevel, confidence.RecommendedCount, confidence.TopScore);
            }

            // Process results
            var searchResults = new FileSearchData();
            searchResults.SearchDurationMs = searchDuration;

            foreach (var scoreDoc in topDocs.ScoreDocs.Take(effectiveMaxResults))
            {
                // Use field selector to load only required fields for optimal performance
                var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, 
                    "path", "filename", "relativePath", "extension", "size", "lastModified", "language");
                
                var result = new InternalFileSearchResult
                {
                    Path = doc.Get("path") ?? "",
                    Filename = doc.Get("filename") ?? "",
                    RelativePath = doc.Get("relativePath") ?? "",
                    Extension = doc.Get("extension") ?? "",
                    Size = long.Parse(doc.Get("size") ?? "0"),
                    LastModified = new DateTime(long.Parse(doc.Get("lastModified") ?? "0")),
                    Score = scoreDoc.Score,
                    Language = doc.Get("language") ?? ""
                };

                searchResults.Results.Add(result);

                // Update statistics
                var ext = result.Extension.ToLower();
                if (!searchResults.ExtensionCounts.ContainsKey(ext))
                    searchResults.ExtensionCounts[ext] = 0;
                searchResults.ExtensionCounts[ext]++;

                var dir = Path.GetDirectoryName(result.RelativePath) ?? "";
                if (!searchResults.DirectoryCounts.ContainsKey(dir))
                    searchResults.DirectoryCounts[dir] = 0;
                searchResults.DirectoryCounts[dir]++;

                if (!string.IsNullOrEmpty(result.Language))
                {
                    if (!searchResults.LanguageCounts.ContainsKey(result.Language))
                        searchResults.LanguageCounts[result.Language] = 0;
                    searchResults.LanguageCounts[result.Language]++;
                }
            }

            Logger.LogInformation("Found {Count} files in {Duration}ms", 
                searchResults.Results.Count, searchDuration);

            // Convert internal results to FileSearchResult format for AIResponseBuilder
            var fileSearchResults = searchResults.Results.Select(r => new FileSearchResult
            {
                FilePath = r.Path,
                FileName = System.IO.Path.GetFileName(r.Path),
                RelativePath = r.Path,
                Extension = System.IO.Path.GetExtension(r.Path),
                Language = "", // TODO: Add language detection if needed
                Score = r.Score
            }).ToList();

            // Create AI-optimized response using centralized builder
            return _aiResponseBuilder.BuildFileSearchResponse(
                query,
                searchType ?? "standard",
                workspacePath,
                fileSearchResults,
                searchResults.SearchDurationMs,
                mode,
                null); // ProjectContext
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            Logger.LogWarning(cbEx, "Circuit breaker is open for file search");
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.CIRCUIT_BREAKER_OPEN,
                    message = cbEx.Message,
                    recovery = _errorRecoveryService.GetCircuitBreakerOpenRecovery(cbEx.OperationName)
                }
            };
        }
        catch (DirectoryNotFoundException dnfEx)
        {
            Logger.LogError(dnfEx, "Directory not found for file search");
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.DIRECTORY_NOT_FOUND,
                    message = dnfEx.Message,
                    recovery = _errorRecoveryService.GetDirectoryNotFoundRecovery(workspacePath)
                }
            };
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Logger.LogError(uaEx, "Permission denied for file search");
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.PERMISSION_DENIED,
                    message = $"Permission denied accessing {workspacePath}: {uaEx.Message}",
                    recovery = (object?)null
                }
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in fast file search for query: {Query}", query);
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.INTERNAL_ERROR,
                    message = $"Search failed: {ex.Message}",
                    recovery = (object?)null
                }
            };
        }
    }

    private Query BuildStandardQuery(string query)
    {
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Version);
        var parser = new MultiFieldQueryParser(Version, 
            new[] { "filename_text", "relativePath" }, 
            analyzer);
        
        parser.AllowLeadingWildcard = true;
        
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
        var parser = new QueryParser(Version, "filename_text", analyzer);
        return parser.Parse(query);
    }

    private Query BuildWildcardQuery(string query)
    {
        if (!query.Contains('*') && !query.Contains('?'))
        {
            query = $"*{query}*";
        }
        
        // Use the non-analyzed "filename_lower" field for wildcard searches to ensure predictable
        // pattern matching for AI agents with case-insensitive behavior across all platforms.
        return new WildcardQuery(new Term("filename_lower", query.ToLowerInvariant()));
    }

    private Query BuildExactQuery(string query)
    {
        return new TermQuery(new Term("filename", query));
    }

    private Query BuildRegexQuery(string query)
    {
        try
        {
            _ = new Regex(query);
            // Use relativePath for regex to allow more flexible pattern matching
            // This allows patterns like "^Test.*" to match files in Test directories
            // or ".*Service\.cs$" to match the full path pattern
            return new RegexpQuery(new Term("relativePath", query));
        }
        catch (ArgumentException)
        {
            Logger.LogWarning("Invalid regex pattern: {Query}, falling back to standard search", query);
            return BuildStandardQuery(query);
        }
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<object>(new
        {
            success = false,
            error = new
            {
                code = ErrorCodes.VALIDATION_ERROR,
                message = "Detail requests not implemented for file search",
                recovery = (object?)null
            }
        });
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is FileSearchData searchData)
        {
            return searchData.Results.Count;
        }
        return 0;
    }

    private class FileSearchData
    {
        public List<InternalFileSearchResult> Results { get; } = new();
        public Dictionary<string, int> ExtensionCounts { get; } = new();
        public Dictionary<string, int> DirectoryCounts { get; } = new();
        public Dictionary<string, int> LanguageCounts { get; } = new();
        public double SearchDurationMs { get; set; }
    }

    private class InternalFileSearchResult
    {
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string Extension { get; set; } = "";
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public float Score { get; set; }
        public string Language { get; set; } = "";
    }
}