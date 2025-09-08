using System.IO;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Services.Utils;
using Lucene.Net.QueryParsers.Classic;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for generating smart code snippets with highlighting for VS Code visualization
/// </summary>
public class SmartSnippetService
{
    private readonly ILogger<SmartSnippetService> _logger;
    private readonly CodeAnalyzer _codeAnalyzer;
    
    private const int CONTEXT_LINES = 3;   // Lines before/after match

    public SmartSnippetService(ILogger<SmartSnippetService> logger, CodeAnalyzer codeAnalyzer)
    {
        _logger = logger;
        _codeAnalyzer = codeAnalyzer;
    }

    /// <summary>
    /// Enhance search results with smart snippets for visualization
    /// </summary>
    public Task<SearchResult> EnhanceWithSnippetsAsync(
        SearchResult result, 
        Query query, 
        IndexSearcher searcher,
        bool forVisualization = false,
        CancellationToken cancellationToken = default)
    {
        if (!forVisualization || result.Hits == null || !result.Hits.Any())
        {
            return Task.FromResult(result);
        }

        _logger.LogDebug("Generating snippets for {HitCount} search results", result.Hits.Count);

        try
        {
            // Create Lucene highlighter for fragment extraction
            // Use chevrons for highlighting to avoid HTML parsing issues
            var formatter = new SimpleHTMLFormatter("«", "»");
            
            var analyzer = _codeAnalyzer;
            
            // Create a content-specific query for highlighting
            // Extract terms from the original query and create a content field query
            var contentQuery = CreateContentQuery(query, analyzer);
            var scorer = new QueryScorer(contentQuery);
            
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new NullFragmenter()
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
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enhance search results with snippets");
            return Task.FromResult(result); // Return original results if snippet generation fails
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

        // Extract context lines around the match first
        if (hit.LineNumber.HasValue)
        {
            var contextInfo = ExtractContextLines(content, hit.LineNumber.Value);
            hit.ContextLines = contextInfo.Lines;
            hit.StartLine = contextInfo.StartLine;
            hit.EndLine = contextInfo.EndLine;
            
            // Apply highlighting to the complete context lines
            try
            {
                var contextText = string.Join("\n", contextInfo.Lines);
                _logger.LogDebug("Highlighting context of {LineCount} complete lines for {FilePath}", contextInfo.Lines.Count, hit.FilePath);
                
                using var tokenStream = analyzer.GetTokenStream("content", contextText);
                var highlightedContext = highlighter.GetBestFragment(tokenStream, contextText);
                
                if (!string.IsNullOrEmpty(highlightedContext))
                {
                    hit.Snippet = highlightedContext;
                    // Don't set HighlightedFragments - this goes to AI and we don't want «» markers there
                    // hit.HighlightedFragments = new List<string> { highlightedContext };
                    _logger.LogDebug("Generated highlighted context snippet for {FilePath}", hit.FilePath);
                }
                else
                {
                    // Fallback to unhighlighted context
                    hit.Snippet = contextText;
                    _logger.LogDebug("Using unhighlighted context for {FilePath}", hit.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to highlight context lines for {FilePath}, using unhighlighted context", hit.FilePath);
                hit.Snippet = string.Join("\n", contextInfo.Lines);
            }
        }

        return hit;
    }

    /// <summary>
    /// Extract context lines around a specific line number
    /// </summary>
    private (List<string> Lines, int StartLine, int EndLine) ExtractContextLines(string content, int matchLineNumber)
    {
        // Use StringReader for cross-platform line ending handling
        var allLines = new List<string>();
        using (var reader = new StringReader(content))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                allLines.Add(line);
            }
        }
        
        // Calculate context window
        var startLine = Math.Max(1, matchLineNumber - CONTEXT_LINES);
        var endLine = Math.Min(allLines.Count, matchLineNumber + CONTEXT_LINES);
        
        // Extract lines (convert to 0-based for array access)
        var startIndex = startLine - 1;
        var count = endLine - startLine + 1;
        
        var contextLines = allLines
            .Skip(startIndex)
            .Take(count)
            .ToList();

        return (contextLines, startLine, endLine);
    }

    /// <summary>
    /// Get document ID from SearchHit
    /// TODO: Optimize by storing docId in SearchHit during initial search
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
                .Replace("content_patterns:", "")
                .Replace("content_symbols:", "")
                .Replace("filename:", "")
                .Replace("filename_lower:", "")
                .Replace("path:", "")
                .Replace("extension:", "")
                .Replace("type_info:", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("+", "")
                .Replace("-", "")
                // Clean up any remaining orphaned colons
                .Replace("::", " ")
                .Replace(":", " ");
                
            // If the cleaned query is empty or too short, return the original
            if (string.IsNullOrWhiteSpace(cleanedQuery) || cleanedQuery.Trim().Length < 2)
            {
                return originalQuery;
            }
            
            // NEW: Validate and preprocess query to handle wildcards safely
            var trimmedQuery = cleanedQuery.Trim();
            if (WildcardValidator.IsInvalidWildcardQuery(trimmedQuery))
            {
                _logger.LogDebug("Invalid wildcard query detected: {Query}, using original query", trimmedQuery);
                return originalQuery;
            }
            
            // Parse the cleaned query for the content field
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            parser.DefaultOperator = QueryParserBase.AND_OPERATOR;
            
            var contentQuery = parser.Parse(trimmedQuery);
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