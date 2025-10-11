namespace COA.CodeSearch.McpServer.Tools.Models;

/// <summary>
/// Result of a read_symbols operation - specific symbol implementations extracted from a file
/// </summary>
public class ReadSymbolsResult
{
    /// <summary>
    /// Path to the file that was read
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Language detected for the file
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Extracted symbols with their code
    /// </summary>
    public List<SymbolCode> Symbols { get; set; } = new();

    /// <summary>
    /// Number of symbols successfully extracted
    /// </summary>
    public int SymbolCount { get; set; }

    /// <summary>
    /// Number of symbols that were requested but not found
    /// </summary>
    public int NotFoundCount { get; set; }

    /// <summary>
    /// List of symbol names that were not found
    /// </summary>
    public List<string> NotFoundSymbols { get; set; } = new();

    /// <summary>
    /// Estimated token count for the response
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Whether the response was truncated due to token limits
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Time taken to extract symbols
    /// </summary>
    public TimeSpan ExtractionTime { get; set; }

    /// <summary>
    /// Whether the extraction was successful
    /// </summary>
    public bool Success { get; set; }
}

/// <summary>
/// Code extracted for a specific symbol
/// </summary>
public class SymbolCode
{
    /// <summary>
    /// Name of the symbol
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kind of symbol (class, method, function, interface, etc.)
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Extracted source code
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Signature/declaration only (for detail_level: signature)
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based)
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Starting column (0-based)
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// Ending column (0-based)
    /// </summary>
    public int EndColumn { get; set; }

    /// <summary>
    /// Symbols that this symbol calls/uses (dependencies)
    /// </summary>
    public List<SymbolReference>? Dependencies { get; set; }

    /// <summary>
    /// Symbols that call this symbol (callers)
    /// </summary>
    public List<SymbolReference>? Callers { get; set; }

    /// <summary>
    /// Inheritance information (base classes, interfaces)
    /// </summary>
    public InheritanceInfo? Inheritance { get; set; }

    /// <summary>
    /// Estimated token count for this symbol's code
    /// </summary>
    public int EstimatedTokens { get; set; }
}

/// <summary>
/// Reference to another symbol (for dependencies and callers)
/// </summary>
public class SymbolReference
{
    /// <summary>
    /// Name of the referenced symbol
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kind of the referenced symbol (method, function, class, etc.)
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// File path where the reference occurs
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number where the reference occurs
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Column where the reference occurs
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Count of how many times this symbol is referenced (for aggregated references)
    /// </summary>
    public int Count { get; set; } = 1;
}

/// <summary>
/// Inheritance information for a symbol
/// </summary>
public class InheritanceInfo
{
    /// <summary>
    /// Base class name (for classes)
    /// </summary>
    public string? BaseClass { get; set; }

    /// <summary>
    /// Implemented interfaces
    /// </summary>
    public List<string> Interfaces { get; set; } = new();

    /// <summary>
    /// Whether this symbol inherits from another
    /// </summary>
    public bool HasInheritance => !string.IsNullOrEmpty(BaseClass) || Interfaces.Count > 0;
}
