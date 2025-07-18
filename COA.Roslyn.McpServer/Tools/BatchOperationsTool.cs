using COA.Roslyn.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.Roslyn.McpServer.Tools;

public class BatchOperationsTool
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

    public BatchOperationsTool(
        ILogger<BatchOperationsTool> logger,
        GoToDefinitionTool goToDefinitionTool,
        FindReferencesTool findReferencesTool,
        SearchSymbolsTool searchSymbolsTool,
        GetDiagnosticsTool diagnosticsTool,
        GetHoverInfoTool hoverTool,
        GetImplementationsTool implementationsTool,
        GetDocumentSymbolsTool documentSymbolsTool,
        GetCallHierarchyTool callHierarchyTool)
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
    }

    public async Task<object> ExecuteAsync(
        JsonElement operations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("BatchOperations request with {Count} operations", operations.GetArrayLength());

            var results = new List<object>();

            foreach (var operation in operations.EnumerateArray())
            {
                var operationType = operation.GetProperty("type").GetString();
                var operationResult = await ExecuteSingleOperationAsync(operationType!, operation, cancellationToken);
                
                results.Add(new
                {
                    type = operationType,
                    success = true,
                    result = operationResult
                });
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

    private async Task<object> ExecuteSingleOperationAsync(string operationType, JsonElement operation, CancellationToken cancellationToken)
    {
        return operationType switch
        {
            "search_symbols" => await _searchSymbolsTool.ExecuteAsync(
                operation.GetProperty("pattern").GetString()!,
                operation.GetProperty("workspacePath").GetString()!,
                operation.TryGetProperty("kinds", out var k) && k.ValueKind == JsonValueKind.Array
                    ? k.EnumerateArray().Select(e => e.GetString()!).ToArray()
                    : null,
                operation.TryGetProperty("fuzzy", out var f) && f.GetBoolean(),
                operation.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : 100,
                cancellationToken),

            "find_references" => await _findReferencesTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                operation.TryGetProperty("includePotential", out var ip) && ip.GetBoolean(),
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
                cancellationToken),

            "get_call_hierarchy" => await _callHierarchyTool.ExecuteAsync(
                operation.GetProperty("filePath").GetString()!,
                operation.GetProperty("line").GetInt32(),
                operation.GetProperty("column").GetInt32(),
                operation.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "both" : "both",
                operation.TryGetProperty("maxDepth", out var md) ? md.GetInt32() : 2,
                cancellationToken),

            _ => throw new NotSupportedException($"Operation type '{operationType}' not supported in batch operations")
        };
    }
}