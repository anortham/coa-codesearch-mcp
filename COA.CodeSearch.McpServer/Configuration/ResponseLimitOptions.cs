namespace COA.CodeSearch.McpServer.Configuration;

/// <summary>
/// Configuration options for response size limits and truncation behavior
/// </summary>
public class ResponseLimitOptions
{
    /// <summary>
    /// Maximum tokens allowed in a response (default: 20,000)
    /// </summary>
    public int MaxTokens { get; set; } = 20000;
    
    /// <summary>
    /// Safety margin as a percentage (0.8 = 80% of MaxTokens)
    /// </summary>
    public double SafetyMargin { get; set; } = 0.8;
    
    /// <summary>
    /// Default maximum results for list operations
    /// </summary>
    public int DefaultMaxResults { get; set; } = 50;
    
    /// <summary>
    /// Whether to enable automatic truncation
    /// </summary>
    public bool EnableTruncation { get; set; } = true;
    
    /// <summary>
    /// Whether to enable pagination support
    /// </summary>
    public bool EnablePagination { get; set; } = true;
    
    /// <summary>
    /// Tool-specific token limits (overrides MaxTokens for specific tools)
    /// </summary>
    public Dictionary<string, int> ToolSpecificLimits { get; set; } = new();
    
    /// <summary>
    /// Tool-specific default result limits
    /// </summary>
    public Dictionary<string, int> ToolSpecificResultLimits { get; set; } = new();
    
    /// <summary>
    /// Whether to log token usage statistics
    /// </summary>
    public bool EnableTokenUsageLogging { get; set; } = true;
    
    /// <summary>
    /// Gets the token limit for a specific tool
    /// </summary>
    public int GetTokenLimitForTool(string toolName)
    {
        return ToolSpecificLimits.TryGetValue(toolName, out var limit) ? limit : MaxTokens;
    }
    
    /// <summary>
    /// Gets the default result limit for a specific tool
    /// </summary>
    public int GetResultLimitForTool(string toolName)
    {
        return ToolSpecificResultLimits.TryGetValue(toolName, out var limit) ? limit : DefaultMaxResults;
    }
}