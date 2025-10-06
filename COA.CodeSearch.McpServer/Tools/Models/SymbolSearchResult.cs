using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Tools.Models;

/// <summary>
/// Result of a symbol search operation
/// </summary>
public class SymbolSearchResult
{
    /// <summary>
    /// List of symbol definitions found
    /// </summary>
    public List<SymbolDefinition> Symbols { get; set; } = new();
    
    /// <summary>
    /// Total number of symbols found (before limiting)
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Time taken to perform the search
    /// </summary>
    public TimeSpan SearchTime { get; set; }
    
    /// <summary>
    /// The original search query
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Number of symbols from Tier 1 (SQLite exact match)
    /// </summary>
    public int? Tier1Count { get; set; }

    /// <summary>
    /// Number of symbols from Tier 2 (Lucene fuzzy match)
    /// </summary>
    public int? Tier2Count { get; set; }

    /// <summary>
    /// Number of symbols from Tier 4 (Semantic similarity)
    /// </summary>
    public int? Tier4Count { get; set; }
}

/// <summary>
/// Represents a symbol definition found in the codebase
/// </summary>
public class SymbolDefinition
{
    /// <summary>
    /// The symbol name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The type of symbol (class, interface, method, function, etc.)
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    
    /// <summary>
    /// Full signature of the symbol
    /// </summary>
    public string Signature { get; set; } = string.Empty;
    
    /// <summary>
    /// File path where the symbol is defined
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Line number where the symbol is defined
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// Column position where the symbol is defined
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// Language of the source file
    /// </summary>
    public string? Language { get; set; }
    
    /// <summary>
    /// Modifiers (public, private, static, etc.)
    /// </summary>
    public List<string> Modifiers { get; set; } = new();
    
    /// <summary>
    /// Base type or parent class (for inheritance)
    /// </summary>
    public string? BaseType { get; set; }
    
    /// <summary>
    /// Implemented interfaces
    /// </summary>
    public List<string>? Interfaces { get; set; }
    
    /// <summary>
    /// For methods: the containing type
    /// </summary>
    public string? ContainingType { get; set; }
    
    /// <summary>
    /// For methods: return type
    /// </summary>
    public string? ReturnType { get; set; }
    
    /// <summary>
    /// For methods: parameter list
    /// </summary>
    public List<string>? Parameters { get; set; }
    
    /// <summary>
    /// Number of references to this symbol (if requested)
    /// </summary>
    public int? ReferenceCount { get; set; }
    
    /// <summary>
    /// Context snippet around the definition
    /// </summary>
    public string? Snippet { get; set; }
    
    /// <summary>
    /// Relevance score from search
    /// </summary>
    public float Score { get; set; }
}