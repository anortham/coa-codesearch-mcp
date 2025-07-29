using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Scoring;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// POC implementation using System.Text.Json JsonNode for improved performance
/// </summary>
public class FastTextSearchToolV3 : ClaudeOptimizedToolBase
{
    public override string ToolName => ToolNames.TextSearch;
    public override string Description => "AI-optimized text search with JsonNode response";
    public override ToolCategory Category => ToolCategory.Search;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly FileIndexingService _fileIndexingService;
    private readonly IContextAwarenessService? _contextAwarenessService;
    private readonly IQueryCacheService _queryCacheService;
    private readonly IFieldSelectorService _fieldSelectorService;
    private readonly IStreamingResultService _streamingResultService;
    private readonly IErrorRecoveryService _errorRecoveryService;
    private readonly SearchResultResourceProvider? _searchResultResourceProvider;
    private readonly IScoringService? _scoringService;
    private readonly IResultConfidenceService? _resultConfidenceService;
    private readonly AIResponseBuilderService _aiResponseBuilder;

    public FastTextSearchToolV3(
        ILogger<FastTextSearchToolV3> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        FileIndexingService fileIndexingService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache,
        IQueryCacheService queryCacheService,
        IFieldSelectorService fieldSelectorService,
        IStreamingResultService streamingResultService,
        IErrorRecoveryService errorRecoveryService,
        AIResponseBuilderService aiResponseBuilder,
        IContextAwarenessService? contextAwarenessService = null,
        SearchResultResourceProvider? searchResultResourceProvider = null,
        IScoringService? scoringService = null,
        IResultConfidenceService? resultConfidenceService = null)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _configuration = configuration;
        _luceneIndexService = luceneIndexService;
        _fileIndexingService = fileIndexingService;
        _contextAwarenessService = contextAwarenessService;
        _queryCacheService = queryCacheService;
        _fieldSelectorService = fieldSelectorService;
        _streamingResultService = streamingResultService;
        _errorRecoveryService = errorRecoveryService;
        _searchResultResourceProvider = searchResultResourceProvider;
        _scoringService = scoringService;
        _resultConfidenceService = resultConfidenceService;
        _aiResponseBuilder = aiResponseBuilder;
    }

    public async Task<JsonNode> ExecuteAsync(
        string query,
        string workspacePath,
        string? filePattern = null,
        string[]? extensions = null,
        int? contextLines = null,
        int maxResults = 50,
        bool caseSensitive = false,
        string searchType = "standard",
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("FastTextSearchV3 request for query: {Query} in {WorkspacePath}", query, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(query))
            {
                return CreateErrorResponse(
                    ErrorCodes.VALIDATION_ERROR, 
                    "Search query cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("searchQuery", "non-empty string"));
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return CreateErrorResponse(
                    ErrorCodes.VALIDATION_ERROR,
                    "Workspace path cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path"));
            }

            // Ensure the directory is indexed first
            if (!await EnsureIndexedAsync(workspacePath, cancellationToken))
            {
                return CreateErrorResponse(
                    ErrorCodes.INDEX_NOT_FOUND,
                    $"No search index exists for {workspacePath}",
                    _errorRecoveryService.GetIndexNotFoundRecovery(workspacePath));
            }

            // Perform the search
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var analyzer = await _luceneIndexService.GetAnalyzerAsync(workspacePath, cancellationToken);

            // Build the query with caching for performance
            var luceneQuery = BuildQueryWithCache(query, searchType, caseSensitive, filePattern, extensions, analyzer);

            // Apply multi-factor scoring if service is available
            if (_scoringService != null)
            {
                var searchContext = new ScoringContext
                {
                    QueryText = query,
                    SearchType = searchType,
                    WorkspacePath = workspacePath
                };
                
                luceneQuery = _scoringService.CreateScoredQuery(luceneQuery, searchContext);
                Logger.LogDebug("Applied multi-factor scoring to text search query");
            }

            // Execute search
            var searchStart = DateTime.UtcNow;
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var searchDuration = (DateTime.UtcNow - searchStart).TotalMilliseconds;
            
            // Apply confidence-based result limiting
            int effectiveMaxResults = maxResults;
            string? confidenceInsight = null;
            if (_resultConfidenceService != null)
            {
                var confidence = _resultConfidenceService.AnalyzeResults(topDocs, maxResults, contextLines > 0);
                effectiveMaxResults = confidence.RecommendedCount;
                confidenceInsight = confidence.Insight;
                Logger.LogDebug("Confidence analysis: level={Level}, recommended={Count}, topScore={Score:F2}", 
                    confidence.ConfidenceLevel, confidence.RecommendedCount, confidence.TopScore);
            }
            
            var results = await ProcessSearchResultsAsync(searcher, topDocs, query, contextLines, effectiveMaxResults, cancellationToken);

            // Get project context and check for alternate results
            var projectContext = await GetProjectContextAsync(workspacePath);
            long? alternateHits = null;
            Dictionary<string, int>? alternateExtensions = null;
            
            if (topDocs.TotalHits == 0 && (filePattern != null || extensions?.Length > 0))
            {
                var (altHits, altExts) = await CheckAlternateSearchResults(query, workspacePath, searchType, caseSensitive, cancellationToken);
                if (altHits > 0)
                {
                    alternateHits = altHits;
                    alternateExtensions = altExts;
                }
            }

            // Convert SearchResult to TextSearchResult for AIResponseBuilder
            var textSearchResults = results.Select(r => new TextSearchResult
            {
                FilePath = r.FilePath,
                FileName = r.FileName,
                RelativePath = r.RelativePath,
                Extension = r.Extension,
                Language = r.Language,
                Score = r.Score,
                Context = r.Context?.Select(c => new TextSearchContextLine
                {
                    LineNumber = c.LineNumber,
                    Content = c.Content,
                    IsMatch = c.IsMatch
                }).ToList()
            }).ToList();

            // Build response using JsonNode
            var response = _aiResponseBuilder.BuildTextSearchResponseAsJsonNode(
                query, searchType, workspacePath, textSearchResults, topDocs.TotalHits,
                filePattern, extensions, mode, projectContext, alternateHits, alternateExtensions);

            // Store search results as a resource if provider is available
            if (_searchResultResourceProvider != null && results.Count > 0)
            {
                var resourceUri = _searchResultResourceProvider.StoreSearchResult(
                    query, 
                    new
                    {
                        results = results,
                        query = response["query"],
                        summary = response["summary"],
                        distribution = response["distribution"],
                        hotspots = response["hotspots"],
                        insights = response["insights"]
                    },
                    new
                    {
                        searchType = searchType,
                        workspacePath = workspacePath,
                        timestamp = DateTime.UtcNow
                    });

                // Add resource URI to response
                response["resourceUri"] = resourceUri;
            }

            var totalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Logger.LogInformation("FastTextSearchV3 completed in {Duration}ms (search: {SearchDuration}ms)", 
                totalDuration, searchDuration);

            return response;
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            Logger.LogWarning(cbEx, "Circuit breaker is open for text search");
            return CreateErrorResponse(
                ErrorCodes.CIRCUIT_BREAKER_OPEN,
                cbEx.Message,
                _errorRecoveryService.GetCircuitBreakerOpenRecovery(cbEx.OperationName));
        }
        catch (DirectoryNotFoundException dnfEx)
        {
            Logger.LogError(dnfEx, "Directory not found for text search");
            return CreateErrorResponse(
                ErrorCodes.DIRECTORY_NOT_FOUND,
                dnfEx.Message,
                _errorRecoveryService.GetDirectoryNotFoundRecovery(workspacePath));
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Logger.LogError(uaEx, "Permission denied for text search");
            return CreateErrorResponse(
                ErrorCodes.PERMISSION_DENIED,
                $"Permission denied accessing {workspacePath}: {uaEx.Message}",
                null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing fast text search V3");
            return CreateErrorResponse(
                ErrorCodes.INTERNAL_ERROR,
                $"Search failed: {ex.Message}",
                null);
        }
    }

    private JsonNode CreateErrorResponse(string code, string message, object? recovery)
    {
        var error = new JsonObject
        {
            ["success"] = false,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (recovery != null)
        {
            error["error"]["recovery"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(recovery));
        }

        return error;
    }

    private async Task<JsonNode> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return CreateErrorResponse(
            ErrorCodes.INTERNAL_ERROR,
            "Detail requests not implemented for text search V3",
            null);
    }

    private async Task<List<SearchResult>> ProcessSearchResultsAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        int maxResults,
        CancellationToken cancellationToken)
    {
        const int StreamingThreshold = 100;
        
        if (topDocs.ScoreDocs.Length >= StreamingThreshold)
        {
            return await ProcessSearchResultsStreamingAsync(searcher, topDocs, query, contextLines, maxResults, cancellationToken);
        }
        else
        {
            return await ProcessSearchResultsOptimizedAsync(searcher, topDocs, query, contextLines, maxResults, cancellationToken);
        }
    }

    private async Task<List<SearchResult>> ProcessSearchResultsOptimizedAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var scoreDocs = topDocs.ScoreDocs.Take(maxResults).ToArray();
        var results = new List<SearchResult>(scoreDocs.Length);
        var fieldSet = _fieldSelectorService.GetFieldSet(FieldSetType.SearchResults);
        
        var parallelOptions = new ParallelOptions 
        { 
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount 
        };
        
        var resultBag = new ConcurrentBag<(SearchResult result, float score)>();
        
        await Parallel.ForEachAsync(scoreDocs, parallelOptions, async (scoreDoc, ct) =>
        {
            var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, fieldSet.Fields);
            var filePath = doc.Get("path");
            
            if (string.IsNullOrEmpty(filePath))
                return;

            var result = new SearchResult
            {
                FilePath = filePath,
                FileName = doc.Get("filename") ?? Path.GetFileName(filePath),
                RelativePath = doc.Get("relativePath") ?? filePath,
                Extension = doc.Get("extension") ?? Path.GetExtension(filePath),
                Score = scoreDoc.Score,
                Language = doc.Get("language") ?? ""
            };

            if (contextLines.HasValue && contextLines.Value > 0)
            {
                result.Context = await GetFileContextAsync(filePath, query, contextLines.Value, ct);
            }

            resultBag.Add((result, scoreDoc.Score));
        });

        return resultBag.OrderByDescending(r => r.score).Select(r => r.result).ToList();
    }

    private async Task<List<SearchResult>> ProcessSearchResultsStreamingAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var fieldSet = _fieldSelectorService.GetFieldSet(FieldSetType.SearchResults);
        var scoreDocs = topDocs.ScoreDocs.Take(maxResults).ToArray();
        var scoreMap = scoreDocs.ToDictionary(sd => sd.Doc, sd => sd.Score);
        
        var streamingOptions = new StreamingOptions
        {
            BatchSize = 50,
            BatchDelay = TimeSpan.FromMilliseconds(2),
            MaxResults = maxResults
        };

        var limitedTopDocs = new TopDocs(topDocs.TotalHits, scoreDocs, topDocs.MaxScore);
        
        await foreach (var batch in _streamingResultService.StreamResultsWithFieldSelectorAsync(
            searcher, limitedTopDocs, (s, docId, fields) => ProcessDocumentWithScore(s, docId, fields, scoreMap), 
            fieldSet.Fields, streamingOptions, cancellationToken))
        {
            foreach (var result in batch.Results)
            {                
                if (contextLines.HasValue && contextLines.Value > 0)
                {
                    result.Context = await GetFileContextAsync(result.FilePath, query, contextLines.Value, cancellationToken);
                }
                
                results.Add(result);
            }
            
            if (batch.BatchNumber % 20 == 0)
            {
                Logger.LogDebug("Processed streaming batch {BatchNumber}, total results: {Total}", 
                    batch.BatchNumber, batch.TotalProcessed);
            }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private SearchResult ProcessDocumentWithScore(IndexSearcher searcher, int docId, string[] fieldNames, Dictionary<int, float> scoreMap)
    {
        var doc = _fieldSelectorService.LoadDocument(searcher, docId, fieldNames);
        var filePath = doc.Get("path");
        
        if (string.IsNullOrEmpty(filePath))
        {
            throw new InvalidOperationException($"Document {docId} has no path field");
        }

        var result = new SearchResult
        {
            FilePath = filePath,
            FileName = doc.Get("filename") ?? Path.GetFileName(filePath),
            RelativePath = doc.Get("relativePath") ?? filePath,
            Extension = doc.Get("extension") ?? Path.GetExtension(filePath),
            Score = scoreMap.GetValueOrDefault(docId, 0f),
            Language = doc.Get("language") ?? ""
        };
        
        return result;
    }

    private Query BuildQueryWithCache(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        var cacheKey = $"{queryText}|{searchType}|{caseSensitive}|{filePattern}|{string.Join(",", extensions ?? Array.Empty<string>())}";
        
        return _queryCacheService.GetOrCreateQuery(cacheKey, searchType, () => 
            BuildQuery(queryText, searchType, caseSensitive, filePattern, extensions, analyzer));
    }

    private Query BuildQuery(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        var booleanQuery = new BooleanQuery();

        Query contentQuery;
        switch (searchType.ToLowerInvariant())
        {
            case "wildcard":
                var wildcardEscaped = EscapeQueryTextForWildcard(queryText);
                contentQuery = new WildcardQuery(new Term("content", wildcardEscaped.ToLowerInvariant()));
                break;
            
            case "fuzzy":
                var fuzzyEscaped = EscapeQueryTextForFuzzy(queryText);
                contentQuery = new FuzzyQuery(new Term("content", fuzzyEscaped.ToLowerInvariant()));
                break;
            
            case "phrase":
                var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                contentQuery = parser.Parse($"\"{EscapeQueryText(queryText)}\"");
                break;
            
            case "regex":
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(queryText);
                    contentQuery = new RegexpQuery(new Term("content", queryText));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Invalid regex pattern: {Query}, falling back to escaped standard search", queryText);
                    var fallbackParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                    fallbackParser.DefaultOperator = Operator.AND;
                    contentQuery = fallbackParser.Parse(EscapeQueryText(queryText));
                }
                break;
            
            default: // standard
                var standardParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                standardParser.DefaultOperator = Operator.AND;
                var escapedQuery = EscapeQueryText(queryText);
                try
                {
                    contentQuery = standardParser.Parse(escapedQuery);
                }
                catch (ParseException ex)
                {
                    Logger.LogWarning(ex, "Failed to parse query even after escaping: {Query}", queryText);
                    contentQuery = new TermQuery(new Term("content", queryText.ToLowerInvariant()));
                }
                break;
        }

        booleanQuery.Add(contentQuery, Occur.MUST);

        if (!string.IsNullOrWhiteSpace(filePattern))
        {
            var pathQuery = new WildcardQuery(new Term("relativePath", $"*{filePattern}*"));
            booleanQuery.Add(pathQuery, Occur.MUST);
        }

        if (extensions?.Length > 0)
        {
            var extensionQuery = new BooleanQuery();
            foreach (var ext in extensions)
            {
                var normalizedExt = ext.StartsWith(".") ? ext : $".{ext}";
                extensionQuery.Add(new TermQuery(new Term("extension", normalizedExt)), Occur.SHOULD);
            }
            booleanQuery.Add(extensionQuery, Occur.MUST);
        }

        return booleanQuery;
    }

    private async Task<bool> EnsureIndexedAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var indexReader = searcher.IndexReader;
            
            if (indexReader.NumDocs < 10)
            {
                Logger.LogInformation("Index is empty or small, performing initial indexing for {WorkspacePath}", workspacePath);
                await _fileIndexingService.IndexDirectoryAsync(workspacePath, workspacePath, cancellationToken);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to ensure index for {WorkspacePath}", workspacePath);
            
            try
            {
                await _fileIndexingService.IndexDirectoryAsync(workspacePath, workspacePath, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task<List<ContextLine>> GetFileContextAsync(string filePath, string query, int contextLines, CancellationToken cancellationToken)
    {
        var contextResults = new List<ContextLine>();
        
        try
        {
            var queryLower = query.ToLowerInvariant();
            
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            var lineNumber = 0;
            var buffer = new List<(int LineNumber, string Content)>(contextLines * 2 + 1);
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                
                buffer.Add((lineNumber, line));
                if (buffer.Count > contextLines * 2 + 1)
                    buffer.RemoveAt(0);
                
                if (line.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    var matchIndex = buffer.Count - 1;
                    var startIndex = Math.Max(0, matchIndex - contextLines);
                    var endIndex = Math.Min(buffer.Count - 1, matchIndex + contextLines);
                    
                    var linesAfter = endIndex - matchIndex;
                    for (int i = 0; i < contextLines - linesAfter && (line = await reader.ReadLineAsync(cancellationToken)) != null; i++)
                    {
                        lineNumber++;
                        buffer.Add((lineNumber, line));
                    }
                    
                    for (int i = startIndex; i < buffer.Count && i <= matchIndex + contextLines; i++)
                    {
                        var (num, content) = buffer[i];
                        contextResults.Add(new ContextLine
                        {
                            LineNumber = num,
                            Content = content,
                            IsMatch = i == matchIndex
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get context for file {FilePath}", filePath);
        }
        
        return contextResults;
    }

    private static string EscapeQueryText(string query)
    {
        var specialChars = new[] { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', '*', '?', ':', '\\', '/', '<', '>' };
        
        var escapedQuery = query;
        foreach (var c in specialChars)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    private static string EscapeQueryTextForWildcard(string query)
    {
        var specialChars = new[] { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', ':', '\\', '/', '<', '>' };
        
        var escapedQuery = query;
        foreach (var c in specialChars)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    private static string EscapeQueryTextForFuzzy(string query)
    {
        var specialChars = new[] { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '*', '?', ':', '\\', '/', '<', '>' };
        
        var escapedQuery = query;
        foreach (var c in specialChars)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is List<SearchResult> results)
        {
            return results.Count;
        }
        return 0;
    }

    private class SearchResult
    {
        public required string FilePath { get; set; }
        public required string FileName { get; set; }
        public required string RelativePath { get; set; }
        public required string Extension { get; set; }
        public required string Language { get; set; }
        public float Score { get; set; }
        public List<ContextLine>? Context { get; set; }
    }

    private class ContextLine
    {
        public int LineNumber { get; set; }
        public required string Content { get; set; }
        public bool IsMatch { get; set; }
    }
    
    private async Task<(long totalHits, Dictionary<string, int> extensionCounts)> CheckAlternateSearchResults(
        string query,
        string workspacePath,
        string searchType,
        bool caseSensitive,
        CancellationToken cancellationToken)
    {
        try
        {
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var analyzer = await _luceneIndexService.GetAnalyzerAsync(workspacePath, cancellationToken);
            
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            parser.AllowLeadingWildcard = true;
            
            Query luceneQuery;
            if (searchType == "fuzzy" && !query.Contains("~"))
            {
                luceneQuery = parser.Parse(query + "~");
            }
            else if (searchType == "phrase")
            {
                luceneQuery = parser.Parse($"\"{query}\"");
            }
            else
            {
                luceneQuery = parser.Parse(query);
            }
            
            var collector = TopScoreDocCollector.Create(1000, true);
            searcher.Search(luceneQuery, collector);
            
            var topDocs = collector.GetTopDocs();
            var extensionCounts = new Dictionary<string, int>();
            
            var minimalFields = new[] { "extension" };
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, minimalFields);
                var extension = doc.Get("extension") ?? ".unknown";
                extensionCounts[extension] = extensionCounts.GetValueOrDefault(extension) + 1;
            }
            
            return (topDocs.TotalHits, extensionCounts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to check alternate search results");
            return (0, new Dictionary<string, int>());
        }
    }
    
    private async Task<ProjectContext?> GetProjectContextAsync(string workspacePath)
    {
        try
        {
            if (_contextAwarenessService != null)
            {
                var context = await _contextAwarenessService.GetCurrentContextAsync();
                return context.ProjectInfo;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get project context");
        }
        
        return null;
    }
}