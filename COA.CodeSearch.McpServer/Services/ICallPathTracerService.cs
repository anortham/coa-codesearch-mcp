using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for tracing call paths through the codebase using SQLite identifiers and symbols.
/// Supports upward tracing (who calls this), downward tracing (what does this call),
/// and semantic bridging for cross-language calls.
/// </summary>
public interface ICallPathTracerService
{
    /// <summary>
    /// Trace upward to find all callers of a symbol (who calls this method).
    /// Uses recursive CTE to build call hierarchy up to maxDepth levels.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolName">Name of the symbol to trace callers for</param>
    /// <param name="maxDepth">Maximum depth to trace (prevents infinite loops)</param>
    /// <param name="caseSensitive">Whether symbol name matching is case sensitive</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of call path nodes representing the call hierarchy</returns>
    Task<List<CallPathNode>> TraceUpwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace downward to find all callees of a symbol (what does this method call).
    /// Returns direct calls made from within the specified symbol.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolName">Name of the symbol to trace callees for</param>
    /// <param name="maxDepth">Maximum depth to trace</param>
    /// <param name="caseSensitive">Whether symbol name matching is case sensitive</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of call path nodes representing what this symbol calls</returns>
    Task<List<CallPathNode>> TraceDownwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace in both directions to build a complete call graph around a symbol.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolName">Name of the symbol to trace</param>
    /// <param name="maxDepth">Maximum depth to trace in each direction</param>
    /// <param name="caseSensitive">Whether symbol name matching is case sensitive</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Bidirectional call path result with both callers and callees</returns>
    Task<BidirectionalCallPath> TraceBothDirectionsAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a node in the call path hierarchy.
/// </summary>
public class CallPathNode
{
    /// <summary>
    /// Identifier that represents the call/reference
    /// </summary>
    public required JulieIdentifier Identifier { get; init; }

    /// <summary>
    /// Symbol that contains this call (the caller)
    /// </summary>
    public JulieSymbol? ContainingSymbol { get; init; }

    /// <summary>
    /// Symbol being called (resolved target)
    /// </summary>
    public JulieSymbol? TargetSymbol { get; init; }

    /// <summary>
    /// Depth in the call hierarchy (0 = direct call)
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Direction of the trace (up = caller, down = callee)
    /// </summary>
    public CallDirection Direction { get; init; }

    /// <summary>
    /// Child nodes in the call path (for recursive tracing)
    /// </summary>
    public List<CallPathNode> Children { get; init; } = new();

    /// <summary>
    /// Whether this call was resolved via semantic similarity (cross-language bridging)
    /// </summary>
    public bool IsSemanticMatch { get; init; }

    /// <summary>
    /// Confidence score for semantic matches (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; init; } = 1.0;
}

/// <summary>
/// Direction of call path tracing
/// </summary>
public enum CallDirection
{
    /// <summary>
    /// Tracing upward (who calls this)
    /// </summary>
    Upward,

    /// <summary>
    /// Tracing downward (what does this call)
    /// </summary>
    Downward
}

/// <summary>
/// Result of bidirectional call path tracing
/// </summary>
public class BidirectionalCallPath
{
    /// <summary>
    /// The target symbol being traced
    /// </summary>
    public required JulieSymbol TargetSymbol { get; init; }

    /// <summary>
    /// Upward call paths (who calls this symbol)
    /// </summary>
    public List<CallPathNode> Callers { get; init; } = new();

    /// <summary>
    /// Downward call paths (what this symbol calls)
    /// </summary>
    public List<CallPathNode> Callees { get; init; } = new();

    /// <summary>
    /// Total number of paths traced
    /// </summary>
    public int TotalPaths => Callers.Count + Callees.Count;
}
