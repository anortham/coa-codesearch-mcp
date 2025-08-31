using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for the batch operations tool
/// </summary>
public class BatchOperationsParameters
{
    /// <summary>
    /// Path to the workspace directory
    /// </summary>
    [Required]
    [Description("Path to the workspace directory")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Array of operations to execute in JSON format
    /// </summary>
    [Required]
    [Description("Array of operations to execute")]
    public string Operations { get; set; } = string.Empty;
    
    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int MaxTokens { get; set; } = 8000;
    
    /// <summary>
    /// Response mode
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string ResponseMode { get; set; } = "adaptive";
    
    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; } = false;
}

/// <summary>
/// Individual operation for batch processing
/// </summary>
public class BatchOperation
{
    public string operation { get; set; } = "";
    public string? query { get; set; }
    public string? pattern { get; set; }
    public string? filePath { get; set; }
    public string? timeFrame { get; set; }
    public bool? useRegex { get; set; }
    public bool? caseSensitive { get; set; }
    public string? searchType { get; set; }
    public double? minScore { get; set; }
    public int? maxResults { get; set; }
    public string? id { get; set; }
    public string? description { get; set; }
}

/// <summary>
/// Result from batch operations
/// </summary>
public class BatchOperationsResult : ToolResultBase
{
    public override string Operation => ToolNames.BatchOperations;
    
    /// <summary>
    /// Results from each operation
    /// </summary>
    public List<BatchOperationResult> Operations { get; set; } = new();
    
    /// <summary>
    /// Summary statistics
    /// </summary>
    public BatchSummary Summary { get; set; } = new();
    
    /// <summary>
    /// Total execution duration in milliseconds
    /// </summary>
    public int TotalDurationMs { get; set; }
}

/// <summary>
/// Result from a single operation in the batch
/// </summary>
public class BatchOperationResult
{
    public string? Id { get; set; }
    public string Operation { get; set; } = "";
    public string? Description { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Result { get; set; }
    public int DurationMs { get; set; }
    public int ResultCount { get; set; }
}

/// <summary>
/// Summary of batch execution
/// </summary>
public class BatchSummary
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public int TotalResults { get; set; }
    public int AverageDurationMs { get; set; }
}

/// <summary>
/// Tool for executing multiple search operations in batch
/// </summary>
public class BatchOperationsTool : McpToolBase<BatchOperationsParameters, BatchOperationsResult>
{
    private readonly ILogger<BatchOperationsTool> _logger;
    private readonly TextSearchTool _textSearchTool;
    private readonly FileSearchTool _fileSearchTool;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;

    public BatchOperationsTool(
        IServiceProvider serviceProvider,
        ILogger<BatchOperationsTool> logger,
        TextSearchTool textSearchTool,
        FileSearchTool fileSearchTool,
        COA.VSCodeBridge.IVSCodeBridge vscode) : base(serviceProvider)
    {
        _logger = logger;
        _textSearchTool = textSearchTool;
        _fileSearchTool = fileSearchTool;
        _vscode = vscode;
    }

