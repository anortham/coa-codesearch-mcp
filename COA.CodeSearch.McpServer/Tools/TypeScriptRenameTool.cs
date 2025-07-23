using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for renaming TypeScript symbols using the TypeScript Language Service
/// </summary>
public class TypeScriptRenameTool
{
    private readonly ILogger<TypeScriptRenameTool> _logger;
    private readonly ITypeScriptAnalysisService _tsService;
    private static readonly Regex IdentifierRegex = new(@"^[a-zA-Z_$][a-zA-Z0-9_$]*$", RegexOptions.Compiled);

    public TypeScriptRenameTool(
        ILogger<TypeScriptRenameTool> logger,
        ITypeScriptAnalysisService tsService)
    {
        _logger = logger;
        _tsService = tsService;
    }

    public async Task<object> RenameSymbolAsync(
        string filePath,
        int line,
        int column,
        string newName,
        bool preview = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("TypeScript RenameSymbol: {File}:{Line}:{Column} to '{NewName}'", filePath, line, column, newName);

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

            // Validate the new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return new
                {
                    success = false,
                    error = "New name cannot be empty"
                };
            }

            if (!IdentifierRegex.IsMatch(newName))
            {
                return new
                {
                    success = false,
                    error = $"'{newName}' is not a valid TypeScript identifier. Identifiers must start with a letter, underscore, or $ and contain only letters, numbers, underscores, or $."
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
            }
            else
            {
                _logger.LogWarning("No tsconfig.json found for {File}, attempting to use file directly", filePath);
            }

            // Get rename information from tsserver
            var renameInfo = await _tsService.GetRenameInfoAsync(filePath, line, column, cancellationToken);

            if (renameInfo == null)
            {
                return new
                {
                    success = false,
                    error = "Failed to get rename information from TypeScript server"
                };
            }

            dynamic renameData = renameInfo;
            
            // Check if rename is possible
            bool canRename = false;
            try 
            {
                canRename = renameData?.info?.canRename ?? false;
            }
            catch 
            {
                // If we can't access canRename, assume false
            }
            
            if (renameData?.info == null || !canRename)
            {
                var errorMessage = (string?)(renameData?.info?.localizedErrorMessage) ?? "Cannot rename at this location";
                return new
                {
                    success = false,
                    error = errorMessage,
                    info = renameData?.info != null ? new
                    {
                        displayName = (string?)renameData.info.displayName,
                        kind = (string?)renameData.info.kind,
                        kindModifiers = (string?)renameData.info.kindModifiers
                    } : null
                };
            }

            // Process rename locations
            var locations = new List<dynamic>();
            var fileChanges = new Dictionary<string, List<dynamic>>();
            var totalChanges = 0;

            if (renameData?.locs != null)
            {
                foreach (var fileLocs in renameData.locs)
                {
                    var file = (string)fileLocs.file;
                    var changes = new List<dynamic>();

                    if (fileLocs.locs != null)
                    {
                        foreach (var loc in fileLocs.locs)
                        {
                        locations.Add(new
                        {
                            filePath = loc.File,
                            line = loc.Line,
                            column = loc.Offset,
                            lineText = loc.LineText
                        });

                        changes.Add(new
                        {
                            line = loc.Line,
                            column = loc.Offset,
                            oldText = (string)renameData.info.displayName,
                            newText = newName,
                            preview = loc.LineText?.Replace((string)renameData.info.displayName, newName)
                        });

                            totalChanges++;
                        }
                    }

                    if (changes.Any())
                    {
                        fileChanges[file] = changes;
                    }
                }
            }

            // Create response
            var response = new
            {
                success = true,
                preview = preview,
                symbol = new
                {
                    name = (string)(renameData?.info?.displayName ?? ""),
                    fullName = (string)(renameData?.info?.fullDisplayName ?? ""),
                    kind = (string)(renameData?.info?.kind ?? ""),
                    kindModifiers = (string)(renameData?.info?.kindModifiers ?? "")
                },
                newName = newName,
                locations = locations,
                fileChanges = fileChanges.Select(fc => new
                {
                    filePath = fc.Key,
                    changes = fc.Value
                }),
                metadata = new
                {
                    totalChanges = totalChanges,
                    filesAffected = fileChanges.Count,
                    language = "typescript"
                }
            };

            if (!preview)
            {
                // In preview mode, we don't apply changes
                // TypeScript rename requires external tooling or manual file editing
                // This is consistent with the C# rename tool behavior
                var responseWithMessage = new
                {
                    response.success,
                    response.preview,
                    response.symbol,
                    response.newName,
                    response.locations,
                    response.fileChanges,
                    response.metadata,
                    message = "TypeScript rename preview generated. To apply changes, use a TypeScript-aware editor or build tool.",
                    hint = "The MCP server provides rename information but doesn't modify TypeScript files directly. Use VS Code or another TypeScript IDE to apply the rename."
                };
                return responseWithMessage;
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TypeScript RenameSymbol");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}