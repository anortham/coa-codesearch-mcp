using COA.Roslyn.McpServer.Models;
using COA.Roslyn.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.Roslyn.McpServer.Tools;

public class AdvancedSymbolSearchTool
{
    private readonly ILogger<AdvancedSymbolSearchTool> _logger;
    private readonly RoslynWorkspaceService _workspaceService;
    private readonly IConfiguration _configuration;

    public AdvancedSymbolSearchTool(ILogger<AdvancedSymbolSearchTool> logger, RoslynWorkspaceService workspaceService, IConfiguration configuration)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string pattern,
        string workspacePath,
        string[]? kinds = null,
        string[]? accessibility = null,
        bool? isStatic = null,
        bool? isAbstract = null,
        bool? isVirtual = null,
        bool? isOverride = null,
        string? returnType = null,
        string? containingType = null,
        string? containingNamespace = null,
        bool fuzzy = false,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("AdvancedSymbolSearch request for pattern: {Pattern} in {WorkspacePath}", pattern, workspacePath);

            // Get the workspace (reuse existing logic from SearchSymbolsTool)
            Workspace? workspace = null;
            if (workspacePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                workspace = await _workspaceService.GetWorkspaceAsync(workspacePath, cancellationToken);
            }
            else if (workspacePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await _workspaceService.GetProjectAsync(workspacePath, cancellationToken);
                workspace = project?.Solution.Workspace;
            }
            else if (Directory.Exists(workspacePath))
            {
                var slnFiles = Directory.GetFiles(workspacePath, "*.sln");
                if (slnFiles.Length == 1)
                {
                    workspace = await _workspaceService.GetWorkspaceAsync(slnFiles[0], cancellationToken);
                }
            }

            if (workspace == null)
            {
                return new
                {
                    success = false,
                    error = $"Could not load workspace: {workspacePath}"
                };
            }

            var solution = workspace.CurrentSolution;
            var results = new List<Models.SymbolInfo>();

