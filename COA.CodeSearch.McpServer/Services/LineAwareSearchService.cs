using COA.CodeSearch.McpServer.Services.Lucene;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for retrieving accurate line numbers directly from indexed line data
/// </summary>
public class LineAwareSearchService
{
    private readonly ILogger<LineAwareSearchService> _logger;
    private readonly LineNumberService _fallbackService; // For backward compatibility

    public LineAwareSearchService(
        ILogger<LineAwareSearchService> logger,
        LineNumberService fallbackService)
    {
        _logger = logger;
        _fallbackService = fallbackService;
    }

    /// <summary>
    /// Get line number for a search term from a document, using new line-aware data if available
    /// </summary>
    public LineAwareResult GetLineNumber(Document document, string queryText, IndexSearcher? searcher = null, int? docId = null)
    {
        // Try new line-aware approach first
        var lineDataResult = GetLineNumberFromLineData(document, queryText);
        if (lineDataResult.IsAccurate)
        {
            return lineDataResult;
        }

        // Fallback to legacy approach for backward compatibility
        if (searcher != null && docId.HasValue)
        {
            _logger.LogDebug("Falling back to legacy line number calculation for query: {Query}", queryText);
            var legacyResult = _fallbackService.CalculateLineNumber(searcher, docId.Value, queryText);
            
            return new LineAwareResult
            {
                LineNumber = legacyResult.LineNumber,
                IsAccurate = legacyResult.HasPreciseLocation,
                IsFromCache = false
            };
        }

        return new LineAwareResult { IsAccurate = false };
    }

    /// <summary>
    /// Get line number using the new line-aware data stored in the document
    /// </summary>
    private LineAwareResult GetLineNumberFromLineData(Document document, string queryText)
    {
        try
        {
            // Check if document has new line data
            var lineDataJson = document.Get("line_data");
            var lineDataVersion = document.GetField("line_data_version")?.GetInt32Value();
            
            if (string.IsNullOrEmpty(lineDataJson) || !lineDataVersion.HasValue)
            {
                _logger.LogTrace("Document does not have line data, using fallback approach");
                return new LineAwareResult { IsAccurate = false };
            }

            // Deserialize line data
            var lineData = LineIndexer.DeserializeLineData(lineDataJson);
            if (lineData == null)
            {
                _logger.LogWarning("Failed to deserialize line data from document");
                return new LineAwareResult { IsAccurate = false };
            }

            // Extract search terms from query
            var searchTerms = ExtractSearchTerms(queryText);
            if (searchTerms.Count == 0)
            {
                return new LineAwareResult { IsAccurate = false };
            }

            // Find best match using cached first matches or term-line mapping
            var bestResult = FindBestLineMatch(lineData, searchTerms);
            
            _logger.LogDebug("Found line number {LineNumber} for query '{Query}' using line-aware data (fromCache: {FromCache})",
                bestResult.LineNumber, queryText, bestResult.IsFromCache);
                
            return bestResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving line number from line data, falling back");
            return new LineAwareResult { IsAccurate = false };
        }
    }

    /// <summary>
    /// Find the best line match from line data using search terms
    /// </summary>
    private LineAwareResult FindBestLineMatch(LineData lineData, List<string> searchTerms)
    {
        LineAwareResult? bestResult = null;
        var earliestLineNumber = int.MaxValue;

        foreach (var term in searchTerms)
        {
            var normalizedTerm = term.ToLowerInvariant();

            // Try cached first match first (most accurate)
            if (lineData.FirstMatches.TryGetValue(normalizedTerm, out var cachedContext))
            {
                if (cachedContext.LineNumber < earliestLineNumber)
                {
                    earliestLineNumber = cachedContext.LineNumber;
                    bestResult = new LineAwareResult
                    {
                        LineNumber = cachedContext.LineNumber,
                        Context = cachedContext,
                        IsFromCache = true,
                        IsAccurate = true
                    };
                }
                continue;
            }

            // Try term-line mapping
            if (lineData.TermLineMap.TryGetValue(normalizedTerm, out var lineNumbers) && lineNumbers.Count > 0)
            {
                var firstLineNumber = lineNumbers[0];
                if (firstLineNumber < earliestLineNumber)
                {
                    earliestLineNumber = firstLineNumber;
                    bestResult = new LineAwareResult
                    {
                        LineNumber = firstLineNumber,
                        Context = CreateContextFromLineData(lineData, firstLineNumber),
                        IsFromCache = false,
                        IsAccurate = true
                    };
                }
            }
        }

        return bestResult ?? new LineAwareResult { IsAccurate = false };
    }

