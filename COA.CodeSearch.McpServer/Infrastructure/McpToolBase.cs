using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using COA.CodeSearch.McpServer.Tools;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Base class for MCP tools with built-in token limit handling
/// </summary>
public abstract class McpToolBase : ITool
{
    // ITool implementation
    public abstract string ToolName { get; }
    public abstract string Description { get; }
    public abstract ToolCategory Category { get; }
    
    protected IResponseSizeEstimator SizeEstimator { get; }
    protected IResultTruncator Truncator { get; }
    protected ResponseLimitOptions Options { get; }
    protected ILogger Logger { get; }
    
    private readonly string _toolName;
    
    protected McpToolBase(
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        ILogger logger)
    {
        SizeEstimator = sizeEstimator;
        Truncator = truncator;
        Options = options.Value;
        Logger = logger;
        _toolName = GetType().Name;
    }
    
    /// <summary>
    /// Creates a response with automatic size checking
    /// </summary>
    protected Task<McpToolResponse<T>> CreateResponseAsync<T>(
        T data, 
        int totalResults,
        CancellationToken cancellationToken)
    {
        var response = new McpToolResponse<T>
        {
            Success = true,
            Data = data,
            Metadata = new ResponseMetadata
            {
                TotalResults = totalResults,
                ReturnedResults = totalResults
            }
        };
        
        // Estimate response size
        var estimatedTokens = SizeEstimator.EstimateTokens(response);
        response.Metadata.EstimatedTokens = estimatedTokens;
        
        // Get tool-specific token limit
        var maxTokens = Options.GetTokenLimitForTool(_toolName);
        
        // Log if approaching limits
        if (estimatedTokens > maxTokens * Options.SafetyMargin)
        {
            Logger.LogWarning(
                "{Tool}: Response size ({EstimatedTokens} tokens) exceeds safety threshold ({Threshold} tokens)",
                _toolName,
                estimatedTokens,
                (int)(maxTokens * Options.SafetyMargin));
        }
        
        // Log token usage if enabled
        if (Options.EnableTokenUsageLogging)
        {
            Logger.LogInformation(
                "{Tool}: Response size {EstimatedTokens} tokens, {Percentage}% of limit",
                _toolName,
                estimatedTokens,
                (estimatedTokens * 100) / maxTokens);
        }
        
        return Task.FromResult(response);
    }
    
    /// <summary>
    /// Creates an error response
    /// </summary>
    protected McpToolResponse<T> CreateErrorResponse<T>(string error)
    {
        Logger.LogError("{Tool}: Error response - {Error}", _toolName, error);
        
        return new McpToolResponse<T>
        {
            Success = false,
            Error = error,
            Metadata = new ResponseMetadata()
        };
    }
    
    /// <summary>
    /// Creates a response from truncated results
    /// </summary>
    protected McpToolResponse<List<T>> CreateTruncatedResponse<T>(
        TruncatedResult<T> truncatedResult)
    {
        var response = new McpToolResponse<List<T>>
        {
            Success = true,
            Data = truncatedResult.Results,
            Metadata = new ResponseMetadata
            {
                TotalResults = truncatedResult.TotalCount,
                ReturnedResults = truncatedResult.ReturnedCount,
                IsTruncated = truncatedResult.IsTruncated,
                TruncationReason = truncatedResult.TruncationReason,
                ContinuationToken = truncatedResult.ContinuationToken,
                EstimatedTokens = truncatedResult.EstimatedReturnedTokens
            }
        };
        
        if (truncatedResult.IsTruncated)
        {
            Logger.LogInformation(
                "{Tool}: Response truncated - returned {Returned} of {Total} results",
                _toolName,
                truncatedResult.ReturnedCount,
                truncatedResult.TotalCount);
        }
        
        return response;
    }
    
    /// <summary>
    /// Applies pagination parameters to a result set
    /// </summary>
    protected List<T> ApplyPagination<T>(
        List<T> results,
        PaginationParams? pagination)
    {
        if (pagination == null)
        {
            return results;
        }
        
        var skip = pagination.Offset ?? 0;
        var take = pagination.MaxResults ?? Options.GetResultLimitForTool(_toolName);
        
        return results.Skip(skip).Take(take).ToList();
    }
    
    /// <summary>
    /// Gets the maximum token limit for this tool
    /// </summary>
    protected int GetMaxTokens()
    {
        return Options.GetTokenLimitForTool(_toolName);
    }
    
    /// <summary>
    /// Gets the default result limit for this tool
    /// </summary>
    protected int GetDefaultResultLimit()
    {
        return Options.GetResultLimitForTool(_toolName);
    }
}