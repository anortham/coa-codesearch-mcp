using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

public class BatchOperationsTool : IBatchOperationsTool
{
    private readonly ILogger<BatchOperationsTool> _logger;
    private readonly GoToDefinitionTool _goToDefinitionTool;
    private readonly FindReferencesTool _findReferencesTool;
    private readonly SearchSymbolsTool _searchSymbolsTool;
    private readonly GetDiagnosticsTool _diagnosticsTool;
    private readonly GetHoverInfoTool _hoverTool;
    private readonly GetImplementationsTool _implementationsTool;
    private readonly GetDocumentSymbolsTool _documentSymbolsTool;
    private readonly GetCallHierarchyTool _callHierarchyTool;
    private readonly FastTextSearchTool _fastTextSearchTool;
    private readonly DependencyAnalysisTool _dependencyAnalysisTool;

    public BatchOperationsTool(
        ILogger<BatchOperationsTool> logger,
        GoToDefinitionTool goToDefinitionTool,
        FindReferencesTool findReferencesTool,
        SearchSymbolsTool searchSymbolsTool,
        GetDiagnosticsTool diagnosticsTool,
        GetHoverInfoTool hoverTool,
        GetImplementationsTool implementationsTool,
        GetDocumentSymbolsTool documentSymbolsTool,
        GetCallHierarchyTool callHierarchyTool,
        FastTextSearchTool fastTextSearchTool,
        DependencyAnalysisTool dependencyAnalysisTool)
    {
        _logger = logger;
        _goToDefinitionTool = goToDefinitionTool;
        _findReferencesTool = findReferencesTool;
        _searchSymbolsTool = searchSymbolsTool;
        _diagnosticsTool = diagnosticsTool;
        _hoverTool = hoverTool;
        _implementationsTool = implementationsTool;
        _documentSymbolsTool = documentSymbolsTool;
        _callHierarchyTool = callHierarchyTool;
        _fastTextSearchTool = fastTextSearchTool;
        _dependencyAnalysisTool = dependencyAnalysisTool;
    }

    public async Task<object> ExecuteAsync(
        JsonElement operations,
        string? defaultWorkspacePath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("BatchOperations request with {Count} operations", operations.GetArrayLength());

            var results = new List<object>();

            foreach (var operation in operations.EnumerateArray())
            {
                // Support both "operation" and "type" keys for flexibility
                string? operationType = null;
                if (operation.TryGetProperty("operation", out var opProp))
                {
                    operationType = opProp.GetString();
                }
                else if (operation.TryGetProperty("type", out var typeProp))
                {
                    operationType = typeProp.GetString();
                }
                else
                {
                    throw new ArgumentException("Each operation must have either an 'operation' or 'type' property");
                }
                
                try
                {
                    var operationResult = await ExecuteSingleOperationAsync(operationType!, operation, defaultWorkspacePath, cancellationToken);
                    
                    results.Add(new
                    {
                        operation = operationType,
                        type = operationType,  // Include both for compatibility
                        success = true,
                        result = operationResult
                    });
                }
                catch (Exception opEx)
                {
                    _logger.LogError(opEx, "Error executing operation {OperationType}", operationType);
                    results.Add(new
                    {
                        operation = operationType,
                        type = operationType,  // Include both for compatibility
                        success = false,
                        error = opEx.Message
                    });
                }
            }

            return new
            {
                success = true,
                totalOperations = results.Count,
                results = results
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch operations");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private string GetWorkspacePath(JsonElement operation, string? defaultWorkspacePath)
    {
        // First check if the operation has its own workspacePath
        if (operation.TryGetProperty("workspacePath", out var wsPath) && wsPath.ValueKind == JsonValueKind.String)
        {
            return wsPath.GetString()!;
        }

        // Check if parameters object has workspacePath
        if (operation.TryGetProperty("parameters", out var paramsProp) && 
            paramsProp.TryGetProperty("workspacePath", out var paramsWsPath) && 
            paramsWsPath.ValueKind == JsonValueKind.String)
        {
            return paramsWsPath.GetString()!;
        }
        
        // Fall back to the default workspacePath from the batch request
        if (!string.IsNullOrEmpty(defaultWorkspacePath))
        {
            return defaultWorkspacePath;
        }
        
        throw new ArgumentException("workspacePath is required either in the operation or as a default parameter");
    }

    private async Task<object> ExecuteSingleOperationAsync(string operationType, JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        return operationType switch
        {
            "search_symbols" => await ExecuteSearchSymbolsAsync(operation, defaultWorkspacePath, cancellationToken),

            "find_references" => await _findReferencesTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                operation.TryGetProperty("includeDeclaration", out var id) ? id.GetBoolean() : true,
                cancellationToken),

            "go_to_definition" => await _goToDefinitionTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                cancellationToken),

            "get_hover_info" => await _hoverTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                cancellationToken),

            "get_implementations" => await _implementationsTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                cancellationToken),

