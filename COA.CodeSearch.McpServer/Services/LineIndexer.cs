using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Services.Lucene;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for processing file content into line-aware index data
/// </summary>
public class LineIndexer
{
    private readonly ILogger<LineIndexer> _logger;
    private readonly LineIndexingOptions _options;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public LineIndexer(ILogger<LineIndexer> logger, LineIndexingOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new LineIndexingOptions();
    }

    /// <summary>
    /// Process file content and extract line-aware data for indexing
    /// </summary>
    public LineData ProcessContent(string content, string? filePath = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new LineData();
        }

        try
        {
            // Split content into lines using cross-platform approach
            var lines = SplitIntoLines(content);
            var termLineMap = new Dictionary<string, List<int>>();
            var firstMatches = new Dictionary<string, LineContext>();

            _logger.LogDebug("Processing {LineCount} lines for line-aware indexing", lines.Length);

            // Process each line to extract terms and build mappings
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineNumber = lineIndex + 1; // 1-based line numbers
                var lineText = lines[lineIndex];
                
                // Skip empty lines for term mapping
                if (string.IsNullOrWhiteSpace(lineText))
                    continue;

                // Extract terms from this line using CodeAnalyzer
                var terms = ExtractTermsFromLine(lineText);
                
                foreach (var term in terms)
                {
                    // Track all line occurrences for this term
                    if (!termLineMap.ContainsKey(term))
                    {
                        termLineMap[term] = new List<int>();
                    }
                    termLineMap[term].Add(lineNumber);

                    // Cache first occurrence with context if not already cached
                    if (!firstMatches.ContainsKey(term) && firstMatches.Count < _options.MaxFirstMatchCache)
                    {
                        var context = CreateLineContext(lines, lineNumber, lineText);
                        firstMatches[term] = context;
                        
                        _logger.LogTrace("Cached first match for term '{Term}' at line {LineNumber}", term, lineNumber);
                    }
                }
            }

            _logger.LogDebug("Extracted {TermCount} unique terms from {LineCount} lines", termLineMap.Count, lines.Length);

            return new LineData
            {
                Lines = lines,
                TermLineMap = termLineMap,
                FirstMatches = firstMatches
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing content for line-aware indexing (file: {FilePath})", filePath ?? "unknown");
            return new LineData(); // Return empty data rather than failing
        }
    }

    /// <summary>
    /// Split content into lines using cross-platform line ending handling
    /// </summary>
    private static string[] SplitIntoLines(string content)
    {
        // Handle various line endings: \r\n, \n, \r
        return content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    /// <summary>
    /// Extract searchable terms from a line using CodeAnalyzer
    /// </summary>
    private HashSet<string> ExtractTermsFromLine(string lineText)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var analyzer = new CodeAnalyzer(LUCENE_VERSION);
            using var tokenStream = analyzer.GetTokenStream("content", lineText);
            var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();

            tokenStream.Reset();
            while (tokenStream.IncrementToken())
            {
                var term = termAttr.ToString();
                
                // Filter terms by minimum length and exclude very common terms
                if (term.Length >= _options.MinTermLength && !IsStopTerm(term))
                {
                    terms.Add(term.ToLowerInvariant());
                }
            }
            tokenStream.End();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to tokenize line, using fallback splitting");
            
            // Fallback to simple word splitting
            var words = lineText.Split(new[] { ' ', '\t', '(', ')', '[', ']', '{', '}', '.', ',', ';', ':' }, 
                StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                var cleaned = word.Trim().ToLowerInvariant();
                if (cleaned.Length >= _options.MinTermLength && !IsStopTerm(cleaned))
                {
                    terms.Add(cleaned);
                }
            }
        }

        return terms;
    }

    /// <summary>
    /// Create line context with surrounding lines
    /// </summary>
    private LineContext CreateLineContext(string[] allLines, int lineNumber, string lineText)
    {
        var startLine = Math.Max(1, lineNumber - _options.ContextLines);
        var endLine = Math.Min(allLines.Length, lineNumber + _options.ContextLines);
        
        var contextLines = new List<string>();
        for (int i = startLine - 1; i < endLine; i++) // Convert to 0-based indexing
        {
            contextLines.Add(allLines[i]);
        }

        return new LineContext
        {
            LineNumber = lineNumber,
            LineText = lineText,
            ContextLines = contextLines,
            StartLine = startLine,
            EndLine = endLine
        };
    }

    /// <summary>
    /// Check if a term should be excluded from indexing (very common terms)
    /// </summary>
    private static bool IsStopTerm(string term)
    {
        // Exclude very common programming terms that don't provide much search value
        var stopTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "or", "to", "in", "of", "a", "an", "is", "was", "are", "were",
            "be", "been", "have", "has", "had", "do", "does", "did", "will", "would",
            "could", "should", "may", "might", "can", "if", "else", "then", "this", "that"
        };
        
        return stopTerms.Contains(term);
    }

    /// <summary>
    /// Serialize LineData to JSON for storage in Lucene fields
    /// </summary>
    public static string SerializeLineData(LineData lineData)
    {
        try
        {
            return JsonSerializer.Serialize(lineData, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
        }
        catch (Exception)
        {
            return "{}"; // Return empty object if serialization fails
        }
    }

    /// <summary>
    /// Deserialize LineData from JSON stored in Lucene fields
    /// </summary>
    public static LineData? DeserializeLineData(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LineData>(json, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
        }
        catch (Exception)
        {
            return null;
        }
    }
}