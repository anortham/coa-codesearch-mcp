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
public class ReplaceLinesTool : CodeSearchToolBase<ReplaceLinesParameters, AIOptimizedResponse<ReplaceLinesResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<ReplaceLinesTool> _logger;

    public ReplaceLinesTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        ILogger<ReplaceLinesTool> logger) : base(serviceProvider)
    {
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public override string Name => ToolNames.ReplaceLines;
    public override string Description => 
        "REPLACE LINES WITHOUT READ - Replace line ranges with new content using precise line positioning. " +
        "Perfect for method body replacement, config updates, or block edits. " +
        "Supports single line (StartLine only) or range (StartLine to EndLine). " +
        "Preserves indentation automatically and provides context verification.";
    public override ToolCategory Category => ToolCategory.Refactoring;

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
            
            // Read current file content
            string[] lines;
            Encoding fileEncoding;
            (lines, fileEncoding) = await ReadFileWithEncodingAsync(filePath, cancellationToken);
            
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
                    $"StartLine {parameters.StartLine} cannot be greater than EndLine {parameters.EndLine}");
            }
            
            // Capture original content for undo purposes
            var originalLines = lines.Skip(startLine).Take(endLine - startLine + 1).ToArray();
            var originalContent = string.Join(Environment.NewLine, originalLines);
            
            // Detect indentation from surrounding lines
            string indentation = "";
            if (parameters.PreserveIndentation)
            {
                indentation = DetectIndentation(lines, startLine, endLine);
            }
            
            // Prepare replacement content with proper indentation
            var contentLines = string.IsNullOrEmpty(parameters.Content) 
                ? new string[0] 
                : parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var indentedContent = ApplyIndentation(contentLines, indentation);
            
            // Perform the replacement
            var newLines = ReplaceLinesAt(lines, startLine, endLine, indentedContent);
            
            // Write back to file with original encoding
            await File.WriteAllLinesAsync(filePath, newLines, fileEncoding, cancellationToken);
            
            // Generate context for verification
            var contextLines = GenerateContext(newLines, startLine, indentedContent.Length, parameters.ContextLines);
            
            var result = new ReplaceLinesResult
            {
                Success = true,
                FilePath = filePath,
                StartLine = parameters.StartLine,
                EndLine = parameters.EndLine ?? parameters.StartLine,
                LinesRemoved = originalLines.Length,
                LinesAdded = indentedContent.Length,
                ContextLines = contextLines,
                OriginalContent = originalContent
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


    private string DetectIndentation(string[] lines, int startLine, int endLine)
    {
        // Look at surrounding lines to detect indentation
        var linesToCheck = new List<int>();
        
        // Add line before range
        if (startLine > 0)
            linesToCheck.Add(startLine - 1);
            
        // Add line after range
        if (endLine + 1 < lines.Length)
            linesToCheck.Add(endLine + 1);
            
        // Add original lines in range (for context)
        for (int i = startLine; i <= endLine && i < lines.Length; i++)
            linesToCheck.Add(i);

        foreach (var lineIndex in linesToCheck)
        {
            if (lineIndex >= 0 && lineIndex < lines.Length)
            {
                var line = lines[lineIndex];
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var match = Regex.Match(line, @"^(\s*)");
                    return match.Groups[1].Value;
                }
            }
        }

        return ""; // No indentation detected
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