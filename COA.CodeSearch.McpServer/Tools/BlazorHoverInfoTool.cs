using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Get hover information for symbols in Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorHoverInfoTool : ITool
{
    public string ToolName => "blazor_hover_info";
    public string Description => "Get hover information (types, documentation, signatures) for symbols in Blazor (.razor) files";
    public ToolCategory Category => ToolCategory.Analysis;
    
    private readonly ILogger<BlazorHoverInfoTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorHoverInfoTool(
        ILogger<BlazorHoverInfoTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Gets hover information for the symbol at the specified location in a Blazor file
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Blazor HoverInfo request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            // Get hover info from Razor analysis service
            var hoverInfo = await _razorAnalysisService.GetHoverInfoAsync(filePath, line, column, cancellationToken);
            
            if (string.IsNullOrEmpty(hoverInfo))
            {
                return new
                {
                    success = true,
                    hoverInfo = (string?)null,
                    hasInfo = false,
                    location = new
                    {
                        filePath,
                        line,
                        column
                    },
                    message = "No hover information available at the specified position",
                    suggestions = new[]
                    {
                        "Ensure the cursor is positioned on a C# symbol (variable, method, type, etc.)",
                        "HTML elements may have limited hover information",
                        "Hover info is most detailed for C# code within @code blocks or expressions"
                    },
                    metadata = new
                    {
                        tool = "blazor_hover_info",
                        languageServer = "rzls",
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            // Parse and format the hover information
            var formattedInfo = FormatHoverInfo(hoverInfo);

            var result = new
            {
                success = true,
                hoverInfo = formattedInfo.content,
                hasInfo = true,
                type = formattedInfo.type,
                location = new
                {
                    filePath,
                    line,
                    column
                },
                raw = hoverInfo, // Include raw response for debugging
                metadata = new
                {
                    tool = "blazor_hover_info",
                    languageServer = "rzls",
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully retrieved hover info for {FilePath} at {Line}:{Column}", filePath, line, column);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor HoverInfo operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor HoverInfo for {FilePath} at {Line}:{Column}", filePath, line, column);
            return CreateErrorResponse($"Error getting hover info: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private (string content, string type) FormatHoverInfo(string rawHoverInfo)
    {
        try
        {
            // The hover info from LSP might be in various formats (markdown, plain text, etc.)
            // Try to extract useful information and categorize it

            if (string.IsNullOrWhiteSpace(rawHoverInfo))
            {
                return ("No information available", "none");
            }

            var info = rawHoverInfo.Trim();

            // Detect common patterns to categorize the hover info
            string infoType = "unknown";
            
            if (info.Contains("class ") || info.Contains("interface ") || info.Contains("struct "))
            {
                infoType = "type";
            }
            else if (info.Contains("void ") || info.Contains("public ") || info.Contains("private ") || 
                     info.Contains("(") && info.Contains(")"))
            {
                infoType = "method";
            }
            else if (info.Contains("property") || info.Contains("get;") || info.Contains("set;"))
            {
                infoType = "property";
            }
            else if (info.Contains("field") || info.Contains("const "))
            {
                infoType = "field";
            }
            else if (info.Contains("namespace"))
            {
                infoType = "namespace";
            }
            else if (info.Contains("parameter"))
            {
                infoType = "parameter";
            }
            else if (info.Contains("local variable"))
            {
                infoType = "variable";
            }

            // Clean up the content for better readability
            var cleanedContent = CleanHoverContent(info);

            return (cleanedContent, infoType);
        }
        catch (Exception ex)
        {
            return ($"Error formatting hover info: {ex.Message}", "error");
        }
    }

    private string CleanHoverContent(string content)
    {
        try
        {
            // Remove excessive whitespace and clean up formatting
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                               .Select(line => line.Trim())
                               .Where(line => !string.IsNullOrEmpty(line))
                               .ToArray();

            // Join with proper spacing
            return string.Join("\n", lines);
        }
        catch
        {
            return content; // Return original if cleaning fails
        }
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            error = message,
            hoverInfo = (string?)null,
            hasInfo = false,
            metadata = new
            {
                tool = "blazor_hover_info",
                timestamp = DateTime.UtcNow
            }
        };
    }
}