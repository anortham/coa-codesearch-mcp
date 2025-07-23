using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for expanding search queries with synonyms, related terms, and contextual variations
/// </summary>
public class QueryExpansionService : IQueryExpansionService
{
    private readonly ILogger<QueryExpansionService> _logger;
    
    // Domain-specific term expansions
    private static readonly Dictionary<string, string[]> DomainExpansions = new()
    {
        // Authentication & Security
        ["auth"] = ["authentication", "login", "signin", "jwt", "oauth", "security", "authorize", "credential"],
        ["login"] = ["authentication", "signin", "auth", "credential", "session"],
        ["jwt"] = ["token", "auth", "authentication", "bearer", "security"],
        ["oauth"] = ["auth", "authentication", "security", "authorization", "token"],
        ["security"] = ["auth", "authentication", "authorization", "permission", "access"],
        
        // Database & Data
        ["db"] = ["database", "sql", "entity", "repository", "data", "table", "query"],
        ["database"] = ["db", "sql", "entity", "repository", "data", "storage"],
        ["sql"] = ["database", "query", "table", "entity", "data"],
        ["entity"] = ["model", "data", "database", "table", "object"],
        ["repository"] = ["data", "database", "storage", "persistence"],
        
        // API & Web
        ["api"] = ["endpoint", "controller", "service", "http", "rest", "web"],
        ["endpoint"] = ["api", "controller", "route", "http", "service"],
        ["controller"] = ["api", "endpoint", "action", "mvc", "web"],
        ["service"] = ["api", "business", "logic", "provider", "manager"],
        ["http"] = ["web", "api", "request", "response", "client"],
        ["rest"] = ["api", "http", "web", "service", "endpoint"],
        
        // Configuration & Settings
        ["config"] = ["configuration", "settings", "options", "parameters", "environment"],
        ["configuration"] = ["config", "settings", "options", "appsettings"],
        ["settings"] = ["config", "configuration", "options", "preferences"],
        ["environment"] = ["env", "config", "settings", "deployment"],
        
        // Testing
        ["test"] = ["testing", "unit", "integration", "spec", "mock"],
        ["mock"] = ["test", "fake", "stub", "testing"],
        ["unit"] = ["test", "testing", "spec"],
        
        // Error Handling
        ["error"] = ["exception", "bug", "issue", "problem", "failure"],
        ["exception"] = ["error", "bug", "failure", "catch"],
        ["bug"] = ["error", "issue", "defect", "problem"],
        
        // Performance
        ["performance"] = ["speed", "optimization", "cache", "memory", "cpu"],
        ["cache"] = ["memory", "performance", "storage", "temporary"],
        ["memory"] = ["performance", "cache", "allocation", "leak"],
        
        // Logging & Monitoring
        ["log"] = ["logging", "trace", "debug", "monitor"],
        ["logging"] = ["log", "trace", "debug", "audit"],
        ["monitor"] = ["logging", "metric", "health", "status"]
    };
    
