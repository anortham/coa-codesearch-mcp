using COA.Roslyn.McpServer.Models;
using COA.Roslyn.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace COA.Roslyn.McpServer.Tools;

public class GetCallHierarchyTool
{
    private readonly ILogger<GetCallHierarchyTool> _logger;
    private readonly RoslynWorkspaceService _workspaceService;

    public GetCallHierarchyTool(ILogger<GetCallHierarchyTool> logger, RoslynWorkspaceService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        string direction = "both",
        int maxDepth = 2,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetCallHierarchy request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            // Only methods, properties, and constructors can have call hierarchies
            if (symbol is not IMethodSymbol && symbol is not IPropertySymbol)
            {
                return new
                {
                    success = false,
                    error = $"Symbol '{symbol.Name}' is not a method or property. Call hierarchy is only available for methods, constructors, and properties."
                };
            }

            var solution = document.Project.Solution;
            var incomingCalls = new List<CallHierarchyItem>();
            var outgoingCalls = new List<CallHierarchyItem>();

            // Get incoming calls (who calls this method)
            if (direction == "incoming" || direction == "both")
            {
                await GetIncomingCallsAsync(symbol, solution, incomingCalls, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
            }

            // Get outgoing calls (what this method calls)
            if (direction == "outgoing" || direction == "both")
            {
                await GetOutgoingCallsAsync(symbol, solution, outgoingCalls, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
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
                direction = direction,
                maxDepth = maxDepth,
                incomingCalls = incomingCalls.ToArray(),
                outgoingCalls = outgoingCalls.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetCallHierarchy");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private async Task GetIncomingCallsAsync(
        ISymbol targetSymbol,
        Solution solution,
        List<CallHierarchyItem> results,
        int remainingDepth,
        HashSet<ISymbol> visited,
        CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0 || !visited.Add(targetSymbol))
            return;

        var callers = await SymbolFinder.FindCallersAsync(targetSymbol, solution, cancellationToken);
        
        foreach (var caller in callers)
        {
            var callerSymbol = caller.CallingSymbol;
            if (callerSymbol == null)
                continue;

            var locations = new List<LocationInfo>();
            foreach (var location in caller.Locations)
            {
                if (location.SourceTree != null)
                {
                    var doc = solution.GetDocument(location.SourceTree);
                    if (doc != null)
                    {
                        var text = await doc.GetTextAsync(cancellationToken);
                        var lineSpan = location.GetLineSpan();
                        var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

                        locations.Add(new LocationInfo
                        {
                            FilePath = doc.FilePath ?? location.SourceTree.FilePath,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            EndColumn = lineSpan.EndLinePosition.Character + 1,
                            PreviewText = lineText.Trim()
                        });
                    }
                }
            }

            var item = new CallHierarchyItem
            {
                Symbol = new SymbolInfo
                {
                    Name = callerSymbol.Name,
                    Kind = callerSymbol.Kind.ToString(),
                    ContainerName = callerSymbol.ContainingType?.ToDisplayString() ?? callerSymbol.ContainingNamespace?.ToDisplayString() ?? ""
                },
                Locations = locations,
                Children = new List<CallHierarchyItem>()
            };

            results.Add(item);

            // Recursively get callers of the caller
            if (remainingDepth > 1)
            {
                await GetIncomingCallsAsync(callerSymbol, solution, item.Children, remainingDepth - 1, visited, cancellationToken);
            }
        }
    }

    private async Task GetOutgoingCallsAsync(
        ISymbol targetSymbol,
        Solution solution,
        List<CallHierarchyItem> results,
        int remainingDepth,
        HashSet<ISymbol> visited,
        CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0 || !visited.Add(targetSymbol))
            return;

        // Get the syntax nodes for this symbol
        var syntaxRefs = targetSymbol.DeclaringSyntaxReferences;
        
        foreach (var syntaxRef in syntaxRefs)
        {
            var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
            var semanticModel = await solution.GetDocument(syntaxRef.SyntaxTree)?.GetSemanticModelAsync(cancellationToken);
            
            if (semanticModel == null)
                continue;

            // Find all invocations in the method body
            var invocations = syntaxNode.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
                if (invokedSymbol == null)
                    continue;

                // Skip if we've already processed this symbol
                if (results.Any(r => SymbolEqualityComparer.Default.Equals(r.Symbol.GetSymbol(), invokedSymbol)))
                    continue;

                var span = invocation.Span;
                var lineSpan = syntaxRef.SyntaxTree.GetLineSpan(span);
                var text = await syntaxRef.SyntaxTree.GetTextAsync(cancellationToken);
                var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

                var item = new CallHierarchyItem
                {
                    Symbol = new SymbolInfo
                    {
                        Name = invokedSymbol.Name,
                        Kind = invokedSymbol.Kind.ToString(),
                        ContainerName = invokedSymbol.ContainingType?.ToDisplayString() ?? invokedSymbol.ContainingNamespace?.ToDisplayString() ?? ""
                    },
                    Locations = new List<LocationInfo>
                    {
                        new LocationInfo
                        {
                            FilePath = syntaxRef.SyntaxTree.FilePath,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            EndColumn = lineSpan.EndLinePosition.Character + 1,
                            PreviewText = lineText.Trim()
                        }
                    },
                    Children = new List<CallHierarchyItem>()
                };

                results.Add(item);

                // Recursively get calls from the invoked method
                if (remainingDepth > 1)
                {
                    await GetOutgoingCallsAsync(invokedSymbol, solution, item.Children, remainingDepth - 1, visited, cancellationToken);
                }
            }
        }
    }

    private class CallHierarchyItem
    {
        public SymbolInfo Symbol { get; set; } = new();
        public List<LocationInfo> Locations { get; set; } = new();
        public List<CallHierarchyItem> Children { get; set; } = new();
    }

    private class SymbolInfo
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ContainerName { get; set; } = "";
        
        private ISymbol? _symbol;
        
        public void SetSymbol(ISymbol symbol)
        {
            _symbol = symbol;
        }
        
        public ISymbol? GetSymbol()
        {
            return _symbol;
        }
    }
}