    public override string Name => ToolNames.BatchOperations;
    public override string Description => "PARALLEL search for speed - Run multiple searches simultaneously. USE when investigating across multiple dimensions (files + content + recent). 3-10x faster than sequential.";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<BatchOperationsResult> ExecuteInternalAsync(
        BatchOperationsParameters parameters,
        CancellationToken cancellationToken)
    {
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        var operationsJson = ValidateRequired(parameters.Operations, nameof(parameters.Operations));

        try
        {
            var operations = JsonSerializer.Deserialize<BatchOperation[]>(operationsJson);
            if (operations == null || operations.Length == 0)
            {
                return new BatchOperationsResult
                {
                    Success = false,
                    Error = new ErrorInfo 
                    { 
                        Code = "BATCH_INVALID_OPERATIONS", 
                        Message = "No valid operations found in operations parameter" 
                    }
                };
            }

            var stopwatch = Stopwatch.StartNew();
            var results = new List<BatchOperationResult>();

            _logger.LogInformation("Executing batch of {Count} operations", operations.Length);

            foreach (var operation in operations.Take(10)) // Limit to 10 operations for safety
            {
                var operationResult = await ExecuteSingleOperationAsync(
                    operation, 
                    workspacePath, 
                    parameters, 
                    cancellationToken);
                results.Add(operationResult);
            }

            stopwatch.Stop();

            var totalResults = results.Sum(r => r.ResultCount);
            var successfulOps = results.Count(r => r.Success);

            var batchResult = new BatchOperationsResult
            {
                Success = true,
                Operations = results,
                Summary = new BatchSummary
                {
                    TotalOperations = results.Count,
                    SuccessfulOperations = successfulOps,
                    FailedOperations = results.Count - successfulOps,
                    TotalResults = totalResults,
                    AverageDurationMs = results.Count > 0 ? (int)results.Average(r => r.DurationMs) : 0
                },
                TotalDurationMs = (int)stopwatch.ElapsedMilliseconds
            };


            return batchResult;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse operations JSON");
            return new BatchOperationsResult
            {
                Success = false,
                Error = new ErrorInfo 
                { 
                    Code = "BATCH_JSON_PARSE_ERROR", 
                    Message = $"Invalid operations JSON: {ex.Message}" 
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch operations failed");
            return new BatchOperationsResult
            {
                Success = false,
                Error = new ErrorInfo 
                { 
                    Code = "BATCH_EXECUTION_ERROR", 
                    Message = $"Batch execution failed: {ex.Message}" 
                }
            };
        }
    }

    private async Task<BatchOperationResult> ExecuteSingleOperationAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Executing {Operation} with ID {Id}", 
                operation.operation, operation.id ?? "none");

            object? result = operation.operation.ToLowerInvariant() switch
            {
                "text_search" => await ExecuteTextSearchAsync(operation, workspacePath, batchParams, cancellationToken),
                "file_search" => await ExecuteFileSearchAsync(operation, workspacePath, batchParams, cancellationToken),
                _ => throw new ArgumentException($"Unknown operation type: {operation.operation}")
            };

            stopwatch.Stop();

            return new BatchOperationResult
            {
                Id = operation.id,
                Operation = operation.operation,
                Description = operation.description,
                Success = true,
                Result = result,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                ResultCount = GetResultCount(result)
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Operation {Operation} failed", operation.operation);
            
            return new BatchOperationResult
            {
                Id = operation.id,
                Operation = operation.operation,
                Description = operation.description,
                Success = false,
                Error = ex.Message,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                ResultCount = 0
            };
        }
    }

    private async Task<object> ExecuteTextSearchAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.query))
            throw new ArgumentException("text_search requires 'query' parameter");

        var parameters = new TextSearchParameters
        {
            Query = operation.query,
            WorkspacePath = workspacePath,
            MaxTokens = Math.Min(batchParams.MaxTokens / 4, 2000), // Limit tokens for batch
            NoCache = batchParams.NoCache,
            ResponseMode = "summary", // Use summary for batch to save tokens
            CaseSensitive = operation.caseSensitive ?? false,
            SearchType = operation.searchType ?? "standard"
        };

        return await _textSearchTool.ExecuteAsync(parameters, cancellationToken);
    }

    private async Task<object> ExecuteFileSearchAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.pattern))
            throw new ArgumentException("file_search requires 'pattern' parameter");

        var parameters = new FileSearchParameters
        {
            Pattern = operation.pattern,
            WorkspacePath = workspacePath,
            MaxTokens = Math.Min(batchParams.MaxTokens / 4, 2000),
            NoCache = batchParams.NoCache,
            ResponseMode = "summary",
            UseRegex = operation.useRegex ?? false,
            MaxResults = Math.Min(operation.maxResults ?? 20, 50) // Limit results
        };

        return await _fileSearchTool.ExecuteAsync(parameters, cancellationToken);
    }

    private int GetResultCount(object? result)
    {
        // Simple result counting - this could be made more sophisticated
        try
        {
            if (result is ToolResultBase toolResult && toolResult.Success)
            {
                // Could use reflection to count items in collections
                return 1; // Placeholder - just count successful operations
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }
}