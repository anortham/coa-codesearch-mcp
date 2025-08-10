using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization.Models;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.Next.McpServer.Models;

/// <summary>
/// Wrapper that combines ToolResultBase with AIOptimizedResponse for MCP tools
/// </summary>
public class TokenOptimizedResult : ToolResultBase
{
    private string _operation = string.Empty;
    
    /// <summary>
    /// The operation name for this result
    /// </summary>
    public override string Operation => _operation;
    
    /// <summary>
    /// Sets the operation name
    /// </summary>
    public void SetOperation(string operation)
    {
        _operation = operation;
    }

    /// <summary>
    /// Gets or sets the response format identifier.
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "ai-optimized";
    
    /// <summary>
    /// Gets or sets the main response data.
    /// </summary>
    [JsonPropertyName("data")]
    public AIResponseData Data { get; set; } = new();
    
    /// <summary>
    /// Gets or sets the list of insights about the data.
    /// </summary>
    [JsonPropertyName("insights")]
    public new List<string> Insights { get; set; } = new();
    
    /// <summary>
    /// Gets or sets the suggested next actions.
    /// </summary>
    [JsonPropertyName("actions")]
    public new List<COA.Mcp.Framework.Models.AIAction> Actions { get; set; } = new();
    
    /// <summary>
    /// Gets or sets the response metadata.
    /// </summary>
    [JsonPropertyName("meta")]
    public new AIResponseMeta Meta { get; set; } = new();

    /// <summary>
    /// Creates a TokenOptimizedResult from an AIOptimizedResponse
    /// </summary>
    public static TokenOptimizedResult FromAIResponse(AIOptimizedResponse aiResponse, string operation)
    {
        var result = new TokenOptimizedResult
        {
            Success = true,
            Format = aiResponse.Format,
            Data = aiResponse.Data,
            Insights = aiResponse.Insights,
            Actions = aiResponse.Actions,
            Meta = aiResponse.Meta
        };
        result._operation = operation;
        return result;
    }

    /// <summary>
    /// Creates an error result
    /// </summary>
    public static TokenOptimizedResult CreateError(string operation, COA.Mcp.Framework.Models.ErrorInfo error)
    {
        var result = new TokenOptimizedResult
        {
            Success = false,
            Error = error,
            Data = new AIResponseData
            {
                Summary = "An error occurred",
                Results = new List<object>(),
                Count = 0
            }
        };
        result._operation = operation;
        return result;
    }
}