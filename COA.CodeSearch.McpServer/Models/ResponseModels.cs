namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Standard response wrapper for all MCP tools with token limit support
/// </summary>
public class McpToolResponse<T>
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public T? Data { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Metadata about the response including truncation information
/// </summary>
public class ResponseMetadata
{
    /// <summary>
    /// Total number of results available
    /// </summary>
    public int TotalResults { get; set; }
    
    /// <summary>
    /// Number of results actually returned
    /// </summary>
    public int ReturnedResults { get; set; }
    
    /// <summary>
    /// Indicates if results were truncated
    /// </summary>
    public bool IsTruncated { get; set; }
    
    /// <summary>
    /// Token to retrieve next page of results
    /// </summary>
    public string? ContinuationToken { get; set; }
    
    /// <summary>
    /// Estimated token count of the response
    /// </summary>
    public int? EstimatedTokens { get; set; }
    
    /// <summary>
    /// Reason for truncation if applicable
    /// </summary>
    public string? TruncationReason { get; set; }
    
    /// <summary>
    /// Additional tool-specific metadata
    /// </summary>
    public Dictionary<string, object>? AdditionalInfo { get; set; }
    
    /// <summary>
    /// Available detail levels for drill-down
    /// </summary>
    public List<DetailLevel>? AvailableDetailLevels { get; set; }
    
    /// <summary>
    /// Token for requesting more details
    /// </summary>
    public string? DetailRequestToken { get; set; }
}

/// <summary>
/// Describes available detail levels for progressive disclosure
/// </summary>
public class DetailLevel
{
    /// <summary>
    /// Identifier for this detail level
    /// </summary>
    public string Id { get; set; } = "";
    
    /// <summary>
    /// Human-readable name
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Description of what this level includes
    /// </summary>
    public string Description { get; set; } = "";
    
    /// <summary>
    /// Estimated tokens for this detail level
    /// </summary>
    public int? EstimatedTokens { get; set; }
    
    /// <summary>
    /// Whether this level is currently active
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Parameters for pagination support
/// </summary>
public class PaginationParams
{
    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int? MaxResults { get; set; }
    
    /// <summary>
    /// Number of results to skip
    /// </summary>
    public int? Offset { get; set; }
    
    /// <summary>
    /// Continuation token from previous response
    /// </summary>
    public string? ContinuationToken { get; set; }
    
    /// <summary>
    /// Whether to include total count (can be expensive)
    /// </summary>
    public bool ReturnTotalCount { get; set; } = true;
}

/// <summary>
/// Response modes for controlling detail level
/// </summary>
public enum ResponseMode
{
    /// <summary>
    /// Return complete details for all results
    /// </summary>
    Full,
    
    /// <summary>
    /// Return only summary information
    /// </summary>
    Summary,
    
    /// <summary>
    /// Return minimal details per item
    /// </summary>
    Compact,
    
    /// <summary>
    /// Reserved for future streaming support
    /// </summary>
    Stream
}

/// <summary>
/// Request for specific details from a previous response
/// </summary>
public class DetailRequest
{
    /// <summary>
    /// Token from the original response
    /// </summary>
    public string? DetailRequestToken { get; set; }
    
    /// <summary>
    /// Specific detail level requested
    /// </summary>
    public string? DetailLevelId { get; set; }
    
    /// <summary>
    /// Specific items to get details for (e.g., file paths)
    /// </summary>
    public List<string>? TargetItems { get; set; }
    
    /// <summary>
    /// Maximum results to return
    /// </summary>
    public int? MaxResults { get; set; }
    
    /// <summary>
    /// Additional parameters for specific detail requests
    /// </summary>
    public Dictionary<string, object>? AdditionalInfo { get; set; }
}