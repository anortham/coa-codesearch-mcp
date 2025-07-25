using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of GetImplementationsTool with structured response format
/// </summary>
public class GetImplementationsToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "get_implementations_v2";
    public override string Description => "AI-optimized implementation discovery";
    public override ToolCategory Category => ToolCategory.Analysis;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public GetImplementationsToolV2(
        ILogger<GetImplementationsToolV2> logger,
        CodeAnalysisService workspaceService,
        IConfiguration configuration,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _workspaceService = workspaceService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("GetImplementationsV2 request for {FilePath} at {Line}:{Column}", filePath, line, column);

            // Get the document
            var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
            if (document == null)
            {
                return CreateErrorResponse<object>($"Could not find document: {filePath}");
            }

            // Get the source text and find the position
            var sourceText = await document.GetTextAsync(cancellationToken);
            var position = sourceText.Lines.GetPosition(new Microsoft.CodeAnalysis.Text.LinePosition(line - 1, column - 1));

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return CreateErrorResponse<object>("Could not get semantic model for document");
            }

            // Find the symbol at the position
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken);
            if (symbol == null)
            {
                return CreateErrorResponse<object>("No symbol found at the specified position");
            }

            var solution = document.Project.Solution;
            var implementationData = await CollectImplementationsAsync(symbol, solution, cancellationToken);

            // Create AI-optimized response
            return CreateAiOptimizedResponse(symbol, implementationData, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in GetImplementationsV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<ImplementationData> CollectImplementationsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var data = new ImplementationData();

        // Handle different symbol types
        if (symbol is INamedTypeSymbol namedType)
        {
            // Find derived types
            var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: cancellationToken);
            foreach (var derivedType in derivedTypes)
            {
                AddImplementation(data, derivedType, "DerivedClass");
            }

            // Find interface implementations
            if (namedType.TypeKind == TypeKind.Interface)
            {
                var implementingTypes = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: cancellationToken);
                foreach (var implementingType in implementingTypes)
                {
                    AddImplementation(data, implementingType, "Implementation");
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
                    AddImplementation(data, overrideMethod, "Override");
                }
            }

            // Find interface implementations
            var implementations = await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: cancellationToken);
            foreach (var impl in implementations)
            {
                if (!SymbolEqualityComparer.Default.Equals(impl, method)) // Don't include self
                {
                    AddImplementation(data, impl, "Implementation");
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
                    AddImplementation(data, overrideProperty, "Override");
                }
            }

            // Find interface implementations
            var implementations = await SymbolFinder.FindImplementationsAsync(property, solution, cancellationToken: cancellationToken);
            foreach (var impl in implementations)
            {
                if (!SymbolEqualityComparer.Default.Equals(impl, property)) // Don't include self
                {
                    AddImplementation(data, impl, "Implementation");
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
                    AddImplementation(data, overrideEvent, "Override");
                }
            }
        }

        return data;
    }

    private void AddImplementation(ImplementationData data, ISymbol symbol, string implementationType)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource && location.SourceTree != null)
            {
                var lineSpan = location.GetLineSpan();
                var containingType = symbol.ContainingType?.ToDisplayString() ?? "";
                
                var impl = new ImplementationItem
                {
                    FilePath = location.SourceTree.FilePath,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    ImplementationType = implementationType,
                    ContainingType = containingType,
                    MemberName = symbol.Name,
                    MemberKind = symbol.Kind.ToString(),
                    ProjectName = GetProjectFromPath(location.SourceTree.FilePath)
                };

                data.Implementations.Add(impl);

                // Update counts
                if (!data.TypeCounts.ContainsKey(implementationType))
                    data.TypeCounts[implementationType] = 0;
                data.TypeCounts[implementationType]++;

                if (!data.ProjectDistribution.ContainsKey(impl.ProjectName))
                    data.ProjectDistribution[impl.ProjectName] = 0;
                data.ProjectDistribution[impl.ProjectName]++;

                if (!data.ContainingTypesMap.ContainsKey(containingType))
                    data.ContainingTypesMap[containingType] = new List<ImplementationItem>();
                data.ContainingTypesMap[containingType].Add(impl);
            }
        }
    }

    private string GetProjectFromPath(string filePath)
    {
        // Simple heuristic - get parent directory name
        var dir = Path.GetDirectoryName(filePath);
        return Path.GetFileName(dir ?? "") ?? "Unknown";
    }

    private object CreateAiOptimizedResponse(
        ISymbol symbol,
        ImplementationData data,
        ResponseMode mode)
    {
        // Analyze the implementation data
        var analysis = AnalyzeImplementations(symbol, data);

        // Generate insights
        var insights = GenerateImplementationInsights(symbol, data, analysis);

        // Generate actions
        var actions = GenerateImplementationActions(symbol, data, analysis);

        // Prepare implementations for response
        var implementations = mode == ResponseMode.Full
            ? PrepareFullImplementations(data)
            : PrepareSummaryImplementations(data, analysis);

        // Create response
        return new
        {
            success = true,
            operation = ToolNames.GetImplementations,
            symbol = new
            {
                name = symbol.Name,
                kind = symbol.Kind.ToString().ToLowerInvariant(),
                container = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
                isAbstract = symbol.IsAbstract,
                isVirtual = symbol.IsVirtual,
                isInterface = symbol.ContainingType?.TypeKind == TypeKind.Interface,
                location = new { file = Path.GetFileName(symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "") }
            },
            summary = new
            {
                totalImplementations = data.Implementations.Count,
                uniqueTypes = data.ContainingTypesMap.Count,
                distribution = data.TypeCounts.OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value),
                projectSpread = data.ProjectDistribution.Count
            },
            analysis = new
            {
                patterns = analysis.Patterns.Take(3).ToList(),
                inheritance = new
                {
                    depth = analysis.MaxInheritanceDepth,
                    chains = analysis.InheritanceChains.Take(3).ToList()
                },
                hotspots = new
                {
                    byType = data.ContainingTypesMap
                        .Where(kv => kv.Value.Count > 1)
                        .OrderByDescending(kv => kv.Value.Count)
                        .Take(3)
                        .Select(kv => new { type = kv.Key, count = kv.Value.Count })
                        .ToList(),
                    byProject = data.ProjectDistribution
                        .OrderByDescending(kv => kv.Value)
                        .Take(3)
                        .Select(kv => new { project = kv.Key, count = kv.Value })
                        .ToList()
                }
            },
            implementations = implementations,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateResponseTokens(data),
                cached = $"impl_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private ImplementationAnalysis AnalyzeImplementations(ISymbol symbol, ImplementationData data)
    {
        var analysis = new ImplementationAnalysis();

        // Pattern detection
        if (data.Implementations.Count == 0)
        {
            analysis.Patterns.Add("No implementations found - may be unused or final");
        }
        else if (data.Implementations.Count == 1)
        {
            analysis.Patterns.Add("Single implementation - consider if abstraction is necessary");
        }
        else if (data.Implementations.Count > 10)
        {
            analysis.Patterns.Add("Many implementations - widely used abstraction");
        }

        // Check for test implementations
        var testImplementations = data.Implementations
            .Where(i => i.FilePath.Contains("Test", StringComparison.OrdinalIgnoreCase))
            .Count();
        if (testImplementations > 0)
        {
            analysis.Patterns.Add($"{testImplementations} test implementations found");
        }

        // Project distribution pattern
        if (data.ProjectDistribution.Count > 3)
        {
            analysis.Patterns.Add("Implementations spread across multiple projects");
        }

        // Inheritance analysis
        if (symbol is INamedTypeSymbol)
        {
            // Simple depth estimation based on type names
            foreach (var impl in data.Implementations)
            {
                var depth = EstimateInheritanceDepth(impl.ContainingType);
                if (depth > analysis.MaxInheritanceDepth)
                    analysis.MaxInheritanceDepth = depth;
            }

            // Find inheritance chains
            var chains = data.ContainingTypesMap
                .Where(kv => kv.Key.Contains("Base") || kv.Key.Contains("Abstract"))
                .Select(kv => kv.Key)
                .Take(3)
                .ToList();
            analysis.InheritanceChains = chains;
        }

        return analysis;
    }

    private int EstimateInheritanceDepth(string typeName)
    {
        // Simple heuristic - count dots in the type name
        return typeName.Count(c => c == '.') + 1;
    }

    private List<object> PrepareFullImplementations(ImplementationData data)
    {
        return data.ContainingTypesMap
            .OrderBy(kv => kv.Key)
            .Select(kv => new
            {
                containingType = kv.Key,
                implementations = kv.Value.Select(impl => new
                {
                    type = impl.ImplementationType.ToLowerInvariant(),
                    member = impl.MemberName,
                    location = new
                    {
                        file = impl.FilePath,
                        line = impl.Line,
                        column = impl.Column
                    }
                }).ToList()
            })
            .ToList<object>();
    }

    private List<object> PrepareSummaryImplementations(ImplementationData data, ImplementationAnalysis analysis)
    {
        // In summary mode, group by project and type
        return data.ProjectDistribution
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new
            {
                project = kv.Key,
                count = kv.Value,
                types = data.Implementations
                    .Where(i => i.ProjectName == kv.Key)
                    .Select(i => i.ContainingType)
                    .Distinct()
                    .Take(3)
                    .ToList()
            })
            .ToList<object>();
    }

    private List<string> GenerateImplementationInsights(ISymbol symbol, ImplementationData data, ImplementationAnalysis analysis)
    {
        var insights = new List<string>();

        // Basic insights
        if (data.Implementations.Count == 0)
        {
            insights.Add($"No implementations found for {symbol.Name}");
            if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                insights.Add("Interface may be unused or only used as a constraint");
            }
        }
        else
        {
            insights.Add($"Found {data.Implementations.Count} implementations across {data.ContainingTypesMap.Count} types");
        }

        // Distribution insights
        if (data.TypeCounts.ContainsKey("Override") && data.TypeCounts["Override"] > 0)
        {
            insights.Add($"{data.TypeCounts["Override"]} overrides - inheritance hierarchy in use");
        }

        if (data.TypeCounts.ContainsKey("Implementation") && data.TypeCounts["Implementation"] > 0)
        {
            insights.Add($"{data.TypeCounts["Implementation"]} interface implementations");
        }

        // Project spread insight
        if (data.ProjectDistribution.Count > 1)
        {
            insights.Add($"Implementations span {data.ProjectDistribution.Count} projects");
        }

        // Inheritance depth insight
        if (analysis.MaxInheritanceDepth > 3)
        {
            insights.Add($"Deep inheritance detected (depth: {analysis.MaxInheritanceDepth})");
        }

        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }

        return insights;
    }

    private List<object> GenerateImplementationActions(ISymbol symbol, ImplementationData data, ImplementationAnalysis analysis)
    {
        var actions = new List<object>();

        // Navigate to implementations
        if (data.Implementations.Any())
        {
            var firstImpl = data.Implementations.First();
            actions.Add(new
            {
                id = "navigate_to_implementation",
                cmd = new
                {
                    file = firstImpl.FilePath,
                    line = firstImpl.Line,
                    column = firstImpl.Column
                },
                tokens = 100,
                priority = "recommended"
            });
        }

        // Analyze usage patterns
        if (data.Implementations.Count > 3)
        {
            actions.Add(new
            {
                id = "analyze_usage_patterns",
                cmd = new
                {
                    symbol = symbol.Name,
                    operation = ToolNames.FindReferences,
                    scope = "implementations"
                },
                tokens = 2000,
                priority = "available"
            });
        }

        // Check for unused implementations
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface && data.Implementations.Count == 0)
        {
            actions.Add(new
            {
                id = "check_interface_usage",
                cmd = new
                {
                    symbol = symbol.Name,
                    operation = "find_references"
                },
                tokens = 1500,
                priority = "recommended"
            });
        }

        // Visualize inheritance hierarchy
        if (data.ContainingTypesMap.Count > 5)
        {
            actions.Add(new
            {
                id = "visualize_hierarchy",
                cmd = new
                {
                    rootType = symbol.ContainingType?.Name,
                    includeInterfaces = true
                },
                tokens = 3000,
                priority = "available"
            });
        }

        // Refactoring suggestions
        if (data.Implementations.Count == 1)
        {
            actions.Add(new
            {
                id = "consider_simplification",
                cmd = new
                {
                    analysis = "single_implementation",
                    suggestion = "Consider removing abstraction"
                },
                tokens = 500,
                priority = "normal"
            });
        }

        return actions;
    }

    private int EstimateResponseTokens(ImplementationData data)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per implementation tokens
        var perImplTokens = 50;
        
        // Type complexity
        var typeTokens = data.ContainingTypesMap.Count * 30;
        
        return baseTokens + (data.Implementations.Count * perImplTokens) + typeTokens;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for implementations"));
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is ImplementationData implData)
        {
            return implData.Implementations.Count;
        }
        return 0;
    }

    private class ImplementationData
    {
        public List<ImplementationItem> Implementations { get; } = new();
        public Dictionary<string, int> TypeCounts { get; } = new();
        public Dictionary<string, int> ProjectDistribution { get; } = new();
        public Dictionary<string, List<ImplementationItem>> ContainingTypesMap { get; } = new();
    }

    private class ImplementationItem
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string ImplementationType { get; set; } = "";
        public string ContainingType { get; set; } = "";
        public string MemberName { get; set; } = "";
        public string MemberKind { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }

    private class ImplementationAnalysis
    {
        public List<string> Patterns { get; set; } = new();
        public int MaxInheritanceDepth { get; set; }
        public List<string> InheritanceChains { get; set; } = new();
    }
}