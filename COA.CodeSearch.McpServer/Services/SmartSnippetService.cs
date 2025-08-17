using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using Lucene.Net.QueryParsers.Classic;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for generating smart code snippets with highlighting for VS Code visualization
/// </summary>
public class SmartSnippetService
{
    private readonly ILogger<SmartSnippetService> _logger;
    
    private const int FRAGMENT_SIZE = 150; // ~3 lines of code
    private const int MAX_FRAGMENTS = 3;   // Top 3 matches per file
    private const int CONTEXT_LINES = 3;   // Lines before/after match
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public SmartSnippetService(ILogger<SmartSnippetService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enhance search results with smart snippets for visualization
    /// </summary>
    public async Task<SearchResult> EnhanceWithSnippetsAsync(
        SearchResult result, 
        Query query, 
        IndexSearcher searcher,
        bool forVisualization = false,
        CancellationToken cancellationToken = default)
    {
        if (!forVisualization || result.Hits == null || !result.Hits.Any())
        {
            return result;
        }

        _logger.LogDebug("Generating snippets for {HitCount} search results", result.Hits.Count);

        try
        {
            // Create Lucene highlighter for fragment extraction
            // Use chevrons for highlighting to avoid HTML parsing issues
            var formatter = new SimpleHTMLFormatter("«", "»");
            
            var analyzer = new CodeAnalyzer(LUCENE_VERSION);
            
            // Create a content-specific query for highlighting
            // Extract terms from the original query and create a content field query
            var contentQuery = CreateContentQuery(query, analyzer);
            var scorer = new QueryScorer(contentQuery);
            
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new SimpleFragmenter(FRAGMENT_SIZE)
            };
            
            _logger.LogDebug("Created highlighter with content query: {Query}", contentQuery?.ToString() ?? "null");
            var enhancedHits = new List<SearchHit>();

            foreach (var hit in result.Hits)
            {
                try
                {
                    var enhancedHit = EnhanceHitWithSnippet(
                        hit, 
                        highlighter, 
                        analyzer, 
                        searcher);
                    
                    enhancedHits.Add(enhancedHit);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate snippet for {FilePath}", hit.FilePath);
                    // Add the original hit without snippet
                    enhancedHits.Add(hit);
                }
            }

            result.Hits = enhancedHits;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enhance search results with snippets");
            return result; // Return original results if snippet generation fails
        }
    }

