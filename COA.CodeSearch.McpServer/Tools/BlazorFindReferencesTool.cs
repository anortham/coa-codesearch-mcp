using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Find all references to symbols in Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorFindReferencesTool : ITool
{
    public string ToolName => "blazor_find_references";
    public string Description => "Find all references to symbols in Blazor (.razor) files - supports C# symbols within components";
    public ToolCategory Category => ToolCategory.Navigation;
    
    private readonly ILogger<BlazorFindReferencesTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorFindReferencesTool(
        ILogger<BlazorFindReferencesTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Finds all references to the symbol at the specified location in a Blazor file
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        bool includeDeclaration = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Blazor FindReferences request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            // Get references from Razor analysis service
            var references = await _razorAnalysisService.FindReferencesAsync(filePath, line, column, includeDeclaration, cancellationToken);
            
            if (references == null || references.Length == 0)
            {
                return new
                {
                    success = true,
                    references = Array.Empty<object>(),
                    count = 0,
                    sourceLocation = new
                    {
                        filePath,
                        line,
                        column
                    },
                    message = "No references found at the specified position",
                    suggestions = new[]
                    {
                        "Ensure the cursor is positioned on a C# symbol (variable, method, type, etc.)",
                        "HTML elements and Razor markup may not have trackable references",
                        "Only C# code within @code blocks or expressions supports reference finding"
                    },
                    metadata = new
                    {
                        tool = "blazor_find_references",
                        languageServer = "rzls",
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            // Convert references to result format
            var referenceResults = references.Select(reference => new
            {
                filePath = reference.SourceTree?.FilePath ?? "Unknown",
                line = reference.GetLineSpan().StartLinePosition.Line + 1, // Convert to 1-based
                column = reference.GetLineSpan().StartLinePosition.Character + 1, // Convert to 1-based
                endLine = reference.GetLineSpan().EndLinePosition.Line + 1,
                endColumn = reference.GetLineSpan().EndLinePosition.Character + 1,
                length = reference.SourceSpan.Length,
                text = GetTextAroundLocation(reference),
                isDeclaration = reference.IsInMetadata // This is a rough approximation
            }).ToArray();

            var result = new
            {
                success = true,
                references = referenceResults,
                count = referenceResults.Length,
                sourceLocation = new
                {
                    filePath,
                    line,
                    column
                },
                settings = new
                {
                    includeDeclaration
                },
                metadata = new
                {
                    tool = "blazor_find_references",
                    languageServer = "rzls",
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully found {Count} references for {FilePath} at {Line}:{Column}",
                referenceResults.Length, filePath, line, column);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor FindReferences operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor FindReferences for {FilePath} at {Line}:{Column}", filePath, line, column);
            return CreateErrorResponse($"Error finding references: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private string GetTextAroundLocation(Location location)
    {
        try
        {
            if (location.SourceTree?.FilePath == null)
            {
                return "";
            }

            var text = location.SourceTree.GetText();
            var span = location.SourceSpan;
            
            // Get the line containing the location
            var line = text.Lines.GetLineFromPosition(span.Start);
            return line.ToString().Trim();
        }
        catch
        {
            return "";
        }
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            error = message,
            references = Array.Empty<object>(),
            count = 0,
            metadata = new
            {
                tool = "blazor_find_references",
                timestamp = DateTime.UtcNow
            }
        };
    }
}