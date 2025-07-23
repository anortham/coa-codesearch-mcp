namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for expanding search queries with synonyms, related terms, and contextual variations
/// </summary>
public interface IQueryExpansionService
{
    /// <summary>
    /// Expand a simple query into multiple related terms with weights
    /// </summary>
    Task<ExpandedQuery> ExpandQueryAsync(string originalQuery, QueryExpansionOptions? options = null);
    
    /// <summary>
    /// Extract technical terms from code-style identifiers (camelCase, PascalCase, etc.)
    /// </summary>
    string[] ExtractCodeTerms(string identifier);
    
    /// <summary>
    /// Get domain-specific synonyms for a term
    /// </summary>
    string[] GetSynonyms(string term);
}

/// <summary>
/// Result of query expansion with weighted terms
/// </summary>
public class ExpandedQuery
{
    public string OriginalQuery { get; set; } = string.Empty;
    public Dictionary<string, float> WeightedTerms { get; set; } = new();
    public string[] ExtractedCodeTerms { get; set; } = Array.Empty<string>();
    public string[] SynonymTerms { get; set; } = Array.Empty<string>();
    public string ExpandedLuceneQuery { get; set; } = string.Empty;
}

/// <summary>
/// Options for controlling query expansion behavior
/// </summary>
public class QueryExpansionOptions
{
    /// <summary>
    /// Maximum number of expansion terms to include
    /// </summary>
    public int MaxExpansionTerms { get; set; } = 10;
    
    /// <summary>
    /// Minimum weight for expansion terms (0.0 - 1.0)
    /// </summary>
    public float MinTermWeight { get; set; } = 0.3f;
    
    /// <summary>
    /// Enable code term extraction (camelCase, etc.)
    /// </summary>
    public bool EnableCodeTermExtraction { get; set; } = true;
    
    /// <summary>
    /// Enable domain synonym expansion
    /// </summary>
    public bool EnableSynonymExpansion { get; set; } = true;
    
    /// <summary>
    /// Domain context for expansion (e.g., "authentication", "database", "web")
    /// </summary>
    public string? DomainContext { get; set; }
}