            // Search through all projects
            await Task.Run(async () =>
            {
                var searchTasks = solution.Projects.Select(async project =>
                {
                    try
                    {
                        var compilation = await project.GetCompilationAsync(cancellationToken);
                        if (compilation == null) return;

                        // Get all symbols in the project
                        var allSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
                            project, 
                            name => fuzzy ? name.Contains(pattern, StringComparison.OrdinalIgnoreCase) 
                                          : name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0, 
                            cancellationToken);

                        foreach (var symbol in allSymbols)
                        {
                            if (results.Count >= maxResults) break;

                            // Apply advanced filters
                            if (!PassesAdvancedFilters(symbol, kinds, accessibility, isStatic, isAbstract, 
                                isVirtual, isOverride, returnType, containingType, containingNamespace))
                                continue;

                            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                            if (location == null) continue;

                            var doc = solution.GetDocument(location.SourceTree);
                            if (doc == null) continue;

                            var lineSpan = location.GetLineSpan();
                            var text = await doc.GetTextAsync(cancellationToken);
                            var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

                            lock (results)
                            {
                                if (results.Count < maxResults)
                                {
                                    results.Add(new Models.SymbolInfo
                                    {
                                        Name = symbol.Name,
                                        Kind = symbol.Kind.ToString(),
                                        ContainerName = GetContainerName(symbol),
                                        Documentation = GetDocumentation(symbol),
                                        Location = new LocationInfo
                                        {
                                            FilePath = doc.FilePath ?? location.SourceTree.FilePath,
                                            Line = lineSpan.StartLinePosition.Line + 1,
                                            Column = lineSpan.StartLinePosition.Character + 1,
                                            EndLine = lineSpan.EndLinePosition.Line + 1,
                                            EndColumn = lineSpan.EndLinePosition.Character + 1,
                                            PreviewText = lineText.Trim()
                                        },
                                        Metadata = GetAdvancedMetadata(symbol)
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing project {ProjectName}", project.Name);
                    }
                });

                await Task.WhenAll(searchTasks);
            }, cancellationToken);

            return new
            {
                success = true,
                pattern = pattern,
                fuzzy = fuzzy,
                totalResults = results.Count,
                appliedFilters = new
                {
                    kinds = kinds,
                    accessibility = accessibility,
                    isStatic = isStatic,
                    isAbstract = isAbstract,
                    isVirtual = isVirtual,
                    isOverride = isOverride,
                    returnType = returnType,
                    containingType = containingType,
                    containingNamespace = containingNamespace
                },
                results = results.OrderBy(r => r.Name).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in advanced symbol search");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private bool PassesAdvancedFilters(ISymbol symbol, string[]? kinds, string[]? accessibility, 
        bool? isStatic, bool? isAbstract, bool? isVirtual, bool? isOverride, 
        string? returnType, string? containingType, string? containingNamespace)
    {
        // Filter by symbol kind
        if (kinds != null && !kinds.Contains(symbol.Kind.ToString(), StringComparer.OrdinalIgnoreCase))
            return false;

        // Filter by accessibility
        if (accessibility != null && !accessibility.Contains(symbol.DeclaredAccessibility.ToString(), StringComparer.OrdinalIgnoreCase))
            return false;

        // Filter by static
        if (isStatic.HasValue && symbol.IsStatic != isStatic.Value)
            return false;

        // Filter by abstract
        if (isAbstract.HasValue && symbol.IsAbstract != isAbstract.Value)
            return false;

        // Filter by virtual
        if (isVirtual.HasValue && symbol.IsVirtual != isVirtual.Value)
            return false;

        // Filter by override
        if (isOverride.HasValue && symbol.IsOverride != isOverride.Value)
            return false;

        // Filter by return type (for methods)
        if (!string.IsNullOrEmpty(returnType) && symbol is IMethodSymbol method)
        {
            if (!method.ReturnType.Name.Contains(returnType, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Filter by containing type
        if (!string.IsNullOrEmpty(containingType))
        {
            if (symbol.ContainingType?.Name?.Contains(containingType, StringComparison.OrdinalIgnoreCase) != true)
                return false;
        }

        // Filter by containing namespace
        if (!string.IsNullOrEmpty(containingNamespace))
        {
            if (symbol.ContainingNamespace?.ToDisplayString()?.Contains(containingNamespace, StringComparison.OrdinalIgnoreCase) != true)
                return false;
        }

        return true;
    }

    private Dictionary<string, object> GetAdvancedMetadata(ISymbol symbol)
    {
        var metadata = new Dictionary<string, object>
        {
            ["Kind"] = symbol.Kind.ToString(),
            ["DeclaredAccessibility"] = symbol.DeclaredAccessibility.ToString(),
            ["IsStatic"] = symbol.IsStatic,
            ["IsAbstract"] = symbol.IsAbstract,
            ["IsVirtual"] = symbol.IsVirtual,
            ["IsOverride"] = symbol.IsOverride,
            ["IsSealed"] = symbol.IsSealed,
            ["ContainingNamespace"] = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            ["ContainingType"] = symbol.ContainingType?.Name ?? ""
        };

        // Add method-specific metadata
        if (symbol is IMethodSymbol method)
        {
            metadata["ReturnType"] = method.ReturnType.ToDisplayString();
            metadata["Parameters"] = method.Parameters.Select(p => new
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(),
                IsOptional = p.IsOptional,
                HasDefaultValue = p.HasExplicitDefaultValue
            }).ToArray();
            metadata["IsAsync"] = method.IsAsync;
            metadata["IsGenericMethod"] = method.IsGenericMethod;
        }

        // Add property-specific metadata
        if (symbol is IPropertySymbol property)
        {
            metadata["Type"] = property.Type.ToDisplayString();
            metadata["IsReadOnly"] = property.IsReadOnly;
            metadata["IsWriteOnly"] = property.IsWriteOnly;
            metadata["IsIndexer"] = property.IsIndexer;
        }

        // Add field-specific metadata
        if (symbol is IFieldSymbol field)
        {
            metadata["Type"] = field.Type.ToDisplayString();
            metadata["IsReadOnly"] = field.IsReadOnly;
            metadata["IsConst"] = field.IsConst;
            metadata["IsVolatile"] = field.IsVolatile;
        }

        return metadata;
    }

    private static string GetContainerName(ISymbol symbol)
    {
        if (symbol.ContainingType != null)
        {
            return symbol.ContainingType.ToDisplayString();
        }
        return symbol.ContainingNamespace?.ToDisplayString() ?? "";
    }

    private static string GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml)) return "";

        // Simple extraction of summary content
        var match = Regex.Match(xml, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}