    // Common code term patterns
    private static readonly Regex CamelCaseRegex = new(@"(?<!^)(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex PascalCaseRegex = new(@"(?<!^)(?=[A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex SnakeCaseRegex = new(@"_", RegexOptions.Compiled);
    private static readonly Regex KebabCaseRegex = new(@"-", RegexOptions.Compiled);
    
    public QueryExpansionService(ILogger<QueryExpansionService> logger)
    {
        _logger = logger;
    }
    
    public Task<ExpandedQuery> ExpandQueryAsync(string originalQuery, QueryExpansionOptions? options = null)
    {
        options ??= new QueryExpansionOptions();
        
        var result = new ExpandedQuery
        {
            OriginalQuery = originalQuery
        };
        
        var allTerms = new Dictionary<string, float>();
        
        // Handle empty/whitespace queries
        if (string.IsNullOrWhiteSpace(originalQuery))
        {
            result.WeightedTerms = new Dictionary<string, float> { [""] = 1.0f };
            result.ExpandedLuceneQuery = "*";
            return Task.FromResult(result);
        }
        
        // Start with the original term at full weight
        var normalizedOriginal = originalQuery.ToLowerInvariant().Trim();
        allTerms[normalizedOriginal] = 1.0f;
        
        // Extract code terms if enabled
        if (options.EnableCodeTermExtraction)
        {
            var codeTerms = ExtractCodeTerms(originalQuery);
            result.ExtractedCodeTerms = codeTerms;
            
            foreach (var term in codeTerms)
            {
                var normalizedTerm = term.ToLowerInvariant();
                if (!allTerms.ContainsKey(normalizedTerm))
                {
                    allTerms[normalizedTerm] = 0.8f; // High weight for extracted terms
                }
            }
        }
        
        // Add domain synonyms if enabled
        if (options.EnableSynonymExpansion)
        {
            var synonyms = GetSynonyms(normalizedOriginal);
            result.SynonymTerms = synonyms;
            
            foreach (var synonym in synonyms)
            {
                var normalizedSynonym = synonym.ToLowerInvariant();
                if (!allTerms.ContainsKey(normalizedSynonym))
                {
                    allTerms[normalizedSynonym] = 0.6f; // Medium weight for synonyms
                }
            }
            
            // Also get synonyms for extracted code terms
            foreach (var codeTerm in result.ExtractedCodeTerms)
            {
                var codeTermSynonyms = GetSynonyms(codeTerm.ToLowerInvariant());
                foreach (var synonym in codeTermSynonyms)
                {
                    var normalizedSynonym = synonym.ToLowerInvariant();
                    if (!allTerms.ContainsKey(normalizedSynonym))
                    {
                        allTerms[normalizedSynonym] = 0.5f; // Lower weight for code term synonyms
                    }
                }
            }
        }
        
        // Filter by minimum weight and max terms
        result.WeightedTerms = allTerms
            .Where(kvp => kvp.Value >= options.MinTermWeight)
            .OrderByDescending(kvp => kvp.Value)
            .Take(options.MaxExpansionTerms)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        // Build Lucene query with boosts
        result.ExpandedLuceneQuery = BuildLuceneQuery(result.WeightedTerms);
        
        _logger.LogDebug("Expanded query '{OriginalQuery}' to {TermCount} terms: {Terms}", 
            originalQuery, result.WeightedTerms.Count, 
            string.Join(", ", result.WeightedTerms.Select(kvp => $"{kvp.Key}^{kvp.Value:F1}")));
        
        return Task.FromResult(result);
    }
    
    public string[] ExtractCodeTerms(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return Array.Empty<string>();
        
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        
        // Handle different naming conventions
        if (identifier.Contains('_'))
        {
            // snake_case
            parts.AddRange(identifier.Split('_', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (identifier.Contains('-'))
        {
            // kebab-case
            parts.AddRange(identifier.Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (CamelCaseRegex.IsMatch(identifier))
        {
            // camelCase or PascalCase
            parts.AddRange(CamelCaseRegex.Split(identifier).Where(p => !string.IsNullOrEmpty(p)));
        }
        else
        {
            // Single word
            parts.Add(identifier);
        }
        
        // Add individual parts
        foreach (var part in parts.Where(p => p.Length > 1))
        {
            terms.Add(part);
            terms.Add(part.ToLowerInvariant());
        }
        
        // Add compound variations if we have multiple parts
        if (parts.Count > 1)
        {
            var validParts = parts.Where(p => p.Length > 0).ToArray();
            if (validParts.Length > 1)
            {
                terms.Add(string.Join("", validParts).ToLowerInvariant());
                terms.Add(string.Join("-", validParts).ToLowerInvariant());
                terms.Add(string.Join("_", validParts).ToLowerInvariant());
            }
        }
        
        return terms.Where(t => t.Length > 1).Distinct().ToArray();
    }
    
    public string[] GetSynonyms(string term)
    {
        var normalizedTerm = term.ToLowerInvariant().Trim();
        var synonyms = new HashSet<string>();
        
        // Direct match
        if (DomainExpansions.TryGetValue(normalizedTerm, out var directSynonyms))
        {
            synonyms.UnionWith(directSynonyms);
        }
        
        // Reverse lookup - find keys that contain this term as a synonym
        foreach (var kvp in DomainExpansions)
        {
            if (kvp.Value.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase))
            {
                synonyms.Add(kvp.Key); // Add the key
                synonyms.UnionWith(kvp.Value); // Add other synonyms
            }
        }
        
        // Partial matches (e.g., "authentication" contains "auth")
        foreach (var kvp in DomainExpansions)
        {
            if (normalizedTerm.Contains(kvp.Key) && kvp.Key.Length >= 3)
            {
                synonyms.Add(kvp.Key);
                synonyms.UnionWith(kvp.Value);
            }
        }
        
        // Remove the original term from synonyms
        synonyms.Remove(normalizedTerm);
        
        return synonyms.ToArray();
    }
    
    private string BuildLuceneQuery(Dictionary<string, float> weightedTerms)
    {
        if (weightedTerms.Count == 0)
            return "*";
        
        var queryParts = new List<string>();
        
        foreach (var term in weightedTerms)
        {
            // Escape special Lucene characters
            var escapedTerm = EscapeLuceneSpecialChars(term.Key);
            
            // Add boost if weight is not 1.0
            if (Math.Abs(term.Value - 1.0f) > 0.01f)
            {
                queryParts.Add($"{escapedTerm}^{term.Value:F1}");
            }
            else
            {
                queryParts.Add(escapedTerm);
            }
        }
        
        // Join with OR operator
        return string.Join(" OR ", queryParts);
    }
    
    private static string EscapeLuceneSpecialChars(string term)
    {
        // Escape special Lucene characters: + - && || ! ( ) { } [ ] ^ " ~ * ? : \ /
        var specialChars = new[] { "+", "-", "&&", "||", "!", "(", ")", "{", "}", "[", "]", "^", "\"", "~", "*", "?", ":", "\\", "/" };
        
        foreach (var specialChar in specialChars)
        {
            term = term.Replace(specialChar, "\\" + specialChar);
        }
        
        return term;
    }
}