using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for finding all references to TypeScript symbols
/// </summary>
public class TypeScriptFindReferencesTool
{
    private readonly ILogger<TypeScriptFindReferencesTool> _logger;
    private readonly TypeScriptAnalysisService _tsService;

    public TypeScriptFindReferencesTool(
        ILogger<TypeScriptFindReferencesTool> logger,
        TypeScriptAnalysisService tsService)
    {
        _logger = logger;
        _tsService = tsService;
    }

    public async Task<object> FindReferencesAsync(
        string filePath,
        int line,
        int column,
        bool includeDeclaration = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("TypeScript FindReferences: {File}:{Line}:{Column}", filePath, line, column);

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

            // Find the nearest TypeScript project
            var projectPath = await _tsService.FindNearestTypeScriptProjectAsync(filePath, cancellationToken);
            
            if (projectPath != null)
            {
                _logger.LogInformation("Using TypeScript project at {ProjectPath} for file {FilePath}", projectPath, filePath);
                // Note: OpenProjectAsync is not implemented correctly and has been removed
                // The TypeScript server will infer the project when we open the file
            }
            else
            {
                _logger.LogWarning("No tsconfig.json found for {File}, attempting to use file directly", filePath);
            }

            // Find all references
            var references = await _tsService.FindReferencesAsync(filePath, line, column, cancellationToken);

            if (!includeDeclaration)
            {
                // Filter out the declaration itself
                references = references.Where(r => 
                    r.File != filePath || r.Line != line || r.Offset != column).ToList();
            }

            // Group references by file
            var groupedByFile = references
                .GroupBy(r => r.File)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new
                    {
                        line = r.Line,
                        column = r.Offset,
                        lineText = r.LineText
                    }).ToList()
                );

            return new
            {
                success = true,
                references = references.Select(r => new
                {
                    filePath = r.File,
                    line = r.Line,
                    column = r.Offset,
                    lineText = r.LineText
                }),
                groupedByFile,
                metadata = new
                {
                    totalResults = references.Count,
                    filesAffected = groupedByFile.Count,
                    includeDeclaration
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TypeScript FindReferences");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}