    /// <summary>
    /// Enhance a single search hit with snippet and context lines
    /// </summary>
    private SearchHit EnhanceHitWithSnippet(
        SearchHit hit,
        Highlighter highlighter,
        CodeAnalyzer analyzer,
        IndexSearcher searcher)
    {
        // Get the document to access stored content
        var docId = GetDocumentId(hit, searcher);
        if (!docId.HasValue)
        {
            return hit;
        }

        var doc = searcher.Doc(docId.Value);
        var content = doc.Get("content");

        if (string.IsNullOrEmpty(content))
        {
            return hit;
        }

        // Extract highlighted fragments using Lucene highlighter
        try
        {
            _logger.LogDebug("Attempting to highlight content of length {Length} for {FilePath}", content.Length, hit.FilePath);
            using var tokenStream = analyzer.GetTokenStream("content", content);
            var fragments = highlighter.GetBestFragments(tokenStream, content, MAX_FRAGMENTS);
            
            _logger.LogDebug("Generated {FragmentCount} fragments for {FilePath}", fragments?.Length ?? 0, hit.FilePath);
            
            if (fragments != null && fragments.Any())
            {
                hit.HighlightedFragments = fragments.ToList();
                hit.Snippet = fragments.FirstOrDefault();
                _logger.LogDebug("Best fragment: {Fragment}", hit.Snippet);
            }
            else
            {
                _logger.LogDebug("No fragments generated for {FilePath}", hit.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate highlighted fragments for {FilePath}", hit.FilePath);
        }

        // This code block is replaced above

        // Extract context lines around the match
        if (hit.LineNumber.HasValue)
        {
            var contextInfo = ExtractContextLines(content, hit.LineNumber.Value);
            hit.ContextLines = contextInfo.Lines;
            hit.StartLine = contextInfo.StartLine;
            hit.EndLine = contextInfo.EndLine;
        }

        return hit;
    }

    /// <summary>
    /// Extract context lines around a specific line number
    /// </summary>
    private (List<string> Lines, int StartLine, int EndLine) ExtractContextLines(string content, int matchLineNumber)
    {
        var lines = content.Split('\n');
        
        // Calculate context window
        var startLine = Math.Max(1, matchLineNumber - CONTEXT_LINES);
        var endLine = Math.Min(lines.Length, matchLineNumber + CONTEXT_LINES);
        
        // Extract lines (convert to 0-based for array access)
        var startIndex = startLine - 1;
        var count = endLine - startLine + 1;
        
        var contextLines = lines
            .Skip(startIndex)
            .Take(count)
            .ToList();

        return (contextLines, startLine, endLine);
    }

    /// <summary>
    /// Get document ID from SearchHit
    /// This is a simplified approach - in production you might want to store docId in SearchHit
    /// </summary>
    private int? GetDocumentId(SearchHit hit, IndexSearcher searcher)
    {
        try
        {
            // Simple approach: search by file path to get document ID
            // This could be optimized by storing docId in SearchHit during initial search
            var pathQuery = new TermQuery(new Term("path", hit.FilePath));
            var topDocs = searcher.Search(pathQuery, 1);
            
            if (topDocs.TotalHits > 0)
            {
                return topDocs.ScoreDocs[0].Doc;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find document ID for {FilePath}", hit.FilePath);
            return null;
        }
    }

    /// <summary>
    /// Create a simple text snippet without HTML highlighting
    /// Used when we want context but not markup
    /// </summary>
    public string CreatePlainTextSnippet(string content, int lineNumber, int contextLines = 2)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = content.Split('\n');
        var startLine = Math.Max(0, lineNumber - contextLines - 1);
        var endLine = Math.Min(lines.Length, lineNumber + contextLines);
        
        var snippet = string.Join('\n', lines.Skip(startLine).Take(endLine - startLine));
        
        // Trim to reasonable length
        if (snippet.Length > 300)
        {
            snippet = snippet.Substring(0, 297) + "...";
        }

        return snippet;
    }

    /// <summary>
    /// Create a content-specific query for highlighting by extracting terms from the original query
    /// </summary>
    private Query CreateContentQuery(Query originalQuery, CodeAnalyzer analyzer)
    {
        try
        {
            // Handle MultiFactorScoreQuery by extracting the base query
            Query queryToProcess = originalQuery;
            if (originalQuery is COA.CodeSearch.McpServer.Scoring.MultiFactorScoreQuery multiFactorQuery)
            {
                // Get the base query from MultiFactorScoreQuery
                var baseQueryField = multiFactorQuery.GetType().GetField("_baseQuery", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (baseQueryField != null)
                {
                    queryToProcess = baseQueryField.GetValue(multiFactorQuery) as Query ?? originalQuery;
                    _logger.LogDebug("Extracted base query from MultiFactorScoreQuery");
                }
            }
            
            // Convert the query to a string and extract meaningful content
            var queryString = queryToProcess.ToString();
            _logger.LogDebug("Query string for highlighting: {QueryString}", queryString);
            
            // Remove field-specific syntax and clean the query
            var cleanedQuery = queryString
                .Replace("content:", "")
                .Replace("filename:", "")
                .Replace("path:", "")
                .Replace("extension:", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("+", "")
                .Replace("-", "");
                
            // If the cleaned query is empty or too short, return the original
            if (string.IsNullOrWhiteSpace(cleanedQuery) || cleanedQuery.Trim().Length < 2)
            {
                return originalQuery;
            }
            
            // Parse the cleaned query for the content field
            var parser = new QueryParser(LUCENE_VERSION, "content", analyzer);
            parser.DefaultOperator = QueryParserBase.AND_OPERATOR;
            
            var contentQuery = parser.Parse(cleanedQuery.Trim());
            _logger.LogDebug("Created content query: {ContentQuery}", contentQuery.ToString());
            
            return contentQuery;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create content query, using original query");
            return originalQuery;
        }
    }
}