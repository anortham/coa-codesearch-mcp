using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
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
        SearchResultResourceProvider? searchResultResourceProvider = null)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _luceneIndexService = luceneIndexService;
        _configuration = configuration;
        _fieldSelectorService = fieldSelectorService;
        _errorRecoveryService = errorRecoveryService;
        _searchResultResourceProvider = searchResultResourceProvider;
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
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Search query cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("nameQuery", "non-empty string"));
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

            // Execute search
            var startTime = DateTime.UtcNow;
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Process results
            var searchResults = new FileSearchData();
            searchResults.SearchDurationMs = searchDuration;

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                // Use field selector to load only required fields for optimal performance
                var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, 
                    "path", "filename", "relativePath", "extension", "size", "lastModified", "language");
                
                var result = new FileSearchResult
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

            // Create AI-optimized response
            return CreateAiOptimizedResponse(query, searchType, workspacePath, searchResults, mode);
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            Logger.LogWarning(cbEx, "Circuit breaker is open for file search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.CIRCUIT_BREAKER_OPEN,
                cbEx.Message,
                _errorRecoveryService.GetCircuitBreakerOpenRecovery(cbEx.OperationName));
        }
        catch (DirectoryNotFoundException dnfEx)
        {
            Logger.LogError(dnfEx, "Directory not found for file search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.DIRECTORY_NOT_FOUND,
                dnfEx.Message,
                _errorRecoveryService.GetDirectoryNotFoundRecovery(workspacePath));
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Logger.LogError(uaEx, "Permission denied for file search");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.PERMISSION_DENIED,
                $"Permission denied accessing {workspacePath}: {uaEx.Message}",
                null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in fast file search for query: {Query}", query);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                $"Search failed: {ex.Message}",
                null);
        }
    }

    private object CreateAiOptimizedResponse(
        string query,
        string? searchType,
        string workspacePath,
        FileSearchData data,
        ResponseMode mode)
    {
        // Analyze the search results
        var analysis = AnalyzeSearchResults(query, data);

        // Generate insights
        var insights = GenerateSearchInsights(query, searchType, data, analysis);

        // Generate actions
        var actions = GenerateSearchActions(query, data, analysis);

        // Prepare results for response
        var results = mode == ResponseMode.Full
            ? PrepareFullResults(data)
            : PrepareSummaryResults(data, analysis);

        // Create response object
        var response = new
        {
            success = true,
            operation = ToolNames.FileSearch,
            query = new
            {
                text = query,
                type = searchType,
                workspace = Path.GetFileName(workspacePath)
            },
            summary = new
            {
                totalFound = data.Results.Count,
                searchTime = $"{data.SearchDurationMs:F1}ms",
                performance = data.SearchDurationMs < 10 ? "excellent" : data.SearchDurationMs < 50 ? "fast" : "normal",
                distribution = new
                {
                    byExtension = data.ExtensionCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .ToDictionary(kv => kv.Key, kv => kv.Value),
                    byLanguage = data.LanguageCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(3)
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                }
            },
            analysis = new
            {
                patterns = analysis.Patterns.Take(3).ToList(),
                matchQuality = new
                {
                    exactMatches = analysis.ExactMatches,
                    partialMatches = analysis.PartialMatches,
                    fuzzyMatches = analysis.FuzzyMatches,
                    avgScore = analysis.AverageScore
                },
                hotspots = new
                {
                    directories = data.DirectoryCounts
                        .Where(kv => kv.Value >= 1)
                        .OrderByDescending(kv => kv.Value)
                        .Take(3)
                        .Select(kv => new { path = kv.Key, count = kv.Value })
                        .ToList(),
                    largeFiles = data.Results
                        .Where(r => r.Size > 1_000_000) // Files > 1MB
                        .OrderByDescending(r => r.Size)
                        .Take(3)
                        .Select(r => new { file = r.Filename, size = FormatSize(r.Size) })
                        .ToList()
                }
            },
            results = results,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateResponseTokens(data),
                cached = $"filesearch_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };

        // Store search results as a resource if provider is available
        if (_searchResultResourceProvider != null && data.Results.Count > 0)
        {
            var resourceUri = _searchResultResourceProvider.StoreSearchResult(
                query,
                new
                {
                    results = data.Results,
                    query = response.query,
                    summary = response.summary,
                    analysis = response.analysis,
                    insights = insights
                },
                new
                {
                    searchType = searchType,
                    workspacePath = workspacePath,
                    timestamp = DateTime.UtcNow
                });

            // Add resource URI to response
            return new
            {
                success = response.success,
                operation = response.operation,
                query = response.query,
                summary = response.summary,
                analysis = response.analysis,
                results = response.results,
                insights = response.insights,
                actions = response.actions,
                meta = response.meta,
                resourceUri = resourceUri
            };
        }

        return response;
    }

    private FileSearchAnalysis AnalyzeSearchResults(string query, FileSearchData data)
    {
        var analysis = new FileSearchAnalysis();

        // Calculate match quality
        foreach (var result in data.Results)
        {
            var filename = result.Filename.ToLower();
            var queryLower = query.ToLower();

            if (filename == queryLower)
                analysis.ExactMatches++;
            else if (filename.Contains(queryLower))
                analysis.PartialMatches++;
            else
                analysis.FuzzyMatches++;

            analysis.TotalScore += result.Score;
        }

        if (data.Results.Any())
        {
            analysis.AverageScore = analysis.TotalScore / data.Results.Count;
        }

        // Pattern detection
        if (data.Results.Count == 0)
        {
            analysis.Patterns.Add("No matches found - check spelling or use fuzzy search");
        }
        else if (data.Results.Count == 1)
        {
            analysis.Patterns.Add("Single match - precise search result");
        }
        else if (data.Results.Count >= 40)
        {
            analysis.Patterns.Add("Many matches - consider refining search");
        }

        // Extension patterns
        if (data.ExtensionCounts.Count == 1)
        {
            analysis.Patterns.Add($"All results are {data.ExtensionCounts.First().Key} files");
        }
        else if (data.ExtensionCounts.Any(kv => kv.Value > data.Results.Count * 0.7))
        {
            var dominant = data.ExtensionCounts.OrderByDescending(kv => kv.Value).First();
            analysis.Patterns.Add($"Predominantly {dominant.Key} files ({dominant.Value * 100 / data.Results.Count}%)");
        }

        // Directory concentration
        if (data.DirectoryCounts.Any(kv => kv.Value > data.Results.Count * 0.5))
        {
            var concentrated = data.DirectoryCounts.OrderByDescending(kv => kv.Value).First();
            analysis.Patterns.Add($"Concentrated in {concentrated.Key} directory");
        }

        return analysis;
    }

    private List<object> PrepareFullResults(FileSearchData data)
    {
        return data.Results.Select(r => new
        {
            path = r.Path,
            filename = r.Filename,
            relativePath = r.RelativePath,
            extension = r.Extension,
            size = r.Size,
            sizeFormatted = FormatSize(r.Size),
            lastModified = r.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
            score = Math.Round(r.Score, 3),
            language = r.Language
        }).ToList<object>();
    }

    private List<object> PrepareSummaryResults(FileSearchData data, FileSearchAnalysis analysis)
    {
        // In summary mode, group by directory and show top matches
        var topMatches = data.Results
            .OrderByDescending(r => r.Score)
            .Take(10)
            .Select(r => new
            {
                file = r.Filename,
                path = r.RelativePath,
                score = Math.Round(r.Score, 2),
                size = FormatSize(r.Size)
            })
            .ToList<object>();

        return topMatches;
    }

    private List<string> GenerateSearchInsights(string query, string? searchType, FileSearchData data, FileSearchAnalysis analysis)
    {
        var insights = new List<string>();

        // Basic result insight
        if (data.Results.Count == 0)
        {
            insights.Add($"No files matching '{query}'");
            if (searchType == "exact")
            {
                insights.Add("Try fuzzy or wildcard search for approximate matches");
            }
        }
        else
        {
            insights.Add($"Found {data.Results.Count} files in {data.SearchDurationMs:F0}ms");
        }

        // Match quality insight
        if (analysis.ExactMatches > 0)
        {
            insights.Add($"{analysis.ExactMatches} exact filename matches");
        }
        else if (analysis.PartialMatches > 0)
        {
            insights.Add($"{analysis.PartialMatches} partial matches - no exact matches found");
        }

        // Performance insight
        if (data.SearchDurationMs < 10)
        {
            insights.Add("⚡ Excellent search performance");
        }

        // File type insights
        if (data.LanguageCounts.Any())
        {
            var topLang = data.LanguageCounts.OrderByDescending(kv => kv.Value).First();
            insights.Add($"Primary language: {topLang.Key} ({topLang.Value} files)");
        }

        // Size insights
        var totalSize = data.Results.Sum(r => r.Size);
        if (totalSize > 10_000_000) // 10MB
        {
            insights.Add($"Total size: {FormatSize(totalSize)}");
        }

        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }
        
        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (data.Results.Count == 0)
            {
                insights.Add($"No files found matching '{query}'");
            }
            else
            {
                insights.Add($"Found {data.Results.Count} files matching '{query}'");
            }
        }

        return insights;
    }

    private List<object> GenerateSearchActions(string query, FileSearchData data, FileSearchAnalysis analysis)
    {
        var actions = new List<object>();

        // Open file action
        if (data.Results.Any())
        {
            var topResult = data.Results.OrderByDescending(r => r.Score).First();
            actions.Add(new
            {
                id = "open_file",
                cmd = new { file = topResult.Path },
                tokens = 100,
                priority = "recommended"
            });
        }

        // Search refinement actions
        if (data.Results.Count > 20)
        {
            // Filter by extension
            var topExt = data.ExtensionCounts.OrderByDescending(kv => kv.Value).First();
            actions.Add(new
            {
                id = "filter_by_type",
                cmd = new { query = query, filter = $"*.{topExt.Key}" },
                tokens = 500,
                priority = "recommended"
            });

            // Search in specific directory
            if (data.DirectoryCounts.Any(kv => kv.Value > 3))
            {
                var topDir = data.DirectoryCounts.OrderByDescending(kv => kv.Value).First();
                actions.Add(new
                {
                    id = "search_in_directory",
                    cmd = new { query = query, path = topDir.Key },
                    tokens = 300,
                    priority = "available"
                });
            }
        }

        // Alternative search suggestions
        if (data.Results.Count == 0)
        {
            actions.Add(new
            {
                id = "try_fuzzy_search",
                cmd = new { query = $"{query}~", searchType = "fuzzy" },
                tokens = 200,
                priority = "recommended"
            });

            actions.Add(new
            {
                id = "try_wildcard_search",
                cmd = new { query = $"*{query}*", searchType = "wildcard" },
                tokens = 200,
                priority = "recommended"
            });
        }

        // Content search in found files
        if (data.Results.Count > 0 && data.Results.Count < 20)
        {
            actions.Add(new
            {
                id = "search_in_files",
                cmd = new
                {
                    operation = ToolNames.TextSearch,
                    files = data.Results.Take(10).Select(r => r.Path).ToList()
                },
                tokens = 1500,
                priority = "available"
            });
        }
        
        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (data.Results.Count > 0)
            {
                actions.Add(new
                {
                    id = "explore_results",
                    cmd = new { expand = "details" },
                    tokens = 1000,
                    priority = "available"
                });
            }
            else
            {
                actions.Add(new
                {
                    id = "broaden_search",
                    cmd = new { query = $"*{query}*", searchType = "wildcard" },
                    tokens = 1500,
                    priority = "recommended"
                });
            }
        }

        return actions;
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int order = 0;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }

    private int EstimateResponseTokens(FileSearchData data)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per result tokens
        var perResultTokens = 30;
        
        // Additional for statistics
        var statsTokens = (data.ExtensionCounts.Count + data.LanguageCounts.Count) * 20;
        
        return baseTokens + (data.Results.Count * perResultTokens) + statsTokens;
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
            return new RegexpQuery(new Term("filename", query));
        }
        catch (ArgumentException)
        {
            Logger.LogWarning("Invalid regex pattern: {Query}, falling back to standard search", query);
            return BuildStandardQuery(query);
        }
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<object>(UnifiedToolResponse<object>.CreateError(
            ErrorCodes.VALIDATION_ERROR,
            "Detail requests not implemented for file search",
            null));
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
        public List<FileSearchResult> Results { get; } = new();
        public Dictionary<string, int> ExtensionCounts { get; } = new();
        public Dictionary<string, int> DirectoryCounts { get; } = new();
        public Dictionary<string, int> LanguageCounts { get; } = new();
        public double SearchDurationMs { get; set; }
    }

    private class FileSearchResult
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

    private class FileSearchAnalysis
    {
        public List<string> Patterns { get; set; } = new();
        public int ExactMatches { get; set; }
        public int PartialMatches { get; set; }
        public int FuzzyMatches { get; set; }
        public float TotalScore { get; set; }
        public float AverageScore { get; set; }
    }
}