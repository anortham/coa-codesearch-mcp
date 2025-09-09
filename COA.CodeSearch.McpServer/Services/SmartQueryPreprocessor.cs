using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Utils;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Intelligently processes search queries to determine the best field and approach
/// for multi-field indexing, solving Lucene query parser limitations.
/// 
/// This service routes queries to optimal Lucene fields based on query characteristics:
/// - Symbol queries → content_symbols field (exact symbol matching)
/// - Pattern queries → content_patterns field (code structure matching)  
/// - Standard queries → content field (full-text search)
/// - Auto mode → intelligent detection based on query patterns
/// 
/// Includes wildcard validation and sanitization to prevent invalid Lucene queries.
/// </summary>
/// <remarks>
/// The preprocessor solves a key limitation of Lucene's QueryParser which cannot handle
/// queries optimally across multiple analyzed fields. By preprocessing queries and routing
/// them to specialized fields, we achieve better search accuracy and performance.
/// 
/// Query Processing Flow:
/// 1. Validate and sanitize wildcards
/// 2. Detect optimal search mode (if Auto)
/// 3. Route to appropriate field based on mode
/// 4. Return processed query with routing information
/// </remarks>
public class SmartQueryPreprocessor
{
    private readonly ILogger<SmartQueryPreprocessor> _logger;

    /// <summary>
    /// Regex pattern for detecting special characters that indicate complex queries.
    /// Used to differentiate between simple symbol queries and complex search patterns.
    /// </summary>
    private static readonly Regex SpecialCharsPattern = new(@"[{}()\[\]<>""':;,\.!@#$%^&*+=|\\~`]", RegexOptions.Compiled);
    
