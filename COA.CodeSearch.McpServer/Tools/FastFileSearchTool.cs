using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Straight blazin' fast file search using Lucene index - find files by name with fuzzy matching, wildcards, and typo correction
/// </summary>
public class FastFileSearchTool : ITool
{
    public string ToolName => "fast_file_search";
    public string Description => "Find files by name with fuzzy matching and typo correction";
    public ToolCategory Category => ToolCategory.Search;
    private readonly ILogger<FastFileSearchTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastFileSearchTool(
        ILogger<FastFileSearchTool> logger,
        ILuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
    }

    public async Task<object> ExecuteAsync(
        string query,
        string workspacePath,
        string? searchType = "standard",
        int maxResults = 50,
        bool includeDirectories = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fast file search for '{Query}' in {WorkspacePath}, Type: {SearchType}", 
                query, workspacePath, searchType);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(query))
            {
                return new
                {
                    success = false,
                    error = "Query cannot be empty"
                };
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = "Workspace path cannot be empty"
                };
            }

            // Get index searcher
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            
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
            var results = new List<object>();
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                
                results.Add(new
                {
                    path = doc.Get("path"),
                    filename = doc.Get("filename"),
                    relativePath = doc.Get("relativePath"),
                    extension = doc.Get("extension"),
                    size = long.Parse(doc.Get("size") ?? "0"),
                    lastModified = new DateTime(long.Parse(doc.Get("lastModified") ?? "0")),
                    score = scoreDoc.Score,
                    language = doc.Get("language") ?? ""
                });
            }

            _logger.LogInformation("Found {Count} files in {Duration}ms - straight blazin' fast!", 
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
                performance = searchDuration < 10 ? "straight blazin'" : "blazin' fast"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fast file search for query: {Query}", query);
            return new
            {
                success = false,
                error = $"Search failed: {ex.Message}",
                query = query
            };
        }
    }

    private Query BuildStandardQuery(string query)
    {
        // Search in both filename and path fields
        // Use StandardAnalyzer for query parsing
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Version);
        var parser = new MultiFieldQueryParser(Version, 
            new[] { "filename_text", "relativePath" }, 
            analyzer);
        
        // Allow wildcards and fuzzy by default
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
        // Add fuzzy operator if not present
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
        // Ensure wildcards are present
        if (!query.Contains('*') && !query.Contains('?'))
        {
            query = $"*{query}*";
        }
        
        return new WildcardQuery(new Term("filename_text", query.ToLower()));
    }

    private Query BuildExactQuery(string query)
    {
        return new TermQuery(new Term("filename", query));
    }

    private Query BuildRegexQuery(string query)
    {
        try
        {
            // Validate regex
            _ = new Regex(query);
            return new RegexpQuery(new Term("filename", query));
        }
        catch (ArgumentException)
        {
            // Fall back to standard query if regex is invalid
            _logger.LogWarning("Invalid regex pattern: {Query}, falling back to standard search", query);
            return BuildStandardQuery(query);
        }
    }
}