using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Navigate to symbol definitions in Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorGoToDefinitionTool : ITool
{
    public string ToolName => "blazor_go_to_definition";
    public string Description => "Navigate to symbol definitions in Blazor (.razor) files - supports C# code within components";
    public ToolCategory Category => ToolCategory.Navigation;
    
    private readonly ILogger<BlazorGoToDefinitionTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorGoToDefinitionTool(
        ILogger<BlazorGoToDefinitionTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Navigates to the definition of the symbol at the specified location in a Blazor file
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Blazor GoToDefinition request for {FilePath} at {Line}:{Column}", filePath, line, column);

            // Validate file path
            if (string.IsNullOrEmpty(filePath))
            {
                return CreateErrorResponse("File path is required");
            }

            // Check if this is a Razor file
            if (!IsRazorFile(filePath))
            {
                return CreateErrorResponse($"File {filePath} is not a Blazor (.razor) file");
            }

            // Check if file exists
            if (!File.Exists(filePath))
            {
                return CreateErrorResponse($"File not found: {filePath}");
            }

            // Validate position
            if (line < 1 || column < 1)
            {
                return CreateErrorResponse("Line and column must be positive integers (1-based)");
            }

            // Check if Razor analysis service is available
            if (!_razorAnalysisService.IsAvailable)
            {
                // Try to initialize the service
                var initialized = await _razorAnalysisService.InitializeAsync(cancellationToken);
                if (!initialized)
                {
                    return CreateErrorResponse("Razor Language Server is not available. Please install VS Code with the C# extension.");
                }
            }

            // Get definition from Razor analysis service
            var definition = await _razorAnalysisService.GetDefinitionAsync(filePath, line, column, cancellationToken);
            
            if (definition == null)
            {
                return new
                {
                    success = false,
                    message = "No definition found at the specified position",
                    location = $"{filePath}:{line}:{column}",
                    suggestions = new[]
                    {
                        "Ensure the cursor is positioned on a C# symbol (variable, method, type, etc.)",
                        "HTML elements and Razor markup do not support go-to-definition",
                        "Only C# code within @code blocks or expressions supports navigation"
                    }
                };
            }

            // Convert to result format
            var result = new
            {
                success = true,
                definition = new
                {
                    filePath = definition.SourceTree?.FilePath ?? filePath,
                    line = definition.GetLineSpan().StartLinePosition.Line + 1, // Convert to 1-based
                    column = definition.GetLineSpan().StartLinePosition.Character + 1, // Convert to 1-based
                    length = definition.SourceSpan.Length
                },
                sourceLocation = new
                {
                    filePath,
                    line,
                    column
                },
                metadata = new
                {
                    tool = "blazor_go_to_definition",
                    languageServer = "rzls",
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully found definition for {FilePath} at {Line}:{Column} -> {TargetFile}:{TargetLine}:{TargetColumn}",
                filePath, line, column,
                result.definition.filePath, result.definition.line, result.definition.column);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor GoToDefinition operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor GoToDefinition for {FilePath} at {Line}:{Column}", filePath, line, column);
            return CreateErrorResponse($"Error finding definition: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            error = message,
            metadata = new
            {
                tool = "blazor_go_to_definition",
                timestamp = DateTime.UtcNow
            }
        };
    }
}