    /// <summary>
    /// Regex pattern for detecting code-specific syntax and patterns.
    /// Matches language keywords, dot notation, scope resolution operators.
    /// </summary>
    private static readonly Regex CodePatternPattern = new(@"\b(class|interface|struct|enum|function|def|func|fn|method|var|let|const)\b|\w+\.\w+|\w+::\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Regex pattern for simple symbol names (identifiers).
    /// Matches valid programming language identifiers without special characters.
    /// </summary>
    private static readonly Regex SymbolPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    
    /// <summary>
    /// Regex pattern for detecting CamelCase naming conventions.
    /// Used to identify symbol queries that benefit from symbol-specific indexing.
    /// </summary>
    private static readonly Regex CamelCasePattern = new(@"[A-Z][a-z]+|[a-z]+[A-Z]", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the SmartQueryPreprocessor.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics and debugging.</param>
    public SmartQueryPreprocessor(ILogger<SmartQueryPreprocessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes a user search query to determine the optimal Lucene field and search approach.
    /// </summary>
    /// <param name="userQuery">The raw search query from the user. Can contain symbols, patterns, or natural language.</param>
    /// <param name="mode">The search mode to use. Auto mode enables intelligent detection based on query characteristics.</param>
    /// <returns>
    /// A <see cref="QueryProcessingResult"/> containing:
    /// - ProcessedQuery: Sanitized and prepared query for Lucene
    /// - TargetField: Optimal field name (content, content_symbols, content_patterns)
    /// - DetectedMode: The search mode used for processing
    /// - Reason: Human-readable explanation of the routing decision
    /// </returns>
    /// <remarks>
    /// Processing Logic:
    /// - Auto mode: Analyzes query to detect if it's a symbol, pattern, or standard search
    /// - Symbol mode: Routes to content_symbols for exact identifier matching
    /// - Pattern mode: Routes to content_patterns for code structure searches  
    /// - Standard mode: Routes to content for general full-text search
    /// 
    /// Wildcard queries are validated and sanitized to prevent Lucene parser errors.
    /// Invalid wildcards (like leading * or pure wildcards) are handled gracefully.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Symbol query - routes to content_symbols
    /// var result = processor.Process("UserService");
    /// // result.TargetField == "content_symbols"
    /// // result.DetectedMode == SearchMode.Symbol
    /// 
    /// // Pattern query - routes to content_patterns  
    /// var result = processor.Process("class MyClass");
    /// // result.TargetField == "content_patterns"
    /// // result.DetectedMode == SearchMode.Pattern
    /// 
    /// // Standard query - routes to content
    /// var result = processor.Process("error handling logic");
    /// // result.TargetField == "content" 
    /// // result.DetectedMode == SearchMode.Standard
    /// </code>
    /// </example>
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

        // Check for invalid wildcard patterns and attempt to sanitize
        if (WildcardValidator.IsInvalidWildcardQuery(userQuery))
        {
            var sanitizedQuery = WildcardValidator.SanitizeWildcardQuery(userQuery);
            if (sanitizedQuery == null)
            {
                // Cannot sanitize, return error result
                return new QueryProcessingResult
                {
                    ProcessedQuery = userQuery,
                    TargetField = "content",
                    DetectedMode = SearchMode.Standard,
                    Reason = "Invalid wildcard pattern (pure wildcards cannot be processed)"
                };
            }
            
            // Use sanitized query and log the change
            _logger.LogDebug("Sanitized invalid wildcard query: '{Original}' -> '{Sanitized}'", userQuery, sanitizedQuery);
            userQuery = sanitizedQuery;
        }

        var detectedMode = mode == SearchMode.Auto ? DetectOptimalMode(userQuery) : mode;
        var result = ProcessForMode(userQuery, detectedMode);

        _logger.LogDebug("Query preprocessing: '{Query}' -> Mode: {Mode}, Field: {Field}, Reason: {Reason}",
            userQuery, result.DetectedMode, result.TargetField, result.Reason);

        return result;
    }

    /// <summary>
    /// Detects the optimal search mode based on query characteristics and content patterns.
    /// </summary>
    /// <param name="query">The user query to analyze.</param>
    /// <returns>
    /// The detected search mode:
    /// - <see cref="SearchMode.Pattern"/> for queries with special characters or syntax
    /// - <see cref="SearchMode.Symbol"/> for code symbols and identifiers
    /// - <see cref="SearchMode.Standard"/> for natural language queries
    /// </returns>
    /// <remarks>
    /// Detection Logic:
    /// 1. Special characters (brackets, operators) → Pattern mode
    /// 2. Code patterns (class, method.call) or simple symbols → Symbol mode  
    /// 3. Everything else → Standard mode
    /// 
    /// This ensures optimal field routing for maximum search accuracy.
    /// </remarks>
    private SearchMode DetectOptimalMode(string query)
    {
        // Check for special characters that should use pattern-preserving search
        if (ContainsSpecialChars(query))
        {
            return SearchMode.Pattern;
        }

        // Check for code patterns (method calls, namespaces, etc.) for enhanced symbol detection
        if (CodePatternPattern.IsMatch(query) || IsSimpleSymbolQuery(query))
        {
            return SearchMode.Symbol;
        }

        // Default to standard search
        return SearchMode.Standard;
    }

    /// <summary>
    /// Processes a query for a specific search mode, routing to the appropriate Lucene field.
    /// </summary>
    /// <param name="query">The sanitized query to process.</param>
    /// <param name="mode">The search mode determining field routing and processing strategy.</param>
    /// <returns>
    /// A <see cref="QueryProcessingResult"/> with mode-specific processing:
    /// - Pattern mode → content_patterns field with minimal processing
    /// - Symbol mode → content_symbols field with symbol-optimized processing  
    /// - Standard mode → content field with standard text processing
    /// - Auto/Fuzzy modes → content field with reason explanation
    /// </returns>
    /// <remarks>
    /// Field Routing Strategy:
    /// - content_patterns: Preserves special characters for syntax matching
    /// - content_symbols: Optimized for identifier and symbol searches
    /// - content: General full-text search with standard analysis
    /// </remarks>
    private QueryProcessingResult ProcessForMode(string query, SearchMode mode)
    {
        return mode switch
        {
            SearchMode.Pattern => new QueryProcessingResult
            {
                ProcessedQuery = query, // Minimal processing for pattern-preserving search
                TargetField = "content_patterns",
                DetectedMode = SearchMode.Pattern,
                Reason = "Special characters detected - using pattern-preserving search"
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
    /// Determines if the query contains special characters that require pattern-preserving field routing.
    /// </summary>
    /// <param name="query">The query to analyze.</param>
    /// <returns>True if the query contains brackets, operators, or syntax characters that should use content_patterns field.</returns>
    /// <remarks>
    /// Detects characters like: { } ( ) [ ] &lt; &gt; " ' : ; , . ! @ # $ % ^ &amp; * + = | \ ~ `
    /// These indicate complex queries that benefit from minimal analysis in the patterns field.
    /// </remarks>
    private bool ContainsSpecialChars(string query)
    {
        return SpecialCharsPattern.IsMatch(query);
    }

    /// <summary>
    /// Determines if the query represents a simple programming symbol or identifier.
    /// </summary>
    /// <param name="query">The query to analyze.</param>
    /// <returns>True if the query matches symbol patterns that should use content_symbols field.</returns>
    /// <remarks>
    /// Symbol Detection Logic:
    /// - Valid identifier pattern (letters, numbers, underscore)
    /// - CamelCase naming convention without spaces
    /// - Single-word queries that look like class/method names
    /// 
    /// Examples: "UserService", "getInstance", "MyClass", "API_KEY"
    /// </remarks>
    private bool IsSimpleSymbolQuery(string query)
    {
        // Simple identifier or CamelCase pattern
        return SymbolPattern.IsMatch(query) || 
               (CamelCasePattern.IsMatch(query) && !query.Contains(' '));
    }

    /// <summary>
    /// Processes a query for standard full-text search in the content field.
    /// </summary>
    /// <param name="query">The query to process.</param>
    /// <returns>The processed query optimized for standard text analysis.</returns>
    /// <remarks>
    /// Currently applies basic trimming as the CodeAnalyzer handles most text processing.
    /// Future enhancements may include query expansion or term boosting.
    /// </remarks>
    private string ProcessStandardQuery(string query)
    {
        // Apply standard Lucene escaping if needed
        // For now, return as-is since CodeAnalyzer handles most cases
        return query.Trim();
    }


    /// <summary>
    /// Processes a query for symbol-specific search in the content_symbols field.
    /// </summary>
    /// <param name="query">The query to process for symbol matching.</param>
    /// <returns>The processed query optimized for symbol identification and matching.</returns>
    /// <remarks>
    /// Symbol Processing Steps:
    /// 1. Trims whitespace
    /// 2. Removes language keywords (class, interface, method, etc.)
    /// 3. Preserves actual symbol names for exact matching
    /// 
    /// This optimization improves symbol search accuracy by focusing on identifiers
    /// rather than language constructs, leveraging the symbol-optimized indexing.
    /// </remarks>
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
/// Represents the result of intelligent query preprocessing, containing the optimized query
/// and routing information for multi-field Lucene search operations.
/// </summary>
/// <remarks>
/// This class encapsulates the output of the SmartQueryPreprocessor, providing:
/// - The sanitized and processed query string
/// - The optimal target field for Lucene searching 
/// - The detected/applied search mode
/// - A human-readable explanation of routing decisions
/// 
/// Used by search tools to route queries to the most appropriate Lucene field
/// for maximum search accuracy and performance.
/// </remarks>
public class QueryProcessingResult
{
    /// <summary>
    /// Gets or sets the processed query string that has been sanitized and optimized for Lucene parsing.
    /// </summary>
    /// <value>
    /// The sanitized query with wildcard validation applied and mode-specific processing completed.
    /// Ready for use with Lucene's QueryParser.
    /// </value>
    public required string ProcessedQuery { get; set; }

    /// <summary>
    /// Gets or sets the optimal Lucene field name for searching based on query characteristics.
    /// </summary>
    /// <value>
    /// The target field name:
    /// - "content" for standard full-text search
    /// - "content_symbols" for symbol and identifier matching
    /// - "content_patterns" for syntax and pattern preservation
    /// </value>
    public required string TargetField { get; set; }

    /// <summary>
    /// Gets or sets the search mode that was detected or explicitly applied during processing.
    /// </summary>
    /// <value>
    /// The <see cref="SearchMode"/> used for query processing and field routing.
    /// </value>
    public SearchMode DetectedMode { get; set; }

    /// <summary>
    /// Gets or sets a human-readable explanation of the query processing and routing decision.
    /// </summary>
    /// <value>
    /// A descriptive string explaining why the specific mode and field were chosen,
    /// useful for debugging and understanding search behavior.
    /// </value>
    /// <example>
    /// Examples: "Symbol search - routed to content_symbols", "Special characters detected - using content_patterns"
    /// </example>
    public required string Reason { get; set; }
}