            "get_document_symbols" => await _documentSymbolsTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.TryGetProperty("includeMembers", out var im) && im.GetBoolean(),
                cancellationToken),

            "get_diagnostics" => await _diagnosticsTool.ExecuteAsync(
                operation.GetProperty("path").GetString()!,
                operation.TryGetProperty("severities", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Select(e => e.GetString()!).ToArray()
                    : null,
                operation.TryGetProperty("maxResults", out var dmr) ? dmr.GetInt32() : 100,
                operation.TryGetProperty("summaryOnly", out var dso) && dso.GetBoolean(),
                cancellationToken),

            "get_call_hierarchy" => await _callHierarchyTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                operation.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "both" : "both",
                operation.TryGetProperty("maxDepth", out var md) ? md.GetInt32() : 2,
                cancellationToken),

            "text_search" or "fast_text_search" or "textSearch" => await ExecuteTextSearchAsync(operation, defaultWorkspacePath, cancellationToken),

            "analyze_dependencies" => await _dependencyAnalysisTool.ExecuteAsync(
                operation.GetProperty("symbol").GetString()!,
                GetWorkspacePath(operation, defaultWorkspacePath),
                operation.TryGetProperty("direction", out var dd) ? dd.GetString() ?? "both" : "both",
                operation.TryGetProperty("depth", out var dp) ? dp.GetInt32() : 3,
                operation.TryGetProperty("includeTests", out var it) && it.GetBoolean(),
                operation.TryGetProperty("includeExternalDependencies", out var ied) && ied.GetBoolean(),
                cancellationToken),

            _ => throw new NotSupportedException($"Operation type '{operationType}' not supported in batch operations")
        };
    }

    private async Task<object> ExecuteTextSearchAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        // First check if parameters are nested under a "parameters" property
        JsonElement parameters = operation;
        if (operation.TryGetProperty("parameters", out var paramsProp))
        {
            parameters = paramsProp;
        }

        // Get the query - this is required
        if (!parameters.TryGetProperty("query", out var queryProp))
        {
            throw new ArgumentException("'query' parameter is required for text_search operation");
        }

        return await _fastTextSearchTool.ExecuteAsync(
            queryProp.GetString()!,
            GetWorkspacePath(parameters, defaultWorkspacePath),
            parameters.TryGetProperty("filePattern", out var fp) ? fp.GetString() : null,
            parameters.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                ? ext.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            parameters.TryGetProperty("contextLines", out var cl) ? cl.GetInt32() : null,
            parameters.TryGetProperty("maxResults", out var tmr) ? tmr.GetInt32() : 50,
            parameters.TryGetProperty("caseSensitive", out var cs) && cs.GetBoolean(),
            parameters.TryGetProperty("searchType", out var st) ? st.GetString() ?? "standard" : "standard",
            cancellationToken);
    }

    private async Task<object> ExecuteSearchSymbolsAsync(JsonElement operation, string? defaultWorkspacePath, CancellationToken cancellationToken)
    {
        // First check if parameters are nested under a "parameters" property
        JsonElement parameters = operation;
        if (operation.TryGetProperty("parameters", out var paramsProp))
        {
            parameters = paramsProp;
        }

        // Get the searchPattern - this is required
        if (!parameters.TryGetProperty("searchPattern", out var searchPatternProp))
        {
            throw new ArgumentException("'searchPattern' parameter is required for search_symbols operation");
        }

        return await _searchSymbolsTool.ExecuteAsync(
            searchPatternProp.GetString()!,
            GetWorkspacePath(parameters, defaultWorkspacePath),
            parameters.TryGetProperty("symbolTypes", out var st) && st.ValueKind == JsonValueKind.Array
                ? st.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null,
            parameters.TryGetProperty("searchType", out var stype) ? stype.GetString() == "fuzzy" : false,
            parameters.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 100,
            cancellationToken);
    }
}