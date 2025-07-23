using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Claude-optimized version of DependencyAnalysisTool with progressive disclosure and performance optimizations
/// </summary>
public class DependencyAnalysisToolV2 : ClaudeOptimizedToolBase
{
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public DependencyAnalysisToolV2(
        ILogger<DependencyAnalysisToolV2> logger,
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
        string symbol,
        string workspacePath,
        string direction = "both",
        int depth = 3,
        bool includeTests = false,
        bool includeExternalDependencies = false,
        ResponseMode mode = ResponseMode.Full,
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

            Logger.LogInformation("DependencyAnalysis request for symbol: {Symbol} in {WorkspacePath}", symbol, workspacePath);

            // Get the workspace
            var workspace = await LoadWorkspaceAsync(workspacePath, cancellationToken);
            if (workspace == null)
            {
                return CreateErrorResponse<object>($"Could not load workspace: {workspacePath}");
            }

            var solution = workspace.CurrentSolution;
            
            // Find the target symbol
            var targetSymbol = await FindSymbolByNameAsync(solution, symbol, cancellationToken);
            if (targetSymbol == null)
            {
                return CreateErrorResponse<object>($"Symbol '{symbol}' not found in workspace");
            }

            // Perform dependency analysis with performance optimizations
            var stopwatch = Stopwatch.StartNew();
            var dependencyData = await AnalyzeDependenciesAsync(
                solution, 
                targetSymbol, 
                direction, 
                depth, 
                includeTests, 
                includeExternalDependencies, 
                cancellationToken);
            
            Logger.LogInformation("Dependency analysis completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            // Create AI-optimized response
            var response = CreateAiOptimizedResponse(dependencyData, targetSymbol, direction, mode, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DependencyAnalysisV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<Workspace?> LoadWorkspaceAsync(string workspacePath, CancellationToken cancellationToken)
    {
        if (workspacePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await _workspaceService.GetWorkspaceAsync(workspacePath, cancellationToken);
        }
        else if (workspacePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await _workspaceService.GetProjectAsync(workspacePath, cancellationToken);
            return project?.Solution.Workspace;
        }
        else if (Directory.Exists(workspacePath))
        {
            var slnFiles = Directory.GetFiles(workspacePath, "*.sln");
            if (slnFiles.Length == 1)
            {
                return await _workspaceService.GetWorkspaceAsync(slnFiles[0], cancellationToken);
            }
        }
        
        return null;
    }

    private async Task<ISymbol?> FindSymbolByNameAsync(Solution solution, string symbolName, CancellationToken cancellationToken)
    {
        // Use parallel search for better performance
        var searchTasks = solution.Projects.Select(async project =>
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null) return null;

            var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
                project, 
                name => name.Equals(symbolName, StringComparison.OrdinalIgnoreCase), 
                cancellationToken);

            return symbols.FirstOrDefault();
        });

        var results = await Task.WhenAll(searchTasks);
        return results.FirstOrDefault(s => s != null);
    }

    private async Task<DependencyData> AnalyzeDependenciesAsync(
        Solution solution,
        ISymbol targetSymbol,
        string direction,
        int depth,
        bool includeTests,
        bool includeExternalDependencies,
        CancellationToken cancellationToken)
    {
        var graph = new OptimizedDependencyGraph();
        var visitedSymbols = new ConcurrentDictionary<string, byte>();
        var circularDependencies = new ConcurrentBag<CircularDependency>();

        // Analyze dependencies based on direction
        var tasks = new List<Task>();
        
        if (direction == "incoming" || direction == "both")
        {
            tasks.Add(AnalyzeIncomingDependenciesAsync(
                solution, targetSymbol, graph, visitedSymbols, circularDependencies, 
                depth, includeTests, includeExternalDependencies, new List<string>(), cancellationToken));
        }

        if (direction == "outgoing" || direction == "both")
        {
            tasks.Add(AnalyzeOutgoingDependenciesAsync(
                solution, targetSymbol, graph, visitedSymbols, circularDependencies,
                depth, includeTests, includeExternalDependencies, new List<string>(), cancellationToken));
        }

        await Task.WhenAll(tasks);

        return new DependencyData
        {
            Symbol = CreateSymbolInfo(targetSymbol),
            Direction = direction,
            Depth = depth,
            IncludeTests = includeTests,
            IncludeExternalDependencies = includeExternalDependencies,
            Graph = graph,
            CircularDependencies = circularDependencies.ToList(),
            Metrics = CalculateMetrics(graph, circularDependencies)
        };
    }

    private async Task AnalyzeIncomingDependenciesAsync(
        Solution solution,
        ISymbol symbol,
        OptimizedDependencyGraph graph,
        ConcurrentDictionary<string, byte> visited,
        ConcurrentBag<CircularDependency> circularDependencies,
        int remainingDepth,
        bool includeTests,
        bool includeExternalDependencies,
        List<string> path,
        CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0) return;

        var symbolKey = GetSymbolKey(symbol);
        
        // Check for circular dependencies
        if (path.Contains(symbolKey))
        {
            circularDependencies.Add(new CircularDependency
            {
                Path = new List<string>(path) { symbolKey },
                Type = "incoming"
            });
            return;
        }

        if (!visited.TryAdd(symbolKey, 0)) return;
        
        var newPath = new List<string>(path) { symbolKey };

        try
        {
            // Find all references to this symbol
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);

            // Process references in parallel for better performance
            var referenceTasks = new List<Task>();
            
