namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Defines different search modes that leverage multi-field indexing for optimal search results
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Automatically detect the best search approach based on query characteristics
    /// </summary>
    Auto,

    /// <summary>
    /// Pattern-preserving search - handles special characters like {}, (), : IRepository<T>
    /// Uses content_patterns field with WhitespaceTokenizer for pattern preservation
    /// </summary>
    Pattern,

    /// <summary>
    /// Search only class/method/symbol names
    /// Uses content_symbols field with symbol-only content
    /// </summary>
    Symbol,

    /// <summary>
    /// Standard tokenization (current default behavior)
    /// Uses content field with standard CodeAnalyzer processing
    /// </summary>
    Standard,

    /// <summary>
    /// Future: Typo-tolerant fuzzy search
    /// Currently maps to Standard mode
    /// </summary>
    Fuzzy
}