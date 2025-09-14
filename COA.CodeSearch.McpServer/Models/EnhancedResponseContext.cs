using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Enhanced response context that includes tool token estimation coordination.
/// Bridges tool estimates with response builder token management.
/// </summary>
public class EnhancedResponseContext : ResponseContext
{
    /// <summary>
    /// Token estimate from the tool execution for coordination with response builder.
    /// </summary>
    public int? ToolTokenEstimate { get; set; }

    /// <summary>
    /// Actual tokens used by the tool (for telemetry feedback).
    /// </summary>
    public int? ToolActualTokens { get; set; }

    /// <summary>
    /// Tool's historical estimation accuracy for dynamic budget adjustment.
    /// </summary>
    public double? ToolEstimationAccuracy { get; set; }

    /// <summary>
    /// Tool category for category-specific budget optimization.
    /// </summary>
    public ToolCategory? ToolCategory { get; set; }

    /// <summary>
    /// Whether the tool indicated high confidence in its estimate.
    /// </summary>
    public bool? ToolHighConfidenceEstimate { get; set; }

    /// <summary>
    /// Expected result complexity from tool analysis.
    /// </summary>
    public ResultComplexity? ExpectedComplexity { get; set; }

    /// <summary>
    /// Tool-specific metadata that might affect token usage.
    /// </summary>
    public Dictionary<string, object>? ToolMetadata { get; set; }
}

/// <summary>
/// Represents the expected complexity of tool results for token coordination.
/// </summary>
public enum ResultComplexity
{
    /// <summary>
    /// Simple results with basic data structures.
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate complexity with nested objects and collections.
    /// </summary>
    Moderate,

    /// <summary>
    /// High complexity with deep nesting, large collections, or rich metadata.
    /// </summary>
    High,

    /// <summary>
    /// Very high complexity requiring aggressive optimization strategies.
    /// </summary>
    VeryHigh
}