using System.ComponentModel;
using System.Text;
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
/// Tool for deleting a range of lines from a file.
/// Enables precise surgical deletion of code blocks at known line positions.
/// </summary>
public class DeleteLinesTool : CodeSearchToolBase<DeleteLinesParameters, AIOptimizedResponse<DeleteLinesResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<DeleteLinesTool> _logger;

    public DeleteLinesTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        ILogger<DeleteLinesTool> logger) : base(serviceProvider)
    {
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public override string Name => ToolNames.DeleteLines;
    public override string Description => 
        "DELETE LINES WITHOUT READ - Delete line ranges using precise line positioning. " +
        "Perfect for removing code blocks, cleaning up sections, or surgical deletions. " +
        "Supports single line (StartLine only) or range (StartLine to EndLine). " +
        "Provides context verification and stores deleted content for recovery.";
    public override ToolCategory Category => ToolCategory.Refactoring;

    protected override async Task<AIOptimizedResponse<DeleteLinesResult>> ExecuteInternalAsync(
        DeleteLinesParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and resolve file path
            var filePath = ValidateAndResolvePath(parameters.FilePath);
            
            // Validate line parameters
            ValidateDeletionParameters(parameters);
            
            // Read current file content using shared utilities for consistency
            string[] lines;
            Encoding fileEncoding;
            string originalContent;
            
            // Read the original content once to ensure consistency
            var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            fileEncoding = FileLineUtilities.DetectEncoding(fileBytes);
            originalContent = fileEncoding.GetString(fileBytes);
            lines = FileLineUtilities.SplitLines(originalContent);
            
            // Determine actual line range
            var startLine = parameters.StartLine - 1; // Convert to 0-based
            var endLine = (parameters.EndLine ?? parameters.StartLine) - 1; // Convert to 0-based
            
            // Validate line range against file content
            if (startLine < 0 || startLine >= lines.Length)
            {
                return CreateErrorResponse(
                    $"StartLine {parameters.StartLine} is out of range. File has {lines.Length} lines. " +
                    $"Valid range: 1-{lines.Length}");
            }
            
            if (endLine < 0 || endLine >= lines.Length)
            {
                return CreateErrorResponse(
                    $"EndLine {parameters.EndLine} is out of range. File has {lines.Length} lines. " +
                    $"Valid range: 1-{lines.Length}");
            }
            
            if (startLine > endLine)
            {
                return CreateErrorResponse(
                    $"EndLine must be >= StartLine. Got StartLine={parameters.StartLine}, EndLine={parameters.EndLine}");
            }
            
            // Capture deleted content for recovery purposes
            var deletedLines = lines.Skip(startLine).Take(endLine - startLine + 1).ToArray();
            var deletedContent = string.Join(Environment.NewLine, deletedLines);
            
            // Perform the deletion
            var newLines = DeleteLinesAt(lines, startLine, endLine);
            
            // Write back to file with original encoding, preserving line endings
            await FileLineUtilities.WriteAllLinesPreservingEndingsAsync(filePath, newLines, fileEncoding, originalContent, cancellationToken);
            // Generate context for verification (adjusted for deletion)
            var contextLines = GenerateContext(newLines, startLine, deletedLines.Length, parameters.ContextLines);
            
            var result = new DeleteLinesResult
            {
                Success = true,
                FilePath = filePath,
                StartLine = parameters.StartLine,
                EndLine = parameters.EndLine ?? parameters.StartLine,
                LinesDeleted = deletedLines.Length,
                ContextLines = contextLines,
                DeletedContent = deletedContent
            };

            _logger.LogInformation("Successfully deleted lines {StartLine}-{EndLine} in {FilePath} " +
                                   "(deleted: {Deleted} lines)", 
                                   parameters.StartLine, result.EndLine, filePath, 
                                   result.LinesDeleted);

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
            _logger.LogError(ex, "Unexpected error deleting lines in {FilePath}", parameters.FilePath);
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

    private void ValidateDeletionParameters(DeleteLinesParameters parameters)
    {
        if (parameters.StartLine < 1)
            throw new ArgumentException("StartLine must be >= 1", nameof(parameters.StartLine));

        if (parameters.EndLine.HasValue && parameters.EndLine < parameters.StartLine)
            throw new ArgumentException("EndLine must be >= StartLine", nameof(parameters.EndLine));

        if (parameters.ContextLines < 0 || parameters.ContextLines > 20)
            throw new ArgumentException("ContextLines must be between 0 and 20", nameof(parameters.ContextLines));
    }


    private string[] DeleteLinesAt(string[] originalLines, int startLine, int endLine)
    {
        var result = new List<string>();
        
        // Add lines before the deletion range
        for (int i = 0; i < startLine; i++)
        {
            result.Add(originalLines[i]);
        }
        
        // Skip the deletion range (lines startLine to endLine inclusive)
        
        // Add lines after the deletion range
        for (int i = endLine + 1; i < originalLines.Length; i++)
        {
            result.Add(originalLines[i]);
        }
        
        return result.ToArray();
    }

    private string[] GenerateContext(string[] newLines, int deletionStartLine, int deletedLineCount, int contextLines)
    {
        if (contextLines == 0) return Array.Empty<string>();

        var contextStart = Math.Max(0, deletionStartLine - contextLines);
        var contextEnd = Math.Min(newLines.Length - 1, deletionStartLine + contextLines - 1);
        
        var context = new List<string>();
        
        // Show context before deletion
        for (int i = contextStart; i < deletionStartLine && i < newLines.Length; i++)
        {
            var lineNumber = i + 1; // Convert to 1-based for display
            context.Add($" {lineNumber,4}: {newLines[i]}");
        }
        
        // Show deletion marker
        if (deletedLineCount > 0)
        {
            var deletionStart = deletionStartLine + 1; // Convert to 1-based
            var deletionEnd = deletionStart + deletedLineCount - 1;
            if (deletedLineCount == 1)
            {
                context.Add($"-     : [Deleted line {deletionStart}]");
            }
            else
            {
                context.Add($"-     : [Deleted lines {deletionStart}-{deletionEnd}]");
            }
        }
        
        // Show context after deletion (lines after deletion point)
        for (int i = deletionStartLine; i <= contextEnd && i < newLines.Length; i++)
        {
            var lineNumber = i + 1; // Convert to 1-based for display (no adjustment needed - these are new line positions)
            context.Add($" {lineNumber,4}: {newLines[i]}");
        }
        
        return context.ToArray();
    }

    private AIOptimizedResponse<DeleteLinesResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<DeleteLinesResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "DELETE_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<DeleteLinesResult>
            {
                Results = new DeleteLinesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                },
                Summary = "Deletion failed",
                Count = 0
            }
        };
    }

    private AIOptimizedResponse<DeleteLinesResult> CreateSuccessResponse(DeleteLinesResult result)
    {
        return new AIOptimizedResponse<DeleteLinesResult>
        {
            Success = true,
            Data = new AIResponseData<DeleteLinesResult>
            {
                Results = result,
                Summary = $"Deleted lines {result.StartLine}-{result.EndLine} ({result.LinesDeleted} lines removed)",
                Count = result.LinesDeleted
            }
        };
    }
}