            foreach (var reference in references)
            {
                var locations = reference.Locations.Where(loc => loc.Location.IsInSource).ToList();
                
                foreach (var location in locations)
                {
                    referenceTasks.Add(ProcessIncomingReferenceAsync(
                        solution, location, symbol, graph, visited, circularDependencies,
                        remainingDepth - 1, includeTests, includeExternalDependencies, newPath, cancellationToken));
                }
            }

            await Task.WhenAll(referenceTasks);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error analyzing incoming dependencies for {Symbol}", symbolKey);
        }
    }

    private async Task ProcessIncomingReferenceAsync(
        Solution solution,
        ReferenceLocation location,
        ISymbol targetSymbol,
        OptimizedDependencyGraph graph,
        ConcurrentDictionary<string, byte> visited,
        ConcurrentBag<CircularDependency> circularDependencies,
        int remainingDepth,
        bool includeTests,
        bool includeExternalDependencies,
        List<string> path,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(location.Document.Id);
        if (document == null) return;

        // Skip test projects if not included
        if (!includeTests && IsTestProject(document.Project))
            return;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return;

        var containingSymbol = semanticModel.GetEnclosingSymbol(location.Location.SourceSpan.Start, cancellationToken);
        if (containingSymbol == null) return;

        var dependency = CreateDependencyInfo(containingSymbol, document, location.Location);
        graph.AddIncomingDependency(GetSymbolKey(targetSymbol), dependency);

        // Recursively analyze this symbol's dependencies
        await AnalyzeIncomingDependenciesAsync(
            solution, containingSymbol, graph, visited, circularDependencies,
            remainingDepth, includeTests, includeExternalDependencies, path, cancellationToken);
    }

    private async Task AnalyzeOutgoingDependenciesAsync(
        Solution solution,
        ISymbol symbol,
        OptimizedDependencyGraph graph,
        ConcurrentDictionary<string, byte> visited,
        ConcurrentBag<CircularDependency> circularDependencies,
        int remainingDepth,
        bool includeTests,
        bool includeExternalDependencies,
        List<string> path,
        CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0) return;

        var symbolKey = GetSymbolKey(symbol);
        
        // Check for circular dependencies
        if (path.Contains(symbolKey))
        {
            circularDependencies.Add(new CircularDependency
            {
                Path = new List<string>(path) { symbolKey },
                Type = "outgoing"
            });
            return;
        }

        if (!visited.TryAdd(symbolKey, 0)) return;
        
        var newPath = new List<string>(path) { symbolKey };

        try
        {
            // Get the syntax nodes for this symbol
            var locations = symbol.Locations.Where(loc => loc.IsInSource).ToList();
            
            var tasks = locations.Select(location =>
                ProcessOutgoingLocationAsync(
                    location, solution, symbol, graph, visited, circularDependencies,
                    remainingDepth, includeTests, includeExternalDependencies, newPath, cancellationToken)
            );

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error analyzing outgoing dependencies for {Symbol}", symbolKey);
        }
    }

    private async Task ProcessOutgoingLocationAsync(
        Location location,
        Solution solution,
        ISymbol sourceSymbol,
        OptimizedDependencyGraph graph,
        ConcurrentDictionary<string, byte> visited,
        ConcurrentBag<CircularDependency> circularDependencies,
        int remainingDepth,
        bool includeTests,
        bool includeExternalDependencies,
        List<string> path,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(location.SourceTree);
        if (document == null) return;

        // Skip test projects if not included
        if (!includeTests && IsTestProject(document.Project))
            return;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return;

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
        if (syntaxRoot == null) return;

        var node = syntaxRoot.FindNode(location.SourceSpan);

        // Find all symbols referenced within this symbol's implementation
        var descendants = node.DescendantNodes();
        
        // Process in parallel for better performance
        var tasks = new List<Task>();
        
        foreach (var descendant in descendants)
        {
            var referencedSymbol = semanticModel.GetSymbolInfo(descendant, cancellationToken).Symbol;
            if (referencedSymbol == null) continue;

            // Skip self-references and built-in types
            if (SymbolEqualityComparer.Default.Equals(referencedSymbol, sourceSymbol)) continue;
            if (IsBuiltInType(referencedSymbol)) continue;

            // Skip external dependencies if not included
            if (!includeExternalDependencies && IsExternalDependency(referencedSymbol, solution))
                continue;

            var dependency = CreateDependencyInfo(referencedSymbol, document, descendant.GetLocation());
            graph.AddOutgoingDependency(GetSymbolKey(sourceSymbol), dependency);

            // Recursively analyze this symbol's dependencies
            tasks.Add(AnalyzeOutgoingDependenciesAsync(
                solution, referencedSymbol, graph, visited, circularDependencies,
                remainingDepth - 1, includeTests, includeExternalDependencies, path, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private ClaudeSummaryData CreateSummaryData(DependencyData data, ISymbol targetSymbol, string direction)
    {
        var insights = new List<string>();
        
        // Analyze dependency patterns
        if (data.Metrics.IncomingCount > 20)
        {
            insights.Add($"High coupling: {data.Metrics.IncomingCount} components depend on this symbol");
        }
        
        if (data.Metrics.OutgoingCount > 30)
        {
            insights.Add($"Complex dependencies: This symbol depends on {data.Metrics.OutgoingCount} other components");
        }
        
        if (data.CircularDependencies.Any())
        {
            insights.Add($"⚠️ Found {data.CircularDependencies.Count} circular dependency path(s) - refactoring recommended");
        }
        
        // Analyze by project
        var projectDistribution = data.Graph.GetProjectDistribution();
        if (projectDistribution.Count > 1)
        {
            insights.Add($"Cross-project dependencies: spans {projectDistribution.Count} projects");
        }
        
        // Coupling analysis
        var (couplingLevel, couplingInsight) = AnalyzeCoupling(data);
        if (!string.IsNullOrEmpty(couplingInsight))
        {
            insights.Add(couplingInsight);
        }
        
        // Architecture insights
        var architectureInsights = AnalyzeArchitecture(data);
        insights.AddRange(architectureInsights);
        
        // Create hotspots (most connected symbols)
        var hotspots = IdentifyDependencyHotspots(data.Graph);
        
        // Categories by layer/type
        var categories = CategorizeDependencies(data.Graph);
        
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = data.Metrics.TotalDependencies,
                AffectedFiles = data.Metrics.UniqueFiles,
                EstimatedFullResponseTokens = SizeEstimator.EstimateTokens(data),
                KeyInsights = insights
            },
            ByCategory = categories,
            Hotspots = hotspots,
            Preview = new ChangePreview
            {
                TopChanges = CreateDependencyPreview(data),
                FullContext = false,
                GetFullContextCommand = new { detailLevel = "graph", depth = 2 }
            }
        };
    }

    private object CreateAiOptimizedResponse(DependencyData data, ISymbol targetSymbol, string direction, ResponseMode mode, CancellationToken cancellationToken)
    {
        // Calculate coupling metrics
        var afferentCoupling = data.Metrics.IncomingCount;
        var efferentCoupling = data.Metrics.OutgoingCount;
        var instability = (afferentCoupling + efferentCoupling) > 0 
            ? (double)efferentCoupling / (afferentCoupling + efferentCoupling) 
            : 0;

        // Analyze circular dependencies
        var circularPaths = data.CircularDependencies
            .Take(5)
            .Select(cd => new { 
                path = string.Join(" → ", cd.Path.Take(5)), 
                length = cd.Path.Count 
            })
            .ToList();

        // Get hotspots (most connected symbols)
        var hotspots = IdentifyDependencyHotspots(data.Graph)
            .Take(5)
            .Select(h => new { 
                file = h.File, 
                connections = h.Occurrences,
                complexity = h.Complexity 
            })
            .ToList();

        // Project distribution
        var projectDist = data.Graph.GetProjectDistribution();
        
        // Generate insights
        var insights = GenerateDependencyInsights(data, targetSymbol, instability);
        
        // Assess health
        var health = AssessDependencyHealth(data, instability);
        
        // Generate actions
        var actions = new List<object>();
        
        if (data.CircularDependencies.Any())
        {
            actions.Add(new 
            { 
                id = "fix_circular", 
                cmd = new { detailLevel = "circular", maxPaths = 10 }, 
                tokens = Math.Min(2500, data.CircularDependencies.Count * 250),
                priority = "critical"
            });
        }
        
        if (afferentCoupling > 20)
        {
            actions.Add(new 
            { 
                id = "analyze_coupling", 
                cmd = new { detailLevel = "coupling", focusOn = "incoming" }, 
                tokens = 2000,
                priority = "high"
            });
        }

        if (hotspots.Any(h => h.connections > 15))
        {
            actions.Add(new 
            { 
                id = "review_hotspots", 
                cmd = new { detailLevel = "hotspots", threshold = 10 }, 
                tokens = 2500,
                priority = "recommended"
            });
        }

        if (mode == ResponseMode.Summary && data.Metrics.TotalDependencies < 500)
        {
            actions.Add(new 
            { 
                id = "full_graph", 
                cmd = new { responseMode = "full" }, 
                tokens = EstimateFullResponseTokens(data),
                priority = "available"
            });
        }

        return new
        {
            success = true,
            operation = "dependency_analysis",
            target = new
            {
                symbol = targetSymbol.Name,
                type = targetSymbol.Kind.ToString().ToLowerInvariant(),
                namespace_ = targetSymbol.ContainingNamespace?.ToString() ?? "global"
            },
            analysis = new
            {
                direction = direction.ToLowerInvariant(),
                depth = data.Depth,
                scope = new
                {
                    includeTests = data.IncludeTests,
                    includeExternal = data.IncludeExternalDependencies
                }
            },
            metrics = new
            {
                incoming = afferentCoupling,
                outgoing = efferentCoupling,
                total = data.Metrics.TotalDependencies,
                instability = Math.Round(instability, 2),
                files = data.Metrics.UniqueFiles,
                projects = projectDist.Count
            },
            health = health,
            circular = new
            {
                found = data.CircularDependencies.Count > 0,
                count = data.CircularDependencies.Count,
                paths = circularPaths
            },
            hotspots = hotspots,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokens = SizeEstimator.EstimateTokens(new { dependencies = data.Metrics.TotalDependencies }),
                cached = $"dep_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private string AssessDependencyHealth(DependencyData data, double instability)
    {
        var score = 100;
        var issues = new List<string>();
        
        // Circular dependencies are critical
        if (data.CircularDependencies.Any())
        {
            score -= Math.Min(50, data.CircularDependencies.Count * 10);
            issues.Add("circular dependencies");
        }
        
        // High coupling
        if (data.Metrics.IncomingCount > 30)
        {
            score -= 20;
            issues.Add("high afferent coupling");
        }
        
        if (data.Metrics.OutgoingCount > 40)
        {
            score -= 15;
            issues.Add("high efferent coupling");
        }
        
        // Instability extremes
        if (instability > 0.9 && data.Metrics.IncomingCount < 3)
        {
            score -= 10;
            issues.Add("unstable with low reuse");
        }
        
        if (score >= 80) return "healthy";
        if (score >= 60) return "moderate";
        if (score >= 40) return "poor";
        return "critical";
    }

    private List<string> GenerateDependencyInsights(DependencyData data, ISymbol targetSymbol, double instability)
    {
        var insights = new List<string>();
        
        // Circular dependency insights
        if (data.CircularDependencies.Any())
        {
            insights.Add($"⚠️ {data.CircularDependencies.Count} circular dependency path(s) detected - requires refactoring");
        }
        
        // Coupling insights
        if (data.Metrics.IncomingCount > 20)
        {
            insights.Add($"High coupling: {data.Metrics.IncomingCount} components depend on this - changes have wide impact");
        }
        
        if (data.Metrics.OutgoingCount > 30)
        {
            insights.Add($"Complex dependencies: depends on {data.Metrics.OutgoingCount} components - consider simplifying");
        }
        
        // Stability insights
        if (instability > 0.8 && data.Metrics.IncomingCount < 3)
        {
            insights.Add("Unstable component with low reuse - candidate for consolidation");
        }
        else if (instability < 0.2 && data.Metrics.OutgoingCount > 10)
        {
            insights.Add("Stable component with many dependencies - potential abstraction leak");
        }
        
        // Architecture insights
        var projectDist = data.Graph.GetProjectDistribution();
        if (projectDist.Count > 3)
        {
            insights.Add($"Cross-cutting concern: spans {projectDist.Count} projects");
        }
        
        // Type-specific insights
        if (targetSymbol.Kind == SymbolKind.NamedType && targetSymbol.IsAbstract)
        {
            insights.Add("Abstract type - stability is important for derived types");
        }
        
        return insights;
    }

    private int EstimateFullResponseTokens(DependencyData data)
    {
        // Estimate ~50 tokens per dependency edge
        return Math.Min(25000, data.Metrics.TotalDependencies * 50);
    }

    private (string level, string insight) AnalyzeCoupling(DependencyData data)
    {
        var afferentCoupling = data.Metrics.IncomingCount;
        var efferentCoupling = data.Metrics.OutgoingCount;
        
        var instability = efferentCoupling > 0 
            ? (double)efferentCoupling / (afferentCoupling + efferentCoupling) 
            : 0;
            
        if (instability > 0.8 && afferentCoupling < 3)
        {
            return ("high", "Highly unstable component - depends on many others but few depend on it");
        }
        else if (instability < 0.2 && efferentCoupling < 3)
        {
            return ("low", "Stable component - many depend on it but it has few dependencies");
        }
        else if (afferentCoupling > 15 && efferentCoupling > 15)
        {
            return ("complex", "God object pattern detected - both heavily used and heavily dependent");
        }
        
        return ("normal", "");
    }

    private List<string> AnalyzeArchitecture(DependencyData data)
    {
        var insights = new List<string>();
        
        // Check for layer violations
        var layerViolations = DetectLayerViolations(data.Graph);
        if (layerViolations.Any())
        {
            insights.Add($"Architecture violations: {layerViolations.Count} dependencies go against typical layering");
        }
        
        // Check for interface segregation
        if (data.Symbol?.Kind == "Interface" && data.Metrics.IncomingCount > 10)
        {
            var avgMethodsPerImplementer = EstimateInterfaceComplexity(data);
            if (avgMethodsPerImplementer > 5)
            {
                insights.Add("Consider interface segregation - this interface may be too large");
            }
        }
        
        return insights;
    }

    private List<LayerViolation> DetectLayerViolations(OptimizedDependencyGraph graph)
    {
        var violations = new List<LayerViolation>();
        
        // Get all dependencies with their relationships
        var allRelationships = graph.GetAllRelationships();
        
        // Simple heuristic: UI shouldn't depend on data layer directly
        foreach (var rel in allRelationships)
        {
            if (IsUILayer(rel.From) && IsDataLayer(rel.To))
            {
                violations.Add(new LayerViolation 
                { 
                    From = rel.From, 
                    To = rel.To, 
                    Type = "UI->Data" 
                });
            }
        }
        
        return violations;
    }

    private bool IsUILayer(string component)
    {
        var lower = component.ToLowerInvariant();
        return lower.Contains("view") || lower.Contains("page") || 
               lower.Contains("component") || lower.Contains("controller");
    }

    private bool IsDataLayer(string component)
    {
        var lower = component.ToLowerInvariant();
        return lower.Contains("repository") || lower.Contains("context") || 
               lower.Contains("entity") || lower.Contains("dal");
    }

    private double EstimateInterfaceComplexity(DependencyData data)
    {
        // Simplified estimation based on dependency count
        return data.Metrics.OutgoingCount / Math.Max(1, data.Metrics.IncomingCount);
    }

    private List<Hotspot> IdentifyDependencyHotspots(OptimizedDependencyGraph graph)
    {
        var connectionCounts = graph.GetConnectionCounts();
        
        return connectionCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => new Hotspot
            {
                File = kvp.Key,
                Occurrences = kvp.Value,
                Complexity = kvp.Value > 20 ? "high" : kvp.Value > 10 ? "medium" : "low",
                Reason = kvp.Value > 20 ? "Central hub - consider refactoring" : null
            })
            .ToList();
    }

    private Dictionary<string, CategorySummary> CategorizeDependencies(OptimizedDependencyGraph graph)
    {
        var categories = new Dictionary<string, CategorySummary>();
        var allDeps = graph.GetAllDependencies();
        
        // Group by kind
        var byKind = allDeps.GroupBy(d => d.Kind);
        
        foreach (var group in byKind)
        {
            categories[group.Key.ToLowerInvariant()] = new CategorySummary
            {
                Files = group.Select(d => d.FilePath).Distinct().Count(),
                Occurrences = group.Count(),
                PrimaryPattern = group.GroupBy(d => d.Project)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key
            };
        }
        
        return categories;
    }

    private List<PreviewItem> CreateDependencyPreview(DependencyData data)
    {
        var items = new List<PreviewItem>();
        
        // Show circular dependencies first
        foreach (var circular in data.CircularDependencies.Take(3))
        {
            items.Add(new PreviewItem
            {
                File = "Circular Dependency",
                Line = 0,
                Preview = string.Join(" → ", circular.Path.Take(4)) + (circular.Path.Count > 4 ? " → ..." : ""),
                Context = $"Type: {circular.Type}"
            });
        }
        
        // Show top incoming dependencies
        var topIncoming = data.Graph.GetTopIncomingDependencies(3 - items.Count);
        foreach (var dep in topIncoming)
        {
            items.Add(new PreviewItem
            {
                File = dep.FilePath,
                Line = dep.Line,
                Preview = $"{dep.ContainingType}.{dep.Name}",
                Context = $"Used by {dep.FullName}"
            });
        }
        
        return items;
    }

    private DependencyMetrics CalculateMetrics(OptimizedDependencyGraph graph, IEnumerable<CircularDependency> circularDeps)
    {
        var allDeps = graph.GetAllDependencies();
        
        return new DependencyMetrics
        {
            TotalDependencies = allDeps.Count,
            IncomingCount = graph.GetIncomingCount(),
            OutgoingCount = graph.GetOutgoingCount(),
            UniqueIncomingSymbols = graph.GetUniqueIncomingSymbols(),
            UniqueOutgoingSymbols = graph.GetUniqueOutgoingSymbols(),
            UniqueFiles = allDeps.Select(d => d.FilePath).Distinct().Count(),
            CircularDependencyCount = circularDeps.Count(),
            MaxDepthReached = graph.GetMaxDepth()
        };
    }

    private bool IsBuiltInType(ISymbol symbol)
    {
        var assembly = symbol.ContainingAssembly?.Name;
        return assembly == "System.Runtime" || 
               assembly == "System.Private.CoreLib" ||
               assembly == "mscorlib";
    }

    private bool IsTestProject(Project project)
    {
        return project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               project.Name.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
               project.FilePath?.Contains("test", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool IsExternalDependency(ISymbol symbol, Solution solution)
    {
        if (symbol.ContainingAssembly == null) return false;
        
        // Check if the assembly is part of our solution
        return !solution.Projects.Any(p => p.AssemblyName == symbol.ContainingAssembly.Name);
    }

    private string GetSymbolKey(ISymbol symbol)
    {
        return $"{symbol.ContainingNamespace?.ToDisplayString()}.{symbol.ContainingType?.Name}.{symbol.Name}";
    }

    private DependencyInfo CreateDependencyInfo(ISymbol symbol, Document document, Location location)
    {
        var lineSpan = location.GetLineSpan();
        
        return new DependencyInfo
        {
            Name = symbol.Name,
            FullName = symbol.ToDisplayString(),
            Kind = symbol.Kind.ToString(),
            ContainingType = symbol.ContainingType?.Name ?? "",
            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Project = document.Project.Name,
            FilePath = document.FilePath ?? "",
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            DeclaredAccessibility = symbol.DeclaredAccessibility.ToString(),
            IsStatic = symbol.IsStatic,
            IsAbstract = symbol.IsAbstract
        };
    }

    private SymbolInfo CreateSymbolInfo(ISymbol symbol)
    {
        return new SymbolInfo
        {
            Name = symbol.Name,
            Kind = symbol.Kind.ToString(),
            ContainerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            IsStatic = symbol.IsStatic,
            IsAbstract = symbol.IsAbstract,
            IsVirtual = symbol.IsVirtual,
            Documentation = symbol.GetDocumentationCommentXml()
        };
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is DependencyData depData)
        {
            return depData.Metrics.TotalDependencies;
        }
        return 0;
    }

    protected override NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = base.GenerateNextActions(data, currentMode, metadata);
        
        if (currentMode == ResponseMode.Summary && data is DependencyData depData)
        {
            actions.Recommended.Clear();
            
            // If circular dependencies exist, prioritize reviewing them
            if (depData.CircularDependencies.Any())
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "review_circular_dependencies",
                    Description = "Review circular dependency paths",
                    Reason = "Circular dependencies create tight coupling and should be resolved",
                    EstimatedTokens = 2000,
                    Priority = "critical",
                    Command = new 
                    { 
                        detailLevel = "circular",
                        detailRequestToken = metadata.DetailRequestToken
                    }
                });
            }
            
            // Review hotspots
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "review_hotspots",
                Description = "Review most connected components",
                Reason = "Identify central hubs that may need refactoring",
                EstimatedTokens = 3000,
                Priority = depData.CircularDependencies.Any() ? "high" : "critical",
                Command = new
                {
                    detailLevel = "hotspots",
                    detailRequestToken = metadata.DetailRequestToken,
                    maxItems = 10
                }
            });
            
            // Visualize dependency graph
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "visualize_graph",
                Description = "Get visual representation of dependencies",
                Reason = "Visual graphs help understand complex relationships",
                EstimatedTokens = 4000,
                Priority = "medium",
                Command = new
                {
                    detailLevel = "graph",
                    detailRequestToken = metadata.DetailRequestToken,
                    depth = 2
                }
            });
        }
        
        return actions;
    }

    protected override ResultContext AnalyzeResultContext<T>(T data)
    {
        var context = base.AnalyzeResultContext(data);
        
        if (data is DependencyData depData)
        {
            // Assess complexity
            if (depData.CircularDependencies.Any())
            {
                context.Impact = "high";
                context.RiskFactors.Add($"{depData.CircularDependencies.Count} circular dependencies create maintenance risks");
            }
            else if (depData.Metrics.TotalDependencies > 100)
            {
                context.Impact = "high";
                context.RiskFactors.Add("Complex dependency web may make changes difficult");
            }
            else if (depData.Metrics.TotalDependencies > 50)
            {
                context.Impact = "moderate";
            }
            else
            {
                context.Impact = "low";
            }
            
            // Add specific risk factors
            if (depData.Metrics.IncomingCount > 20)
            {
                context.RiskFactors.Add("High afferent coupling - changes will impact many components");
            }
            
            if (depData.Metrics.OutgoingCount > 30)
            {
                context.RiskFactors.Add("High efferent coupling - fragile to changes in dependencies");
            }
            
            // Suggestions
            context.Suggestions = new List<string>();
            
            if (depData.CircularDependencies.Any())
            {
                context.Suggestions.Add("Break circular dependencies using interfaces or mediator pattern");
            }
            
            if (depData.Metrics.IncomingCount > 15 && depData.Metrics.OutgoingCount > 15)
            {
                context.Suggestions.Add("Consider splitting this component - it has too many responsibilities");
            }
            
            var projectCount = depData.Graph.GetProjectDistribution().Count;
            if (projectCount > 3)
            {
                context.Suggestions.Add($"Dependencies span {projectCount} projects - consider consolidating or using facades");
            }
        }
        
        return context;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        if (DetailCache == null || string.IsNullOrEmpty(request.DetailRequestToken))
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Detail request token is required"));
        }

        var cachedData = DetailCache.GetDetailData<DependencyData>(request.DetailRequestToken);
        if (cachedData == null)
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Invalid or expired detail request token"));
        }

        var result = request.DetailLevelId switch
        {
            "circular" => GetCircularDependencyDetails(cachedData, request),
            "hotspots" => GetHotspotDetails(cachedData, request),
            "graph" => GetGraphDetails(cachedData, request),
            "layer" => GetLayerDetails(cachedData, request),
            _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}")
        };
        
        return Task.FromResult(result);
    }

    private object GetCircularDependencyDetails(DependencyData data, DetailRequest request)
    {
        var maxItems = Convert.ToInt32(request.MaxResults ?? 10);
        
        var circularPaths = data.CircularDependencies
            .Take(maxItems)
            .Select(c => new
            {
                path = c.Path,
                type = c.Type,
                length = c.Path.Count,
                components = c.Path.Select(p => new
                {
                    name = p,
                    isInterface = p.Contains("I") && char.IsUpper(p[p.IndexOf("I") + 1])
                })
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "circular",
            circularDependencies = circularPaths,
            summary = new
            {
                total = data.CircularDependencies.Count,
                returned = circularPaths.Count,
                avgPathLength = circularPaths.Any() ? circularPaths.Average(p => p.length) : 0
            },
            suggestions = GenerateCircularDependencySuggestions(circularPaths),
            metadata = new ResponseMetadata
            {
                TotalResults = data.CircularDependencies.Count,
                ReturnedResults = circularPaths.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(circularPaths)
            }
        };
    }

    private List<string> GenerateCircularDependencySuggestions(dynamic circularPaths)
    {
        var suggestions = new List<string>
        {
            "Consider introducing interfaces to break direct dependencies",
            "Use dependency injection to invert dependencies",
            "Apply the Dependency Inversion Principle (DIP)",
            "Consider using events or mediator pattern for decoupling"
        };
        
        return suggestions;
    }

    private object GetHotspotDetails(DependencyData data, DetailRequest request)
    {
        var maxItems = Convert.ToInt32(request.MaxResults ?? 10);
        var hotspots = data.Graph.GetConnectionCounts()
            .OrderByDescending(kvp => kvp.Value)
            .Take(maxItems)
            .Select(kvp => new
            {
                component = kvp.Key,
                totalConnections = kvp.Value,
                incoming = data.Graph.GetIncomingDependencies(kvp.Key).Count(),
                outgoing = data.Graph.GetOutgoingDependencies(kvp.Key).Count(),
                projects = data.Graph.GetProjectsForComponent(kvp.Key)
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "hotspots",
            hotspots = hotspots,
            metadata = new ResponseMetadata
            {
                TotalResults = data.Graph.GetConnectionCounts().Count,
                ReturnedResults = hotspots.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(hotspots)
            }
        };
    }

    private object GetGraphDetails(DependencyData data, DetailRequest request)
    {
        var depth = Convert.ToInt32(request.AdditionalInfo?["depth"] ?? 2);
        var graphData = data.Graph.GetGraphVisualization(depth);
        
        return new
        {
            success = true,
            detailLevel = "graph",
            graph = graphData,
            visualization = GenerateAsciiGraph(graphData),
            metadata = new ResponseMetadata
            {
                TotalResults = graphData.Nodes.Count,
                ReturnedResults = graphData.Nodes.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(graphData)
            }
        };
    }

    private string GenerateAsciiGraph(GraphVisualizationData graphData)
    {
        // Simple ASCII representation
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Dependency Graph:");
        sb.AppendLine("================");
        
        foreach (var node in graphData.Nodes.Take(10))
        {
            sb.AppendLine($"\n{node.Name}");
            
            var outgoing = graphData.Edges.Where(e => e.From == node.Id).Take(5);
            foreach (var edge in outgoing)
            {
                var target = graphData.Nodes.FirstOrDefault(n => n.Id == edge.To);
                sb.AppendLine($"  → {target?.Name ?? edge.To}");
            }
        }
        
        return sb.ToString();
    }

    private object GetLayerDetails(DependencyData data, DetailRequest request)
    {
        var layers = AnalyzeLayers(data.Graph);
        
        return new
        {
            success = true,
            detailLevel = "layers",
            layers = layers,
            violations = DetectLayerViolations(data.Graph),
            metadata = new ResponseMetadata
            {
                TotalResults = layers.Count,
                ReturnedResults = layers.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(layers)
            }
        };
    }

    private Dictionary<string, LayerInfo> AnalyzeLayers(OptimizedDependencyGraph graph)
    {
        var layers = new Dictionary<string, LayerInfo>();
        var allDeps = graph.GetAllDependencies();
        
        foreach (var dep in allDeps)
        {
            var layer = DetermineLayer(dep.FullName);
            if (!layers.ContainsKey(layer))
            {
                layers[layer] = new LayerInfo { Name = layer, Components = new HashSet<string>() };
            }
            layers[layer].Components.Add(dep.FullName);
        }
        
        return layers;
    }

    private string DetermineLayer(string componentName)
    {
        var lower = componentName.ToLowerInvariant();
        
        if (lower.Contains("controller") || lower.Contains("view") || lower.Contains("page"))
            return "Presentation";
        if (lower.Contains("service") || lower.Contains("manager"))
            return "Business";
        if (lower.Contains("repository") || lower.Contains("context") || lower.Contains("dal"))
            return "Data";
        if (lower.Contains("model") || lower.Contains("entity"))
            return "Domain";
        
        return "Infrastructure";
    }

    // Data structures
    private class DependencyData
    {
        public SymbolInfo? Symbol { get; set; }
        public string Direction { get; set; } = "";
        public int Depth { get; set; }
        public bool IncludeTests { get; set; }
        public bool IncludeExternalDependencies { get; set; }
        public OptimizedDependencyGraph Graph { get; set; } = new();
        public List<CircularDependency> CircularDependencies { get; set; } = new();
        public DependencyMetrics Metrics { get; set; } = new();
    }

    private class OptimizedDependencyGraph
    {
        private readonly ConcurrentDictionary<string, HashSet<DependencyInfo>> _incomingDependencies = new();
        private readonly ConcurrentDictionary<string, HashSet<DependencyInfo>> _outgoingDependencies = new();
        private int _maxDepth = 0;

        public void AddIncomingDependency(string symbol, DependencyInfo dependency)
        {
            _incomingDependencies.AddOrUpdate(symbol,
                new HashSet<DependencyInfo> { dependency },
                (key, set) => { set.Add(dependency); return set; });
        }

        public void AddOutgoingDependency(string symbol, DependencyInfo dependency)
        {
            _outgoingDependencies.AddOrUpdate(symbol,
                new HashSet<DependencyInfo> { dependency },
                (key, set) => { set.Add(dependency); return set; });
        }

        public List<DependencyInfo> GetAllDependencies()
        {
            var all = new HashSet<DependencyInfo>();
            
            foreach (var deps in _incomingDependencies.Values)
                all.UnionWith(deps);
                
            foreach (var deps in _outgoingDependencies.Values)
                all.UnionWith(deps);
                
            return all.ToList();
        }

        public List<DependencyRelationship> GetAllRelationships()
        {
            var relationships = new List<DependencyRelationship>();
            
            foreach (var kvp in _incomingDependencies)
            {
                foreach (var dep in kvp.Value)
                {
                    relationships.Add(new DependencyRelationship 
                    { 
                        From = dep.FullName, 
                        To = kvp.Key, 
                        Type = "incoming" 
                    });
                }
            }
            
            foreach (var kvp in _outgoingDependencies)
            {
                foreach (var dep in kvp.Value)
                {
                    relationships.Add(new DependencyRelationship 
                    { 
                        From = kvp.Key, 
                        To = dep.FullName, 
                        Type = "outgoing" 
                    });
                }
            }
            
            return relationships;
        }

        public Dictionary<string, int> GetConnectionCounts()
        {
            var counts = new Dictionary<string, int>();
            
            foreach (var kvp in _incomingDependencies)
            {
                counts[kvp.Key] = kvp.Value.Count;
            }
            
            foreach (var kvp in _outgoingDependencies)
            {
                if (counts.ContainsKey(kvp.Key))
                    counts[kvp.Key] += kvp.Value.Count;
                else
                    counts[kvp.Key] = kvp.Value.Count;
            }
            
            return counts;
        }

        public Dictionary<string, int> GetProjectDistribution()
        {
            var projects = new Dictionary<string, int>();
            
            foreach (var dep in GetAllDependencies())
            {
                if (!projects.ContainsKey(dep.Project))
                    projects[dep.Project] = 0;
                projects[dep.Project]++;
            }
            
            return projects;
        }

        public List<DependencyInfo> GetIncomingDependencies(string symbol)
        {
            return _incomingDependencies.TryGetValue(symbol, out var deps) 
                ? deps.ToList() 
                : new List<DependencyInfo>();
        }

        public List<DependencyInfo> GetOutgoingDependencies(string symbol)
        {
            return _outgoingDependencies.TryGetValue(symbol, out var deps) 
                ? deps.ToList() 
                : new List<DependencyInfo>();
        }

        public List<DependencyInfo> GetTopIncomingDependencies(int count)
        {
            return _incomingDependencies
                .OrderByDescending(kvp => kvp.Value.Count)
                .Take(count)
                .SelectMany(kvp => kvp.Value.Take(1))
                .ToList();
        }

        public List<string> GetProjectsForComponent(string component)
        {
            var projects = new HashSet<string>();
            
            if (_incomingDependencies.TryGetValue(component, out var incoming))
            {
                projects.UnionWith(incoming.Select(d => d.Project));
            }
            
            if (_outgoingDependencies.TryGetValue(component, out var outgoing))
            {
                projects.UnionWith(outgoing.Select(d => d.Project));
            }
            
            return projects.ToList();
        }

        public GraphVisualizationData GetGraphVisualization(int maxDepth)
        {
            var nodes = new List<GraphNode>();
            var edges = new List<GraphEdge>();
            var nodeMap = new Dictionary<string, string>();
            
            var allSymbols = new HashSet<string>();
            allSymbols.UnionWith(_incomingDependencies.Keys);
            allSymbols.UnionWith(_outgoingDependencies.Keys);
            
            // Create nodes
            foreach (var symbol in allSymbols.Take(50)) // Limit for visualization
            {
                var nodeId = $"node_{nodes.Count}";
                nodeMap[symbol] = nodeId;
                
                nodes.Add(new GraphNode
                {
                    Id = nodeId,
                    Name = symbol,
                    IncomingCount = GetIncomingDependencies(symbol).Count,
                    OutgoingCount = GetOutgoingDependencies(symbol).Count
                });
            }
            
            // Create edges
            foreach (var symbol in nodeMap.Keys)
            {
                var outgoing = GetOutgoingDependencies(symbol);
                foreach (var dep in outgoing.Where(d => nodeMap.ContainsKey(d.FullName)))
                {
                    edges.Add(new GraphEdge
                    {
                        From = nodeMap[symbol],
                        To = nodeMap[dep.FullName],
                        Type = dep.Kind
                    });
                }
            }
            
            return new GraphVisualizationData { Nodes = nodes, Edges = edges };
        }

        public int GetIncomingCount() => _incomingDependencies.Sum(kvp => kvp.Value.Count);
        public int GetOutgoingCount() => _outgoingDependencies.Sum(kvp => kvp.Value.Count);
        public int GetUniqueIncomingSymbols() => _incomingDependencies.SelectMany(kvp => kvp.Value).Select(d => d.FullName).Distinct().Count();
        public int GetUniqueOutgoingSymbols() => _outgoingDependencies.SelectMany(kvp => kvp.Value).Select(d => d.FullName).Distinct().Count();
        public int GetMaxDepth() => _maxDepth;
        
        public void UpdateMaxDepth(int depth)
        {
            if (depth > _maxDepth) _maxDepth = depth;
        }
    }

    private class DependencyInfo : IEquatable<DependencyInfo>
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ContainingType { get; set; } = "";
        public string ContainingNamespace { get; set; } = "";
        public string Project { get; set; } = "";
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public string DeclaredAccessibility { get; set; } = "";
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }

        public bool Equals(DependencyInfo? other)
        {
            if (other == null) return false;
            return FullName == other.FullName && Project == other.Project;
        }

        public override bool Equals(object? obj) => Equals(obj as DependencyInfo);
        public override int GetHashCode() => HashCode.Combine(FullName, Project);
    }

    private class CircularDependency
    {
        public List<string> Path { get; set; } = new();
        public string Type { get; set; } = "";
    }

    private class DependencyMetrics
    {
        public int TotalDependencies { get; set; }
        public int IncomingCount { get; set; }
        public int OutgoingCount { get; set; }
        public int UniqueIncomingSymbols { get; set; }
        public int UniqueOutgoingSymbols { get; set; }
        public int UniqueFiles { get; set; }
        public int CircularDependencyCount { get; set; }
        public int MaxDepthReached { get; set; }
    }

    private class SymbolInfo
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ContainerName { get; set; } = "";
        public string Accessibility { get; set; } = "";
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public string? Documentation { get; set; }
    }

    private class LayerViolation
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
    }

    private class DependencyRelationship
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
    }

    private class LayerInfo
    {
        public string Name { get; set; } = "";
        public HashSet<string> Components { get; set; } = new();
    }

    private class GraphNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int IncomingCount { get; set; }
        public int OutgoingCount { get; set; }
    }

    private class GraphEdge
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
    }

    private class GraphVisualizationData
    {
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphEdge> Edges { get; set; } = new();
    }
}