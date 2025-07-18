using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class GetCallHierarchyTool
{
    private readonly ILogger<GetCallHierarchyTool> _logger;
    private readonly CodeAnalysisService _workspaceService;

    public GetCallHierarchyTool(ILogger<GetCallHierarchyTool> logger, CodeAnalysisService workspaceService)
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

            // Find a suitable symbol - either at the exact position or the enclosing method/property
            var symbol = await FindCallableSymbolAsync(document, position, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return new
                {
                    success = false,
                    error = "No method or property found at or near the specified position. Try positioning the cursor on the method/property name."
                };
            }

            _logger.LogInformation("Found callable symbol: {SymbolName} of type {SymbolType} at {Line}:{Column}", 
                symbol.Name, symbol.GetType().Name, line, column);

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

    /// <summary>
    /// Find a callable symbol (method/property) at or near the specified position
    /// This is more forgiving than FindSymbolAtPositionAsync and will look for enclosing methods
    /// </summary>
    private async Task<ISymbol?> FindCallableSymbolAsync(
        Document document, 
        int position, 
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // First try to find symbol at exact position
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel, position, document.Project.Solution.Workspace, cancellationToken);
        
        if (symbol is IMethodSymbol or IPropertySymbol)
        {
            _logger.LogDebug("Found callable symbol at exact position: {Symbol}", symbol.Name);
            return symbol;
        }

        // If not found or not a callable symbol, try to find the enclosing method/property
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return null;

        var token = root.FindToken(position);
        var node = token.Parent;

        // Walk up the syntax tree to find a method or property declaration
        while (node != null)
        {
            ISymbol? declaredSymbol = null;
            
            switch (node)
            {
                case MethodDeclarationSyntax methodDecl:
                    declaredSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    break;
                    
                case PropertyDeclarationSyntax propDecl:
                    declaredSymbol = semanticModel.GetDeclaredSymbol(propDecl);
                    break;
                    
                case ConstructorDeclarationSyntax ctorDecl:
                    declaredSymbol = semanticModel.GetDeclaredSymbol(ctorDecl);
                    break;
                    
                case AccessorDeclarationSyntax accessor:
                    declaredSymbol = semanticModel.GetDeclaredSymbol(accessor);
                    break;
            }
            
            if (declaredSymbol != null)
            {
                _logger.LogDebug("Found enclosing callable symbol: {Symbol}", declaredSymbol.Name);
                return declaredSymbol;
            }
            
            node = node.Parent;
        }

        // Last resort: try to find any method/property on the same line
        var lineSpan = (await document.GetTextAsync(cancellationToken)).Lines.GetLineFromPosition(position).Span;
        var nodesOnLine = root.DescendantNodes().Where(n => lineSpan.IntersectsWith(n.Span));
        
        foreach (var lineNode in nodesOnLine)
        {
            ISymbol? lineSymbol = null;
            switch (lineNode)
            {
                case MethodDeclarationSyntax methodDecl:
                    lineSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    break;
                case PropertyDeclarationSyntax propDecl:
                    lineSymbol = semanticModel.GetDeclaredSymbol(propDecl);
                    break;
                case ConstructorDeclarationSyntax ctorDecl:
                    lineSymbol = semanticModel.GetDeclaredSymbol(ctorDecl);
                    break;
            }
            
            if (lineSymbol != null)
            {
                _logger.LogInformation("Found callable symbol on same line: {Symbol}", lineSymbol.Name);
                return lineSymbol;
            }
        }

        _logger.LogWarning("No callable symbol found at position {Position} or on line", position);
        return null;
    }
}