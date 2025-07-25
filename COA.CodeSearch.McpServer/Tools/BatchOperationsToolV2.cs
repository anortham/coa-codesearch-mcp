using System.Text.Json;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of BatchOperationsTool with structured response format
/// </summary>
public class BatchOperationsToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "batch_operations_v2";
    public override string Description => "AI-optimized batch operations";
    public override ToolCategory Category => ToolCategory.Batch;
    private readonly IConfiguration _configuration;
    private readonly INotificationService? _notificationService;
    
    // V2 tools
    private readonly SearchSymbolsToolV2 _searchSymbolsToolV2;
    private readonly FindReferencesToolV2 _findReferencesToolV2;
    private readonly GetImplementationsToolV2 _getImplementationsToolV2;
    private readonly GetCallHierarchyToolV2 _getCallHierarchyToolV2;
    private readonly FastTextSearchToolV2 _fastTextSearchToolV2;
    
    // V1 tools (no V2 available yet)
    private readonly GoToDefinitionTool _goToDefinitionTool;
    private readonly GetHoverInfoTool _getHoverInfoTool;
    private readonly GetDocumentSymbolsTool _getDocumentSymbolsTool;
    
    // Already V2 (registered without _v2 suffix)
    private readonly GetDiagnosticsToolV2 _getDiagnosticsToolV2;
    private readonly DependencyAnalysisToolV2 _dependencyAnalysisToolV2;

    public BatchOperationsToolV2(
        ILogger<BatchOperationsToolV2> logger,
        IConfiguration configuration,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache,
        INotificationService? notificationService,
        // V2 tools
        SearchSymbolsToolV2 searchSymbolsToolV2,
        FindReferencesToolV2 findReferencesToolV2,
        GetImplementationsToolV2 getImplementationsToolV2,
        GetCallHierarchyToolV2 getCallHierarchyToolV2,
        FastTextSearchToolV2 fastTextSearchToolV2,
        // V1 tools
        GoToDefinitionTool goToDefinitionTool,
        GetHoverInfoTool getHoverInfoTool,
        GetDocumentSymbolsTool getDocumentSymbolsTool,
        // Already V2
        GetDiagnosticsToolV2 getDiagnosticsToolV2,
        DependencyAnalysisToolV2 dependencyAnalysisToolV2)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _configuration = configuration;
        _notificationService = notificationService;
        _searchSymbolsToolV2 = searchSymbolsToolV2;
        _findReferencesToolV2 = findReferencesToolV2;
        _getImplementationsToolV2 = getImplementationsToolV2;
        _getCallHierarchyToolV2 = getCallHierarchyToolV2;
        _fastTextSearchToolV2 = fastTextSearchToolV2;
        _goToDefinitionTool = goToDefinitionTool;
        _getHoverInfoTool = getHoverInfoTool;
        _getDocumentSymbolsTool = getDocumentSymbolsTool;
        _getDiagnosticsToolV2 = getDiagnosticsToolV2;
        _dependencyAnalysisToolV2 = dependencyAnalysisToolV2;
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
            var currentOperation = 0;

            // Send initial progress notification
            await SendProgressNotification(progressToken, 0, operationCount, "Starting batch operations...");

            // Execute each operation
            foreach (var operation in operations.EnumerateArray())
            {
                currentOperation++;
                var operationType = operation.GetProperty("operation").GetString() 
                    ?? operation.GetProperty("type").GetString() 
                    ?? throw new InvalidOperationException("Operation must have 'operation' or 'type' property");

                await SendProgressNotification(progressToken, currentOperation, operationCount, 
                    $"Executing {operationType}...");

                try
                {
                    var operationResult = await ExecuteOperationAsync(operation, workspacePath, cancellationToken);
                    results.Add(new
                    {
                        success = true,
                        operation = operationType,
                        result = operationResult
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error executing operation {Type}", operationType);
                    results.Add(new
                    {
                        success = false,
                        operation = operationType,
                        error = ex.Message
                    });
                }
            }

            // Create batch result
            var batchResult = new
            {
                success = true,
                results = results,
                summary = new
                {
                    total = operationCount,
                    successful = results.Count(r => GetSuccess(r)),
                    failed = results.Count(r => !GetSuccess(r))
                }
            };

            // Create AI-optimized response
            var resultJson = JsonSerializer.Serialize(batchResult);
            var resultDoc = JsonDocument.Parse(resultJson);
            return CreateAiOptimizedResponse(operations, resultDoc.RootElement, mode);
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
            ToolNames.SearchSymbols => await ExecuteSearchSymbolsAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.FindReferences => await ExecuteFindReferencesAsync(operation, cancellationToken),
            ToolNames.GoToDefinition => await ExecuteGoToDefinitionAsync(operation, cancellationToken),
            ToolNames.GetHoverInfo => await ExecuteGetHoverInfoAsync(operation, cancellationToken),
            ToolNames.GetImplementations => await ExecuteGetImplementationsAsync(operation, cancellationToken),
            ToolNames.GetDocumentSymbols => await ExecuteGetDocumentSymbolsAsync(operation, cancellationToken),
            ToolNames.GetDiagnostics => await ExecuteGetDiagnosticsAsync(operation, cancellationToken),
            ToolNames.GetCallHierarchy => await ExecuteGetCallHierarchyAsync(operation, cancellationToken),
            ToolNames.TextSearch => await ExecuteTextSearchAsync(operation, defaultWorkspacePath, cancellationToken),
            ToolNames.DependencyAnalysis => await ExecuteAnalyzeDependenciesAsync(operation, defaultWorkspacePath, cancellationToken),
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

    // Individual operation execution methods
    
    private async Task<object> ExecuteSearchSymbolsAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        if (!operation.TryGetProperty("searchPattern", out var searchPatternProp))
        {
            throw new InvalidOperationException("search_symbols operation requires 'searchPattern'");
        }

        return await _searchSymbolsToolV2.ExecuteAsync(
            searchPatternProp.GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("kinds", out var k) && k.ValueKind == JsonValueKind.Array
                ? k.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            operation.TryGetProperty("fuzzy", out var f) && f.GetBoolean(),
            operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 100,
            ResponseMode.Summary, // Always use summary mode in batch operations
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteFindReferencesAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _findReferencesToolV2.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.GetProperty("line").GetInt32(),
            operation.GetProperty("column").GetInt32(),
            operation.TryGetProperty("includeDeclaration", out var id) && id.GetBoolean(),
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteGoToDefinitionAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _goToDefinitionTool.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.GetProperty("line").GetInt32(),
            operation.GetProperty("column").GetInt32(),
            cancellationToken);
    }

    private async Task<object> ExecuteGetHoverInfoAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _getHoverInfoTool.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.GetProperty("line").GetInt32(),
            operation.GetProperty("column").GetInt32(),
            cancellationToken);
    }

    private async Task<object> ExecuteGetImplementationsAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _getImplementationsToolV2.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.GetProperty("line").GetInt32(),
            operation.GetProperty("column").GetInt32(),
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteGetDocumentSymbolsAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _getDocumentSymbolsTool.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.TryGetProperty("includeMembers", out var im) && im.GetBoolean(),
            cancellationToken);
    }

    private async Task<object> ExecuteGetDiagnosticsAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _getDiagnosticsToolV2.ExecuteAsync(
            operation.GetProperty("path").GetString()!,
            operation.TryGetProperty("severities", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

    private async Task<object> ExecuteGetCallHierarchyAsync(JsonElement operation, CancellationToken cancellationToken)
    {
        return await _getCallHierarchyToolV2.ExecuteAsync(
            operation.GetProperty("filePath").GetString()!,
            operation.GetProperty("line").GetInt32(),
            operation.GetProperty("column").GetInt32(),
            operation.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "both" : "both",
            operation.TryGetProperty("maxDepth", out var md) ? md.GetInt32() : 2,
            ResponseMode.Summary,
            null,
            cancellationToken);
    }

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

    private async Task<object> ExecuteAnalyzeDependenciesAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        return await _dependencyAnalysisToolV2.ExecuteAsync(
            operation.GetProperty("symbol").GetString()!,
            GetWorkspacePath(operation, defaultWorkspacePath),
            operation.TryGetProperty("direction", out var dd) ? dd.GetString() ?? "both" : "both",
            operation.TryGetProperty("depth", out var dp) ? dp.GetInt32() : 3,
            operation.TryGetProperty("includeTests", out var it) && it.GetBoolean(),
            operation.TryGetProperty("includeExternalDependencies", out var ied) && ied.GetBoolean(),
            ResponseMode.Summary,
            null,
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
                hotspots = (analysis.FileReferences.Any() || analysis.SymbolReferences.Any()) ? new
                {
                    byFile = analysis.FileReferences
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => new { file = kv.Key, operations = kv.Value })
                        .ToList(),
                    bySymbol = analysis.SymbolReferences
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => new { symbol = kv.Key, operations = kv.Value })
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

            // Extract file and symbol references
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
        // Extract file references
        if (resultData.ValueKind == JsonValueKind.Object)
        {
            // Check for file path in various common locations
            string? filePath = null;
            if (resultData.TryGetProperty("filePath", out var fp))
                filePath = fp.GetString();
            else if (resultData.TryGetProperty("location", out var loc) && loc.TryGetProperty("filePath", out var locFp))
                filePath = locFp.GetString();
            else if (resultData.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array && locs.GetArrayLength() > 0)
            {
                var firstLoc = locs[0];
                if (firstLoc.TryGetProperty("filePath", out var firstFp))
                    filePath = firstFp.GetString();
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                if (!analysis.FileReferences.ContainsKey(filePath))
                    analysis.FileReferences[filePath] = 0;
                analysis.FileReferences[filePath]++;
            }

            // Extract symbol references
            string? symbolName = null;
            if (resultData.TryGetProperty("symbol", out var sym))
            {
                if (sym.ValueKind == JsonValueKind.String)
                    symbolName = sym.GetString();
                else if (sym.ValueKind == JsonValueKind.Object && sym.TryGetProperty("name", out var symName))
                    symbolName = symName.GetString();
            }
            else if (resultData.TryGetProperty("name", out var name))
                symbolName = name.GetString();

            if (!string.IsNullOrEmpty(symbolName))
            {
                if (!analysis.SymbolReferences.ContainsKey(symbolName))
                    analysis.SymbolReferences[symbolName] = 0;
                analysis.SymbolReferences[symbolName]++;
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

        // Pattern: Multiple operations on same file
        var fileGroups = new Dictionary<string, int>();
        foreach (var op in opArray)
        {
            if (op.TryGetProperty("filePath", out var fp))
            {
                var filePath = fp.GetString() ?? "";
                if (!fileGroups.ContainsKey(filePath))
                    fileGroups[filePath] = 0;
                fileGroups[filePath]++;
            }
        }

        var multiFileOps = fileGroups.Where(kv => kv.Value > 2).ToList();
        if (multiFileOps.Any())
        {
            var topFile = multiFileOps.OrderByDescending(kv => kv.Value).First();
            analysis.Patterns.Add($"Focused analysis: {topFile.Value} operations on {Path.GetFileName(topFile.Key)}");
        }

        // Pattern: Search followed by navigation
        bool hasSearch = opArray.Any(op => 
            op.TryGetProperty("operation", out var opType) && 
            (opType.GetString() == ToolNames.SearchSymbols || opType.GetString() == ToolNames.TextSearch));
        bool hasNavigation = opArray.Any(op => 
            op.TryGetProperty("operation", out var opType) && 
            (opType.GetString() == ToolNames.GoToDefinition || opType.GetString() == ToolNames.FindReferences));

        if (hasSearch && hasNavigation)
        {
            analysis.Patterns.Add("Search and navigate pattern detected");
        }

        // Pattern: Diagnostic analysis
        var diagnosticCount = analysis.OperationTypeCounts.GetValueOrDefault("get_diagnostics", 0);
        if (diagnosticCount > 0)
        {
            analysis.Patterns.Add($"Code quality check across {diagnosticCount} targets");
        }

        // Pattern: Dependency exploration
        if (analysis.OperationTypeCounts.ContainsKey("analyze_dependencies"))
        {
            analysis.Patterns.Add("Architectural dependency analysis");
        }
    }

    private void FindCorrelations(BatchAnalysis analysis)
    {
        // Correlation: Files with multiple operation types
        var fileOperationTypes = new Dictionary<string, HashSet<string>>();
        
        // This would require more detailed tracking during extraction
        // For now, provide basic correlations
        
        if (analysis.FileReferences.Count > 5 && analysis.OperationTypeCounts.Count > 3)
        {
            analysis.Correlations.Add("Multiple operation types across multiple files - comprehensive analysis");
        }

        if (analysis.FailureCount > 0 && analysis.FailureCount < analysis.TotalOperations / 2)
        {
            analysis.Correlations.Add($"Partial failures ({analysis.FailureCount}/{analysis.TotalOperations}) - some targets may be invalid");
        }

        if (analysis.SymbolReferences.Count > 10)
        {
            analysis.Correlations.Add("Wide symbol coverage - exploring interconnected code");
        }
    }

    private string EstimateExecutionTime(Dictionary<string, int> operationTypeCounts)
    {
        // Rough estimates in milliseconds
        var timeEstimates = new Dictionary<string, int>
        {
            [ToolNames.SearchSymbols] = 100,
            [ToolNames.FindReferences] = 200,
            [ToolNames.GoToDefinition] = 50,
            [ToolNames.GetDiagnostics] = 300,
            [ToolNames.DependencyAnalysis] = 500,
            [ToolNames.TextSearch] = 150,
            [ToolNames.GetHoverInfo] = 30,
            [ToolNames.GetImplementations] = 150,
            [ToolNames.GetCallHierarchy] = 250
        };

        var totalMs = operationTypeCounts.Sum(kv => 
            timeEstimates.GetValueOrDefault(kv.Key, 100) * kv.Value);

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
            "find_references" => new 
            { 
                found = resultData.TryGetProperty("references", out var refs) ? refs.GetArrayLength() : 0 
            },
            "search_symbols" => new 
            { 
                found = resultData.TryGetProperty("symbols", out var syms) ? syms.GetArrayLength() : 0 
            },
            "text_search" => new 
            { 
                matches = resultData.TryGetProperty("totalMatches", out var tm) ? tm.GetInt32() : 0 
            },
            "get_diagnostics" => new 
            { 
                errors = resultData.TryGetProperty("errorCount", out var ec) ? ec.GetInt32() : 0,
                warnings = resultData.TryGetProperty("warningCount", out var wc) ? wc.GetInt32() : 0
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
            var topFile = analysis.FileReferences.OrderByDescending(kv => kv.Value).First();
            if (topFile.Value > 2)
            {
                insights.Add($"File focus: {Path.GetFileName(topFile.Key)} ({topFile.Value} operations)");
            }
        }

        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }

        // Performance insight
        insights.Add($"Estimated execution time: {analysis.EstimatedExecutionTime}");

        // Correlation insights
        if (analysis.Correlations.Any())
        {
            insights.Add(analysis.Correlations.First());
        }
        
        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (analysis.TotalOperations == 0)
            {
                insights.Add("No operations were executed");
            }
            else
            {
                insights.Add($"Batch execution completed: {analysis.TotalOperations} operations processed");
            }
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

        // Deep dive into hotspots
        if (analysis.FileReferences.Any())
        {
            var topFile = analysis.FileReferences.OrderByDescending(kv => kv.Value).First();
            if (topFile.Value > 3)
            {
                actions.Add(new
                {
                    id = "analyze_hotspot",
                    cmd = new { file = topFile.Key, operations = new[] { "get_document_symbols", "get_diagnostics" } },
                    tokens = 1500,
                    priority = "recommended"
                });
            }
        }

        // Follow up on search results
        if (analysis.OperationTypeCounts.ContainsKey(ToolNames.SearchSymbols) || 
            analysis.OperationTypeCounts.ContainsKey(ToolNames.TextSearch))
        {
            actions.Add(new
            {
                id = "navigate_results",
                cmd = new { operations = new[] { ToolNames.GoToDefinition, ToolNames.FindReferences }, limit = 5 },
                tokens = 2000,
                priority = "available"
            });
        }

        // Expand analysis
        if (analysis.TotalOperations < 10)
        {
            actions.Add(new
            {
                id = "expand_scope",
                cmd = new { addOperations = new[] { ToolNames.DependencyAnalysis, ToolNames.GetCallHierarchy } },
                tokens = 3000,
                priority = "available"
            });
        }

        // Export detailed results
        if (analysis.TotalOperations > 5)
        {
            actions.Add(new
            {
                id = "export_detailed",
                cmd = new { format = "markdown", includeMetrics = true },
                tokens = analysis.TotalOperations * 100,
                priority = "available"
            });
        }
        
        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (analysis.TotalOperations > 0)
            {
                actions.Add(new
                {
                    id = "view_summary",
                    cmd = new { showDetails = true },
                    tokens = 1000,
                    priority = "available"
                });
            }
            else
            {
                actions.Add(new
                {
                    id = "setup_operations",
                    cmd = new { operations = new[] { "get_diagnostics", "find_references" } },
                    tokens = 1500,
                    priority = "recommended"
                });
            }
        }

        return actions;
    }

    private int EstimateResponseTokens(BatchAnalysis analysis)
    {
        // Base tokens for structure
        var baseTokens = 300;
        
        // Per operation tokens
        var perOpTokens = 100;
        
        // Additional tokens for complexity
        var complexityTokens = (analysis.FileReferences.Count + analysis.SymbolReferences.Count) * 20;
        
        return baseTokens + (analysis.TotalOperations * perOpTokens) + complexityTokens;
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
        public int AverageTokensPerOperation => TotalOperations > 0 ? 200 : 0; // Rough estimate
        
        public Dictionary<string, int> OperationTypeCounts { get; set; } = new();
        public Dictionary<string, int> ErrorSummary { get; set; } = new();
        public Dictionary<string, int> FileReferences { get; set; } = new();
        public Dictionary<string, int> SymbolReferences { get; set; } = new();
        public List<string> Patterns { get; set; } = new();
        public List<string> Correlations { get; set; } = new();
    }
}