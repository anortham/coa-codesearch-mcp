namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Defines different search modes for text_search tool
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Automatically detect the best search approach based on query characteristics.
    /// Uses SmartQueryPreprocessor to route queries to optimal fields (symbols, patterns, etc.)
    /// </summary>
    Auto,

    /// <summary>
    /// Exact literal matching - no fuzzy tolerance, no wildcards.
    /// Best for precise code searches where you know the exact text.
    /// </summary>
    Exact,

    /// <summary>
    /// Typo-tolerant fuzzy search using Lucene FuzzyQuery.
    /// Finds similar matches with slight variations (typos, spelling differences).
    /// </summary>
    Fuzzy,

    /// <summary>
    /// Semantic vector similarity search using embeddings (Tier 3 search).
    /// Finds conceptually similar code across languages using HNSW vector index.
    /// </summary>
    Semantic,

    /// <summary>
    /// Regular expression pattern matching using Lucene RegexpQuery.
    /// Supports full regex syntax for complex pattern matching.
    /// </summary>
    Regex,

    // DEPRECATED - kept for backward compatibility
    /// <summary>
    /// [DEPRECATED] Use Auto instead. Pattern-preserving search.
    /// </summary>
    [Obsolete("Use Auto mode instead - smart routing handles patterns automatically")]
    Pattern,

    /// <summary>
    /// [DEPRECATED] Use Auto instead. Symbol-only search.
    /// </summary>
    [Obsolete("Use Auto mode instead - smart routing targets symbol field when appropriate")]
    Symbol,

    /// <summary>
    /// [DEPRECATED] Use Auto instead. Standard tokenization.
    /// </summary>
    [Obsolete("Use Auto mode instead")]
    Standard
}