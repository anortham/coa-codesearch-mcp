using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Dual-format response optimized for AI agent consumption and token efficiency
/// </summary>
public class AIOptimizedResponse
{
    /// <summary>
    /// Response format indicator for AI parsing
    /// </summary>
    public string Format { get; set; } = "ai-optimized";

    /// <summary>
    /// Structured data for AI consumption
    /// </summary>
    public AIResponseData Data { get; set; } = new();

    /// <summary>
    /// Human-readable markdown summary
    /// </summary>
    public string? DisplayMarkdown { get; set; }

    /// <summary>
    /// Contextual actions based on results
    /// </summary>
    public List<AIAction> Actions { get; set; } = new();

    /// <summary>
    /// Key insights derived from the data
    /// </summary>
    public List<string> Insights { get; set; } = new();

    /// <summary>
    /// Token usage and expansion metadata
    /// </summary>
    public AIResponseMeta Meta { get; set; } = new();
}

/// <summary>
/// Structured data portion of AI response
/// </summary>
public class AIResponseData
{
    /// <summary>
    /// Result summary for quick understanding
    /// </summary>
    public ResultSummary Summary { get; set; } = new();

    /// <summary>
    /// Key items (memories, files, etc.) limited by token budget
    /// </summary>
    public List<object> Items { get; set; } = new();

    /// <summary>
    /// Distribution analysis for pattern recognition
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> Distribution { get; set; } = new();

    /// <summary>
    /// Hot spots or high-concentration areas
    /// </summary>
    public List<object> Hotspots { get; set; } = new();
}

/// <summary>
/// Contextual action with exact execution parameters
/// </summary>
public class AIAction
{
    /// <summary>
    /// Unique action identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable action description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// MCP tool command with parameters
    /// </summary>
    public AIActionCommand Command { get; set; } = new();

    /// <summary>
    /// Estimated token cost of executing this action
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Action priority for AI decision making
    /// </summary>
    public ActionPriority Priority { get; set; } = ActionPriority.Medium;

    /// <summary>
    /// When this action is most relevant
    /// </summary>
    public ActionContext Context { get; set; } = ActionContext.Always;
}

/// <summary>
/// MCP tool command with parameters
/// </summary>
public class AIActionCommand
{
    /// <summary>
    /// MCP tool name
    /// </summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>
    /// Tool parameters as key-value pairs
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Result summary for quick understanding
/// </summary>
public class ResultSummary
{
    /// <summary>
    /// Total items found
    /// </summary>
    public int TotalFound { get; set; }

    /// <summary>
    /// Items returned in this response
    /// </summary>
    public int Returned { get; set; }

    /// <summary>
    /// Whether results were truncated
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Primary result type or category
    /// </summary>
    public string? PrimaryType { get; set; }
}

/// <summary>
/// Response metadata for token management
/// </summary>
public class AIResponseMeta
{
    /// <summary>
    /// Current response mode (summary/full)
    /// </summary>
    public string Mode { get; set; } = "summary";

    /// <summary>
    /// Estimated token count for this response
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Token budget used for sizing decisions
    /// </summary>
    public int TokenBudget { get; set; } = 2000;

    /// <summary>
    /// Cache key for progressive disclosure
    /// </summary>
    public string? CacheKey { get; set; }

    /// <summary>
    /// Whether auto-mode switching occurred
    /// </summary>
    public bool AutoModeSwitch { get; set; }

    /// <summary>
    /// Available expansion options
    /// </summary>
    public List<string> AvailableExpansions { get; set; } = new();
}

/// <summary>
/// Action priority levels for AI decision making
/// </summary>
public enum ActionPriority
{
    /// <summary>
    /// Critical action that should be taken immediately
    /// </summary>
    High,

    /// <summary>
    /// Important action that provides significant value
    /// </summary>
    Medium,

    /// <summary>
    /// Optional action for additional context
    /// </summary>
    Low
}

/// <summary>
/// When an action is most relevant
/// </summary>
public enum ActionContext
{
    /// <summary>
    /// Action is always relevant
    /// </summary>
    Always,

    /// <summary>
    /// Action is relevant when no results found
    /// </summary>
    EmptyResults,

    /// <summary>
    /// Action is relevant when many results found
    /// </summary>
    ManyResults,

    /// <summary>
    /// Action is relevant for specific result types
    /// </summary>
    SpecificType,

    /// <summary>
    /// Action is relevant for follow-up exploration
    /// </summary>
    Exploration
}