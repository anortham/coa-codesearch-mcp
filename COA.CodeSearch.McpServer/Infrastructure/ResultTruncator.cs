using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Handles truncation of large result sets to stay within token limits
/// </summary>
public interface IResultTruncator
{
    /// <summary>
    /// Truncates results based on token limit
    /// </summary>
    TruncatedResult<T> TruncateResults<T>(
        IEnumerable<T> results, 
        int maxTokens,
        Func<T, int>? tokenEstimator = null);
        
    /// <summary>
    /// Truncates results based on count limit
    /// </summary>
    TruncatedResult<T> TruncateByCount<T>(
        IEnumerable<T> results,
        int maxCount);
}

/// <summary>
/// Contains truncated results with metadata about the truncation
/// </summary>
public class TruncatedResult<T>
{
    public List<T> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int ReturnedCount { get; set; }
    public bool IsTruncated { get; set; }
    public string? TruncationReason { get; set; }
    public string? ContinuationToken { get; set; }
    public int EstimatedTotalTokens { get; set; }
    public int EstimatedReturnedTokens { get; set; }
}

/// <summary>
/// Default implementation of result truncator
/// </summary>
public class ResultTruncator : IResultTruncator
{
    private readonly IResponseSizeEstimator _sizeEstimator;
    private readonly ILogger<ResultTruncator> _logger;
    
    // Base overhead for response structure (metadata, wrapping, etc.)
    private const int BaseResponseOverhead = 200;
    
    public ResultTruncator(
        IResponseSizeEstimator sizeEstimator,
        ILogger<ResultTruncator> logger)
    {
        _sizeEstimator = sizeEstimator;
        _logger = logger;
    }
    
    public TruncatedResult<T> TruncateResults<T>(
        IEnumerable<T> results, 
        int maxTokens,
        Func<T, int>? tokenEstimator = null)
    {
        var resultList = results.ToList();
        var truncatedResult = new TruncatedResult<T>
        {
            TotalCount = resultList.Count
        };
        
        // Use provided estimator or default to serialization-based estimation
        if (tokenEstimator == null)
        {
            tokenEstimator = item => item != null ? _sizeEstimator.EstimateTokens(item) : 0;
        }
        
        int currentTokens = BaseResponseOverhead;
        int totalEstimatedTokens = BaseResponseOverhead;
        
        // First pass: estimate total tokens
        foreach (var result in resultList)
        {
            totalEstimatedTokens += tokenEstimator(result);
        }
        
        truncatedResult.EstimatedTotalTokens = totalEstimatedTokens;
        
        // Second pass: add results until we approach the limit
        foreach (var result in resultList)
        {
            var itemTokens = tokenEstimator(result);
            
            // Check if adding this item would exceed the limit
            // Always include at least one result if possible
            if (currentTokens + itemTokens > maxTokens && truncatedResult.Results.Count > 0)
            {
                truncatedResult.IsTruncated = true;
                truncatedResult.TruncationReason = $"Response size limit reached ({maxTokens} tokens)";
                
                // Generate continuation token if needed
                if (truncatedResult.Results.Count < resultList.Count)
                {
                    truncatedResult.ContinuationToken = GenerateContinuationToken(
                        truncatedResult.Results.Count, 
                        resultList.Count);
                }
                
                break;
            }
            
            truncatedResult.Results.Add(result);
            currentTokens += itemTokens;
        }
        
        truncatedResult.ReturnedCount = truncatedResult.Results.Count;
        truncatedResult.EstimatedReturnedTokens = currentTokens;
        
        if (truncatedResult.IsTruncated)
        {
            _logger.LogInformation(
                "Truncated results: returned {Returned} of {Total} items, " +
                "{ReturnedTokens} of estimated {TotalTokens} tokens",
                truncatedResult.ReturnedCount,
                truncatedResult.TotalCount,
                truncatedResult.EstimatedReturnedTokens,
                truncatedResult.EstimatedTotalTokens);
        }
        
        return truncatedResult;
    }
    
    public TruncatedResult<T> TruncateByCount<T>(
        IEnumerable<T> results,
        int maxCount)
    {
        var resultList = results.ToList();
        var truncatedResult = new TruncatedResult<T>
        {
            TotalCount = resultList.Count,
            Results = resultList.Take(maxCount).ToList()
        };
        
        truncatedResult.ReturnedCount = truncatedResult.Results.Count;
        truncatedResult.IsTruncated = truncatedResult.TotalCount > truncatedResult.ReturnedCount;
        
        if (truncatedResult.IsTruncated)
        {
            truncatedResult.TruncationReason = $"Result count limit reached ({maxCount} items)";
            truncatedResult.ContinuationToken = GenerateContinuationToken(
                truncatedResult.ReturnedCount, 
                truncatedResult.TotalCount);
            
            _logger.LogInformation(
                "Truncated results by count: returned {Returned} of {Total} items",
                truncatedResult.ReturnedCount,
                truncatedResult.TotalCount);
        }
        
        return truncatedResult;
    }
    
    private string GenerateContinuationToken(int currentPosition, int totalCount)
    {
        // Simple continuation token encoding the position
        // In production, this might include additional context like sort order, filters, etc.
        var tokenData = new
        {
            offset = currentPosition,
            total = totalCount,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }
}