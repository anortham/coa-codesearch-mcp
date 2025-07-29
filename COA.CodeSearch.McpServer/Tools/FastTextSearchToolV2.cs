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

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of FastTextSearchTool with structured response format
/// Updated for memory lifecycle testing - improved error handling
/// </summary>
public class FastTextSearchToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => ToolNames.TextSearch;
    public override string Description => "AI-optimized text search with insights";
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

    public FastTextSearchToolV2(
        ILogger<FastTextSearchToolV2> logger,
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

    public async Task<object> ExecuteAsync(
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
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("FastTextSearch request for query: {Query} in {WorkspacePath}", query, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(query))
            {
                return new
                {
                    success = false,
                    error = new
                    {
                        code = ErrorCodes.VALIDATION_ERROR,
                        message = "Search query cannot be empty",
                        recovery = _errorRecoveryService.GetValidationErrorRecovery("searchQuery", "non-empty string")
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
                        recovery = _errorRecoveryService.GetValidationErrorRecovery("workspacePath",
                            "absolute directory path")
                    }
                };
            }

            // Ensure the directory is indexed first
            if (!await EnsureIndexedAsync(workspacePath, cancellationToken))
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

            // Get the searcher
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
                
                // Wrap query with multi-factor scoring
                luceneQuery = _scoringService.CreateScoredQuery(luceneQuery, searchContext);
                Logger.LogDebug("Applied multi-factor scoring to text search query");
            }

            // Execute search
            var topDocs = searcher.Search(luceneQuery, maxResults);
            
            // Apply confidence-based result limiting if service is available
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

            // Create AI-optimized response using the service
            var response = _aiResponseBuilder.BuildTextSearchResponse(
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
                        query = ((dynamic)response).query,
                        summary = ((dynamic)response).summary,
                        distribution = ((dynamic)response).distribution,
                        hotspots = ((dynamic)response).hotspots,
                        insights = ((dynamic)response).insights
                    },
                    new
                    {
                        searchType = searchType,
                        workspacePath = workspacePath,
                        timestamp = DateTime.UtcNow
                    });

                // Add resource URI to response using fast dynamic pattern
                dynamic d = response;
                return new
                {
                    success = d.success,
                    operation = d.operation,
                    query = d.query,
                    summary = d.summary,
                    results = d.results,
                    resultsSummary = d.resultsSummary,
                    distribution = d.distribution,
                    hotspots = d.hotspots,
                    insights = d.insights,
                    actions = d.actions,
                    meta = d.meta,
                    resourceUri = resourceUri
                };
            }

            return response;
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            Logger.LogWarning(cbEx, "Circuit breaker is open for text search");
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
            Logger.LogError(dnfEx, "Directory not found for text search");
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
            Logger.LogError(uaEx, "Permission denied for text search");
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.PERMISSION_DENIED,
                    message = $"Permission denied accessing {workspacePath}: {uaEx.Message}"
                }
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing fast text search");
            return new
            {
                success = false,
                error = new
                {
                    code = ErrorCodes.INTERNAL_ERROR,
                    message = $"Search failed: {ex.Message}"
                }
            };
        }
    }


    private async Task<List<SearchResult>> ProcessSearchResultsAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        int maxResults,
        CancellationToken cancellationToken)
    {
        const int StreamingThreshold = 100; // Use streaming for 100+ results
        
        if (topDocs.ScoreDocs.Length >= StreamingThreshold)
        {
            return await ProcessSearchResultsStreamingAsync(searcher, topDocs, query, contextLines, maxResults, cancellationToken);
        }
        else
        {
            return await ProcessSearchResultsOptimizedAsync(searcher, topDocs, query, contextLines, maxResults, cancellationToken);
        }
    }

    /// <summary>
    /// Optimized processing for smaller result sets using field selectors
    /// </summary>
    private async Task<List<SearchResult>> ProcessSearchResultsOptimizedAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        int maxResults,
        CancellationToken cancellationToken)
    {
        // Limit results based on confidence analysis
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
            // Use field selector for efficient document loading - PRIMARY OPTIMIZATION
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

            // Add context if requested
            if (contextLines.HasValue && contextLines.Value > 0)
            {
                result.Context = await GetFileContextAsync(filePath, query, contextLines.Value, ct);
            }

            resultBag.Add((result, scoreDoc.Score));
        });

        // Sort by score and return
        return resultBag.OrderByDescending(r => r.score).Select(r => r.result).ToList();
    }

    /// <summary>
    /// Memory-efficient streaming processing for large result sets
    /// </summary>
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
        
        // Limit results based on confidence analysis
        var scoreDocs = topDocs.ScoreDocs.Take(maxResults).ToArray();
        
        // Create score lookup for efficient score retrieval
        var scoreMap = scoreDocs.ToDictionary(sd => sd.Doc, sd => sd.Score);
        
        var streamingOptions = new StreamingOptions
        {
            BatchSize = 50,
            BatchDelay = TimeSpan.FromMilliseconds(2),
            MaxResults = maxResults
        };

        // Create limited TopDocs for streaming
        var limitedTopDocs = new TopDocs(topDocs.TotalHits, scoreDocs, topDocs.MaxScore);
        
        // Stream results with field selector optimization
        await foreach (var batch in _streamingResultService.StreamResultsWithFieldSelectorAsync(
            searcher, limitedTopDocs, (s, docId, fields) => ProcessDocumentWithScore(s, docId, fields, scoreMap), 
            fieldSet.Fields, streamingOptions, cancellationToken))
        {
            // Process each batch
            foreach (var result in batch.Results)
            {                
                // Add context if requested (done in batch to reduce I/O overhead)
                if (contextLines.HasValue && contextLines.Value > 0)
                {
                    result.Context = await GetFileContextAsync(result.FilePath, query, contextLines.Value, cancellationToken);
                }
                
                results.Add(result);
            }
            
            // Log progress for very large result sets
            if (batch.BatchNumber % 20 == 0)
            {
                Logger.LogDebug("Processed streaming batch {BatchNumber}, total results: {Total}", 
                    batch.BatchNumber, batch.TotalProcessed);
            }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>
    /// Document processor for streaming with score preservation
    /// </summary>
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
            Score = scoreMap.GetValueOrDefault(docId, 0f), // Get actual score from TopDocs
            Language = doc.Get("language") ?? ""
        };
        
        return result;
    }

    private async Task<object> CreateAiOptimizedResponse(
        string query,
        string searchType,
        string workspacePath,
        List<SearchResult> results,
        long totalHits,
        string? filePattern,
        string[]? extensions,
        ResponseMode mode,
        CancellationToken cancellationToken = default)
    {
        // Group by extension
        var byExtension = results
            .GroupBy(r => r.Extension)
            .ToDictionary(
                g => g.Key,
                g => new { count = g.Count(), files = g.Select(r => r.FileName).Distinct().Count() }
            );

        // Group by directory
        var byDirectory = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(
                g => g.Key,
                g => g.Count()
            );

        // Find hotspot files
        var hotspots = results
            .GroupBy(r => r.RelativePath)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new HotspotInfo
            { 
                File = g.Key, 
                Matches = g.Count(),
                Lines = g.SelectMany(r => r.Context?.Where(c => c.IsMatch).Select(c => c.LineNumber) ?? Enumerable.Empty<int>()).Distinct().Count()
            })
            .ToList();

        // Check for alternate results if we got zero results with file restrictions
        long? alternateHits = null;
        Dictionary<string, int>? alternateExtensions = null;
        if (totalHits == 0 && (filePattern != null || extensions?.Length > 0))
        {
            var (altHits, altExts) = await CheckAlternateSearchResults(query, workspacePath, searchType, false, cancellationToken);
            if (altHits > 0)
            {
                alternateHits = altHits;
                alternateExtensions = altExts;
            }
        }
        
        // Get project context
        var projectContext = await GetProjectContextAsync(workspacePath);
        
        // Generate insights
        var insights = GenerateSearchInsights(query, searchType, workspacePath, results, totalHits, filePattern, extensions, projectContext, alternateHits, alternateExtensions);

        // Generate actions
        var actions = GenerateSearchActions(query, searchType, results, totalHits, hotspots, 
            byExtension.ToDictionary(kvp => kvp.Key, kvp => (dynamic)kvp.Value), mode);

        // Determine how many results to include inline based on token budget, mode, and context
        // Token-aware result limiting: with context lines, fewer results to stay under token limits
        var hasContext = results.Any(r => r.Context?.Any() == true);
        var maxInlineResults = hasContext ? 5 : 10; // Fewer results when including context
        var includeResults = mode == ResponseMode.Full || results.Count <= maxInlineResults;
        var inlineResults = includeResults ? results : results.Take(maxInlineResults).ToList();
        
        // Pre-estimate response size and apply hard safety limit
        var preEstimatedTokens = EstimateResponseTokens(inlineResults) + 500; // Add overhead for metadata
        var safetyLimitApplied = false;
        if (preEstimatedTokens > 5000)
        {
            Logger.LogWarning("Pre-estimated response ({Tokens} tokens) exceeds safety threshold. Forcing minimal results.", preEstimatedTokens);
            // Force minimal results to ensure we stay under limit
            inlineResults = results.Take(3).ToList();
            // Remove context from these results to save even more tokens
            foreach (var result in inlineResults)
            {
                result.Context = null;
            }
            safetyLimitApplied = true;
            // Add a warning to insights
            insights.Insert(0, $"⚠️ Response size limit applied ({preEstimatedTokens} tokens). Showing 3 results without context.");
        }
        
        // Create response object with hybrid approach: include first page of results
        var response = new
        {
            success = true,
            operation = ToolNames.TextSearch,
            query = new
            {
                text = query,
                type = searchType,
                filePattern = filePattern,
                extensions = extensions,
                workspace = workspacePath
            },
            summary = new
            {
                totalHits = totalHits,
                returnedResults = results.Count,
                filesMatched = results.Select(r => r.FilePath).Distinct().Count(),
                truncated = totalHits > results.Count
            },
            // Include actual results for immediate AI agent use
            // Use consistent field names with file_search for AI agent consistency
            results = inlineResults.Select(r => new
            {
                file = r.FileName,
                path = r.RelativePath,
                score = Math.Round(r.Score, 2),
                // Only include context if it exists to manage token usage
                context = r.Context?.Any() == true ? r.Context.Select(c => new
                {
                    line = c.LineNumber,
                    content = c.Content,
                    match = c.IsMatch
                }).ToList() : null
            }).ToList(),
            resultsSummary = new
            {
                included = inlineResults.Count,
                total = results.Count,
                hasMore = results.Count > inlineResults.Count
            },
            distribution = new
            {
                byExtension = byExtension,
                byDirectory = byDirectory
            },
            hotspots = hotspots.Select(h => new { file = h.File, matches = h.Matches, lines = h.Lines }).ToList(),
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = safetyLimitApplied ? "safety-limited" : mode.ToString().ToLowerInvariant(),
                indexed = true,
                tokens = EstimateResponseTokens(inlineResults),
                cached = $"txt_{Guid.NewGuid().ToString("N")[..8]}",
                safetyLimitApplied = safetyLimitApplied,
                originalEstimatedTokens = safetyLimitApplied ? preEstimatedTokens : (int?)null
            }
        };


        // Store search results as a resource if provider is available
        if (_searchResultResourceProvider != null && results.Count > 0)
        {
            var resourceUri = _searchResultResourceProvider.StoreSearchResult(
                query, 
                new
                {
                    results = results,
                    query = response.query,
                    summary = response.summary,
                    distribution = response.distribution,
                    hotspots = response.hotspots,
                    insights = response.insights
                },
                new
                {
                    searchType = searchType,
                    workspacePath = workspacePath,
                    timestamp = DateTime.UtcNow
                });

            // Add resource URI to response while keeping results
            return new
            {
                success = response.success,
                operation = response.operation,
                query = response.query,
                summary = response.summary,
                results = response.results,  // Include results in response
                resultsSummary = response.resultsSummary,
                distribution = response.distribution,
                hotspots = response.hotspots,
                insights = response.insights,
                actions = response.actions,
                meta = response.meta,
                resourceUri = resourceUri  // Also provide URI for complete results
            };
        }

        return response;
    }

    private List<string> GenerateSearchInsights(
        string query,
        string searchType,
        string workspacePath,
        List<SearchResult> results,
        long totalHits,
        string? filePattern,
        string[]? extensions,
        ProjectContext? projectContext,
        long? alternateHits,
        Dictionary<string, int>? alternateExtensions)
    {
        var insights = new List<string>();

        // Basic result insights
        if (totalHits == 0)
        {
            insights.Add($"No matches found for '{query}'");
            
            // Check if alternate search would find results
            if (alternateHits > 0 && alternateExtensions != null)
            {
                var topExtensions = alternateExtensions
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"{kvp.Key} ({kvp.Value})")
                    .ToList();
                    
                insights.Add($"Found {alternateHits} matches in other file types: {string.Join(", ", topExtensions)}");
                insights.Add($"💡 TIP: Remove filePattern/extensions to search ALL file types");
                insights.Add($"🔍 Try: text_search --query \"{query}\" --workspacePath \"{workspacePath}\"");
                
                // Project-aware suggestions
                if (projectContext?.Technologies?.Contains("blazor", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 Blazor project detected - UI components are in .razor files!");
                        insights.Add($"🔍 Try: text_search --query \"{query}\" --extensions .cs,.razor --workspacePath \"{workspacePath}\"");
                    }
                }
                else if (projectContext?.Technologies?.Contains("aspnet", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 ASP.NET project detected - views are in .cshtml files!");
                        insights.Add($"🔍 Try: text_search --query \"{query}\" --extensions .cs,.cshtml --workspacePath \"{workspacePath}\"");
                    }
                }
            }
            else
            {
                // Original suggestions when no alternate results
                if (searchType == "standard" && !query.Contains("*"))
                {
                    insights.Add("Try wildcard search with '*' or fuzzy search with '~'");
                }
                if (extensions?.Length > 0)
                {
                    insights.Add($"Search limited to: {string.Join(", ", extensions)}");
                }
                if (!string.IsNullOrEmpty(filePattern))
                {
                    insights.Add($"Results filtered by pattern: {filePattern}");
                }
            }
        }
        else if (totalHits > results.Count)
        {
            insights.Add($"Showing {results.Count} of {totalHits} total matches");
            if (totalHits > 100)
            {
                insights.Add("Consider refining search or using file patterns");
            }
        }

        // File type insights
        var extensionGroups = results.GroupBy(r => r.Extension).OrderByDescending(g => g.Count()).ToList();
        if (extensionGroups.Count > 1)
        {
            var topTypes = string.Join(", ", extensionGroups.Take(3).Select(g => $"{g.Key} ({g.Count()})"));
            insights.Add($"Most matches in: {topTypes}");
        }

        // Concentration insights
        var filesWithMatches = results.Select(r => r.FilePath).Distinct().Count();
        if (filesWithMatches > 0 && totalHits > filesWithMatches * 2)
        {
            var avgMatchesPerFile = totalHits / filesWithMatches;
            insights.Add($"Average {avgMatchesPerFile:F1} matches per file - some files have high concentration");
        }

        // Search type insights
        if (searchType == "fuzzy" && results.Any())
        {
            insights.Add("Fuzzy search found approximate matches");
        }
        else if (searchType == "phrase")
        {
            insights.Add("Exact phrase search - results contain the full phrase");
        }

        // Pattern insights
        if (!string.IsNullOrEmpty(filePattern))
        {
            insights.Add($"Results filtered by pattern: {filePattern}");
        }

        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (totalHits > 0)
            {
                insights.Add($"Found {totalHits} matches for '{query}' in {filesWithMatches} files");
                if (extensionGroups.Any())
                {
                    insights.Add($"Search matched files of type: {string.Join(", ", extensionGroups.Select(g => g.Key))}");
                }
            }
            else
            {
                insights.Add($"No matches found for '{query}'");
            }
        }

        return insights;
    }

    private List<object> GenerateSearchActions(
        string query,
        string searchType,
        List<SearchResult> results,
        long totalHits,
        List<HotspotInfo> hotspots,
        Dictionary<string, dynamic> byExtension,
        ResponseMode mode)
    {
        var actions = new List<object>();

        // Refine search actions
        if (totalHits > 100)
        {
            if (byExtension.Count > 1)
            {
                var topExt = byExtension.OrderByDescending(kvp => (int)kvp.Value.count).First();
                actions.Add(new
                {
                    id = "filter_by_type",
                    cmd = new { query = query, extensions = new[] { topExt.Key } },
                    tokens = Math.Min(2000, (int)topExt.Value.count * 50),
                    priority = "recommended"
                });
            }

            actions.Add(new
            {
                id = "narrow_search",
                cmd = new { query = $"\"{query}\" AND specific_term", searchType = "standard" },
                tokens = 1500,
                priority = "normal"
            });
        }

        // Context actions
        if (hotspots.Any() && results.Any(r => r.Context == null))
        {
            actions.Add(new
            {
                id = "add_context",
                cmd = new { query = query, contextLines = 3 },
                tokens = EstimateContextTokens(results.Take(20).ToList(), 3),
                priority = "recommended"
            });
        }

        // Explore hotspots
        if (hotspots.Any())
        {
            var topHotspot = hotspots.First();
            actions.Add(new
            {
                id = "explore_hotspot",
                cmd = new { file = topHotspot.File },
                tokens = 1000,
                priority = "normal"
            });
        }

        // Alternative search types
        if (searchType == "standard" && !query.Contains("*"))
        {
            actions.Add(new
            {
                id = "try_wildcard",
                cmd = new { query = $"*{query}*", searchType = "wildcard" },
                tokens = 2000,
                priority = "available"
            });

            actions.Add(new
            {
                id = "try_fuzzy",
                cmd = new { query = query.TrimEnd('~') + "~", searchType = "fuzzy" },
                tokens = 2000,
                priority = "available"
            });
        }

        // Full details action
        if (mode == ResponseMode.Summary && results.Count < 100)
        {
            actions.Add(new
            {
                id = "full_details",
                cmd = new { responseMode = "full" },
                tokens = EstimateFullResponseTokens(results),
                priority = "available"
            });
        }

        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (totalHits > 0)
            {
                actions.Add(new
                {
                    id = "refine_search",
                    cmd = new { query = query + "*", searchType = "wildcard" },
                    tokens = 2000,
                    priority = "recommended"
                });
            }
            else
            {
                actions.Add(new
                {
                    id = "try_broader_search",
                    cmd = new { query = "*" + query + "*", searchType = "wildcard" },
                    tokens = 3000,
                    priority = "recommended"
                });
            }
        }

        return actions;
    }

    private int EstimateResponseTokens(List<SearchResult> results)
    {
        // Estimate ~100 tokens per result without context, ~200 with context
        var hasContext = results.Any(r => r.Context != null);
        var tokensPerResult = hasContext ? 200 : 100;
        return Math.Min(25000, results.Count * tokensPerResult);
    }

    private int EstimateContextTokens(List<SearchResult> results, int contextLines)
    {
        // Estimate ~50 tokens per context line
        return results.Count * contextLines * 2 * 50; // *2 for before and after
    }

    private int EstimateFullResponseTokens(List<SearchResult> results)
    {
        return EstimateResponseTokens(results) + 1000; // Add overhead for full structure
    }


    private Query BuildQueryWithCache(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        // Create a cache key that includes all query parameters
        var cacheKey = $"{queryText}|{searchType}|{caseSensitive}|{filePattern}|{string.Join(",", extensions ?? Array.Empty<string>())}";
        
        return _queryCacheService.GetOrCreateQuery(cacheKey, searchType, () => 
            BuildQuery(queryText, searchType, caseSensitive, filePattern, extensions, analyzer));
    }

    private Query BuildQuery(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        var booleanQuery = new BooleanQuery();

        // Main content query
        Query contentQuery;
        switch (searchType.ToLowerInvariant())
        {
            case "wildcard":
                // For wildcard queries, we need to escape special characters except * and ?
                var wildcardEscaped = EscapeQueryTextForWildcard(queryText);
                contentQuery = new WildcardQuery(new Term("content", wildcardEscaped.ToLowerInvariant()));
                break;
            
            case "fuzzy":
                // For fuzzy queries, escape all special characters except ~
                var fuzzyEscaped = EscapeQueryTextForFuzzy(queryText);
                contentQuery = new FuzzyQuery(new Term("content", fuzzyEscaped.ToLowerInvariant()));
                break;
            
            case "phrase":
                var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                contentQuery = parser.Parse($"\"{EscapeQueryText(queryText)}\"");
                break;
            
            case "regex":
                // For regex queries, use RegexpQuery directly without escaping
                try
                {
                    // Validate regex first
                    _ = new System.Text.RegularExpressions.Regex(queryText);
                    contentQuery = new RegexpQuery(new Term("content", queryText));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Invalid regex pattern: {Query}, falling back to escaped standard search", queryText);
                    // Fall back to standard search with escaping
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
                    // Last resort: treat as a simple term query
                    contentQuery = new TermQuery(new Term("content", queryText.ToLowerInvariant()));
                }
                break;
        }

        booleanQuery.Add(contentQuery, Occur.MUST);

        // File pattern filter
        if (!string.IsNullOrWhiteSpace(filePattern))
        {
            var pathQuery = new WildcardQuery(new Term("relativePath", $"*{filePattern}*"));
            booleanQuery.Add(pathQuery, Occur.MUST);
        }

        // Extension filters
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
            // Check if index exists and is recent
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var indexReader = searcher.IndexReader;
            
            // If index is empty or very small, reindex
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
            
            // Try to create a new index
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
                
                // Keep a sliding window of lines
                buffer.Add((lineNumber, line));
                if (buffer.Count > contextLines * 2 + 1)
                    buffer.RemoveAt(0);
                
                // Check for match
                if (line.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    // Found a match, add context from buffer
                    var matchIndex = buffer.Count - 1;
                    var startIndex = Math.Max(0, matchIndex - contextLines);
                    var endIndex = Math.Min(buffer.Count - 1, matchIndex + contextLines);
                    
                    // Read ahead for context after match
                    var linesAfter = endIndex - matchIndex;
                    for (int i = 0; i < contextLines - linesAfter && (line = await reader.ReadLineAsync(cancellationToken)) != null; i++)
                    {
                        lineNumber++;
                        buffer.Add((lineNumber, line));
                    }
                    
                    // Add context lines
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
        // Lucene special characters that need escaping
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
        // For wildcard queries, escape all special chars EXCEPT * and ?
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
        // For fuzzy queries, escape all special chars EXCEPT ~
        var specialChars = new[] { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '*', '?', ':', '\\', '/', '<', '>' };
        
        var escapedQuery = query;
        foreach (var c in specialChars)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }


    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for text search"));
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

    private class HotspotInfo
    {
        public required string File { get; set; }
        public int Matches { get; set; }
        public int Lines { get; set; }
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
            
            // Build query without file pattern restrictions
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
            
            // Search without restrictions
            var collector = TopScoreDocCollector.Create(1000, true);
            searcher.Search(luceneQuery, collector);
            
            var topDocs = collector.GetTopDocs();
            var extensionCounts = new Dictionary<string, int>();
            
            // Count extensions using field selector for performance
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