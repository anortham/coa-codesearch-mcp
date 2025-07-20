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

            // Find and open the TypeScript project
            var directory = Path.GetDirectoryName(filePath);
            var tsProjects = await _tsService.DetectTypeScriptProjectsAsync(directory ?? ".", cancellationToken);
            
            if (tsProjects.Count > 0)
            {
                var projectPath = tsProjects.FirstOrDefault(p => 
                    filePath.StartsWith(Path.GetDirectoryName(p) ?? "", StringComparison.OrdinalIgnoreCase));
                    
                if (projectPath != null)
                {
                    await _tsService.OpenProjectAsync(projectPath, cancellationToken);
                }
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