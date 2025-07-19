namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Response structure optimized for Claude AI consumption
/// </summary>
public class ClaudeOptimizedResponse<T>
{
    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// The response mode used (auto-selected based on size)
    /// </summary>
    public string Mode { get; set; } = "full";
    
    /// <summary>
    /// Whether mode was automatically switched due to size
    /// </summary>
    public bool AutoModeSwitch { get; set; }
    
    /// <summary>
    /// The main response data
    /// </summary>
    public T? Data { get; set; }
    
    /// <summary>
    /// Suggested next actions for Claude
    /// </summary>
    public NextActions? NextActions { get; set; }
    
    /// <summary>
    /// Context to help Claude understand the results
    /// </summary>
    public ResultContext? Context { get; set; }
    
    /// <summary>
    /// Standard metadata
    /// </summary>
    public ResponseMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Provides Claude with clear next steps
/// </summary>
public class NextActions
{
    /// <summary>
    /// Recommended actions based on the results
    /// </summary>
    public List<RecommendedAction> Recommended { get; set; } = new();
    
    /// <summary>
    /// Other available actions
    /// </summary>
    public List<AvailableAction> Available { get; set; } = new();
}

/// <summary>
/// A recommended action for Claude
/// </summary>
public class RecommendedAction
{
    /// <summary>
    /// Action identifier
    /// </summary>
    public string Action { get; set; } = "";
    
    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; set; } = "";
    
    /// <summary>
    /// Why this is recommended
    /// </summary>
    public string? Reason { get; set; }
    
    /// <summary>
    /// Estimated token cost
    /// </summary>
    public int EstimatedTokens { get; set; }
    
    /// <summary>
    /// Ready-to-use command parameters
    /// </summary>
    public object? Command { get; set; }
    
    /// <summary>
    /// Priority level
    /// </summary>
    public string Priority { get; set; } = "medium";
}

/// <summary>
/// An available action for Claude
/// </summary>
public class AvailableAction
{
    /// <summary>
    /// Action identifier
    /// </summary>
    public string Action { get; set; } = "";
    
    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; set; } = "";
    
    /// <summary>
    /// Estimated token cost
    /// </summary>
    public int EstimatedTokens { get; set; }
    
    /// <summary>
    /// Any warnings about this action
    /// </summary>
    public string? Warning { get; set; }
}

/// <summary>
/// Context to help Claude understand the results
/// </summary>
public class ResultContext
{
    /// <summary>
    /// Overall impact level (low, medium, high)
    /// </summary>
    public string Impact { get; set; } = "medium";
    
    /// <summary>
    /// Risk factors to consider
    /// </summary>
    public List<string> RiskFactors { get; set; } = new();
    
    /// <summary>
    /// Helpful suggestions based on the results
    /// </summary>
    public List<string> Suggestions { get; set; } = new();
    
    /// <summary>
    /// Key insights from analyzing the results
    /// </summary>
    public List<string> KeyInsights { get; set; } = new();
}

/// <summary>
/// Summary data structure optimized for Claude
/// </summary>
public class ClaudeSummaryData
{
    /// <summary>
    /// High-level overview
    /// </summary>
    public Overview Overview { get; set; } = new();
    
    /// <summary>
    /// Categorized summary information
    /// </summary>
    public Dictionary<string, CategorySummary> ByCategory { get; set; } = new();
    
    /// <summary>
    /// Most important items to review
    /// </summary>
    public List<Hotspot> Hotspots { get; set; } = new();
    
    /// <summary>
    /// Preview of actual changes
    /// </summary>
    public ChangePreview? Preview { get; set; }
}

/// <summary>
/// High-level overview for Claude
/// </summary>
public class Overview
{
    public int TotalItems { get; set; }
    public int AffectedFiles { get; set; }
    public int EstimatedFullResponseTokens { get; set; }
    public List<string> KeyInsights { get; set; } = new();
}

/// <summary>
/// Summary by category
/// </summary>
public class CategorySummary
{
    public int Files { get; set; }
    public int Occurrences { get; set; }
    public string? PrimaryPattern { get; set; }
}

/// <summary>
/// High-impact areas for Claude to focus on
/// </summary>
public class Hotspot
{
    public string File { get; set; } = "";
    public int Occurrences { get; set; }
    public string Complexity { get; set; } = "medium";
    public string? Reason { get; set; }
}

/// <summary>
/// Preview of changes for Claude
/// </summary>
public class ChangePreview
{
    public List<PreviewItem> TopChanges { get; set; } = new();
    public bool FullContext { get; set; }
    public object? GetFullContextCommand { get; set; }
}

/// <summary>
/// A preview item
/// </summary>
public class PreviewItem
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Preview { get; set; } = "";
    public string? Context { get; set; }
}

/// <summary>
/// Smart batch request optimized for Claude
/// </summary>
public class SmartBatchCriteria
{
    /// <summary>
    /// Minimum occurrences to include a file
    /// </summary>
    public int? MinOccurrences { get; set; }
    
    /// <summary>
    /// Categories to include
    /// </summary>
    public List<string>? Categories { get; set; }
    
    /// <summary>
    /// Maximum tokens to return
    /// </summary>
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Whether to include surrounding context
    /// </summary>
    public bool IncludeContext { get; set; } = true;
    
    /// <summary>
    /// Specific files to include/exclude
    /// </summary>
    public List<string>? IncludeFiles { get; set; }
    public List<string>? ExcludeFiles { get; set; }
}