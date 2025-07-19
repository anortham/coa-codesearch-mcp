using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Estimates the token size of responses to prevent exceeding MCP token limits
/// </summary>
public interface IResponseSizeEstimator
{
    /// <summary>
    /// Estimates the number of tokens for a given response object
    /// </summary>
    int EstimateTokens(object response);
    
    /// <summary>
    /// Checks if a response will exceed the specified token limit
    /// </summary>
    bool WillExceedLimit(object response, int maxTokens);
    
    /// <summary>
    /// Estimates the JSON character size of a response
    /// </summary>
    int EstimateJsonSize(object response);
}

/// <summary>
/// Default implementation of response size estimator
/// </summary>
public class ResponseSizeEstimator : IResponseSizeEstimator
{
    private readonly ILogger<ResponseSizeEstimator> _logger;
    
    // Approximate conversion ratios based on OpenAI tokenization
    private const double TokenToCharRatio = 0.25; // 1 token ≈ 4 characters
    private const double SafetyMultiplier = 1.2; // Add 20% safety margin
    
    // Cache for JSON serializer options
    private readonly JsonSerializerOptions _jsonOptions;
    
    public ResponseSizeEstimator(ILogger<ResponseSizeEstimator> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }
    
    public int EstimateTokens(object response)
    {
        try
        {
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var estimatedTokens = (int)(json.Length * TokenToCharRatio * SafetyMultiplier);
            
            _logger.LogDebug(
                "Estimated {Tokens} tokens for response of {Chars} characters",
                estimatedTokens,
                json.Length);
            
            return estimatedTokens;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to estimate response size, returning conservative estimate");
            return int.MaxValue; // Conservative estimate to trigger truncation
        }
    }
    
    public bool WillExceedLimit(object response, int maxTokens)
    {
        var estimated = EstimateTokens(response);
        var willExceed = estimated > maxTokens;
        
        if (willExceed)
        {
            _logger.LogWarning(
                "Response estimated at {EstimatedTokens} tokens exceeds limit of {MaxTokens}",
                estimated,
                maxTokens);
        }
        
        return willExceed;
    }
    
    public int EstimateJsonSize(object response)
    {
        try
        {
            return JsonSerializer.Serialize(response, _jsonOptions).Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to estimate JSON size");
            return 0;
        }
    }
}