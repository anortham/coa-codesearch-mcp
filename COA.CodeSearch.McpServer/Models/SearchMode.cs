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
    /// Exact string matching - handles special characters like {}, (), etc.
    /// Uses content_literal field with KeywordTokenizer for no tokenization
    /// </summary>
    Literal,

    /// <summary>
    /// Code-aware tokenization with enhanced processing
    /// Uses content_code field with always-on CamelCase splitting
    /// </summary>
    Code,

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