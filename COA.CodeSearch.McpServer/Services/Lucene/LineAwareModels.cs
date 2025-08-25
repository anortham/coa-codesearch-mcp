using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Services.Lucene;

/// <summary>
/// Context information for a specific line match
/// </summary>
public class LineContext
{
    public int LineNumber { get; set; }
    public string LineText { get; set; } = string.Empty;
    public List<string> ContextLines { get; set; } = new();
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

/// <summary>
/// Complete line data extracted during indexing
/// </summary>
public class LineData
{
    public string[] Lines { get; set; } = Array.Empty<string>();
    public Dictionary<string, List<int>> TermLineMap { get; set; } = new();
    public Dictionary<string, LineContext> FirstMatches { get; set; } = new();
    public int LineCount => Lines.Length;
}

/// <summary>
/// Configuration for line-aware indexing
/// </summary>
public class LineIndexingOptions
{
    /// <summary>
    /// Number of context lines to include before and after a match
    /// </summary>
    public int ContextLines { get; set; } = 3;
    
    /// <summary>
    /// Maximum number of first matches to cache per term
    /// </summary>
    public int MaxFirstMatchCache { get; set; } = 1000;
    
    /// <summary>
    /// Minimum term length to include in line mapping
    /// </summary>
    public int MinTermLength { get; set; } = 2;
    
    /// <summary>
    /// Whether to store complete line text for each line
    /// </summary>
    public bool StoreAllLines { get; set; } = true;
}

/// <summary>
/// Result of line-aware line number lookup
/// </summary>
public class LineAwareResult
{
    public int? LineNumber { get; set; }
    public LineContext? Context { get; set; }
    public bool IsFromCache { get; set; }
    public bool IsAccurate { get; set; } = true;
}