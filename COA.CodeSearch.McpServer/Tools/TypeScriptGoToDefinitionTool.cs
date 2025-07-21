using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for navigating to TypeScript symbol definitions
/// </summary>
public class TypeScriptGoToDefinitionTool
{
    private readonly ILogger<TypeScriptGoToDefinitionTool> _logger;
    private readonly TypeScriptAnalysisService _tsService;

    public TypeScriptGoToDefinitionTool(
        ILogger<TypeScriptGoToDefinitionTool> logger,
        TypeScriptAnalysisService tsService)
    {
        _logger = logger;
        _tsService = tsService;
    }

    public async Task<object> GoToDefinitionAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("TypeScript GoToDefinition: {File}:{Line}:{Column}", filePath, line, column);

            // Check if TypeScript server is available
            if (!_tsService.IsAvailable)
            {
                _logger.LogWarning("TypeScript server is not available. Node.js may not be installed or TypeScript may not be configured.");
                return new
                {
                    success = false,
                    error = "TypeScript server is not available. Please ensure Node.js is installed and in PATH.",
                    hint = "TypeScript features require Node.js. Install Node.js from https://nodejs.org/ and restart the MCP server."
                };
            }

            // Ensure the file exists
            if (!File.Exists(filePath))
            {
                return new
                {
                    success = false,
                    error = $"File not found: {filePath}"
                };
            }

            // Find the nearest TypeScript project by searching upward
            var projectPath = await _tsService.FindNearestTypeScriptProjectAsync(filePath, cancellationToken);
            
            if (projectPath == null)
            {
                _logger.LogWarning("No tsconfig.json found for {File}, attempting to use file directly", filePath);
                // Try to use the file directly without a project
                // This may work for simple cases but might miss project-specific configurations
            }
            else
            {
                _logger.LogInformation("Using TypeScript project at {ProjectPath} for file {FilePath}", projectPath, filePath);
                // Note: OpenProjectAsync is not implemented correctly and has been removed
                // The TypeScript server will infer the project when we open the file
            }

            // Get the definition
            var definition = await _tsService.GetDefinitionAsync(filePath, line, column, cancellationToken);

            if (definition == null)
            {
                return new
                {
                    success = false,
                    error = "No definition found at the specified location"
                };
            }

            // Read the line at the definition location for preview
            string? previewText = null;
            try
            {
                if (File.Exists(definition.File))
                {
                    var lines = await File.ReadAllLinesAsync(definition.File, cancellationToken);
                    if (definition.Line > 0 && definition.Line <= lines.Length)
                    {
                        previewText = lines[definition.Line - 1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read preview text from {File}", definition.File);
            }

            return new
            {
                success = true,
                definition = new
                {
                    filePath = definition.File,
                    line = definition.Line,
                    column = definition.Offset,
                    previewText = previewText ?? definition.LineText
                },
                metadata = new
                {
                    language = "typescript",
                    fileExtension = Path.GetExtension(definition.File)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TypeScript GoToDefinition");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}