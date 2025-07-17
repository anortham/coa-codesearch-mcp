using COA.Roslyn.McpServer.Models;
using COA.Roslyn.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace COA.Roslyn.McpServer.Tools;

public class FindReferencesTool
{
    private readonly ILogger<FindReferencesTool> _logger;
    private readonly RoslynWorkspaceService _workspaceService;

    public FindReferencesTool(ILogger<FindReferencesTool> logger, RoslynWorkspaceService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        bool includePotential = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("FindReferences request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            // Find all references
            var references = await SymbolFinder.FindReferencesAsync(
                symbol, 
                document.Project.Solution, 
                cancellationToken);

            var referenceLocations = new List<ReferenceLocation>();
            
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    // Skip if not including potential references
                    if (!includePotential && location.IsCandidateLocation)
                        continue;

                    var refDoc = location.Document;
                    var span = location.Location.SourceSpan;
                    var text = await refDoc.GetTextAsync(cancellationToken);
                    var lineSpan = text.Lines.GetLinePositionSpan(span);
                    var lineText = text.Lines[lineSpan.Start.Line].ToString();

                    referenceLocations.Add(new ReferenceLocation
                    {
                        FilePath = refDoc.FilePath ?? "",
                        Line = lineSpan.Start.Line + 1,
                        Column = lineSpan.Start.Character + 1,
                        EndLine = lineSpan.End.Line + 1,
                        EndColumn = lineSpan.End.Character + 1,
                        PreviewText = lineText.Trim(),
                        IsDefinition = reference.Definition.Locations.Any(l => 
                            l.IsInSource && 
                            l.SourceTree?.FilePath == refDoc.FilePath && 
                            l.SourceSpan == span),
                        IsImplicit = location.IsImplicit,
                        IsPotential = location.IsCandidateLocation
                    });
                }
            }

            // Group by file
            var groupedReferences = referenceLocations
                .GroupBy(r => r.FilePath)
                .Select(g => new
                {
                    filePath = g.Key,
                    references = g.OrderBy(r => r.Line).ThenBy(r => r.Column).ToArray()
                })
                .OrderBy(g => g.filePath)
                .ToArray();

            return new
            {
                success = true,
                symbol = new
                {
                    name = symbol.Name,
                    kind = symbol.Kind.ToString(),
                    containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? ""
                },
                totalReferences = referenceLocations.Count,
                fileCount = groupedReferences.Length,
                referencesByFile = groupedReferences
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindReferences");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private class ReferenceLocation : LocationInfo
    {
        public bool IsDefinition { get; init; }
        public bool IsImplicit { get; init; }
        public bool IsPotential { get; init; }
    }
}