using COA.CodeSearch.McpServer.Services.Lucene;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for line-aware indexing operations that provides accurate line numbers
/// </summary>
public class LineAwareIndexingService
{
    private readonly ILogger<LineAwareIndexingService> _logger;
    private readonly LineIndexer _lineIndexer;
    private readonly LineIndexingOptions _options;

    public LineAwareIndexingService(
        ILogger<LineAwareIndexingService> logger,
        LineIndexer lineIndexer,
        LineIndexingOptions? options = null)
    {
        _logger = logger;
        _lineIndexer = lineIndexer;
        _options = options ?? new LineIndexingOptions();
    }

    /// <summary>
    /// Process file content and return line-aware data ready for Lucene indexing
    /// </summary>
    public LineData ProcessFileContent(string content, string filePath)
    {
        _logger.LogDebug("Processing file for line-aware indexing: {FilePath}", filePath);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lineData = _lineIndexer.ProcessContent(content, filePath);
        stopwatch.Stop();
        
        _logger.LogDebug("Processed {LineCount} lines with {TermCount} unique terms in {ElapsedMs}ms for {FilePath}",
            lineData.LineCount, lineData.TermLineMap.Count, stopwatch.ElapsedMilliseconds, filePath);
        
        return lineData;
    }

    /// <summary>
    /// Find line number for a search term using cached first matches or term-line mapping
    /// </summary>
    public LineAwareResult FindLineNumber(LineData lineData, string searchTerm)
    {
        if (lineData == null || string.IsNullOrEmpty(searchTerm))
        {
            return new LineAwareResult { IsAccurate = false };
        }

        var normalizedTerm = searchTerm.ToLowerInvariant();

        // Try cached first match first (fastest)
        if (lineData.FirstMatches.TryGetValue(normalizedTerm, out var cachedContext))
        {
            return new LineAwareResult
            {
                LineNumber = cachedContext.LineNumber,
                Context = cachedContext,
                IsFromCache = true,
                IsAccurate = true
            };
        }

        // Try term-line mapping (still fast)
        if (lineData.TermLineMap.TryGetValue(normalizedTerm, out var lineNumbers) && lineNumbers.Count > 0)
        {
            var firstLineNumber = lineNumbers[0];
            var context = CreateLineContextFromLineData(lineData, firstLineNumber);
            
            return new LineAwareResult
            {
                LineNumber = firstLineNumber,
                Context = context,
                IsFromCache = false,
                IsAccurate = true
            };
        }

        // No exact match found
        return new LineAwareResult { IsAccurate = false };
    }

    /// <summary>
    /// Find line numbers for multiple search terms (for complex queries)
    /// </summary>
    public Dictionary<string, LineAwareResult> FindLineNumbers(LineData lineData, IEnumerable<string> searchTerms)
    {
        var results = new Dictionary<string, LineAwareResult>();
        
        foreach (var term in searchTerms)
        {
            results[term] = FindLineNumber(lineData, term);
        }
        
        return results;
    }

    /// <summary>
    /// Get all line numbers where a term appears
    /// </summary>
    public List<int> GetAllLineNumbers(LineData lineData, string searchTerm)
    {
        if (lineData?.TermLineMap.TryGetValue(searchTerm.ToLowerInvariant(), out var lineNumbers) == true)
        {
            return new List<int>(lineNumbers);
        }
        
        return new List<int>();
    }

    /// <summary>
    /// Create line context from line data for a specific line number
    /// </summary>
    private LineContext? CreateLineContextFromLineData(LineData lineData, int lineNumber)
    {
        if (lineData.Lines == null || lineNumber < 1 || lineNumber > lineData.Lines.Length)
        {
            return null;
        }

        var startLine = Math.Max(1, lineNumber - _options.ContextLines);
        var endLine = Math.Min(lineData.Lines.Length, lineNumber + _options.ContextLines);
        
        var contextLines = new List<string>();
        for (int i = startLine - 1; i < endLine; i++) // Convert to 0-based indexing
        {
            contextLines.Add(lineData.Lines[i]);
        }

        return new LineContext
        {
            LineNumber = lineNumber,
            LineText = lineData.Lines[lineNumber - 1], // Convert to 0-based
            ContextLines = contextLines,
            StartLine = startLine,
            EndLine = endLine
        };
    }

    /// <summary>
    /// Validate line data integrity
    /// </summary>
    public bool ValidateLineData(LineData lineData)
    {
        if (lineData == null) return false;
        
        try
        {
            // Check basic consistency
            if (lineData.Lines == null || lineData.TermLineMap == null || lineData.FirstMatches == null)
            {
                _logger.LogWarning("LineData has null collections");
                return false;
            }

            // Validate line numbers in term mappings
            foreach (var kvp in lineData.TermLineMap)
            {
                foreach (var lineNum in kvp.Value)
                {
                    if (lineNum < 1 || lineNum > lineData.Lines.Length)
                    {
                        _logger.LogWarning("Invalid line number {LineNum} for term '{Term}' (max: {MaxLine})", 
                            lineNum, kvp.Key, lineData.Lines.Length);
                        return false;
                    }
                }
            }

            // Validate first match contexts
            foreach (var kvp in lineData.FirstMatches)
            {
                var context = kvp.Value;
                if (context.LineNumber < 1 || context.LineNumber > lineData.Lines.Length)
                {
                    _logger.LogWarning("Invalid line number in first match context for term '{Term}'", kvp.Key);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating line data");
            return false;
        }
    }

    /// <summary>
    /// Get statistics about processed line data
    /// </summary>
    public LineDataStatistics GetStatistics(LineData lineData)
    {
        if (lineData == null)
        {
            return new LineDataStatistics();
        }

        var termOccurrences = lineData.TermLineMap.Values.Sum(list => list.Count);
        var avgTermsPerLine = lineData.LineCount > 0 ? (double)termOccurrences / lineData.LineCount : 0;
        var avgOccurrencesPerTerm = lineData.TermLineMap.Count > 0 ? (double)termOccurrences / lineData.TermLineMap.Count : 0;

        return new LineDataStatistics
        {
            LineCount = lineData.LineCount,
            UniqueTermCount = lineData.TermLineMap.Count,
            TotalTermOccurrences = termOccurrences,
            CachedFirstMatches = lineData.FirstMatches.Count,
            AverageTermsPerLine = avgTermsPerLine,
            AverageOccurrencesPerTerm = avgOccurrencesPerTerm
        };
    }
}

/// <summary>
/// Statistics about processed line data
/// </summary>
public class LineDataStatistics
{
    public int LineCount { get; set; }
    public int UniqueTermCount { get; set; }
    public int TotalTermOccurrences { get; set; }
    public int CachedFirstMatches { get; set; }
    public double AverageTermsPerLine { get; set; }
    public double AverageOccurrencesPerTerm { get; set; }
}