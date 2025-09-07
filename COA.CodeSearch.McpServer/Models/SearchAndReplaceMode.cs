namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Defines different matching modes for search and replace operations
/// to handle various real-world code editing scenarios
/// </summary>
public enum SearchAndReplaceMode
{
    /// <summary>
    /// Exact character-for-character matching (current default behavior)
    /// Requires perfect whitespace, indentation, and formatting match
    /// </summary>
    Exact,

    /// <summary>
    /// Whitespace-insensitive matching - normalizes spaces, tabs, and line endings
    /// Treats "    " (4 spaces) and "\t" (tab) as equivalent
    /// Handles minor indentation differences
    /// </summary>
    WhitespaceInsensitive,

    /// <summary>
    /// Multi-line pattern support - can match patterns that span multiple lines
    /// Useful for replacing entire method bodies, class definitions, etc.
    /// </summary>
    MultiLine,

    /// <summary>
    /// Fuzzy matching - tolerates minor formatting differences
    /// Combines whitespace normalization with multi-line support
    /// Most flexible option for dynamic code editing
    /// </summary>
    Fuzzy
}