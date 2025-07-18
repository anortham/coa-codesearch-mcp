using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

public class SearchSymbolsTool
{
    private readonly ILogger<SearchSymbolsTool> _logger;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public SearchSymbolsTool(ILogger<SearchSymbolsTool> logger, CodeAnalysisService workspaceService, IConfiguration configuration)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string pattern,
        string workspacePath,
        string[]? kinds = null,
        bool fuzzy = false,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("SearchSymbols request for pattern: {Pattern} in {WorkspacePath}", pattern, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return new
                {
                    success = false,
                    error = "Search pattern cannot be empty"
                };
            }

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
                // Look for a .sln file in the directory
                var slnFiles = Directory.GetFiles(workspacePath, "*.sln");
                
                // Temporary debug logging
                try
                {
                    var logPath = @"C:\temp\mcp-workspace-debug.log";
                    System.IO.Directory.CreateDirectory(@"C:\temp");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SearchSymbols - Directory: {workspacePath}\n");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SearchSymbols - Found {slnFiles.Length} .sln files\n");
                    foreach (var sln in slnFiles)
                    {
                        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SearchSymbols - Solution: {sln}\n");
                    }
                }
                catch { }
                
                if (slnFiles.Length == 1)
                {
                    workspace = await _workspaceService.GetWorkspaceAsync(slnFiles[0], cancellationToken);
                }
                else if (slnFiles.Length > 1)
                {
                    return new
                    {
                        success = false,
                        error = $"Multiple solution files found in {workspacePath}. Please specify the exact .sln file path."
                    };
                }
                else
                {
                    // Look for a single .csproj file
                    var csprojFiles = Directory.GetFiles(workspacePath, "*.csproj");
                    if (csprojFiles.Length == 1)
                    {
                        var project = await _workspaceService.GetProjectAsync(csprojFiles[0], cancellationToken);
                        workspace = project?.Solution.Workspace;
                    }
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
            var symbolKinds = ParseSymbolKinds(kinds);
            
            // Convert pattern to regex or use fuzzy matching
            Func<string, bool> matcher;
            if (fuzzy)
            {
                matcher = name => FuzzyMatch(pattern.ToLowerInvariant(), name.ToLowerInvariant());
            }
            else
            {
                var regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                matcher = name => regex.IsMatch(name);
            }

            var results = new List<Models.SymbolInfo>();
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _configuration.GetValue("McpServer:ParallelismDegree", 4)
            };

            // Search in all projects in parallel
            await Parallel.ForEachAsync(solution.Projects, parallelOptions, async (project, ct) =>
            {
                if (!project.SupportsCompilation)
                    return;

                var compilation = await project.GetCompilationAsync(ct);
                if (compilation == null)
                    return;

                // Search in all symbols
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

            return new
            {
                success = true,
                pattern = pattern,
                fuzzy = fuzzy,
                totalResults = results.Count,
                results = results.OrderBy(r => r.Name).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SearchSymbols");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
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
                    result.Add(SymbolKind.NamedType);
                    break;
                case "interface":
                    result.Add(SymbolKind.NamedType);
                    break;
                case "enum":
                    result.Add(SymbolKind.NamedType);
                    break;
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