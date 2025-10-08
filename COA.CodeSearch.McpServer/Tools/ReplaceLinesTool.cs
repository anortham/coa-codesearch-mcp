using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
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
/// Tool for replacing a range of lines in a file with new content.
/// Enables precise surgical editing of code blocks at known line positions.
/// </summary>
[Obsolete("Use EditLinesTool with operation='replace' instead. This tool will be removed in a future version.", error: false)]
public class ReplaceLinesTool : CodeSearchToolBase<ReplaceLinesParameters, AIOptimizedResponse<ReplaceLinesResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly UnifiedFileEditService _fileEditService;
    private readonly ILogger<ReplaceLinesTool> _logger;

    /// <summary>
    /// Initializes a new instance of the ReplaceLinesTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="fileEditService">Unified file edit service</param>
    /// <param name="logger">Logger instance</param>
    public ReplaceLinesTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        UnifiedFileEditService fileEditService,
        ILogger<ReplaceLinesTool> logger) : base(serviceProvider, logger)
    {
        _pathResolutionService = pathResolutionService;
        _fileEditService = fileEditService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.ReplaceLines;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "REPLACE LINES WITHOUT READ - Replace line ranges with new content using precise line positioning. " +
        "Perfect for method body replacement, config updates, or block edits. " +
        "Supports single line (StartLine only) or range (StartLine to EndLine). " +
        "Preserves indentation automatically and provides context verification.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Refactoring;

    /// <summary>
    /// Executes the replace lines operation to replace specified lines with new content.
    /// </summary>
    /// <param name="parameters">Replace lines parameters including file path, line range, and new content</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Replace lines results with replacement details and context</returns>
    protected override async Task<AIOptimizedResponse<ReplaceLinesResult>> ExecuteInternalAsync(
        ReplaceLinesParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and resolve file path
            var filePath = ValidateAndResolvePath(parameters.FilePath);
            
            // Validate line parameters
            ValidateReplacementParameters(parameters);
            
            // Use UnifiedFileEditService for reliable, concurrent line replacement
            var editResult = await _fileEditService.ReplaceLinesAsync(
                filePath,
                parameters.StartLine,
                parameters.EndLine,
                parameters.Content ?? string.Empty,
                parameters.PreserveIndentation,
                cancellationToken);

            if (!editResult.Success)
            {
                return CreateErrorResponse(editResult.ErrorMessage ?? "Replace operation failed");
            }

            // Generate context for verification
            var modifiedLines = FileLineUtilities.SplitLines(editResult.ModifiedContent ?? "");
            var originalLines = FileLineUtilities.SplitLines(editResult.OriginalContent ?? "");
            var actualEndLine = parameters.EndLine ?? parameters.StartLine;
            
            // Calculate lines added from the replacement content
            var replacementLines = string.IsNullOrEmpty(parameters.Content) 
                ? new string[0] 
                : parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            var contextLines = GenerateContext(modifiedLines, parameters.StartLine - 1, 
                replacementLines.Length, parameters.ContextLines); // Pass actual replacement line count

            var result = new ReplaceLinesResult
            {
                Success = true,
                FilePath = filePath,
                StartLine = parameters.StartLine,
                EndLine = actualEndLine,
                LinesRemoved = (actualEndLine - parameters.StartLine + 1),
                LinesAdded = replacementLines.Length,
                ContextLines = contextLines,
                OriginalContent = editResult.DeletedContent
            };

            _logger.LogInformation("Successfully replaced lines {StartLine}-{EndLine} in {FilePath} " +
                                   "(removed: {Removed}, added: {Added})", 
                                   parameters.StartLine, result.EndLine, filePath, 
                                   result.LinesRemoved, result.LinesAdded);

            return CreateSuccessResponse(result);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateErrorResponse($"Access denied to file: {parameters.FilePath}");
        }
        catch (FileNotFoundException)
        {
            return CreateErrorResponse($"File not found: {parameters.FilePath}");
        }
        catch (IOException ex)
        {
            return CreateErrorResponse($"IO error accessing file {parameters.FilePath}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error replacing lines in {FilePath}", parameters.FilePath);
            return CreateErrorResponse($"Unexpected error: {ex.Message}");
        }
    }

    private string ValidateAndResolvePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath is required", nameof(filePath));

        // Resolve to absolute path
        var resolvedPath = Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);
        
        // Verify file exists
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"File not found: {resolvedPath}");
        }
        
        return resolvedPath;
    }

    private void ValidateReplacementParameters(ReplaceLinesParameters parameters)
    {
        if (parameters.StartLine < 1)
            throw new ArgumentException("StartLine must be >= 1", nameof(parameters.StartLine));

        if (parameters.EndLine.HasValue && parameters.EndLine < parameters.StartLine)
            throw new ArgumentException("EndLine must be >= StartLine", nameof(parameters.EndLine));

        if (parameters.Content == null)
            throw new ArgumentException("Content is required (use empty string to delete lines)", nameof(parameters.Content));

        if (parameters.ContextLines < 0 || parameters.ContextLines > 20)
            throw new ArgumentException("ContextLines must be between 0 and 20", nameof(parameters.ContextLines));
    }

    private async Task<(string[] lines, Encoding encoding)> ReadFileWithEncodingAsync(string filePath, CancellationToken cancellationToken)
    {
        // Use shared utility for consistent line handling
        return await FileLineUtilities.ReadFileWithEncodingAsync(filePath, cancellationToken);
    }


    private string[] ApplyIndentation(string[] contentLines, string indentation)
    {
        return FileLineUtilities.ApplyIndentation(contentLines, indentation);
    }

    private string[] ReplaceLinesAt(string[] originalLines, int startLine, int endLine, string[] replacementLines)
    {
        var result = new List<string>();
        
        // Add lines before the replacement range
        for (int i = 0; i < startLine; i++)
        {
            result.Add(originalLines[i]);
        }
        
        // Add replacement lines
        result.AddRange(replacementLines);
        
        // Add lines after the replacement range
        for (int i = endLine + 1; i < originalLines.Length; i++)
        {
            result.Add(originalLines[i]);
        }
        
        return result.ToArray();
    }

    private string[] GenerateContext(string[] newLines, int replacementStartLine, int replacementLineCount, int contextLines)
    {
        if (contextLines == 0) return Array.Empty<string>();

        var contextStart = Math.Max(0, replacementStartLine - contextLines);
        var contextEnd = Math.Min(newLines.Length - 1, replacementStartLine + replacementLineCount + contextLines - 1);
        
        var context = new List<string>();
        
        for (int i = contextStart; i <= contextEnd; i++)
        {
            var lineNumber = i + 1; // Convert to 1-based for display
            var prefix = (i >= replacementStartLine && i < replacementStartLine + replacementLineCount) ? "+" : " ";
            context.Add($"{prefix}{lineNumber,4}: {(i < newLines.Length ? newLines[i] : "")}");
        }
        
        return context.ToArray();
    }

    private AIOptimizedResponse<ReplaceLinesResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<ReplaceLinesResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "REPLACE_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<ReplaceLinesResult>
            {
                Results = new ReplaceLinesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                },
                Summary = "Replacement failed",
                Count = 0
            }
        };
    }

    private AIOptimizedResponse<ReplaceLinesResult> CreateSuccessResponse(ReplaceLinesResult result)
    {
        return new AIOptimizedResponse<ReplaceLinesResult>
        {
            Success = true,
            Data = new AIResponseData<ReplaceLinesResult>
            {
                Results = result,
                Summary = $"Replaced lines {result.StartLine}-{result.EndLine} (removed: {result.LinesRemoved}, added: {result.LinesAdded})",
                Count = result.LinesAdded
            }
        };
    }
}
