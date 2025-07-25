using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of SearchSymbolsTool with structured response format
/// </summary>
public class SearchSymbolsToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "search_symbols_v2";
    public override string Description => "AI-optimized symbol search with structured response";
    public override ToolCategory Category => ToolCategory.Search;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public SearchSymbolsToolV2(
        ILogger<SearchSymbolsToolV2> logger,
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
        string pattern,
        string workspacePath,
        string[]? kinds = null,
        bool fuzzy = false,
        int maxResults = 100,
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

            Logger.LogInformation("SearchSymbols request for pattern: {Pattern} in {WorkspacePath}", pattern, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return CreateErrorResponse<object>("Search pattern cannot be empty");
            }

            // Get the workspace
            var workspace = await LoadWorkspaceAsync(workspacePath, cancellationToken);
            if (workspace == null)
            {
                return CreateErrorResponse<object>($"Could not load workspace: {workspacePath}");
            }

            var solution = workspace.CurrentSolution;
            var symbolKinds = ParseSymbolKinds(kinds);
            
            // Create matcher
            var matcher = CreateMatcher(pattern, fuzzy);

            // Search for symbols
            var results = await SearchSymbolsAsync(solution, matcher, symbolKinds, maxResults, cancellationToken);

            // Create AI-optimized response
            var response = CreateAiOptimizedResponse(pattern, fuzzy, results, mode, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in SearchSymbolsV2");
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
            else if (slnFiles.Length == 0)
            {
                var csprojFiles = Directory.GetFiles(workspacePath, "*.csproj");
                if (csprojFiles.Length == 1)
                {
                    var project = await _workspaceService.GetProjectAsync(csprojFiles[0], cancellationToken);
                    return project?.Solution.Workspace;
                }
            }
        }
        return null;
    }

    private Func<string, bool> CreateMatcher(string pattern, bool fuzzy)
    {
        if (fuzzy)
        {
            return name => FuzzyMatch(pattern.ToLowerInvariant(), name.ToLowerInvariant());
        }
        else
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            return name => regex.IsMatch(name);
        }
    }

    private async Task<List<Models.SymbolInfo>> SearchSymbolsAsync(
        Solution solution,
        Func<string, bool> matcher,
        HashSet<SymbolKind> symbolKinds,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<Models.SymbolInfo>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _configuration.GetValue("McpServer:ParallelismDegree", 4)
        };

        await Parallel.ForEachAsync(solution.Projects, parallelOptions, async (project, ct) =>
        {
            if (!project.SupportsCompilation)
                return;

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation == null)
                return;

            var visitor = new SymbolSearchVisitor(matcher, symbolKinds, maxResults - results.Count);
            visitor.Visit(compilation.GlobalNamespace);

            foreach (var symbol in visitor.FoundSymbols)
            {
                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                if (location == null)
                    continue;

                var doc = project.GetDocument(location.SourceTree);
                if (doc == null)
                    continue;

                var lineSpan = location.GetLineSpan();
                var text = await doc.GetTextAsync(ct);
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
                                FilePath = doc.FilePath ?? location.SourceTree?.FilePath ?? "Unknown",
                                Line = lineSpan.StartLinePosition.Line + 1,
                                Column = lineSpan.StartLinePosition.Character + 1,
                                EndLine = lineSpan.EndLinePosition.Line + 1,
                                EndColumn = lineSpan.EndLinePosition.Character + 1,
                                PreviewText = lineText.Trim()
                            },
                            Metadata = new Dictionary<string, object>
                            {
                                ["ProjectName"] = project.Name ?? "Unknown",
                                ["AssemblyName"] = project.AssemblyName ?? project.Name ?? "Unknown",
                                ["IsStatic"] = symbol.IsStatic,
                                ["IsAbstract"] = symbol.IsAbstract,
                                ["IsVirtual"] = symbol.IsVirtual,
                                ["IsOverride"] = symbol.IsOverride,
                                ["DeclaredAccessibility"] = symbol.DeclaredAccessibility.ToString()
                            }
                        });
                    }
                }
            }
        });

        return results;
    }

    private object CreateAiOptimizedResponse(
        string pattern,
        bool fuzzy,
        List<Models.SymbolInfo> results,
        ResponseMode mode,
        CancellationToken cancellationToken)
    {
        // Group results by type
        var byType = results.GroupBy(r => r.Kind).ToDictionary(
            g => g.Key.ToLowerInvariant(),
            g => new { count = g.Count(), symbols = g.Select(s => s.Name).Distinct().Take(5).ToList() }
        );

        // Group by project
        var byProject = results
            .GroupBy(r => r.Metadata?["ProjectName"]?.ToString() ?? "Unknown")
            .ToDictionary(
                g => g.Key,
                g => g.Count()
            );

        // Find hotspots (files with most matches)
        var hotspots = results
            .GroupBy(r => r.Location?.FilePath ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { file = g.Key, matches = g.Count() })
            .ToList();

        // Generate insights
        var insights = GenerateSymbolInsights(pattern, fuzzy, results);

        // Generate actions
        var actions = new List<object>();

        if (results.Count >= 100)
        {
            actions.Add(new 
            { 
                id = "refine_search", 
                cmd = new { pattern = pattern + "*", kinds = new[] { "class", "interface" } }, 
                tokens = 1500,
                priority = "recommended"
            });
        }

        if (byType.Count > 1)
        {
            var topType = byType.OrderByDescending(kvp => kvp.Value.count).First();
            actions.Add(new 
            { 
                id = "filter_by_type", 
                cmd = new { pattern = pattern, kinds = new[] { topType.Key } }, 
                tokens = Math.Min(1000, topType.Value.count * 50),
                priority = "normal"
            });
        }

        if (hotspots.Any())
        {
            actions.Add(new 
            { 
                id = "explore_hotspot", 
                cmd = new { file = hotspots.First().file }, 
                tokens = 500,
                priority = "normal"
            });
        }

        if (mode == ResponseMode.Summary && results.Count < 200)
        {
            actions.Add(new 
            { 
                id = "full_details", 
                cmd = new { responseMode = "full" }, 
                tokens = EstimateFullResponseTokens(results),
                priority = "available"
            });
        }

        return new
        {
            success = true,
            operation = ToolNames.SearchSymbols,
            query = new
            {
                pattern = pattern,
                fuzzy = fuzzy,
                mode = fuzzy ? "fuzzy" : "wildcard"
            },
            summary = new
            {
                total = results.Count,
                projects = byProject.Count,
                types = byType,
                truncated = results.Count >= 100
            },
            distribution = byProject,
            hotspots = hotspots,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokens = SizeEstimator.EstimateTokens(new { results = results.Take(10) }),
                cached = $"sym_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private List<string> GenerateSymbolInsights(string pattern, bool fuzzy, List<Models.SymbolInfo> results)
    {
        var insights = new List<string>();

        if (results.Count == 0)
        {
            insights.Add($"No symbols found matching '{pattern}'");
            if (!fuzzy && !pattern.Contains("*"))
            {
                insights.Add("Try wildcard pattern (e.g., '*Service') or fuzzy search");
            }
        }
        else if (results.Count >= 100)
        {
            insights.Add($"100+ matches - search truncated. Refine pattern for better results");
        }

        // Type distribution insights
        var typeGroups = results.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()).ToList();
        if (typeGroups.Any())
        {
            var topTypes = string.Join(", ", typeGroups.Take(3).Select(g => $"{g.Key} ({g.Count()})"));
            insights.Add($"Most common: {topTypes}");
        }

        // Naming pattern insights
        if (results.Count > 10)
        {
            var interfaces = results.Where(r => r.Name.StartsWith("I") && r.Kind == "NamedType").Count();
            if (interfaces > results.Count * 0.3)
            {
                insights.Add($"{interfaces} interfaces found - consider filtering by type");
            }

            var testSymbols = results.Where(r => r.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)).Count();
            if (testSymbols > results.Count * 0.3)
            {
                insights.Add($"{testSymbols} test-related symbols - exclude test projects if needed");
            }
        }

        // Accessibility insights
        var publicSymbols = results.Where(r => r.Metadata?["DeclaredAccessibility"]?.ToString() == "Public").Count();
        if (publicSymbols > 0 && publicSymbols < results.Count)
        {
            insights.Add($"{publicSymbols}/{results.Count} are public - consider API surface");
        }

        return insights;
    }

    private int EstimateFullResponseTokens(List<Models.SymbolInfo> results)
    {
        // Estimate ~150 tokens per symbol with full details
        return Math.Min(25000, results.Count * 150);
    }

    private static HashSet<SymbolKind> ParseSymbolKinds(string[]? kinds)
    {
        var result = new HashSet<SymbolKind>();
        
        if (kinds == null || kinds.Length == 0)
        {
            // Default to common symbol kinds
            result.Add(SymbolKind.NamedType);
            result.Add(SymbolKind.Method);
            result.Add(SymbolKind.Property);
            result.Add(SymbolKind.Field);
            result.Add(SymbolKind.Event);
            return result;
        }

        foreach (var kind in kinds)
        {
            switch (kind.ToLowerInvariant())
            {
                case "class":
                case "interface":
                case "enum":
                case "struct":
                    result.Add(SymbolKind.NamedType);
                    break;
                case "method":
                    result.Add(SymbolKind.Method);
                    break;
                case "property":
                    result.Add(SymbolKind.Property);
                    break;
                case "field":
                    result.Add(SymbolKind.Field);
                    break;
                case "event":
                    result.Add(SymbolKind.Event);
                    break;
                case "namespace":
                    result.Add(SymbolKind.Namespace);
                    break;
            }
        }

        return result;
    }

    private static bool FuzzyMatch(string pattern, string text)
    {
        var patternIdx = 0;
        var textIdx = 0;

        while (patternIdx < pattern.Length && textIdx < text.Length)
        {
            if (pattern[patternIdx] == text[textIdx])
            {
                patternIdx++;
            }
            textIdx++;
        }

        return patternIdx == pattern.Length;
    }

    private static string GetContainerName(ISymbol symbol)
    {
        if (symbol.ContainingType != null)
            return symbol.ContainingType.ToDisplayString();
        if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
            return symbol.ContainingNamespace.ToDisplayString();
        return "";
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        // Simple extraction of summary tag
        var match = Regex.Match(xml, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is List<Models.SymbolInfo> results)
        {
            return results.Count;
        }
        return 0;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for symbol search"));
    }

    private class SymbolSearchVisitor : SymbolVisitor
    {
        private readonly Func<string, bool> _matcher;
        private readonly HashSet<SymbolKind> _kinds;
        private readonly int _maxResults;
        public List<ISymbol> FoundSymbols { get; } = new();

        public SymbolSearchVisitor(Func<string, bool> matcher, HashSet<SymbolKind> kinds, int maxResults)
        {
            _matcher = matcher;
            _kinds = kinds;
            _maxResults = maxResults;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (FoundSymbols.Count >= _maxResults)
                    return;
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            if (_kinds.Contains(SymbolKind.NamedType) && _matcher(symbol.Name))
            {
                FoundSymbols.Add(symbol);
            }

            if (FoundSymbols.Count >= _maxResults)
                return;

            foreach (var member in symbol.GetMembers())
            {
                if (FoundSymbols.Count >= _maxResults)
                    return;
                member.Accept(this);
            }
        }

        public override void VisitMethod(IMethodSymbol symbol)
        {
            if (_kinds.Contains(SymbolKind.Method) && _matcher(symbol.Name) && symbol.MethodKind == MethodKind.Ordinary)
            {
                FoundSymbols.Add(symbol);
            }
        }

        public override void VisitProperty(IPropertySymbol symbol)
        {
            if (_kinds.Contains(SymbolKind.Property) && _matcher(symbol.Name))
            {
                FoundSymbols.Add(symbol);
            }
        }

        public override void VisitField(IFieldSymbol symbol)
        {
            if (_kinds.Contains(SymbolKind.Field) && _matcher(symbol.Name))
            {
                FoundSymbols.Add(symbol);
            }
        }

        public override void VisitEvent(IEventSymbol symbol)
        {
            if (_kinds.Contains(SymbolKind.Event) && _matcher(symbol.Name))
            {
                FoundSymbols.Add(symbol);
            }
        }
    }
}