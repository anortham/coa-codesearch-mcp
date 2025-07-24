using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class GoToDefinitionTool : ITool
{
    public string ToolName => "go_to_definition";
    public string Description => "Navigate to symbol definitions instantly - works across entire solutions for C# and TypeScript";
    public ToolCategory Category => ToolCategory.Navigation;
    private readonly ILogger<GoToDefinitionTool> _logger;
    private readonly CodeAnalysisService _workspaceService;
    private readonly TypeScriptGoToDefinitionTool? _typeScriptTool;

    public GoToDefinitionTool(
        ILogger<GoToDefinitionTool> logger, 
        CodeAnalysisService workspaceService,
        TypeScriptGoToDefinitionTool? typeScriptTool = null)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _typeScriptTool = typeScriptTool;
    }

    /// <summary>
    /// Navigates to the definition of the symbol at the specified location
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GoToDefinition request for {FilePath} at {Line}:{Column}", filePath, line, column);

            // Check if this is a TypeScript file
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (IsTypeScriptFile(extension))
            {
                if (_typeScriptTool != null)
                {
                    _logger.LogInformation("Delegating to TypeScript GoToDefinition tool");
                    return await _typeScriptTool.GoToDefinitionAsync(filePath, line, column, cancellationToken);
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = "TypeScript analysis is not available"
                    };
                }
            }

            // Get the document
            var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
            if (document == null)
            {
                return new
                {
                    success = false,
                    error = $"Could not find document: {filePath}"
                };
            }

            // Get the source text and find the position
            var sourceText = await document.GetTextAsync(cancellationToken);
            var position = sourceText.Lines.GetPosition(new Microsoft.CodeAnalysis.Text.LinePosition(line - 1, column - 1));

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return new
                {
                    success = false,
                    error = "Could not get semantic model for document"
                };
            }

            // Find the symbol at the position
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken);
            if (symbol == null)
            {
                return new
                {
                    success = false,
                    error = "No symbol found at the specified position"
                };
            }

            // Get the definition locations
            var definitions = new List<LocationInfo>();
            
            foreach (var location in symbol.Locations)
            {
                if (location.IsInSource && location.SourceTree != null)
                {
                    var lineSpan = location.GetLineSpan();
                    var definitionDoc = document.Project.Solution.GetDocument(location.SourceTree);
                    
                    if (definitionDoc != null)
                    {
                        var text = await definitionDoc.GetTextAsync(cancellationToken);
                        var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();
                        
                        definitions.Add(new LocationInfo
                        {
                            FilePath = definitionDoc.FilePath ?? location.SourceTree.FilePath,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            EndColumn = lineSpan.EndLinePosition.Character + 1,
                            PreviewText = lineText.Trim()
                        });
                    }
                }
            }

            // If no source locations, check for metadata
            if (definitions.Count == 0 && symbol.Locations.Any(l => l.IsInMetadata))
            {
                return new
                {
                    success = true,
                    symbol = new
                    {
                        name = symbol.Name,
                        kind = symbol.Kind.ToString(),
                        containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
                        isExternal = true,
                        assemblyName = symbol.ContainingAssembly?.Name
                    },
                    definitions = Array.Empty<LocationInfo>(),
                    message = "Symbol is defined in external assembly"
                };
            }

            return new
            {
                success = true,
                symbol = new
                {
                    name = symbol.Name,
                    kind = symbol.Kind.ToString(),
                    containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? ""
                },
                definitions = definitions.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GoToDefinition");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private bool IsTypeScriptFile(string extension)
    {
        return extension == ".ts" || extension == ".tsx" || extension == ".js" || extension == ".jsx" || extension == ".vue";
    }
}