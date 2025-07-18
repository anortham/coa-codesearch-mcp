using COA.Roslyn.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.Roslyn.McpServer.Tools;

public class DependencyAnalysisTool
{
    private readonly ILogger<DependencyAnalysisTool> _logger;
    private readonly RoslynWorkspaceService _workspaceService;
    private readonly IConfiguration _configuration;

    public DependencyAnalysisTool(ILogger<DependencyAnalysisTool> logger, RoslynWorkspaceService workspaceService, IConfiguration configuration)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string symbol,
        string workspacePath,
        string direction = "both", // incoming, outgoing, both
        int depth = 3,
        bool includeTests = false,
        bool includeExternalDependencies = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("DependencyAnalysis request for symbol: {Symbol} in {WorkspacePath}", symbol, workspacePath);

            // Get the workspace
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
            
            // Find the target symbol
            var targetSymbol = await FindSymbolByName(solution, symbol, cancellationToken);
            if (targetSymbol == null)
            {
                return new
                {
                    success = false,
                    error = $"Symbol '{symbol}' not found in workspace"
                };
            }

            var dependencyGraph = new DependencyGraph();
            var visitedSymbols = new HashSet<string>();

            // Analyze dependencies based on direction
            if (direction == "incoming" || direction == "both")
            {
                await AnalyzeIncomingDependencies(solution, targetSymbol, dependencyGraph, visitedSymbols, depth, includeTests, includeExternalDependencies, cancellationToken);
            }

            if (direction == "outgoing" || direction == "both")
            {
                await AnalyzeOutgoingDependencies(solution, targetSymbol, dependencyGraph, visitedSymbols, depth, includeTests, includeExternalDependencies, cancellationToken);
            }

            return new
            {
                success = true,
                symbol = symbol,
                direction = direction,
                depth = depth,
                includeTests = includeTests,
                includeExternalDependencies = includeExternalDependencies,
                dependencyGraph = dependencyGraph.ToResult()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dependency analysis");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private async Task<ISymbol?> FindSymbolByName(Solution solution, string symbolName, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null) continue;

            // Search for the symbol in this project
            var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
                project, 
                name => name.Equals(symbolName, StringComparison.OrdinalIgnoreCase), 
                cancellationToken);

            var targetSymbol = symbols.FirstOrDefault();
            if (targetSymbol != null)
                return targetSymbol;
        }

        return null;
    }

    private async Task AnalyzeIncomingDependencies(Solution solution, ISymbol symbol, DependencyGraph graph, 
        HashSet<string> visited, int remainingDepth, bool includeTests, bool includeExternalDependencies, CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0) return;

        var symbolKey = GetSymbolKey(symbol);
        if (visited.Contains(symbolKey)) return;
        visited.Add(symbolKey);

        try
        {
            // Find all references to this symbol
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (!location.Location.IsInSource) continue;

                    var document = solution.GetDocument(location.Document.Id);
                    if (document == null) continue;

                    // Skip test projects if not included
                    if (!includeTests && IsTestProject(document.Project))
                        continue;

                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    if (semanticModel == null) continue;

                    var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                    if (syntaxRoot == null) continue;

                    var node = syntaxRoot.FindNode(location.Location.SourceSpan);
                    var containingSymbol = semanticModel.GetEnclosingSymbol(location.Location.SourceSpan.Start, cancellationToken);

                    if (containingSymbol != null)
                    {
                        var dependency = CreateDependencyInfo(containingSymbol, document, location.Location);
                        graph.AddIncomingDependency(symbolKey, dependency);

                        // Recursively analyze this symbol's dependencies
                        await AnalyzeIncomingDependencies(solution, containingSymbol, graph, visited, remainingDepth - 1, includeTests, includeExternalDependencies, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing incoming dependencies for {Symbol}", symbolKey);
        }
    }

    private async Task AnalyzeOutgoingDependencies(Solution solution, ISymbol symbol, DependencyGraph graph, 
        HashSet<string> visited, int remainingDepth, bool includeTests, bool includeExternalDependencies, CancellationToken cancellationToken)
    {
        if (remainingDepth <= 0) return;

        var symbolKey = GetSymbolKey(symbol);
        if (visited.Contains(symbolKey)) return;
        visited.Add(symbolKey);

        try
        {
            // Get the syntax nodes for this symbol
            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource) continue;

                var document = solution.GetDocument(location.SourceTree);
                if (document == null) continue;

                // Skip test projects if not included
                if (!includeTests && IsTestProject(document.Project))
                    continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) continue;

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                if (syntaxRoot == null) continue;

                var node = syntaxRoot.FindNode(location.SourceSpan);

                // Find all symbols referenced within this symbol's implementation
                var descendants = node.DescendantNodes();
                foreach (var descendant in descendants)
                {
                    var referencedSymbol = semanticModel.GetSymbolInfo(descendant, cancellationToken).Symbol;
                    if (referencedSymbol == null) continue;

                    // Skip self-references and built-in types
                    if (SymbolEqualityComparer.Default.Equals(referencedSymbol, symbol)) continue;
                    if (referencedSymbol.ContainingAssembly?.Name == "System.Runtime") continue;

                    // Skip external dependencies if not included
                    if (!includeExternalDependencies && IsExternalDependency(referencedSymbol, solution))
                        continue;

                    var dependency = CreateDependencyInfo(referencedSymbol, document, descendant.GetLocation());
                    graph.AddOutgoingDependency(symbolKey, dependency);

                    // Recursively analyze this symbol's dependencies
                    await AnalyzeOutgoingDependencies(solution, referencedSymbol, graph, visited, remainingDepth - 1, includeTests, includeExternalDependencies, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing outgoing dependencies for {Symbol}", symbolKey);
        }
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
        foreach (var project in solution.Projects)
        {
            if (project.AssemblyName == symbol.ContainingAssembly.Name)
                return false;
        }

        return true;
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
}

public class DependencyGraph
{
    private readonly Dictionary<string, List<DependencyInfo>> _incomingDependencies = new();
    private readonly Dictionary<string, List<DependencyInfo>> _outgoingDependencies = new();

    public void AddIncomingDependency(string symbol, DependencyInfo dependency)
    {
        if (!_incomingDependencies.ContainsKey(symbol))
            _incomingDependencies[symbol] = new List<DependencyInfo>();
        
        _incomingDependencies[symbol].Add(dependency);
    }

    public void AddOutgoingDependency(string symbol, DependencyInfo dependency)
    {
        if (!_outgoingDependencies.ContainsKey(symbol))
            _outgoingDependencies[symbol] = new List<DependencyInfo>();
        
        _outgoingDependencies[symbol].Add(dependency);
    }

    public object ToResult()
    {
        return new
        {
            incomingDependencies = _incomingDependencies.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GroupBy(d => d.FullName).Select(g => g.First()).ToArray()
            ),
            outgoingDependencies = _outgoingDependencies.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GroupBy(d => d.FullName).Select(g => g.First()).ToArray()
            ),
            summary = new
            {
                totalIncomingDependencies = _incomingDependencies.Values.SelectMany(v => v).Count(),
                totalOutgoingDependencies = _outgoingDependencies.Values.SelectMany(v => v).Count(),
                uniqueIncomingSymbols = _incomingDependencies.Values.SelectMany(v => v).Select(d => d.FullName).Distinct().Count(),
                uniqueOutgoingSymbols = _outgoingDependencies.Values.SelectMany(v => v).Select(d => d.FullName).Distinct().Count()
            }
        };
    }
}

public class DependencyInfo
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
}