    /// <summary>
    /// Create line context from line data for a specific line number
    /// </summary>
    private LineContext? CreateContextFromLineData(LineData lineData, int lineNumber)
    {
        if (lineData.Lines == null || lineNumber < 1 || lineNumber > lineData.Lines.Length)
        {
            return null;
        }

        const int contextLines = 3;
        var startLine = Math.Max(1, lineNumber - contextLines);
        var endLine = Math.Min(lineData.Lines.Length, lineNumber + contextLines);
        
        var contextLinesList = new List<string>();
        for (int i = startLine - 1; i < endLine; i++) // Convert to 0-based indexing
        {
            contextLinesList.Add(lineData.Lines[i]);
        }

        return new LineContext
        {
            LineNumber = lineNumber,
            LineText = lineData.Lines[lineNumber - 1], // Convert to 0-based
            ContextLines = contextLinesList,
            StartLine = startLine,
            EndLine = endLine
        };
    }

    /// <summary>
    /// Extract search terms from query text, similar to LineNumberService but simpler
    /// </summary>
    private List<string> ExtractSearchTerms(string queryText)
    {
        var terms = new List<string>();
        
        if (string.IsNullOrEmpty(queryText)) return terms;

        // Clean query text of common Lucene syntax
        var cleaned = queryText
            .Replace("content:", "")
            .Replace("MultiFactorScore(", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("\"", "")
            .Replace("+", " ")
            .Replace("-", " ")
            .Replace("*", "")
            .Replace("?", "")
            .Replace("~", "")
            .Replace("^", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("{", "")
            .Replace("}", "")
            .Replace(", factors=", " ");

        // Simple word splitting
        var words = cleaned.Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var word in words)
        {
            var trimmed = word.Trim().ToLowerInvariant();
            if (trimmed.Length > 1 && !IsStopWord(trimmed))
            {
                terms.Add(trimmed);
            }
        }

        return terms;
    }

    /// <summary>
    /// Check if a word is a common stop word that should be ignored
    /// </summary>
    private static bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "or", "to", "in", "of", "a", "an", "is", "was", "are", "were",
            "be", "been", "have", "has", "had", "do", "does", "did", "will", "would",
            "could", "should", "may", "might", "can", "if", "else", "then", "this", "that",
            "factors" // Lucene-specific
        };
        
        return stopWords.Contains(word);
    }

    /// <summary>
    /// Check if a document has the new line-aware data
    /// </summary>
    public bool HasLineAwareData(Document document)
    {
        var lineDataVersion = document.GetField("line_data_version")?.GetInt32Value();
        return lineDataVersion.HasValue && lineDataVersion.Value >= 1;
    }

    /// <summary>
    /// Get statistics about line-aware data usage across search results
    /// </summary>
    public LineAwareSearchStatistics GetSearchStatistics(IEnumerable<Document> documents)
    {
        var total = 0;
        var hasLineData = 0;
        var hasLegacyData = 0;

        foreach (var doc in documents)
        {
            total++;
            
            if (HasLineAwareData(doc))
            {
                hasLineData++;
            }
            else if (!string.IsNullOrEmpty(doc.Get("line_breaks")))
            {
                hasLegacyData++;
            }
        }

        return new LineAwareSearchStatistics
        {
            TotalDocuments = total,
            DocumentsWithLineData = hasLineData,
            DocumentsWithLegacyData = hasLegacyData,
            LineDataCoverage = total > 0 ? (double)hasLineData / total : 0,
            RequiresMigration = hasLegacyData > 0
        };
    }
}

/// <summary>
/// Statistics about line-aware search data usage
/// </summary>
public class LineAwareSearchStatistics
{
    public int TotalDocuments { get; set; }
    public int DocumentsWithLineData { get; set; }
    public int DocumentsWithLegacyData { get; set; }
    public double LineDataCoverage { get; set; }
    public bool RequiresMigration { get; set; }
}