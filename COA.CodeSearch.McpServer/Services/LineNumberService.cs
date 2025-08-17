using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Lucene.Net.Analysis.TokenAttributes;
using COA.CodeSearch.McpServer.Services.Analysis;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for calculating line numbers from Lucene term vector positions
/// </summary>
public class LineNumberService
{
    private readonly ILogger<LineNumberService> _logger;

    public LineNumberService(ILogger<LineNumberService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate line number from term vector offsets
    /// </summary>
    public LineNumberResult CalculateLineNumber(IndexSearcher searcher, int docId, string queryText)
    {
        try
        {
            // Get document
            var doc = searcher.Doc(docId);
            
            // Get line break positions
            var lineBreaksJson = doc.Get("line_breaks");
            if (string.IsNullOrEmpty(lineBreaksJson))
            {
                return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
            }

            var lineBreaks = JsonSerializer.Deserialize<int[]>(lineBreaksJson);
            if (lineBreaks == null || lineBreaks.Length == 0)
            {
                _logger.LogDebug("No line breaks found for docId {DocId}", docId);
                return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
            }
            
            _logger.LogDebug("Found {LineBreakCount} line breaks for docId {DocId}: [{LineBreaks}]", 
                lineBreaks.Length, docId, string.Join(", ", lineBreaks.Take(5)) + (lineBreaks.Length > 5 ? "..." : ""));

            // Get term vectors from IndexReader
            Fields termVectors = null;
            var indexReader = searcher.IndexReader;
            
            // Handle both composite and atomic readers
            if (indexReader is CompositeReader)
            {
                var leaves = indexReader.Leaves;
                foreach (var leaf in leaves)
                {
                    if (docId >= leaf.DocBase && docId < leaf.DocBase + leaf.Reader.MaxDoc)
                    {
                        var localDocId = docId - leaf.DocBase;
                        termVectors = leaf.Reader.GetTermVectors(localDocId);
                        break;
                    }
                }
            }
            else if (indexReader is AtomicReader atomicReader)
            {
                termVectors = atomicReader.GetTermVectors(docId);
            }

            if (termVectors == null)
            {
                return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
            }

            // Get terms for content_tv field
            var terms = termVectors.GetTerms("content_tv");
            if (terms == null)
            {
                return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
            }

            // Find first matching term offset
            var queryTerms = ExtractQueryTerms(queryText);
            _logger.LogDebug("Extracted query terms: [{Terms}] from query: {Query}", string.Join(", ", queryTerms), queryText);
            
            var firstMatchOffset = GetFirstMatchingOffset(terms, queryTerms);
            _logger.LogDebug("First match offset: {Offset} for docId {DocId}", firstMatchOffset, docId);

            if (firstMatchOffset.HasValue)
            {
                var lineNumber = GetLineNumberFromOffset(firstMatchOffset.Value, lineBreaks);
                
                // Debug: Log first few line breaks to understand positions
                var firstBreaks = string.Join(", ", lineBreaks.Take(10).Select((pos, idx) => $"Line {idx+1} ends at {pos}"));
                _logger.LogDebug("First line breaks: {LineBreaks}", firstBreaks);
                _logger.LogDebug("Calculated line number {LineNumber} from offset {Offset} using {LineBreakCount} line breaks", 
                    lineNumber, firstMatchOffset.Value, lineBreaks.Length);
                
                return new LineNumberResult
                {
                    LineNumber = lineNumber,
                    CharacterOffset = firstMatchOffset.Value,
                    HasPreciseLocation = true
                };
            }

            return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate line number for doc {DocId}", docId);
            return new LineNumberResult { LineNumber = 1, HasPreciseLocation = false };
        }
    }

    /// <summary>
    /// Get line number from character offset using binary search
    /// </summary>
    private int GetLineNumberFromOffset(int characterOffset, int[] lineBreakPositions)
    {
        if (lineBreakPositions.Length == 0) return 1;

        // Binary search for the line break position
        var lineIndex = Array.BinarySearch(lineBreakPositions, characterOffset);
        
        if (lineIndex >= 0)
        {
            // Exact match on a line break - we're at the end of that line
            return lineIndex + 2; // +2 because we're at the END of line lineIndex
        }
        else
        {
            // Not an exact match - get the insertion point
            var insertionPoint = ~lineIndex;
            // insertionPoint tells us how many line breaks come before our position
            // So if there are N line breaks before us, we're on line N+1
            // 
            // For now, just use the standard calculation without adjustment
            // The offset discrepancy needs more investigation
            var lineNumber = insertionPoint + 1; // Line numbers are 1-based
            
            // Ensure we never return negative or zero line numbers
            return Math.Max(1, lineNumber);
        }
    }

    /// <summary>
    /// Extract search terms from query text using CodeAnalyzer to match indexing
    /// </summary>
    private List<string> ExtractQueryTerms(string queryText)
    {
        var terms = new List<string>();
        
        if (string.IsNullOrEmpty(queryText)) return terms;

        try
        {
            // Use CodeAnalyzer to tokenize the same way as indexing
            using var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            
            // Clean query text of Lucene syntax
            var cleaned = queryText
                .Replace("content:", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("\"", "")
                .Replace("+", "")
                .Replace("-", "")
                .Replace("*", "")
                .Replace("?", "")
                .Replace("~", "")
                .Replace("^", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "");

            // Tokenize using CodeAnalyzer
            using var tokenStream = analyzer.GetTokenStream("content", cleaned);
            var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
            
            tokenStream.Reset();
            while (tokenStream.IncrementToken())
            {
                var term = termAttr.ToString();
                if (term.Length > 1) // Only meaningful terms
                {
                    terms.Add(term.ToLowerInvariant());
                }
            }
            tokenStream.End();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to tokenize query text, falling back to simple splitting");
            
            // Fallback to simple tokenization
            var words = queryText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                var trimmed = word.Trim().ToLowerInvariant();
                if (trimmed.Length > 1)
                {
                    terms.Add(trimmed);
                }
            }
        }

        return terms;
    }

    /// <summary>
    /// Find the first matching term offset from terms using exact matching
    /// </summary>
    private int? GetFirstMatchingOffset(Terms terms, List<string> queryTerms)
    {
        try
        {
            var termsEnum = terms.GetEnumerator();
            var allMatchOffsets = new List<int>();
            var termsChecked = 0;
            var maxTermsToLog = 20;

            // First pass: collect all exact matches
            while (termsEnum.MoveNext())
            {
                var termBytes = termsEnum.Term;
                var termText = termBytes.Utf8ToString().ToLowerInvariant();
                
                if (termsChecked < maxTermsToLog)
                {
                    _logger.LogTrace("Checking term '{Term}' against query terms", termText);
                }
                termsChecked++;

                // Use exact matching first, then fallback to contains
                bool isMatch = false;
                foreach (var queryTerm in queryTerms)
                {
                    if (termText.Equals(queryTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                        break;
                    }
                }

                if (isMatch)
                {
                    // Get postings with offsets
                    var docsAndPositions = termsEnum.DocsAndPositions(null, null, DocsAndPositionsFlags.OFFSETS);
                    if (docsAndPositions != null && docsAndPositions.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                    {
                        var freq = docsAndPositions.Freq;
                        for (int i = 0; i < freq; i++)
                        {
                            var position = docsAndPositions.NextPosition();
                            var startOffset = docsAndPositions.StartOffset;
                            
                            if (startOffset >= 0)
                            {
                                allMatchOffsets.Add(startOffset);
                                _logger.LogDebug("Found exact match '{Term}' at offset {Offset}", termText, startOffset);
                            }
                        }
                    }
                }
            }

            // If no exact matches, try substring matching
            if (allMatchOffsets.Count == 0)
            {
                termsEnum = terms.GetEnumerator();
                while (termsEnum.MoveNext())
                {
                    var termBytes = termsEnum.Term;
                    var termText = termBytes.Utf8ToString().ToLowerInvariant();

                    foreach (var queryTerm in queryTerms)
                    {
                        if (termText.Contains(queryTerm) && queryTerm.Length > 2)
                        {
                            var docsAndPositions = termsEnum.DocsAndPositions(null, null, DocsAndPositionsFlags.OFFSETS);
                            if (docsAndPositions != null && docsAndPositions.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                            {
                                var freq = docsAndPositions.Freq;
                                for (int i = 0; i < freq; i++)
                                {
                                    var position = docsAndPositions.NextPosition();
                                    var startOffset = docsAndPositions.StartOffset;
                                    
                                    if (startOffset >= 0)
                                    {
                                        allMatchOffsets.Add(startOffset);
                                        _logger.LogDebug("Found substring match '{Term}' contains '{QueryTerm}' at offset {Offset}", termText, queryTerm, startOffset);
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }

            // Return the earliest offset
            _logger.LogDebug("Checked {TermCount} terms, found {MatchCount} matches", termsChecked, allMatchOffsets.Count);
            if (allMatchOffsets.Count > 0)
            {
                var minOffset = allMatchOffsets.Min();
                _logger.LogDebug("Returning earliest offset: {Offset} from matches: [{Offsets}]", 
                    minOffset, string.Join(", ", allMatchOffsets.Take(5)));
                return minOffset;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting term offsets from terms");
            return null;
        }
    }

    /// <summary>
    /// Serialize line break positions to JSON
    /// </summary>
    public static string SerializeLineBreaks(string content)
    {
        var positions = new List<int>();
        
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                positions.Add(i);
            }
        }

        return JsonSerializer.Serialize(positions.ToArray());
    }
}

/// <summary>
/// Result of line number calculation
/// </summary>
public class LineNumberResult
{
    public int LineNumber { get; set; } = 1;
    public int? CharacterOffset { get; set; }
    public bool HasPreciseLocation { get; set; }
}