using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
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
    public override string ToolName => "fast_text_search_v2";
    public override string Description => "AI-optimized text search with insights";
    public override ToolCategory Category => ToolCategory.Search;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly FileIndexingService _fileIndexingService;
    private readonly IContextAwarenessService? _contextAwarenessService;

    public FastTextSearchToolV2(
        ILogger<FastTextSearchToolV2> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        FileIndexingService fileIndexingService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache,
        IContextAwarenessService? contextAwarenessService = null)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _configuration = configuration;
        _luceneIndexService = luceneIndexService;
        _fileIndexingService = fileIndexingService;
        _contextAwarenessService = contextAwarenessService;
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
                return CreateErrorResponse<object>("Search query cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return CreateErrorResponse<object>("Workspace path cannot be empty");
            }

            // Ensure the directory is indexed first
            if (!await EnsureIndexedAsync(workspacePath, cancellationToken))
            {
                return CreateErrorResponse<object>("Failed to index workspace");
            }

            // Get the searcher
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var analyzer = await _luceneIndexService.GetAnalyzerAsync(workspacePath, cancellationToken);

            // Build the query
            var luceneQuery = BuildQuery(query, searchType, caseSensitive, filePattern, extensions, analyzer);

            // Execute search
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var results = await ProcessSearchResultsAsync(searcher, topDocs, query, contextLines, cancellationToken);

            // Create AI-optimized response
            return await CreateAiOptimizedResponse(query, searchType, workspacePath, results, topDocs.TotalHits, filePattern, extensions, mode, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing fast text search");
            return CreateErrorResponse<object>($"Search failed: {ex.Message}");
        }
    }


    private async Task<List<SearchResult>> ProcessSearchResultsAsync(
        IndexSearcher searcher,
        TopDocs topDocs,
        string query,
        int? contextLines,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<SearchResult>();
        
        var parallelOptions = new ParallelOptions 
        { 
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount 
        };
        
        await Parallel.ForEachAsync(topDocs.ScoreDocs, parallelOptions, async (scoreDoc, ct) =>
        {
            var doc = searcher.Doc(scoreDoc.Doc);
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

            results.Add(result);
        });

        return results.OrderByDescending(r => r.Score).ToList();
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

        // Create response
        return new
        {
            success = true,
            operation = "fast_text_search",
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
                mode = mode.ToString().ToLowerInvariant(),
                indexed = true,
                tokens = EstimateResponseTokens(results),
                cached = $"txt_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
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
                insights.Add($"🔍 Try: fast_text_search --query \"{query}\" --workspacePath \"{workspacePath}\"");
                
                // Project-aware suggestions
                if (projectContext?.Technologies?.Contains("blazor", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 Blazor project detected - UI components are in .razor files!");
                        insights.Add($"🔍 Try: fast_text_search --query \"{query}\" --extensions .cs,.razor --workspacePath \"{workspacePath}\"");
                    }
                }
                else if (projectContext?.Technologies?.Contains("aspnet", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 ASP.NET project detected - views are in .cshtml files!");
                        insights.Add($"🔍 Try: fast_text_search --query \"{query}\" --extensions .cs,.cshtml --workspacePath \"{workspacePath}\"");
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

    private Query BuildQuery(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        var booleanQuery = new BooleanQuery();

        // Main content query
        Query contentQuery;
        switch (searchType.ToLowerInvariant())
        {
            case "wildcard":
                contentQuery = new WildcardQuery(new Term("content", queryText.ToLowerInvariant()));
                break;
            
            case "fuzzy":
                contentQuery = new FuzzyQuery(new Term("content", queryText.ToLowerInvariant()));
                break;
            
            case "phrase":
                var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                contentQuery = parser.Parse($"\"{EscapeQueryText(queryText)}\"");
                break;
            
            default: // standard
                var standardParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                standardParser.DefaultOperator = Operator.AND;
                var escapedQuery = EscapeQueryText(queryText);
                contentQuery = standardParser.Parse(escapedQuery);
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
            
            // Count extensions
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
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