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
/// Tool for inserting text at a specific line in a file without requiring prior file reading.
/// This enables editing files using line-precise positioning from search results.
/// </summary>
public class InsertAtLineTool : CodeSearchToolBase<InsertAtLineParameters, AIOptimizedResponse<InsertAtLineResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<InsertAtLineTool> _logger;

    public InsertAtLineTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        ILogger<InsertAtLineTool> logger) : base(serviceProvider)
    {
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public override string Name => ToolNames.InsertAtLine;
    public override string Description => 
        "INSERT CODE WITHOUT READ - Insert text at specific line numbers with 100% accuracy. " +
        "Uses line-precise positioning from search results. Preserves indentation automatically. " +
        "USAGE TIP: Provide content without leading spaces to get automatic indentation, or with spaces to control indentation manually. " +
        "Essential for dogfooding - modify CodeSearch using CodeSearch itself!";
    public override ToolCategory Category => ToolCategory.Refactoring;

    protected override async Task<AIOptimizedResponse<InsertAtLineResult>> ExecuteInternalAsync(
        InsertAtLineParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and resolve file path
            var filePath = ValidateAndResolvePath(parameters.FilePath);
            
            // Validate line number and content
            ValidateInsertParameters(parameters);
            
            // Read current file content
            string[] lines;
            Encoding fileEncoding;
            (lines, fileEncoding) = await ReadFileWithEncodingAsync(filePath, cancellationToken);
            
            // Validate line number against actual file content
            if (parameters.LineNumber > lines.Length + 1)
            {
                return CreateErrorResponse(
                    $"Line number {parameters.LineNumber} exceeds file length ({lines.Length} lines). " +
                    $"Valid range: 1-{lines.Length + 1}");
            }
            
            // Detect indentation using centralized consistency-aware algorithm
            string indentation = "";
            if (parameters.PreserveIndentation)
            {
                indentation = FileLineUtilities.DetectIndentationForInsertion(lines, parameters.LineNumber - 1);
            }
            
            // Prepare content with proper indentation
            var contentLines = parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var indentedContent = ApplyIndentation(contentLines, indentation);
            
            // Insert content at specified line
            var newLines = InsertLinesAt(lines, parameters.LineNumber - 1, indentedContent);
            
            // Write back to file with original encoding
            await File.WriteAllLinesAsync(filePath, newLines, fileEncoding, cancellationToken);
            
            // Generate context for verification
            var contextLines = GenerateContext(newLines, parameters.LineNumber - 1, 
                indentedContent.Length, parameters.ContextLines);
            
            var result = new InsertAtLineResult
            {
                Success = true,
                FilePath = filePath,
                InsertedAtLine = parameters.LineNumber,
                LinesInserted = indentedContent.Length,
                ContextLines = contextLines,
                DetectedIndentation = string.IsNullOrEmpty(indentation) ? "none" : $"'{indentation}'",
                TotalFileLines = newLines.Length
            };
            
            _logger.LogDebug("Successfully inserted {LineCount} lines at line {LineNumber} in {FilePath}",
                indentedContent.Length, parameters.LineNumber, filePath);
            
            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert content at line {LineNumber} in {FilePath}",
                parameters.LineNumber, parameters.FilePath);
            
            return CreateErrorResponse($"Insertion failed: {ex.Message}");
        }
    }

    private string ValidateAndResolvePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty");
        }

        // Resolve to absolute path
        var resolvedPath = Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);
        
        // Verify file exists
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"File not found: {resolvedPath}");
        }
        
        return resolvedPath;
    }


    private void ValidateInsertParameters(InsertAtLineParameters parameters)
    {
        if (parameters.LineNumber < 1)
        {
            throw new ArgumentException("Line number must be 1 or greater");
        }

        if (string.IsNullOrEmpty(parameters.Content))
        {
            throw new ArgumentException("Content cannot be null or empty");
        }

        if (parameters.ContextLines < 0 || parameters.ContextLines > 20)
        {
            throw new ArgumentException("Context lines must be between 0 and 20");
        }
    }

    private async Task<(string[] lines, Encoding encoding)> ReadFileWithEncodingAsync(
        string filePath, CancellationToken cancellationToken)
    {
        // Use shared utility for consistent line handling
        return await FileLineUtilities.ReadFileWithEncodingAsync(filePath, cancellationToken);
    }


    private string[] ApplyIndentation(string[] contentLines, string indentation)
    {
        return FileLineUtilities.ApplyIndentation(contentLines, indentation);
    }

    private string[] InsertLinesAt(string[] originalLines, int insertIndex, string[] newLines)
    {
        var result = new string[originalLines.Length + newLines.Length];
        
        // Copy lines before insertion point
        Array.Copy(originalLines, 0, result, 0, insertIndex);
        
        // Insert new lines
        Array.Copy(newLines, 0, result, insertIndex, newLines.Length);
        
        // Copy remaining lines after insertion point
        Array.Copy(originalLines, insertIndex, result, insertIndex + newLines.Length, 
            originalLines.Length - insertIndex);
        
        return result;
    }

    private string[] GenerateContext(string[] allLines, int insertIndex, int insertedCount, int contextLines)
    {
        if (contextLines == 0)
            return Array.Empty<string>();
        
        var startIndex = Math.Max(0, insertIndex - contextLines);
        var endIndex = Math.Min(allLines.Length - 1, insertIndex + insertedCount + contextLines - 1);
        var totalLines = endIndex - startIndex + 1;
        
        var context = new string[totalLines];
        for (int i = 0; i < totalLines; i++)
        {
            var lineNumber = startIndex + i + 1; // 1-based
            var isInserted = i >= (insertIndex - startIndex) && i < (insertIndex - startIndex + insertedCount);
            var marker = isInserted ? "→ " : "  ";
            context[i] = $"{lineNumber:000} {marker}{allLines[startIndex + i]}";
        }
        
        return context;
    }

    private AIOptimizedResponse<InsertAtLineResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<InsertAtLineResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "INSERT_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<InsertAtLineResult>
            {
                Results = new InsertAtLineResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    FilePath = ""
                },
                Summary = "Insertion failed",
                Count = 0
            },
        };
    }

    private AIOptimizedResponse<InsertAtLineResult> CreateSuccessResponse(InsertAtLineResult result)
    {
        return new AIOptimizedResponse<InsertAtLineResult>
        {
            Success = true,
            Data = new AIResponseData<InsertAtLineResult>
            {
                Results = result,
                Summary = $"Inserted {result.LinesInserted} lines at line {result.InsertedAtLine}",
                Count = result.LinesInserted
            },
        };
    }
}
