using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
/// Tool for executing multiple search operations in batch
/// </summary>
public class BatchOperationsTool : McpToolBase<BatchOperationsParameters, AIOptimizedResponse<BatchResult>>
{
    private readonly ILogger<BatchOperationsTool> _logger;
    private readonly TextSearchTool _textSearchTool;
    private readonly FileSearchTool _fileSearchTool;
    private readonly DirectorySearchTool _directorySearchTool;
    private readonly RecentFilesTool _recentFilesTool;
    private readonly SimilarFilesTool _similarFilesTool;
    private readonly BaseResponseBuilder<BatchResult, BatchResult> _responseBuilder;
    private readonly IResourceStorageService _storageService;

    public BatchOperationsTool(
        ILogger<BatchOperationsTool> logger,
        TextSearchTool textSearchTool,
        FileSearchTool fileSearchTool,
        DirectorySearchTool directorySearchTool,
        RecentFilesTool recentFilesTool,
        SimilarFilesTool similarFilesTool,
        IResourceStorageService storageService) : base(logger)
    {
        _logger = logger;
        _textSearchTool = textSearchTool;
        _fileSearchTool = fileSearchTool;
        _directorySearchTool = directorySearchTool;
        _recentFilesTool = recentFilesTool;
        _similarFilesTool = similarFilesTool;
        _storageService = storageService;
        _responseBuilder = new BaseResponseBuilder<BatchResult, BatchResult>(null, storageService);
    }

    public override string Name => ToolNames.BatchOperations;
    public override string Description => "Execute multiple search operations in batch for efficient bulk processing";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<BatchResult>> ExecuteInternalAsync(
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
                throw new ArgumentException("No valid operations found in operations parameter");
            }

            var stopwatch = Stopwatch.StartNew();
            var results = new List<BatchOperationResult>();

            _logger.LogInformation("Executing batch of {Count} operations", operations.Length);

            foreach (var operation in operations)
            {
                var operationResult = await ExecuteSingleOperationAsync(
                    operation, 
                    workspacePath, 
                    parameters, 
                    cancellationToken);
                results.Add(operationResult);
            }

            stopwatch.Stop();

            var batchResult = new BatchResult
            {
                Operations = results,
                Summary = new BatchSummary
                {
                    TotalOperations = operations.Length,
                    SuccessfulOperations = results.Count(r => r.Success),
                    FailedOperations = results.Count(r => !r.Success),
                    TotalDurationMs = (int)stopwatch.ElapsedMilliseconds
                }
            };

            return await _responseBuilder.BuildResponseAsync(
                new List<BatchResult> { batchResult },
                parameters.MaxTokens,
                parameters.ResponseMode,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse operations JSON");
            throw new ArgumentException($"Invalid operations JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch operations failed");
            throw;
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
                "directory_search" => await ExecuteDirectorySearchAsync(operation, workspacePath, batchParams, cancellationToken),
                "recent_files" => await ExecuteRecentFilesAsync(operation, workspacePath, batchParams, cancellationToken),
                "similar_files" => await ExecuteSimilarFilesAsync(operation, workspacePath, batchParams, cancellationToken),
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
                DurationMs = (int)stopwatch.ElapsedMilliseconds
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
                DurationMs = (int)stopwatch.ElapsedMilliseconds
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
            MaxTokens = batchParams.MaxTokens / 4, // Allocate portion of tokens
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
            MaxTokens = batchParams.MaxTokens / 4,
            NoCache = batchParams.NoCache,
            ResponseMode = "summary",
            UseRegex = operation.useRegex ?? false,
            MaxResults = operation.maxResults ?? 20
        };

        return await _fileSearchTool.ExecuteAsync(parameters, cancellationToken);
    }

    private async Task<object> ExecuteDirectorySearchAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.pattern))
            throw new ArgumentException("directory_search requires 'pattern' parameter");

        var parameters = new DirectorySearchParameters
        {
            Pattern = operation.pattern,
            WorkspacePath = workspacePath,
            MaxTokens = batchParams.MaxTokens / 4,
            NoCache = batchParams.NoCache,
            ResponseMode = "summary",
            UseRegex = operation.useRegex ?? false,
            MaxResults = operation.maxResults ?? 20
        };

        return await _directorySearchTool.ExecuteAsync(parameters, cancellationToken);
    }

    private async Task<object> ExecuteRecentFilesAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        var parameters = new RecentFilesParameters
        {
            WorkspacePath = workspacePath,
            MaxTokens = batchParams.MaxTokens / 4,
            NoCache = batchParams.NoCache,
            ResponseMode = "summary",
            TimeFrame = operation.timeFrame ?? "7d",
            MaxResults = operation.maxResults ?? 20
        };

        return await _recentFilesTool.ExecuteAsync(parameters, cancellationToken);
    }

    private async Task<object> ExecuteSimilarFilesAsync(
        BatchOperation operation,
        string workspacePath,
        BatchOperationsParameters batchParams,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.filePath))
            throw new ArgumentException("similar_files requires 'filePath' parameter");

        var parameters = new SimilarFilesParameters
        {
            FilePath = operation.filePath,
            WorkspacePath = workspacePath,
            MaxTokens = batchParams.MaxTokens / 4,
            NoCache = batchParams.NoCache,
            ResponseMode = "summary",
            MinScore = operation.minScore ?? 0.1,
            MaxResults = operation.maxResults ?? 10
        };

        return await _similarFilesTool.ExecuteAsync(parameters, cancellationToken);
    }
}

/// <summary>
/// Result from batch operations
/// </summary>
public class BatchResult
{
    public List<BatchOperationResult> Operations { get; set; } = new();
    public BatchSummary Summary { get; set; } = new();
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
}

/// <summary>
/// Summary of batch execution
/// </summary>
public class BatchSummary
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public int TotalDurationMs { get; set; }
}