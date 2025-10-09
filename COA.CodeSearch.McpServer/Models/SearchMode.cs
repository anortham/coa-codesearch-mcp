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

    // Internal routing modes - used by Auto mode's smart detection
    /// <summary>
    /// Pattern-preserving search mode (internal use).
    /// Routes to content_patterns field for code structure matching with minimal analysis.
    /// Recommended: Use Auto mode for automatic detection.
    /// </summary>
    Pattern,

    /// <summary>
    /// Symbol-only search mode (internal use).
    /// Routes to content_symbols field for identifier and CamelCase matching.
    /// Recommended: Use Auto mode for automatic detection.
    /// </summary>
    Symbol,

    /// <summary>
    /// Standard tokenization search mode (internal use).
    /// Routes to content field for general full-text search.
    /// Recommended: Use Auto mode for automatic detection.
    /// </summary>
    Standard
}