using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Intelligently processes search queries to determine the best field and approach
/// for multi-field indexing, solving Lucene query parser limitations
/// </summary>
public class SmartQueryPreprocessor
{
    private readonly ILogger<SmartQueryPreprocessor> _logger;

    // Patterns for detecting query characteristics
    private static readonly Regex SpecialCharsPattern = new(@"[{}()\[\]<>""':;,\.!@#$%^&*+=|\\~`]", RegexOptions.Compiled);
    private static readonly Regex CodePatternPattern = new(@"\b(class|interface|struct|enum|function|def|func|fn|method|var|let|const)\b|\w+\.\w+|\w+::\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SymbolPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex CamelCasePattern = new(@"[A-Z][a-z]+|[a-z]+[A-Z]", RegexOptions.Compiled);

    public SmartQueryPreprocessor(ILogger<SmartQueryPreprocessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Process a user query and determine the optimal field and search approach
    /// </summary>
    public QueryProcessingResult Process(string userQuery, SearchMode mode = SearchMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return new QueryProcessingResult
            {
                ProcessedQuery = userQuery ?? string.Empty,
                TargetField = "content",
                DetectedMode = SearchMode.Standard,
                Reason = "Empty query defaults to standard search"
            };
        }

        var detectedMode = mode == SearchMode.Auto ? DetectOptimalMode(userQuery) : mode;
        var result = ProcessForMode(userQuery, detectedMode);

        _logger.LogDebug("Query preprocessing: '{Query}' -> Mode: {Mode}, Field: {Field}, Reason: {Reason}",
            userQuery, result.DetectedMode, result.TargetField, result.Reason);

        return result;
    }

    /// <summary>
    /// Detect the optimal search mode based on query characteristics
    /// </summary>
    private SearchMode DetectOptimalMode(string query)
    {
        // Check for special characters that break Lucene parser
        if (ContainsSpecialChars(query))
        {
            return SearchMode.Literal;
        }

        // Check for code patterns (method calls, namespaces, etc.)
        if (CodePatternPattern.IsMatch(query))
        {
            return SearchMode.Code;
        }

        // Check if it looks like a simple symbol name
        if (IsSimpleSymbolQuery(query))
        {
            return SearchMode.Symbol;
        }

        // Default to standard search
        return SearchMode.Standard;
    }

    /// <summary>
    /// Process query for a specific mode
    /// </summary>
    private QueryProcessingResult ProcessForMode(string query, SearchMode mode)
    {
        return mode switch
        {
            SearchMode.Literal => new QueryProcessingResult
            {
                ProcessedQuery = query, // No processing for literal search
                TargetField = "content_literal",
                DetectedMode = SearchMode.Literal,
                Reason = "Special characters detected - using exact matching"
            },

            SearchMode.Code => new QueryProcessingResult
            {
                ProcessedQuery = ProcessCodeQuery(query),
                TargetField = "content_code",
                DetectedMode = SearchMode.Code,
                Reason = "Code patterns detected - using enhanced code tokenization"
            },

            SearchMode.Symbol => new QueryProcessingResult
            {
                ProcessedQuery = ProcessSymbolQuery(query),
                TargetField = "content_symbols",
                DetectedMode = SearchMode.Symbol,
                Reason = "Symbol pattern detected - searching symbol-only field"
            },

            SearchMode.Fuzzy => new QueryProcessingResult
            {
                ProcessedQuery = ProcessStandardQuery(query),
                TargetField = "content", // Fuzzy not implemented yet
                DetectedMode = SearchMode.Standard,
                Reason = "Fuzzy search not implemented - falling back to standard"
            },

            _ => new QueryProcessingResult // Standard and Auto fallback
            {
                ProcessedQuery = ProcessStandardQuery(query),
                TargetField = "content",
                DetectedMode = SearchMode.Standard,
                Reason = "Standard search with current CodeAnalyzer"
            }
        };
    }

    /// <summary>
    /// Check if query contains special characters that break Lucene parsing
    /// </summary>
    private bool ContainsSpecialChars(string query)
    {
        return SpecialCharsPattern.IsMatch(query);
    }

    /// <summary>
    /// Check if query looks like a simple symbol search
    /// </summary>
    private bool IsSimpleSymbolQuery(string query)
    {
        // Simple identifier or CamelCase pattern
        return SymbolPattern.IsMatch(query) || 
               (CamelCasePattern.IsMatch(query) && !query.Contains(' '));
    }

    /// <summary>
    /// Process query for standard search
    /// </summary>
    private string ProcessStandardQuery(string query)
    {
        // Apply standard Lucene escaping if needed
        // For now, return as-is since CodeAnalyzer handles most cases
        return query.Trim();
    }

    /// <summary>
    /// Process query for code-aware search
    /// </summary>
    private string ProcessCodeQuery(string query)
    {
        // Enhanced processing for code patterns
        // Could add more sophisticated code-aware processing here
        return query.Trim();
    }

    /// <summary>
    /// Process query for symbol-only search
    /// </summary>
    private string ProcessSymbolQuery(string query)
    {
        // Extract symbol names, remove noise words
        var processed = query.Trim();
        
        // Remove common noise words that might appear in symbol searches
        var noiseWords = new[] { "class", "interface", "method", "function", "def", "var", "let", "const" };
        foreach (var noise in noiseWords)
        {
            processed = Regex.Replace(processed, $@"\b{noise}\s+", "", RegexOptions.IgnoreCase);
        }

        return processed.Trim();
    }
}

/// <summary>
/// Result of query preprocessing
/// </summary>
public class QueryProcessingResult
{
    /// <summary>
    /// The processed query string ready for Lucene
    /// </summary>
    public required string ProcessedQuery { get; set; }

    /// <summary>
    /// The target field name to search in (content, content_literal, etc.)
    /// </summary>
    public required string TargetField { get; set; }

    /// <summary>
    /// The detected/applied search mode
    /// </summary>
    public SearchMode DetectedMode { get; set; }

    /// <summary>
    /// Human-readable reason for the mode selection
    /// </summary>
    public required string Reason { get; set; }
}