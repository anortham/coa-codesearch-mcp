using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of GetCallHierarchyTool with structured response format
/// </summary>
public class GetCallHierarchyToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "get_call_hierarchy_v2";
    public override string Description => "AI-optimized call hierarchy analysis";
    public override ToolCategory Category => ToolCategory.Analysis;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public GetCallHierarchyToolV2(
        ILogger<GetCallHierarchyToolV2> logger,
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
        string direction = "both",
        int maxDepth = 2,
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

            Logger.LogInformation("GetCallHierarchy request for {FilePath} at {Line}:{Column}", filePath, line, column);

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

            // Find a suitable symbol
            var symbol = await FindCallableSymbolAsync(document, position, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return CreateErrorResponse<object>("No method or property found at or near the specified position. Try positioning the cursor on the method/property name.");
            }

            Logger.LogInformation("Found callable symbol: {SymbolName} of type {SymbolType}", symbol.Name, symbol.GetType().Name);

            var solution = document.Project.Solution;
            var hierarchyData = new CallHierarchyData();

            // Get incoming calls (who calls this method)
            if (direction == "incoming" || direction == "both")
            {
                await GetIncomingCallsAsync(symbol, solution, hierarchyData, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
            }

            // Get outgoing calls (what this method calls)
            if (direction == "outgoing" || direction == "both")
            {
                await GetOutgoingCallsAsync(symbol, solution, hierarchyData, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
            }

            // Create AI-optimized response
            return CreateAiOptimizedResponse(symbol, direction, maxDepth, hierarchyData, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in GetCallHierarchyV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private object CreateAiOptimizedResponse(
        ISymbol symbol,
        string direction,
        int maxDepth,
        CallHierarchyData data,
        ResponseMode mode)
    {
        // Analyze the hierarchy data
        var analysis = AnalyzeCallHierarchy(data);

        // Generate insights
        var insights = GenerateCallHierarchyInsights(symbol, direction, analysis);

        // Generate actions
        var actions = GenerateCallHierarchyActions(symbol, direction, analysis);

        // Create response
        return new
        {
            success = true,
            operation = ToolNames.GetCallHierarchy,
            symbol = new
            {
                name = symbol.Name,
                kind = symbol.Kind.ToString().ToLowerInvariant(),
                container = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
                location = new { file = data.RootLocation?.FilePath, line = data.RootLocation?.Line }
            },
            query = new
            {
                direction = direction,
                maxDepth = maxDepth
            },
            summary = new
            {
                totalCalls = analysis.TotalCalls,
                uniqueMethods = analysis.UniqueMethods,
                maxCallDepth = analysis.MaxDepth,
                circularDependencies = analysis.CircularDependencies.Count,
                hotspots = analysis.Hotspots.Take(5).Select(h => new { method = h.Key, callCount = h.Value }).ToList()
            },
            analysis = new
            {
                callPaths = new
                {
                    incoming = analysis.IncomingPaths,
                    outgoing = analysis.OutgoingPaths
                },
                criticalPaths = analysis.CriticalPaths.Take(3).ToList(),
                recursivePatterns = analysis.RecursivePatterns.Take(3).ToList(),
                crossProjectCalls = analysis.CrossProjectCalls
            },
            issues = new
            {
                circular = analysis.CircularDependencies.Take(5).ToList(),
                deepNesting = analysis.DeepNestingPoints.Take(5).ToList(),
                unusedMethods = direction == "incoming" && analysis.IncomingPaths == 0 ? new[] { symbol.Name } : Array.Empty<string>()
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = analysis.WasTruncated,
                tokens = EstimateResponseTokens(analysis),
                cached = $"call_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private CallHierarchyAnalysis AnalyzeCallHierarchy(CallHierarchyData data)
    {
        var analysis = new CallHierarchyAnalysis();
        var callCounts = new Dictionary<string, int>();
        var visitedPaths = new HashSet<string>();
        var circularPaths = new HashSet<string>();

        // Analyze incoming calls
        foreach (var call in data.IncomingCalls)
        {
            AnalyzeCallPath(call, callCounts, visitedPaths, circularPaths, analysis, "incoming", 0);
        }

        // Analyze outgoing calls
        foreach (var call in data.OutgoingCalls)
        {
            AnalyzeCallPath(call, callCounts, visitedPaths, circularPaths, analysis, "outgoing", 0);
        }

        // Identify hotspots
        analysis.Hotspots = callCounts.OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Count unique methods
        analysis.UniqueMethods = callCounts.Count;
        analysis.TotalCalls = data.IncomingCalls.Count + data.OutgoingCalls.Count;

        // Find critical paths (paths with most calls)
        analysis.CriticalPaths = visitedPaths
            .Where(p => p.Split(" -> ").Length > 2)
            .OrderByDescending(p => p.Split(" -> ").Length)
            .Take(5)
            .ToList();

        // Find circular dependencies
        analysis.CircularDependencies = circularPaths.ToList();

        return analysis;
    }

    private void AnalyzeCallPath(
        CallHierarchyItem item,
        Dictionary<string, int> callCounts,
        HashSet<string> visitedPaths,
        HashSet<string> circularPaths,
        CallHierarchyAnalysis analysis,
        string direction,
        int depth,
        string path = "")
    {
        var methodName = $"{item.Symbol.ContainerName}.{item.Symbol.Name}";
        
        // Update call counts
        if (!callCounts.ContainsKey(methodName))
            callCounts[methodName] = 0;
        callCounts[methodName]++;

        // Update path tracking
        var currentPath = string.IsNullOrEmpty(path) ? methodName : $"{path} -> {methodName}";
        
        // Check for circular dependency
        if (path.Contains(methodName))
        {
            circularPaths.Add(currentPath);
            analysis.RecursivePatterns.Add(methodName);
            return;
        }

        visitedPaths.Add(currentPath);

        // Update depth tracking
        if (depth > analysis.MaxDepth)
            analysis.MaxDepth = depth;

        // Check for deep nesting
        if (depth > 3)
        {
            analysis.DeepNestingPoints.Add($"{methodName} (depth: {depth})");
        }

        // Track cross-project calls
        if (item.Locations.Any())
        {
            var projects = item.Locations
                .Select(l => GetProjectFromPath(l.FilePath))
                .Distinct()
                .Count();
            if (projects > 1)
            {
                analysis.CrossProjectCalls++;
            }
        }

        // Update direction-specific counts
        if (direction == "incoming")
            analysis.IncomingPaths++;
        else
            analysis.OutgoingPaths++;

        // Recurse through children
        foreach (var child in item.Children)
        {
            AnalyzeCallPath(child, callCounts, visitedPaths, circularPaths, analysis, direction, depth + 1, currentPath);
        }
    }

    private string GetProjectFromPath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "Unknown";

        // Simple heuristic: use parent directory name as project
        var dir = Path.GetDirectoryName(filePath);
        return Path.GetFileName(dir ?? "") ?? "Unknown";
    }

    private List<string> GenerateCallHierarchyInsights(ISymbol symbol, string direction, CallHierarchyAnalysis analysis)
    {
        var insights = new List<string>();

        // Basic insights
        if (analysis.TotalCalls == 0)
        {
            insights.Add($"No {direction} calls found for {symbol.Name}");
            if (direction == "incoming")
            {
                insights.Add("Method might be unused or only called via reflection/dynamic invocation");
            }
        }
        else
        {
            insights.Add($"Found {analysis.TotalCalls} calls across {analysis.UniqueMethods} unique methods");
        }

        // Circular dependency insights
        if (analysis.CircularDependencies.Any())
        {
            insights.Add($"⚠️ {analysis.CircularDependencies.Count} circular dependencies detected");
        }

        // Hotspot insights
        if (analysis.Hotspots.Any())
        {
            var topHotspot = analysis.Hotspots.First();
            insights.Add($"Most active: {topHotspot.Key} ({topHotspot.Value} calls)");
        }

        // Depth insights
        if (analysis.MaxDepth > 5)
        {
            insights.Add($"Deep call chain detected (max depth: {analysis.MaxDepth})");
        }

        // Cross-project insights
        if (analysis.CrossProjectCalls > 0)
        {
            insights.Add($"{analysis.CrossProjectCalls} cross-project dependencies");
        }

        // Direction-specific insights
        if (direction == "incoming" && analysis.IncomingPaths > 10)
        {
            insights.Add("High coupling - consider if this method has too many responsibilities");
        }
        else if (direction == "outgoing" && analysis.OutgoingPaths > 15)
        {
            insights.Add("Complex method - consider breaking down into smaller functions");
        }

        // Recursive pattern insights
        if (analysis.RecursivePatterns.Any())
        {
            insights.Add($"Recursive patterns in: {string.Join(", ", analysis.RecursivePatterns.Take(3))}");
        }
        
        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            insights.Add($"Call hierarchy analysis complete for {symbol.Name} - no significant patterns detected");
        }

        return insights;
    }

    private List<object> GenerateCallHierarchyActions(ISymbol symbol, string direction, CallHierarchyAnalysis analysis)
    {
        var actions = new List<object>();

        // Fix circular dependencies
        if (analysis.CircularDependencies.Any())
        {
            actions.Add(new
            {
                id = "fix_circular",
                cmd = new { action = "analyze_circular_dependencies", patterns = analysis.CircularDependencies.Take(3) },
                tokens = 3000,
                priority = "critical"
            });
        }

        // Analyze hotspots
        if (analysis.Hotspots.Count > 3)
        {
            var topHotspot = analysis.Hotspots.First();
            actions.Add(new
            {
                id = "analyze_hotspot",
                cmd = new { file = analysis.HotspotLocations?.FirstOrDefault()?.FilePath, symbol = topHotspot.Key },
                tokens = 2000,
                priority = "recommended"
            });
        }

        // Explore critical paths
        if (analysis.CriticalPaths.Any())
        {
            actions.Add(new
            {
                id = "trace_critical_path",
                cmd = new { path = analysis.CriticalPaths.First(), visualize = true },
                tokens = 2500,
                priority = "normal"
            });
        }

        // Switch direction
        if (direction != "both")
        {
            var oppositeDirection = direction == "incoming" ? "outgoing" : "incoming";
            actions.Add(new
            {
                id = "explore_opposite",
                cmd = new { direction = oppositeDirection, maxDepth = 2 },
                tokens = EstimateDirectionTokens(oppositeDirection, analysis),
                priority = "available"
            });
        }

        // Increase depth
        if (analysis.MaxDepth >= 2 && !analysis.WasTruncated)
        {
            actions.Add(new
            {
                id = "increase_depth",
                cmd = new { direction = direction, maxDepth = 3 },
                tokens = EstimateDepthTokens(3, analysis),
                priority = "available"
            });
        }

        // Find unused code
        if (direction == "incoming" && analysis.IncomingPaths == 0)
        {
            actions.Add(new
            {
                id = "find_similar_unused",
                cmd = new { pattern = $"*{GetMethodPattern(symbol)}*", findUnused = true },
                tokens = 1500,
                priority = "normal"
            });
        }
        
        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            actions.Add(new
            {
                id = "expand_analysis",
                cmd = new { direction = "both", maxDepth = 3 },
                tokens = 2000,
                priority = "available"
            });
        }

        return actions;
    }

    private string GetMethodPattern(ISymbol symbol)
    {
        // Extract a pattern from the method name for finding similar methods
        var name = symbol.Name;
        if (name.StartsWith("Get")) return "Get";
        if (name.StartsWith("Set")) return "Set";
        if (name.StartsWith("Process")) return "Process";
        if (name.StartsWith("Handle")) return "Handle";
        return name.Length > 4 ? name.Substring(0, 4) : name;
    }

    private int EstimateResponseTokens(CallHierarchyAnalysis analysis)
    {
        // Estimate based on complexity
        var baseTokens = 200;
        var perCallTokens = 50;
        var totalTokens = baseTokens + (analysis.TotalCalls * perCallTokens);
        return Math.Min(5000, totalTokens);
    }

    private int EstimateDirectionTokens(string direction, CallHierarchyAnalysis analysis)
    {
        // Estimate tokens for exploring opposite direction
        return direction == "incoming" ? 2000 : 1500;
    }

    private int EstimateDepthTokens(int depth, CallHierarchyAnalysis analysis)
    {
        // Estimate exponential growth with depth
        return Math.Min(5000, 1000 * (int)Math.Pow(1.5, depth));
    }

    private async Task GetIncomingCallsAsync(
        ISymbol targetSymbol,
        Solution solution,
        CallHierarchyData data,
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
                Symbol = new HierarchySymbolInfo
                {
                    Name = callerSymbol.Name,
                    Kind = callerSymbol.Kind.ToString(),
                    ContainerName = callerSymbol.ContainingType?.ToDisplayString() ?? callerSymbol.ContainingNamespace?.ToDisplayString() ?? ""
                },
                Locations = locations,
                Children = new List<CallHierarchyItem>()
            };

            data.IncomingCalls.Add(item);

            // Store root location if not set
            if (data.RootLocation == null && locations.Any())
            {
                data.RootLocation = locations.First();
            }

            // Recursively get callers of the caller
            if (remainingDepth > 1)
            {
                var childData = new CallHierarchyData();
                await GetIncomingCallsAsync(callerSymbol, solution, childData, remainingDepth - 1, visited, cancellationToken);
                item.Children.AddRange(childData.IncomingCalls);
            }
        }
    }

    private async Task GetOutgoingCallsAsync(
        ISymbol targetSymbol,
        Solution solution,
        CallHierarchyData data,
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
            var document = solution.GetDocument(syntaxRef.SyntaxTree);
            if (document == null)
                continue;
                
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            
            if (semanticModel == null)
                continue;

            // Store root location if not set
            if (data.RootLocation == null)
            {
                var rootSpan = syntaxNode.Span;
                var rootLineSpan = syntaxRef.SyntaxTree.GetLineSpan(rootSpan);
                data.RootLocation = new LocationInfo
                {
                    FilePath = syntaxRef.SyntaxTree.FilePath,
                    Line = rootLineSpan.StartLinePosition.Line + 1,
                    Column = rootLineSpan.StartLinePosition.Character + 1
                };
            }

            // Find all invocations in the method body
            var invocations = syntaxNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
                if (invokedSymbol == null)
                    continue;

                // Skip if we've already processed this symbol
                if (data.ProcessedSymbols.Contains(invokedSymbol))
                    continue;

                data.ProcessedSymbols.Add(invokedSymbol);

                var span = invocation.Span;
                var lineSpan = syntaxRef.SyntaxTree.GetLineSpan(span);
                var text = await syntaxRef.SyntaxTree.GetTextAsync(cancellationToken);
                var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

                var item = new CallHierarchyItem
                {
                    Symbol = new HierarchySymbolInfo
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

                data.OutgoingCalls.Add(item);

                // Recursively get calls from the invoked method
                if (remainingDepth > 1)
                {
                    var childData = new CallHierarchyData();
                    await GetOutgoingCallsAsync(invokedSymbol, solution, childData, remainingDepth - 1, visited, cancellationToken);
                    item.Children.AddRange(childData.OutgoingCalls);
                }
            }
        }
    }

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
            Logger.LogDebug("Found callable symbol at exact position: {Symbol}", symbol.Name);
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
                Logger.LogDebug("Found enclosing callable symbol: {Symbol}", declaredSymbol.Name);
                return declaredSymbol;
            }
            
            node = node.Parent;
        }

        return null;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for call hierarchy"));
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is CallHierarchyData hierarchyData)
        {
            return hierarchyData.IncomingCalls.Count + hierarchyData.OutgoingCalls.Count;
        }
        return 0;
    }

    private class CallHierarchyData
    {
        public List<CallHierarchyItem> IncomingCalls { get; } = new();
        public List<CallHierarchyItem> OutgoingCalls { get; } = new();
        public HashSet<ISymbol> ProcessedSymbols { get; } = new(SymbolEqualityComparer.Default);
        public LocationInfo? RootLocation { get; set; }
    }

    private class CallHierarchyItem
    {
        public HierarchySymbolInfo Symbol { get; set; } = new();
        public List<LocationInfo> Locations { get; set; } = new();
        public List<CallHierarchyItem> Children { get; set; } = new();
    }

    private class HierarchySymbolInfo
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ContainerName { get; set; } = "";
    }

    private class CallHierarchyAnalysis
    {
        public int TotalCalls { get; set; }
        public int UniqueMethods { get; set; }
        public int MaxDepth { get; set; }
        public int IncomingPaths { get; set; }
        public int OutgoingPaths { get; set; }
        public int CrossProjectCalls { get; set; }
        public bool WasTruncated { get; set; }
        public Dictionary<string, int> Hotspots { get; set; } = new();
        public List<string> CriticalPaths { get; set; } = new();
        public List<string> CircularDependencies { get; set; } = new();
        public List<string> RecursivePatterns { get; set; } = new();
        public List<string> DeepNestingPoints { get; set; } = new();
        public List<LocationInfo>? HotspotLocations { get; set; }
    }
}