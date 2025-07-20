using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Text;

namespace COA.CodeSearch.McpServer.Tools;

public class GetHoverInfoTool
{
    private readonly ILogger<GetHoverInfoTool> _logger;
    private readonly CodeAnalysisService _workspaceService;
    private readonly TypeScriptHoverInfoTool _tsHoverTool;

    public GetHoverInfoTool(
        ILogger<GetHoverInfoTool> logger, 
        CodeAnalysisService workspaceService,
        TypeScriptHoverInfoTool tsHoverTool)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _tsHoverTool = tsHoverTool;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetHoverInfo request for {FilePath} at {Line}:{Column}", filePath, line, column);

            // Check if this is a TypeScript/JavaScript file
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".ts" || extension == ".tsx" || extension == ".js" || extension == ".jsx" || extension == ".mjs" || extension == ".cjs")
            {
                _logger.LogDebug("Delegating to TypeScript hover info for {FilePath}", filePath);
                return await _tsHoverTool.ExecuteAsync(filePath, line, column, cancellationToken);
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

            // Find the node at the position
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var node = root.FindToken(position).Parent;

            if (node == null)
            {
                return new
                {
                    success = false,
                    error = "No syntax node found at the specified position"
                };
            }

            // Get symbol info
            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            if (symbol == null)
            {
                // Try to get type info if no symbol
                var typeInfo = semanticModel.GetTypeInfo(node, cancellationToken);
                if (typeInfo.Type != null)
                {
                    return new
                    {
                        success = true,
                        hoverInfo = new
                        {
                            type = "type",
                            displayString = typeInfo.Type.ToDisplayString(),
                            kind = typeInfo.Type.TypeKind.ToString(),
                            documentation = GetDocumentation(typeInfo.Type),
                            metadata = GetTypeMetadata(typeInfo.Type)
                        }
                    };
                }

                return new
                {
                    success = false,
                    error = "No symbol or type information found at the specified position"
                };
            }

            // Build hover info
            var hoverInfo = new
            {
                type = "symbol",
                name = symbol.Name,
                kind = symbol.Kind.ToString(),
                displayString = GetSymbolDisplayString(symbol),
                documentation = GetDocumentation(symbol),
                containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
                metadata = GetSymbolMetadata(symbol),
                parameters = GetParameterInfo(symbol),
                returnType = GetReturnType(symbol)
            };

            return new
            {
                success = true,
                hoverInfo = hoverInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetHoverInfo");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private static string GetSymbolDisplayString(ISymbol symbol)
    {
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | 
                          SymbolDisplayMemberOptions.IncludeType | 
                          SymbolDisplayMemberOptions.IncludeAccessibility |
                          SymbolDisplayMemberOptions.IncludeModifiers,
            parameterOptions: SymbolDisplayParameterOptions.IncludeName | 
                            SymbolDisplayParameterOptions.IncludeType | 
                            SymbolDisplayParameterOptions.IncludeDefaultValue,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        return symbol.ToDisplayString(format);
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var sb = new StringBuilder();

        // Extract summary
        var summaryMatch = System.Text.RegularExpressions.Regex.Match(xml, @"<summary>(.*?)</summary>", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (summaryMatch.Success)
        {
            var summary = summaryMatch.Groups[1].Value.Trim();
            summary = System.Text.RegularExpressions.Regex.Replace(summary, @"\s+", " ");
            sb.AppendLine(summary);
        }

        // Extract remarks
        var remarksMatch = System.Text.RegularExpressions.Regex.Match(xml, @"<remarks>(.*?)</remarks>", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (remarksMatch.Success)
        {
            if (sb.Length > 0) sb.AppendLine();
            var remarks = remarksMatch.Groups[1].Value.Trim();
            remarks = System.Text.RegularExpressions.Regex.Replace(remarks, @"\s+", " ");
            sb.AppendLine($"Remarks: {remarks}");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static Dictionary<string, object> GetSymbolMetadata(ISymbol symbol)
    {
        var metadata = new Dictionary<string, object>
        {
            ["IsStatic"] = symbol.IsStatic,
            ["IsAbstract"] = symbol.IsAbstract,
            ["IsVirtual"] = symbol.IsVirtual,
            ["IsOverride"] = symbol.IsOverride,
            ["IsSealed"] = symbol.IsSealed,
            ["IsExtern"] = symbol.IsExtern,
            ["DeclaredAccessibility"] = symbol.DeclaredAccessibility.ToString()
        };

        if (symbol is IMethodSymbol method)
        {
            metadata["IsAsync"] = method.IsAsync;
            metadata["IsExtensionMethod"] = method.IsExtensionMethod;
            metadata["IsGenericMethod"] = method.IsGenericMethod;
            metadata["Arity"] = method.Arity;
        }
        else if (symbol is IPropertySymbol property)
        {
            metadata["IsIndexer"] = property.IsIndexer;
            metadata["IsReadOnly"] = property.IsReadOnly;
            metadata["IsWriteOnly"] = property.IsWriteOnly;
        }
        else if (symbol is IFieldSymbol field)
        {
            metadata["IsConst"] = field.IsConst;
            metadata["IsReadOnly"] = field.IsReadOnly;
            metadata["IsVolatile"] = field.IsVolatile;
        }
        else if (symbol is INamedTypeSymbol type)
        {
            metadata["TypeKind"] = type.TypeKind.ToString();
            metadata["IsGenericType"] = type.IsGenericType;
            metadata["Arity"] = type.Arity;
            metadata["IsValueType"] = type.IsValueType;
            metadata["IsReferenceType"] = type.IsReferenceType;
        }

        return metadata;
    }

    private static Dictionary<string, object> GetTypeMetadata(ITypeSymbol type)
    {
        return new Dictionary<string, object>
        {
            ["TypeKind"] = type.TypeKind.ToString(),
            ["IsValueType"] = type.IsValueType,
            ["IsReferenceType"] = type.IsReferenceType,
            ["IsAnonymousType"] = type.IsAnonymousType,
            ["IsTupleType"] = type.IsTupleType,
            ["IsNullable"] = type.NullableAnnotation == NullableAnnotation.Annotated,
            ["SpecialType"] = type.SpecialType.ToString()
        };
    }

    private static object[]? GetParameterInfo(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            return method.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type.ToDisplayString(),
                isOptional = p.IsOptional,
                isParams = p.IsParams,
                isRef = p.RefKind == RefKind.Ref,
                isOut = p.RefKind == RefKind.Out,
                isIn = p.RefKind == RefKind.In,
                hasDefaultValue = p.HasExplicitDefaultValue,
                defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
            }).ToArray();
        }
        else if (symbol is IPropertySymbol property && property.IsIndexer)
        {
            return property.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type.ToDisplayString(),
                isOptional = p.IsOptional,
                isParams = p.IsParams,
                isRef = p.RefKind == RefKind.Ref,
                isOut = p.RefKind == RefKind.Out,
                isIn = p.RefKind == RefKind.In,
                hasDefaultValue = p.HasExplicitDefaultValue,
                defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
            }).ToArray();
        }

        return null;
    }

    private static string? GetReturnType(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.ReturnType.ToDisplayString(),
            IPropertySymbol property => property.Type.ToDisplayString(),
            IFieldSymbol field => field.Type.ToDisplayString(),
            IEventSymbol @event => @event.Type.ToDisplayString(),
            ILocalSymbol local => local.Type.ToDisplayString(),
            IParameterSymbol parameter => parameter.Type.ToDisplayString(),
            _ => null
        };
    }
}