using System.ComponentModel;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Unified tool for editing file lines: insert, replace, or delete operations.
/// Consolidates insert_at_line, replace_lines, and delete_lines into a single interface.
/// </summary>
public class EditLinesTool : CodeSearchToolBase<EditLinesParameters, AIOptimizedResponse<EditLinesResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly UnifiedFileEditService _fileEditService;
    private readonly ILogger<EditLinesTool> _logger;

    /// <summary>
    /// Initializes a new instance of the EditLinesTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="fileEditService">Unified file edit service</param>
    /// <param name="logger">Logger instance</param>
    public EditLinesTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        UnifiedFileEditService fileEditService,
        ILogger<EditLinesTool> logger) : base(serviceProvider, logger)
    {
        _pathResolutionService = pathResolutionService;
        _fileEditService = fileEditService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.EditLines;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "Unified line editing tool - insert, replace, or delete lines with precision positioning. " +
        "Preserves indentation automatically. Use operation='insert' to add lines, operation='replace' to update lines, " +
        "operation='delete' to remove lines. Minimal usage: edit_lines(file, operation, lineNumber, content). " +
        "Replaces: insert_at_line, replace_lines, delete_lines.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Refactoring;

    /// <summary>
    /// Executes the line editing operation based on the specified operation type.
    /// </summary>
    /// <param name="parameters">Edit lines parameters including operation, file path, line range, and content</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Edit lines results with operation details and context</returns>
    protected override async Task<AIOptimizedResponse<EditLinesResult>> ExecuteInternalAsync(
        EditLinesParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and resolve file path
            var filePath = ValidateAndResolvePath(parameters.FilePath);

            // Validate operation and parameters
            ValidateEditParameters(parameters);

            // Route to appropriate operation
            var operation = parameters.Operation.ToLowerInvariant();
            FileEditResult editResult;

            switch (operation)
            {
                case "insert":
                    if (string.IsNullOrEmpty(parameters.Content))
                        throw new ArgumentException("Content is required for insert operation");

                    editResult = await _fileEditService.InsertAtLineAsync(
                        filePath,
                        parameters.StartLine,
                        parameters.Content,
                        parameters.PreserveIndentation,
                        cancellationToken);
                    break;

                case "replace":
                    if (string.IsNullOrEmpty(parameters.Content))
                        throw new ArgumentException("Content is required for replace operation");

                    var replaceEndLine = parameters.EndLine ?? parameters.StartLine;
                    editResult = await _fileEditService.ReplaceLinesAsync(
                        filePath,
                        parameters.StartLine,
                        replaceEndLine,
                        parameters.Content,
                        parameters.PreserveIndentation,
                        cancellationToken);
                    break;

                case "delete":
                    var deleteEndLine = parameters.EndLine ?? parameters.StartLine;
                    editResult = await _fileEditService.DeleteLinesAsync(
                        filePath,
                        parameters.StartLine,
                        deleteEndLine,
                        cancellationToken);
                    break;

                default:
                    throw new ArgumentException($"Invalid operation: {parameters.Operation}. Must be 'insert', 'replace', or 'delete'");
            }

            if (!editResult.Success)
            {
                return CreateErrorResponse(operation, editResult.ErrorMessage ?? $"{operation} operation failed");
            }

            // Generate context for verification
            var modifiedLines = FileLineUtilities.SplitLines(editResult.ModifiedContent ?? "");
            var contextLines = GenerateContext(
                operation,
                modifiedLines,
                parameters.StartLine,
                parameters.EndLine ?? parameters.StartLine,
                parameters.Content,
                parameters.ContextLines);

            // Calculate lines added/removed based on operation
            int linesAdded = 0, linesRemoved = 0;
            switch (operation)
            {
                case "insert":
                    linesAdded = parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                    break;
                case "replace":
                    var replaceEnd = parameters.EndLine ?? parameters.StartLine;
                    linesRemoved = replaceEnd - parameters.StartLine + 1;
                    linesAdded = parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                    break;
                case "delete":
                    var deleteEnd = parameters.EndLine ?? parameters.StartLine;
                    linesRemoved = deleteEnd - parameters.StartLine + 1;
                    break;
            }

            var result = new EditLinesResult
            {
                Success = true,
                Operation = operation,
                FilePath = filePath,
                StartLine = parameters.StartLine,
                EndLine = parameters.EndLine,
                LinesAdded = linesAdded,
                LinesRemoved = linesRemoved,
                ContextLines = contextLines,
                DetectedIndentation = editResult.DetectedIndentation,
                DeletedContent = editResult.DeletedContent,
                TotalFileLines = modifiedLines.Length
            };

            _logger.LogInformation("Successfully executed {Operation} operation at line {StartLine} in {FilePath} " +
                                   "(added: {Added}, removed: {Removed})",
                                   operation, parameters.StartLine, filePath, linesAdded, linesRemoved);

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Operation} operation in {FilePath}",
                parameters.Operation, parameters.FilePath);

            return CreateErrorResponse(parameters.Operation, $"Edit failed: {ex.Message}");
        }
    }

    private string ValidateAndResolvePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath is required");

        // Resolve to absolute path
        var resolvedPath = Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);

        // Verify file exists
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {resolvedPath}");

        return resolvedPath;
    }

    private void ValidateEditParameters(EditLinesParameters parameters)
    {
        // Validate operation
        var operation = parameters.Operation.ToLowerInvariant();
        if (operation != "insert" && operation != "replace" && operation != "delete")
            throw new ArgumentException($"Invalid operation: {parameters.Operation}. Must be 'insert', 'replace', or 'delete'");

        // Validate line numbers
        if (parameters.StartLine < 1)
            throw new ArgumentException("StartLine must be >= 1");

        if (parameters.EndLine.HasValue && parameters.EndLine < parameters.StartLine)
            throw new ArgumentException("EndLine must be >= StartLine");

        // Validate context lines
        if (parameters.ContextLines < 0 || parameters.ContextLines > 20)
            throw new ArgumentException("ContextLines must be between 0 and 20");
    }

    private string[] GenerateContext(
        string operation,
        string[] allLines,
        int startLine,
        int endLine,
        string content,
        int contextLines)
    {
        if (contextLines == 0)
            return Array.Empty<string>();

        var startIndex = startLine - 1; // Convert to 0-based
        var endIndex = endLine - 1;     // Convert to 0-based

        // Calculate context range based on operation
        int contextStart, contextEnd, affectedLineCount;

        switch (operation)
        {
            case "insert":
                affectedLineCount = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                contextStart = Math.Max(0, startIndex - contextLines);
                contextEnd = Math.Min(allLines.Length - 1, startIndex + affectedLineCount + contextLines - 1);
                break;

            case "replace":
                affectedLineCount = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                contextStart = Math.Max(0, startIndex - contextLines);
                contextEnd = Math.Min(allLines.Length - 1, startIndex + affectedLineCount + contextLines - 1);
                break;

            case "delete":
                contextStart = Math.Max(0, startIndex - contextLines);
                contextEnd = Math.Min(allLines.Length - 1, startIndex + contextLines - 1);
                affectedLineCount = 0; // For delete, we show the remaining lines
                break;

            default:
                return Array.Empty<string>();
        }

        var context = new List<string>();

        for (int i = contextStart; i <= contextEnd && i < allLines.Length; i++)
        {
            var lineNumber = i + 1; // 1-based display
            var isAffected = i >= startIndex && i < startIndex + affectedLineCount;
            var marker = isAffected ? "→ " : "  ";
            context.Add($"{lineNumber:000} {marker}{allLines[i]}");
        }

        return context.ToArray();
    }

    private AIOptimizedResponse<EditLinesResult> CreateErrorResponse(string operation, string errorMessage)
    {
        return new AIOptimizedResponse<EditLinesResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "EDIT_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<EditLinesResult>
            {
                Results = new EditLinesResult
                {
                    Success = false,
                    Operation = operation,
                    ErrorMessage = errorMessage,
                    FilePath = ""
                },
                Summary = $"{operation} operation failed",
                Count = 0
            }
        };
    }

    private AIOptimizedResponse<EditLinesResult> CreateSuccessResponse(EditLinesResult result)
    {
        var summary = result.Operation switch
        {
            "insert" => $"Inserted {result.LinesAdded} lines at line {result.StartLine}",
            "replace" => $"Replaced lines {result.StartLine}-{result.EndLine} (removed: {result.LinesRemoved}, added: {result.LinesAdded})",
            "delete" => $"Deleted lines {result.StartLine}-{result.EndLine} ({result.LinesRemoved} lines removed)",
            _ => $"{result.Operation} completed"
        };

        return new AIOptimizedResponse<EditLinesResult>
        {
            Success = true,
            Data = new AIResponseData<EditLinesResult>
            {
                Results = result,
                Summary = summary,
                Count = result.LinesAdded > 0 ? result.LinesAdded : result.LinesRemoved
            }
        };
    }
}
