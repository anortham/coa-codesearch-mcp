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
    private readonly UnifiedFileEditService _fileEditService;
    private readonly ILogger<InsertAtLineTool> _logger;

    /// <summary>
    /// Initializes a new instance of the InsertAtLineTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="fileEditService">Unified file edit service</param>
    /// <param name="logger">Logger instance</param>
    public InsertAtLineTool(
        IServiceProvider serviceProvider,
        IPathResolutionService pathResolutionService,
        UnifiedFileEditService fileEditService,
        ILogger<InsertAtLineTool> logger) : base(serviceProvider, logger)
    {
        _pathResolutionService = pathResolutionService;
        _fileEditService = fileEditService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.InsertAtLine;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "INSERT CODE WITHOUT READ - Insert text at specific line numbers with 100% accuracy. " +
        "Uses line-precise positioning from search results. Preserves indentation automatically. " +
        "USAGE TIP: Provide content without leading spaces to get automatic indentation, or with spaces to control indentation manually. " +
        "Essential for dogfooding - modify CodeSearch using CodeSearch itself!";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Refactoring;

    /// <summary>
    /// Executes the insert at line operation to add content at a specific line number.
    /// </summary>
    /// <param name="parameters">Insert at line parameters including file path, line number, and content</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Insert at line results with insertion details and context</returns>
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
            
            // Use UnifiedFileEditService for reliable, concurrent insertion
            var editResult = await _fileEditService.InsertAtLineAsync(
                filePath,
                parameters.LineNumber,
                parameters.Content,
                parameters.PreserveIndentation,
                cancellationToken);

            if (!editResult.Success)
            {
                return CreateErrorResponse(editResult.ErrorMessage ?? "Insert operation failed");
            }

            // Generate context for verification
            var modifiedLines = FileLineUtilities.SplitLines(editResult.ModifiedContent ?? "");
            var insertedLineCount = parameters.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
            var contextLines = GenerateContext(modifiedLines, parameters.LineNumber - 1, 
                insertedLineCount, parameters.ContextLines);
            
            var result = new InsertAtLineResult
            {
                Success = true,
                FilePath = filePath,
                InsertedAtLine = parameters.LineNumber,
                LinesInserted = insertedLineCount,
                ContextLines = contextLines,
                DetectedIndentation = editResult.DetectedIndentation ?? "none", // Use actual detected indentation
                TotalFileLines = modifiedLines.Length
            };
            
            _logger.LogDebug("Successfully inserted {LineCount} lines at line {LineNumber} in {FilePath}",
                insertedLineCount, parameters.LineNumber, filePath);
            
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