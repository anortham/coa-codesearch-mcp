using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class GetImplementationsTool
{
    private readonly ILogger<GetImplementationsTool> _logger;
    private readonly CodeAnalysisService _workspaceService;

    public GetImplementationsTool(ILogger<GetImplementationsTool> logger, CodeAnalysisService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetImplementations request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            var implementations = new List<ImplementationInfo>();
            var solution = document.Project.Solution;

            // Handle different symbol types
            if (symbol is INamedTypeSymbol namedType)
            {
                // Find derived types
                var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: cancellationToken);
                foreach (var derivedType in derivedTypes)
                {
                    await AddSymbolLocations(derivedType, "Derived Class", implementations, cancellationToken);
                }

                // Find interface implementations
                if (namedType.TypeKind == TypeKind.Interface)
                {
                    var implementingTypes = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: cancellationToken);
                    foreach (var implementingType in implementingTypes)
                    {
                        await AddSymbolLocations(implementingType, "Implementation", implementations, cancellationToken);
                    }
                }
            }
            else if (symbol is IMethodSymbol method)
            {
                // Find overrides and implementations
                if (method.IsAbstract || method.IsVirtual || method.ContainingType.TypeKind == TypeKind.Interface)
                {
                    var overrides = await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: cancellationToken);
                    foreach (var overrideMethod in overrides)
                    {
                        await AddSymbolLocations(overrideMethod, "Override", implementations, cancellationToken);
                    }
                }

                // Find interface implementations
                var implementations2 = await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: cancellationToken);
                foreach (var impl in implementations2)
                {
                    if (!SymbolEqualityComparer.Default.Equals(impl, method)) // Don't include self
                    {
                        await AddSymbolLocations(impl, "Implementation", implementations, cancellationToken);
                    }
                }
            }
            else if (symbol is IPropertySymbol property)
            {
                // Find overrides and implementations
                if (property.IsAbstract || property.IsVirtual || property.ContainingType.TypeKind == TypeKind.Interface)
                {
                    var overrides = await SymbolFinder.FindOverridesAsync(property, solution, cancellationToken: cancellationToken);
                    foreach (var overrideProperty in overrides)
                    {
                        await AddSymbolLocations(overrideProperty, "Override", implementations, cancellationToken);
                    }
                }

                // Find interface implementations
                var implementations2 = await SymbolFinder.FindImplementationsAsync(property, solution, cancellationToken: cancellationToken);
                foreach (var impl in implementations2)
                {
                    if (!SymbolEqualityComparer.Default.Equals(impl, property)) // Don't include self
                    {
                        await AddSymbolLocations(impl, "Implementation", implementations, cancellationToken);
                    }
                }
            }
            else if (symbol is IEventSymbol eventSymbol)
            {
                // Find overrides and implementations
                if (eventSymbol.IsAbstract || eventSymbol.IsVirtual || eventSymbol.ContainingType.TypeKind == TypeKind.Interface)
                {
                    var overrides = await SymbolFinder.FindOverridesAsync(eventSymbol, solution, cancellationToken: cancellationToken);
                    foreach (var overrideEvent in overrides)
                    {
                        await AddSymbolLocations(overrideEvent, "Override", implementations, cancellationToken);
                    }
                }
            }

            // Group by containing type
            var groupedImplementations = implementations
                .GroupBy(i => i.ContainingType)
                .Select(g => new
                {
                    containingType = g.Key,
                    implementations = g.OrderBy(i => i.MemberName).ToArray()
                })
                .OrderBy(g => g.containingType)
                .ToArray();

            return new
            {
                success = true,
                symbol = new
                {
                    name = symbol.Name,
                    kind = symbol.Kind.ToString(),
                    containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
                    isAbstract = symbol.IsAbstract,
                    isVirtual = symbol.IsVirtual,
                    isInterface = symbol.ContainingType?.TypeKind == TypeKind.Interface
                },
                totalImplementations = implementations.Count,
                implementationsByType = groupedImplementations
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetImplementations");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private async Task AddSymbolLocations(ISymbol symbol, string implementationType, List<ImplementationInfo> implementations, CancellationToken cancellationToken)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource && location.SourceTree != null)
            {
                var lineSpan = location.GetLineSpan();
                
                implementations.Add(new ImplementationInfo
                {
                    FilePath = location.SourceTree.FilePath,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    PreviewText = "", // Will be filled later if we need the text
                    ImplementationType = implementationType,
                    ContainingType = symbol.ContainingType?.ToDisplayString() ?? "",
                    MemberName = symbol.Name,
                    MemberKind = symbol.Kind.ToString()
                });
            }
        }
    }

    private class ImplementationInfo : LocationInfo
    {
        public string ImplementationType { get; init; } = "";
        public string ContainingType { get; init; } = "";
        public string MemberName { get; init; } = "";
        public string MemberKind { get; init; } = "";
    }
}