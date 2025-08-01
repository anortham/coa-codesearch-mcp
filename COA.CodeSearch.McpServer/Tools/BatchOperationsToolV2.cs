using System.Text.Json;
using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized batch operations for text search and file analysis tools
/// </summary>
[McpServerToolType]
public class BatchOperationsToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "batch_operations_v2";
    public override string Description => "AI-optimized batch operations for text search and file analysis";
    public override ToolCategory Category => ToolCategory.Batch;
    private readonly IConfiguration _configuration;
    private readonly INotificationService? _notificationService;
    private readonly AIResponseBuilderService _responseBuilder;
    
    // Available tools for batch operations
    private readonly FastTextSearchToolV2 _fastTextSearchToolV2;
    private readonly FastFileSearchToolV2 _fastFileSearchToolV2;
    private readonly FastRecentFilesTool _fastRecentFilesTool;
    private readonly FastFileSizeAnalysisTool _fastFileSizeAnalysisTool;
    private readonly FastSimilarFilesTool _fastSimilarFilesTool;
    private readonly FastDirectorySearchTool _fastDirectorySearchTool;

    public BatchOperationsToolV2(
        ILogger<BatchOperationsToolV2> logger,
        IConfiguration configuration,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache,
        INotificationService? notificationService,
        AIResponseBuilderService responseBuilder,
        FastTextSearchToolV2 fastTextSearchToolV2,
        FastFileSearchToolV2 fastFileSearchToolV2,
        FastRecentFilesTool fastRecentFilesTool,
        FastFileSizeAnalysisTool fastFileSizeAnalysisTool,
        FastSimilarFilesTool fastSimilarFilesTool,
        FastDirectorySearchTool fastDirectorySearchTool)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _configuration = configuration;
        _notificationService = notificationService;
        _responseBuilder = responseBuilder;
        _fastTextSearchToolV2 = fastTextSearchToolV2;
        _fastFileSearchToolV2 = fastFileSearchToolV2;
        _fastRecentFilesTool = fastRecentFilesTool;
        _fastFileSizeAnalysisTool = fastFileSizeAnalysisTool;
        _fastSimilarFilesTool = fastSimilarFilesTool;
        _fastDirectorySearchTool = fastDirectorySearchTool;
    }

    /// <summary>
    /// Attribute-based ExecuteAsync method for MCP registration
    /// </summary>
    [McpServerTool(Name = "batch_operations")]
    [Description("Execute multiple code analysis operations in parallel for comprehensive insights. Combines results across different analysis types, identifies patterns, and suggests next steps. Faster than running operations sequentially. Supported: text_search, file_search, recent_files, similar_files, directory_search.")]
    public async Task<object> ExecuteAsync(BatchOperationsV2Params parameters)
    {
        if (parameters == null) throw new InvalidParametersException("Parameters are required");
        
        var operations = parameters.Operations;
        
        // Handle case where operations comes as a JSON string that needs parsing
        if (operations.ValueKind == JsonValueKind.String)
        {
            var jsonString = operations.GetString();
            if (string.IsNullOrWhiteSpace(jsonString))
                throw new InvalidParametersException("operations are required and cannot be empty");
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                operations = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new InvalidParametersException($"Invalid JSON in operations parameter: {ex.Message}");
            }
        }
        
        if (operations.ValueKind == JsonValueKind.Undefined || 
            (operations.ValueKind == JsonValueKind.Array && operations.GetArrayLength() == 0))
            throw new InvalidParametersException("operations are required and cannot be empty");
        
        return await ExecuteAsync(
            operations,
            parameters.WorkspacePath,
            Enum.TryParse<ResponseMode>(parameters.Mode, true, out var mode) ? mode : ResponseMode.Summary,
            parameters.DetailRequest,
            CancellationToken.None);
    }

    public async Task<object> ExecuteAsync(
        JsonElement operations,
        string? workspacePath = null,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            var operationCount = operations.GetArrayLength();
            Logger.LogInformation("BatchOperationsV2 request with {Count} operations", operationCount);

            var results = new List<object>();
            var progressToken = $"batch-operations-{Guid.NewGuid():N}";

            // Send initial progress notification
            await SendProgressNotification(progressToken, 0, operationCount, "Starting batch operations...");

            // Execute all operations in parallel
            var operationArray = operations.EnumerateArray().ToArray();
            var operationTasks = new List<Task<object>>();
            
            for (int i = 0; i < operationArray.Length; i++)
            {
                var operation = operationArray[i];
                var index = i; // Capture index for closure
                
                operationTasks.Add(ExecuteOperationWithIndexAsync(operation, workspacePath, progressToken, operationCount, index, cancellationToken));
            }

            // Wait for all operations to complete in parallel
            var operationResults = await Task.WhenAll(operationTasks);

            // Sort results back to original order and add to results list
            results.AddRange(operationResults.OrderBy(r => GetOperationIndex(r)));

            // Create batch result model
            var batchResult = new BatchOperationResult
            {
                Operations = results.Select((r, i) => ConvertToBatchOperationEntry(r, i)).ToList(),
                TotalExecutionTime = 100 * operationCount // Estimate in milliseconds
            };

            // Create request model for response builder
            var batchRequest = new BatchOperationRequest
            {
                Operations = operationArray.Select(op => 
                {
                    var opType = op.GetProperty("operation").GetString() ?? op.GetProperty("type").GetString() ?? "unknown";
                    var parameters = new Dictionary<string, object>();
                    
                    // Extract all properties except 'operation' and 'type' as parameters
                    foreach (var prop in op.EnumerateObject())
                    {
                        if (prop.Name != "operation" && prop.Name != "type")
                        {
                            parameters[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                                JsonValueKind.Number => prop.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Array => prop.Value.ToString(),
                                JsonValueKind.Object => prop.Value.ToString(),
                                _ => prop.Value.ToString()
                            };
                        }
                    }
                    
                    return new BatchOperationDefinition
                    {
                        Operation = opType,
                        OperationType = opType,
                        Parameters = parameters
                    };
                }).ToList(),
                DefaultWorkspacePath = workspacePath
            };

            // Use AIResponseBuilderService to build the response
            return _responseBuilder.BuildBatchOperationsResponse(batchRequest, batchResult, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in BatchOperationsV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<object> ExecuteOperationAsync(
        JsonElement operation,
        string? defaultWorkspacePath,
        CancellationToken cancellationToken)
    {
        var operationType = operation.GetProperty("operation").GetString() 
            ?? operation.GetProperty("type").GetString() 
            ?? throw new InvalidOperationException("Operation must have 'operation' or 'type' property");

        return operationType switch
        {
            ToolNames.TextSearch => await ExecuteTextSearchAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.FileSearch => await ExecuteFileSearchAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.RecentFiles => await ExecuteRecentFilesAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.FileSizeAnalysis => await ExecuteFileSizeAnalysisAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.SimilarFiles => await ExecuteSimilarFilesAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.DirectorySearch => await ExecuteDirectorySearchAsync(operation, defaultWorkspacePath, cancellationToken),
            _ => throw new NotSupportedException($"Operation type '{operationType}' not supported in batch operations")
        };
    }

    private async Task SendProgressNotification(string token, int current, int total, string message)
    {
        if (_notificationService != null)
        {
            var percentage = total > 0 ? (int)((current * 100.0) / total) : 0;
            await _notificationService.SendProgressAsync(token, percentage, total, message);
        }
    }

    private static bool GetSuccess(object result)
    {
        if (result is JsonElement json)
        {
            return json.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        
        var resultJson = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(resultJson);
        return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
    }

    private static int GetOperationIndex(object result)
    {
        var resultJson = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(resultJson);
        return doc.RootElement.TryGetProperty("index", out var index) ? index.GetInt32() : 0;
    }

    private BatchOperationEntry ConvertToBatchOperationEntry(object result, int index)
    {
        dynamic d = result;
        
        // Handle both success and error cases
        string? error = null;
        object? resultData = null;
        
        try
        {
            // Try to get error property if it exists (for failed operations)
            error = d.error?.ToString();
        }
        catch
        {
            // Property doesn't exist, which is fine for successful operations
        }
        
        try
        {
            // Try to get result property if it exists (for successful operations)
            resultData = d.result;
        }
        catch
        {
            // Property doesn't exist, which is fine for failed operations
        }
        
        return new BatchOperationEntry
        {
            OperationType = d.operation?.ToString() ?? "unknown",
            Success = d.success ?? false,
            Result = resultData,
            Error = error,
            Index = index
        };
    }

    private async Task<object> ExecuteOperationWithIndexAsync(
        JsonElement operation, 
        string? workspacePath, 
        string progressToken, 
        int operationCount, 
        int index, 
        CancellationToken cancellationToken)
    {
        var operationType = operation.GetProperty("operation").GetString() 
            ?? operation.GetProperty("type").GetString() 
            ?? throw new InvalidOperationException("Operation must have 'operation' or 'type' property");

        await SendProgressNotification(progressToken, index + 1, operationCount, 
            $"Executing {operationType}...");

        try
        {
            var operationResult = await ExecuteOperationAsync(operation, workspacePath, cancellationToken);
            return new
            {
                success = true,
                operation = operationType,
                result = operationResult,
                index = index // Preserve original order
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing operation {Type}", operationType);
            return new
            {
                success = false,
                operation = operationType,
                error = ex.Message,
                index = index // Preserve original order
            };
        }
    }

    // Individual operation execution methods
    
    private async Task<object> ExecuteTextSearchAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        if (!operation.TryGetProperty("query", out var queryProp))
        {
            throw new InvalidOperationException("text_search operation requires 'query'");
        }

        return await _fastTextSearchToolV2.ExecuteAsync(
            queryProp.GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("filePattern", out var fp) ? fp.GetString() : null,
            operation.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                ? ext.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            operation.TryGetProperty("contextLines", out var cl) ? cl.GetInt32() : 0,
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 50,
            operation.TryGetProperty("caseSensitive", out var cs) && cs.GetBoolean(),
            operation.TryGetProperty("searchType", out var st) ? st.GetString() ?? "standard" : "standard",
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteFileSearchAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        if (!operation.TryGetProperty("query", out var queryProp))
        {
            throw new InvalidOperationException("file_search operation requires 'query'");
        }

        return await _fastFileSearchToolV2.ExecuteAsync(
            queryProp.GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("searchType", out var st) ? st.GetString() ?? "standard" : "standard",
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 50,
            operation.TryGetProperty("includeDirectories", out var id) && id.GetBoolean(),
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteRecentFilesAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        return await _fastRecentFilesTool.ExecuteAsync(
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("timeFrame", out var tf) ? tf.GetString() ?? "24h" : "24h",
            operation.TryGetProperty("filePattern", out var fp) ? fp.GetString() : null,
            operation.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                ? ext.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 50,
            operation.TryGetProperty("includeSize", out var is_) ? is_.GetBoolean() : true,
            cancellationToken);
    }

    private async Task<object> ExecuteFileSizeAnalysisAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        return await _fastFileSizeAnalysisTool.ExecuteAsync(
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "largest" : "largest",
            operation.TryGetProperty("minSize", out var minS) ? minS.GetInt64() : null,
            operation.TryGetProperty("maxSize", out var maxS) ? maxS.GetInt64() : null,
            operation.TryGetProperty("filePattern", out var fp) ? fp.GetString() : null,
            operation.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                ? ext.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 50,
            operation.TryGetProperty("includeAnalysis", out var ia) ? ia.GetBoolean() : true,
            cancellationToken);
    }

    private async Task<object> ExecuteSimilarFilesAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        if (!operation.TryGetProperty("sourcePath", out var sourceFilePathProp))
        {
            throw new InvalidOperationException("similar_files operation requires 'sourcePath'");
        }

        return await _fastSimilarFilesTool.ExecuteAsync(
            sourceFilePathProp.GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 10,
            operation.TryGetProperty("minTermFreq", out var mtf) ? mtf.GetInt32() : 2,
            operation.TryGetProperty("minDocFreq", out var mdf) ? mdf.GetInt32() : 2,
            operation.TryGetProperty("minWordLength", out var minWL) ? minWL.GetInt32() : 4,
            operation.TryGetProperty("maxWordLength", out var maxWL) ? maxWL.GetInt32() : 30,
            operation.TryGetProperty("excludeExtensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                ? ext.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            operation.TryGetProperty("includeScore", out var is_) ? is_.GetBoolean() : true,
            cancellationToken);
    }

    private async Task<object> ExecuteDirectorySearchAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        if (!operation.TryGetProperty("query", out var queryProp))
        {
            throw new InvalidOperationException("directory_search operation requires 'query'");
        }

        return await _fastDirectorySearchTool.ExecuteAsync(
            queryProp.GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("searchType", out var st) ? st.GetString() ?? "standard" : "standard",
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 30,
            operation.TryGetProperty("includeFileCount", out var ifc) ? ifc.GetBoolean() : true,
            operation.TryGetProperty("groupByDirectory", out var gbd) ? gbd.GetBoolean() : true,
            cancellationToken);
    }

    private string GetWorkspacePath(JsonElement operation, string? defaultWorkspacePath)
    {
        // First check if the operation has its own workspacePath
        if (operation.TryGetProperty("workspacePath", out var wsPath) && wsPath.ValueKind == JsonValueKind.String)
        {
            return wsPath.GetString()!;
        }

        // Otherwise use the default
        if (string.IsNullOrEmpty(defaultWorkspacePath))
        {
            throw new InvalidOperationException("Operation requires workspacePath but none was provided");
        }

        return defaultWorkspacePath;
    }

    private object CreateAiOptimizedResponse(
        JsonElement operations,
        JsonElement result,
        ResponseMode mode)
    {
        // Analyze the batch results
        var analysis = AnalyzeBatchResults(operations, result);

        // Generate insights
        var insights = GenerateBatchInsights(analysis);

        // Generate actions
        var actions = GenerateBatchActions(analysis);

        // Prepare operation results based on mode
        var operationResults = mode == ResponseMode.Full
            ? PrepareFullResults(result.GetProperty("results"))
            : PrepareSummaryResults(result.GetProperty("results"), analysis);

        // Create response
        return new
        {
            success = true,
            operation = ToolNames.BatchOperations,
            batch = new
            {
                totalOperations = analysis.TotalOperations,
                successCount = analysis.SuccessCount,
                failureCount = analysis.FailureCount,
                operationTypes = analysis.OperationTypeCounts.ToDictionary(kv => kv.Key, kv => kv.Value)
            },
            summary = new
            {
                executionTime = analysis.EstimatedExecutionTime,
                successRate = analysis.SuccessRate,
                topOperations = analysis.OperationTypeCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => new { type = kv.Key, count = kv.Value })
                    .ToList(),
                errorSummary = analysis.ErrorSummary.Any() ? analysis.ErrorSummary : null
            },
            analysis = new
            {
                patterns = analysis.Patterns.Take(3).ToList(),
                hotspots = analysis.FileReferences.Any() ? new
                {
                    byFile = analysis.FileReferences
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => new { file = kv.Key, operations = kv.Value })
                        .ToList()
                } : null,
                correlations = analysis.Correlations.Take(3).ToList()
            },
            results = operationResults,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                avgTokensPerOp = analysis.AverageTokensPerOperation,
                totalTokens = EstimateResponseTokens(analysis),
                cached = $"batch_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private BatchAnalysis AnalyzeBatchResults(JsonElement operations, JsonElement result)
    {
        var analysis = new BatchAnalysis();
        var results = result.GetProperty("results");
        
        analysis.TotalOperations = results.GetArrayLength();

        foreach (var opResult in results.EnumerateArray())
        {
            var operationType = opResult.GetProperty("operation").GetString() ?? "unknown";
            var success = opResult.GetProperty("success").GetBoolean();

            // Count operation types
            if (!analysis.OperationTypeCounts.ContainsKey(operationType))
                analysis.OperationTypeCounts[operationType] = 0;
            analysis.OperationTypeCounts[operationType]++;

            // Count success/failure
            if (success)
            {
                analysis.SuccessCount++;
            }
            else
            {
                analysis.FailureCount++;
                var error = opResult.GetProperty("error").GetString() ?? "Unknown error";
                if (!analysis.ErrorSummary.ContainsKey(error))
                    analysis.ErrorSummary[error] = 0;
                analysis.ErrorSummary[error]++;
            }

            // Extract file references
            if (success && opResult.TryGetProperty("result", out var resultData))
            {
                ExtractReferences(resultData, operationType, analysis);
            }
        }

        // Calculate success rate
        analysis.SuccessRate = analysis.TotalOperations > 0 
            ? (double)analysis.SuccessCount / analysis.TotalOperations 
            : 0.0;

        // Estimate execution time (heuristic based on operation types)
        analysis.EstimatedExecutionTime = EstimateExecutionTime(analysis.OperationTypeCounts);

        // Detect patterns
        DetectBatchPatterns(operations, analysis);

        // Find correlations
        FindCorrelations(analysis);

        return analysis;
    }

    private void ExtractReferences(JsonElement resultData, string operationType, BatchAnalysis analysis)
    {
        // Extract file references from search results
        if (resultData.ValueKind == JsonValueKind.Object)
        {
            // Check for files array in search results
            if (resultData.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in files.EnumerateArray())
                {
                    string? filePath = null;
                    if (file.ValueKind == JsonValueKind.String)
                    {
                        filePath = file.GetString();
                    }
                    else if (file.ValueKind == JsonValueKind.Object && file.TryGetProperty("path", out var path))
                    {
                        filePath = path.GetString();
                    }

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        if (!analysis.FileReferences.ContainsKey(filePath))
                            analysis.FileReferences[filePath] = 0;
                        analysis.FileReferences[filePath]++;
                    }
                }
            }

            // Check for totalMatches or similar count properties
            if (resultData.TryGetProperty("totalMatches", out var totalMatches))
            {
                analysis.TotalResultItems += totalMatches.GetInt32();
            }
            else if (resultData.TryGetProperty("count", out var count))
            {
                analysis.TotalResultItems += count.GetInt32();
            }
        }
        else if (resultData.ValueKind == JsonValueKind.Array)
        {
            // For array results, count items
            analysis.TotalResultItems += resultData.GetArrayLength();
        }
    }

    private void DetectBatchPatterns(JsonElement operations, BatchAnalysis analysis)
    {
        var opArray = operations.EnumerateArray().ToList();

        // Pattern: Multiple search operations
        var searchOps = analysis.OperationTypeCounts.Where(kv => 
            kv.Key.Contains("search", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        if (searchOps > 2)
        {
            analysis.Patterns.Add($"Comprehensive search strategy: {searchOps} search operations");
        }

        // Pattern: File analysis workflow
        var fileAnalysisOps = analysis.OperationTypeCounts.Where(kv => 
            kv.Key.Contains("file", StringComparison.OrdinalIgnoreCase) || 
            kv.Key.Contains("recent", StringComparison.OrdinalIgnoreCase) ||
            kv.Key.Contains("similar", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        if (fileAnalysisOps > 1)
        {
            analysis.Patterns.Add($"File discovery and analysis workflow");
        }

        // Pattern: Size and structure analysis
        if (analysis.OperationTypeCounts.ContainsKey(ToolNames.FileSizeAnalysis) && 
            analysis.OperationTypeCounts.ContainsKey(ToolNames.DirectorySearch))
        {
            analysis.Patterns.Add("Project structure analysis pattern");
        }
    }

    private void FindCorrelations(BatchAnalysis analysis)
    {
        if (analysis.FileReferences.Count > 5 && analysis.OperationTypeCounts.Count > 2)
        {
            analysis.Correlations.Add("Multiple search types across many files - comprehensive discovery");
        }

        if (analysis.FailureCount > 0 && analysis.FailureCount < analysis.TotalOperations / 2)
        {
            analysis.Correlations.Add($"Partial failures ({analysis.FailureCount}/{analysis.TotalOperations}) - some search targets may be invalid");
        }

        if (analysis.TotalResultItems > 100)
        {
            analysis.Correlations.Add("High result volume - consider refining search criteria");
        }
    }

    private string EstimateExecutionTime(Dictionary<string, int> operationTypeCounts)
    {
        // Rough estimates in milliseconds for text search operations
        var timeEstimates = new Dictionary<string, int>
        {
            [ToolNames.TextSearch] = 50,
            [ToolNames.FileSearch] = 30,
            [ToolNames.RecentFiles] = 25,
            [ToolNames.FileSizeAnalysis] = 40,
            [ToolNames.SimilarFiles] = 100,
            [ToolNames.DirectorySearch] = 20
        };

        var totalMs = operationTypeCounts.Sum(kv => 
            timeEstimates.GetValueOrDefault(kv.Key, 50) * kv.Value);

        if (totalMs < 1000)
            return $"{totalMs}ms";
        else if (totalMs < 60000)
            return $"{totalMs / 1000.0:F1}s";
        else
            return $"{totalMs / 60000.0:F1}m";
    }

    private List<object> PrepareSummaryResults(JsonElement results, BatchAnalysis analysis)
    {
        var summaryResults = new List<object>();
        var resultsByType = new Dictionary<string, List<JsonElement>>();

        // Group results by operation type
        foreach (var result in results.EnumerateArray())
        {
            var opType = result.GetProperty("operation").GetString() ?? "unknown";
            if (!resultsByType.ContainsKey(opType))
                resultsByType[opType] = new List<JsonElement>();
            resultsByType[opType].Add(result);
        }

        // Create summary for each operation type
        foreach (var (opType, opResults) in resultsByType)
        {
            var successCount = opResults.Count(r => r.GetProperty("success").GetBoolean());
            var failureCount = opResults.Count - successCount;

            var summary = new
            {
                operation = opType,
                count = opResults.Count,
                success = successCount,
                failures = failureCount,
                examples = opResults.Take(2).Select(r => new
                {
                    success = r.GetProperty("success").GetBoolean(),
                    summary = CreateOperationSummary(r)
                }).ToList()
            };

            summaryResults.Add(summary);
        }

        return summaryResults;
    }

    private object CreateOperationSummary(JsonElement result)
    {
        if (!result.GetProperty("success").GetBoolean())
        {
            return new { error = result.GetProperty("error").GetString() };
        }

        var opType = result.GetProperty("operation").GetString();
        var resultData = result.GetProperty("result");

        // Create operation-specific summaries
        return opType switch
        {
            ToolNames.TextSearch => new 
            { 
                matches = resultData.TryGetProperty("totalMatches", out var tm) ? tm.GetInt32() : 0,
                files = resultData.TryGetProperty("files", out var files) ? files.GetArrayLength() : 0
            },
            ToolNames.FileSearch => new 
            { 
                found = resultData.TryGetProperty("files", out var files) ? files.GetArrayLength() : 0 
            },
            ToolNames.RecentFiles => new 
            { 
                found = resultData.TryGetProperty("files", out var files) ? files.GetArrayLength() : 0 
            },
            ToolNames.FileSizeAnalysis => new 
            { 
                analyzed = resultData.TryGetProperty("totalFiles", out var tf) ? tf.GetInt32() : 0 
            },
            ToolNames.SimilarFiles => new 
            { 
                found = resultData.TryGetProperty("similarFiles", out var sf) ? sf.GetArrayLength() : 0 
            },
            ToolNames.DirectorySearch => new 
            { 
                found = resultData.TryGetProperty("directories", out var dirs) ? dirs.GetArrayLength() : 0 
            },
            _ => new { hasResult = true }
        };
    }

    private List<object> PrepareFullResults(JsonElement results)
    {
        // In full mode, return all results but with some structure
        return results.EnumerateArray().Select(r => new
        {
            operation = r.GetProperty("operation").GetString(),
            success = r.GetProperty("success").GetBoolean(),
            result = r.TryGetProperty("result", out var res) ? (object?)res : null,
            error = r.TryGetProperty("error", out var err) ? err.GetString() : null
        }).ToList<object>();
    }

    private List<string> GenerateBatchInsights(BatchAnalysis analysis)
    {
        var insights = new List<string>();

        // Success rate insight
        if (analysis.SuccessRate < 1.0)
        {
            insights.Add($"{analysis.FailureCount} operations failed ({(1 - analysis.SuccessRate) * 100:F0}%)");
            if (analysis.ErrorSummary.Any())
            {
                var topError = analysis.ErrorSummary.OrderByDescending(kv => kv.Value).First();
                insights.Add($"Most common error: {topError.Key}");
            }
        }
        else
        {
            insights.Add($"All {analysis.TotalOperations} operations completed successfully");
        }

        // Operation distribution insight
        if (analysis.OperationTypeCounts.Count > 1)
        {
            var topOp = analysis.OperationTypeCounts.OrderByDescending(kv => kv.Value).First();
            insights.Add($"Primary focus: {topOp.Key} ({topOp.Value} operations)");
        }

        // File hotspot insight
        if (analysis.FileReferences.Any())
        {
            var fileCount = analysis.FileReferences.Count;
            var totalFileOps = analysis.FileReferences.Sum(kv => kv.Value);
            insights.Add($"Discovered {fileCount} files across {totalFileOps} operations");
        }

        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }

        // Performance insight
        insights.Add($"Estimated execution time: {analysis.EstimatedExecutionTime}");

        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            insights.Add($"Batch execution completed: {analysis.TotalOperations} search operations processed");
        }

        return insights;
    }

    private List<object> GenerateBatchActions(BatchAnalysis analysis)
    {
        var actions = new List<object>();

        // Retry failed operations
        if (analysis.FailureCount > 0)
        {
            actions.Add(new
            {
                id = "retry_failures",
                cmd = new { filterBy = "failed", maxRetries = 1 },
                tokens = analysis.FailureCount * 200,
                priority = "recommended"
            });
        }

        // Expand search scope
        if (analysis.TotalResultItems < 10 && analysis.OperationTypeCounts.ContainsKey(ToolNames.TextSearch))
        {
            actions.Add(new
            {
                id = "expand_search",
                cmd = new { broaderTerms = true, includeComments = true },
                tokens = 1500,
                priority = "recommended"
            });
        }

        // Analyze discovered files
        if (analysis.FileReferences.Count > 5)
        {
            actions.Add(new
            {
                id = "analyze_files",
                cmd = new { operations = new[] { ToolNames.FileSizeAnalysis, ToolNames.SimilarFiles } },
                tokens = 2000,
                priority = "available"
            });
        }

        // Refine search criteria
        if (analysis.TotalResultItems > 100)
        {
            actions.Add(new
            {
                id = "refine_search",
                cmd = new { addFilters = true, reduceScope = true },
                tokens = 1000,
                priority = "recommended"
            });
        }

        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            actions.Add(new
            {
                id = "view_results",
                cmd = new { showDetails = true },
                tokens = 1000,
                priority = "available"
            });
        }

        return actions;
    }

    private int EstimateResponseTokens(BatchAnalysis analysis)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per operation tokens (reduced for simpler operations)
        var perOpTokens = 80;
        
        // Additional tokens for file references
        var fileTokens = analysis.FileReferences.Count * 15;
        
        return baseTokens + (analysis.TotalOperations * perOpTokens) + fileTokens;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for batch operations"));
    }

    protected override int GetTotalResults<T>(T data)
    {
        // For batch operations, return the number of operations
        if (data is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("totalOperations", out var total))
            {
                return total.GetInt32();
            }
        }
        return 0;
    }

    private class BatchAnalysis
    {
        public int TotalOperations { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double SuccessRate { get; set; }
        public string EstimatedExecutionTime { get; set; } = "0ms";
        public int TotalResultItems { get; set; }
        public int AverageTokensPerOperation => TotalOperations > 0 ? 150 : 0; // Reduced for simpler operations
        
        public Dictionary<string, int> OperationTypeCounts { get; set; } = new();
        public Dictionary<string, int> ErrorSummary { get; set; } = new();
        public Dictionary<string, int> FileReferences { get; set; } = new();
        public List<string> Patterns { get; set; } = new();
        public List<string> Correlations { get; set; } = new();
    }
}

/// <summary>
/// Parameters for BatchOperationsV2 tool
/// </summary>
public class BatchOperationsV2Params
{
    [Description("Array of operations to execute. Format: [{\"operation\": \"text_search\", \"query\": \"TODO\"}, {\"operation\": \"file_search\", \"query\": \"*.cs\"}]. Each operation must have 'operation' field plus operation-specific parameters.")]
    public JsonElement Operations { get; set; }
    
    [Description("Default workspace path for operations")]
    public string? WorkspacePath { get; set; }
    
    [Description("Response mode: 'summary' (default) or 'full'")]
    public string? Mode { get; set; } = "summary";
    
    [Description(@"Request more details from a previous summary response.
Example: After getting a summary with 150 results, use the provided 
detailRequestToken to get full results.

Usage:
1. First call returns summary with metadata.detailRequestToken
2. Second call with detailRequest gets additional data")]
    public DetailRequest? DetailRequest { get; set; }
}