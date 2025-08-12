using System.Text.RegularExpressions;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Preprocesses search queries to handle code-specific syntax and special characters.
/// </summary>
public class QueryPreprocessor
{
    private static readonly LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    
    // Lucene special characters that need escaping
    private static readonly char[] LuceneSpecialChars = { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', '*', '?', ':', '\\', '/', '<', '>' };
    private static readonly char[] LuceneSpecialCharsExceptWildcard = { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', ':', '\\', '/', '<', '>' };
    private static readonly char[] LuceneSpecialCharsExceptFuzzy = { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '*', '?', ':', '\\', '/', '<', '>' };
    
    // Meaningful two-character operators that are allowed in code searches
    private static readonly HashSet<string> AllowedTwoCharOperators = new() 
    { 
        "=>", "??", "?.", "::", "->", "+=", "-=", "*=", "/=", 
        "==", "!=", ">=", "<=", "&&", "||", "<<", ">>" 
    };
    
    private readonly ILogger<QueryPreprocessor> _logger;

    public QueryPreprocessor(ILogger<QueryPreprocessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build a Lucene query with proper handling of code syntax and special characters
    /// </summary>
    public Query BuildQuery(string queryText, string searchType, bool caseSensitive, StandardAnalyzer analyzer)
    {
        switch (searchType.ToLowerInvariant())
        {
            case "wildcard":
                return BuildWildcardQuery(queryText);
            
            case "fuzzy":
                return BuildFuzzyQuery(queryText);
            
            case "phrase":
                return BuildPhraseQuery(queryText, analyzer);
            
            case "regex":
                return BuildRegexQuery(queryText, analyzer);
            
            case "literal":
            case "code":
                return BuildCodeQuery(queryText, analyzer);
            
            default:
                return BuildStandardQuery(queryText, analyzer);
        }
    }

    private Query BuildWildcardQuery(string queryText)
    {
        // For wildcard queries, escape special characters except * and ?
        var escapedQuery = EscapeQueryTextForWildcard(queryText);
        return new WildcardQuery(new Term("content", escapedQuery.ToLowerInvariant()));
    }

    private Query BuildFuzzyQuery(string queryText)
    {
        // For fuzzy queries, escape all special characters except ~
        var escapedQuery = EscapeQueryTextForFuzzy(queryText);
        return new FuzzyQuery(new Term("content", escapedQuery.ToLowerInvariant()));
    }

    private Query BuildPhraseQuery(string queryText, StandardAnalyzer analyzer)
    {
        var parser = new QueryParser(LUCENE_VERSION, "content", analyzer);
        return parser.Parse($"\"{EscapeQueryText(queryText)}\"");
    }

    private Query BuildRegexQuery(string queryText, StandardAnalyzer analyzer)
    {
        try
        {
            // Validate regex first
            _ = new Regex(queryText);
            
            // Try to optimize simple regex patterns
            if (queryText.Contains(".*") || queryText.Contains(".+"))
            {
                // Patterns like "async.*Task" need special handling
                // Convert to a phrase query with slop for better performance
                var parts = Regex.Split(queryText, @"\.\*|\.\+");
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    _logger.LogInformation("Regex search: Converting pattern '{Query}' to phrase query with slop", queryText);
                    var phraseQuery = new PhraseQuery();
                    phraseQuery.Slop = 20; // Allow up to 20 words between terms
                    phraseQuery.Add(new Term("content", parts[0].ToLowerInvariant()));
                    phraseQuery.Add(new Term("content", parts[1].ToLowerInvariant()));
                    return phraseQuery;
                }
            }
            
            // For other regex patterns, use RegexpQuery
            return new RegexpQuery(new Term("content", queryText.ToLowerInvariant()));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern: {Query}", queryText);
            // Fall back to literal search
            return BuildCodeQuery(queryText, analyzer);
        }
    }

    private Query BuildCodeQuery(string queryText, StandardAnalyzer analyzer)
    {
        _logger.LogInformation("Code search: processing query '{Query}'", queryText);
        
        // Special handling for code patterns with brackets or special operators
        if (ContainsCodeSyntax(queryText))
        {
            _logger.LogInformation("Code search: detected code syntax in query '{Query}'", queryText);
            
            // For code with special characters like [Fact], MyClass : Interface, etc.
            // We need to handle these specially
            if (queryText.Contains(':') && !queryText.Contains("::"))
            {
                // Handle inheritance syntax like "MyClass : IInterface"
                var parts = queryText.Split(':');
                if (parts.Length == 2)
                {
                    var booleanQuery = new BooleanQuery();
                    booleanQuery.Add(new TermQuery(new Term("content", parts[0].Trim().ToLowerInvariant())), Occur.MUST);
                    booleanQuery.Add(new TermQuery(new Term("content", parts[1].Trim().ToLowerInvariant())), Occur.MUST);
                    return booleanQuery;
                }
            }
            
            // For brackets like [Fact], [HttpGet], use phrase query
            if (queryText.Contains('[') || queryText.Contains(']'))
            {
                var parser = new QueryParser(LUCENE_VERSION, "content", analyzer);
                parser.DefaultOperator = Operator.AND;
                try
                {
                    // Wrap in quotes to make it a phrase query
                    return parser.Parse($"\"{queryText}\"");
                }
                catch (ParseException)
                {
                    // If parsing fails, fall back to term query
                    return new TermQuery(new Term("content", queryText.ToLowerInvariant()));
                }
            }
            
            // For operators like =>, ??, etc.
            if (AllowedTwoCharOperators.Contains(queryText))
            {
                // These operators are typically tokenized away, so search for code around them
                return new TermQuery(new Term("content", queryText.ToLowerInvariant()));
            }
        }
        
        // Default code search
        var codeParser = new QueryParser(LUCENE_VERSION, "content", analyzer);
        codeParser.DefaultOperator = Operator.AND;
        return codeParser.Parse(EscapeQueryText(queryText));
    }

    private Query BuildStandardQuery(string queryText, StandardAnalyzer analyzer)
    {
        var parser = new QueryParser(LUCENE_VERSION, "content", analyzer);
        parser.DefaultOperator = Operator.AND;
        
        try
        {
            return parser.Parse(EscapeQueryText(queryText));
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Failed to parse standard query: {Query}", queryText);
            // Fall back to simple term query
            return new TermQuery(new Term("content", queryText.ToLowerInvariant()));
        }
    }

    private bool ContainsCodeSyntax(string query)
    {
        return query.Contains('[') || query.Contains(']') || 
               query.Contains('{') || query.Contains('}') ||
               query.Contains(':') || query.Contains("=>") ||
               query.Contains("??") || query.Contains("&&") ||
               query.Contains("||") || query.Contains("++") ||
               query.Contains("--") || query.Contains("!=") ||
               query.Contains("==") || query.Contains(">=") ||
               query.Contains("<=");
    }

    public static string EscapeQueryText(string query)
    {
        // Lucene special characters that need escaping
        // Note: We're excluding [ and ] as they cause issues even when escaped
        var escapedQuery = query;
        foreach (var c in LuceneSpecialChars)
        {
            // Skip escaping brackets as they need special handling
            if (c == '[' || c == ']')
                continue;
                
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    private static string EscapeQueryTextForWildcard(string query)
    {
        // For wildcard queries, escape all special chars EXCEPT * and ?
        var escapedQuery = query;
        foreach (var c in LuceneSpecialCharsExceptWildcard)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    private static string EscapeQueryTextForFuzzy(string query)
    {
        // For fuzzy queries, escape all special chars EXCEPT ~
        var escapedQuery = query;
        foreach (var c in LuceneSpecialCharsExceptFuzzy)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    /// <summary>
    /// Validates if a query meets minimum requirements
    /// </summary>
    public bool IsValidQuery(string query, string searchType, out string? errorMessage)
    {
        errorMessage = null;
        
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length < 3)
        {
            // Only allow specific meaningful two-character operators
            if (trimmedQuery.Length == 2 && AllowedTwoCharOperators.Contains(trimmedQuery))
            {
                return true; // This is an allowed two-character operator
            }
            
            errorMessage = "Query too short. Minimum 3 characters required (except for specific operators).";
            return false;
        }
        
        return true;
    }
}