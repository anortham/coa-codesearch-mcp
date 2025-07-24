using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class GetDocumentSymbolsTool : ITool
{
    public string ToolName => "get_document_symbols";
    public string Description => "Get outline of all symbols in a file";
    public ToolCategory Category => ToolCategory.Analysis;
    private readonly ILogger<GetDocumentSymbolsTool> _logger;
    private readonly CodeAnalysisService _workspaceService;

    public GetDocumentSymbolsTool(ILogger<GetDocumentSymbolsTool> logger, CodeAnalysisService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        bool includeMembers = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetDocumentSymbols request for {FilePath}", filePath);

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

            // Get syntax tree and semantic model
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
            {
                return new
                {
                    success = false,
                    error = "Could not get syntax tree for document"
                };
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return new
                {
                    success = false,
                    error = "Could not get semantic model for document"
                };
            }

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var symbols = new List<DocumentSymbol>();

            // Visit all type declarations
            var visitor = new DocumentSymbolVisitor(semanticModel, includeMembers);
            visitor.Visit(root);

            return new
            {
                success = true,
                filePath = filePath,
                symbols = visitor.Symbols.ToArray(),
                namespaces = visitor.Namespaces.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetDocumentSymbols");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private class DocumentSymbolVisitor : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly bool _includeMembers;
        public List<DocumentSymbol> Symbols { get; } = new();
        public List<string> Namespaces { get; } = new();

        public DocumentSymbolVisitor(SemanticModel semanticModel, bool includeMembers)
        {
            _semanticModel = semanticModel;
            _includeMembers = includeMembers;
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var namespaceName = node.Name.ToString();
            if (!Namespaces.Contains(namespaceName))
            {
                Namespaces.Add(namespaceName);
            }
            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            var namespaceName = node.Name.ToString();
            if (!Namespaces.Contains(namespaceName))
            {
                Namespaces.Add(namespaceName);
            }
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            AddTypeSymbol(node, "Class");
            if (_includeMembers)
            {
                base.VisitClassDeclaration(node);
            }
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            AddTypeSymbol(node, "Interface");
            if (_includeMembers)
            {
                base.VisitInterfaceDeclaration(node);
            }
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            AddTypeSymbol(node, "Struct");
            if (_includeMembers)
            {
                base.VisitStructDeclaration(node);
            }
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var span = node.Identifier.Span;
                var lineSpan = node.SyntaxTree.GetLineSpan(span);

                Symbols.Add(new DocumentSymbol
                {
                    Name = symbol.Name,
                    Kind = "Enum",
                    FullName = symbol.ToDisplayString(),
                    ContainerName = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    IsStatic = symbol.IsStatic,
                    IsAbstract = symbol.IsAbstract,
                    IsSealed = symbol.IsSealed,
                    Accessibility = symbol.DeclaredAccessibility.ToString(),
                    Children = new List<DocumentSymbol>()
                });
            }
            
            if (_includeMembers)
            {
                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            AddTypeSymbol(node, node.ClassOrStructKeyword.Text == "class" ? "Record" : "RecordStruct");
            if (_includeMembers)
            {
                base.VisitRecordDeclaration(node);
            }
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_includeMembers)
            {
                AddMemberSymbol(node, "Method", node.Identifier.Text);
            }
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (_includeMembers)
            {
                AddMemberSymbol(node, "Property", node.Identifier.Text);
            }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (_includeMembers)
            {
                foreach (var variable in node.Declaration.Variables)
                {
                    AddMemberSymbol(node, "Field", variable.Identifier.Text);
                }
            }
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            if (_includeMembers)
            {
                AddMemberSymbol(node, "Event", node.Identifier.Text);
            }
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (_includeMembers)
            {
                AddMemberSymbol(node, "Constructor", node.Identifier.Text);
            }
        }

        private void AddTypeSymbol(TypeDeclarationSyntax node, string kind)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var span = node.Identifier.Span;
                var lineSpan = node.SyntaxTree.GetLineSpan(span);

                Symbols.Add(new DocumentSymbol
                {
                    Name = symbol.Name,
                    Kind = kind,
                    FullName = symbol.ToDisplayString(),
                    ContainerName = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    IsStatic = symbol.IsStatic,
                    IsAbstract = symbol.IsAbstract,
                    IsSealed = symbol.IsSealed,
                    Accessibility = symbol.DeclaredAccessibility.ToString(),
                    Children = new List<DocumentSymbol>()
                });
            }
        }

        private void AddMemberSymbol(MemberDeclarationSyntax node, string kind, string name)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && Symbols.Count > 0)
            {
                var parent = Symbols.Last();
                var span = node.Span;
                var lineSpan = node.SyntaxTree.GetLineSpan(span);

                var memberSymbol = new DocumentSymbol
                {
                    Name = name,
                    Kind = kind,
                    FullName = symbol.ToDisplayString(),
                    ContainerName = parent.FullName,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    IsStatic = symbol.IsStatic,
                    IsAbstract = symbol.IsAbstract,
                    IsSealed = symbol.IsSealed,
                    Accessibility = symbol.DeclaredAccessibility.ToString(),
                    Children = new List<DocumentSymbol>()
                };

                // Add return type info for methods and properties
                if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
                {
                    memberSymbol.ReturnType = method.ReturnType.ToDisplayString();
                    memberSymbol.IsAsync = method.IsAsync;
                }
                else if (symbol is IPropertySymbol property)
                {
                    memberSymbol.ReturnType = property.Type.ToDisplayString();
                    memberSymbol.IsReadOnly = property.IsReadOnly;
                    memberSymbol.IsWriteOnly = property.IsWriteOnly;
                }
                else if (symbol is IFieldSymbol field)
                {
                    memberSymbol.ReturnType = field.Type.ToDisplayString();
                    memberSymbol.IsReadOnly = field.IsReadOnly;
                    memberSymbol.IsConst = field.IsConst;
                }

                parent.Children.Add(memberSymbol);
            }
        }
    }

    private class DocumentSymbol
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string FullName { get; set; } = "";
        public string ContainerName { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public string Accessibility { get; set; } = "";
        public string? ReturnType { get; set; }
        public bool? IsAsync { get; set; }
        public bool? IsReadOnly { get; set; }
        public bool? IsWriteOnly { get; set; }
        public bool? IsConst { get; set; }
        public List<DocumentSymbol> Children { get; set; } = new();
    }
}