using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool for searching TypeScript symbols across a codebase
/// </summary>
public class TypeScriptSearchTool : ITool
{
    public string ToolName => "search_typescript";
    public string Description => "Search for TypeScript symbols across codebase";
    public ToolCategory Category => ToolCategory.TypeScript;
    private readonly ILogger<TypeScriptSearchTool> _logger;
    private readonly TypeScriptTextAnalysisService _tsAnalysis;
    private readonly ILuceneIndexService _luceneService;

    public TypeScriptSearchTool(
        ILogger<TypeScriptSearchTool> logger,
        TypeScriptTextAnalysisService tsAnalysis,
        ILuceneIndexService luceneService)
    {
        _logger = logger;
        _tsAnalysis = tsAnalysis;
        _luceneService = luceneService;
    }

    public async Task<object> SearchTypeScriptAsync(
        string symbolName,
        string workspacePath,
        string mode = "both",
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Searching TypeScript for '{Symbol}' in {Path} (mode: {Mode})", 
                symbolName, workspacePath, mode);

            var results = new
            {
                success = true,
                query = symbolName,
                mode = mode,
                definition = (TypeScriptSymbolLocation?)null,
                references = new List<TypeScriptSymbolLocation>(),
                projects = new List<TypeScriptProject>(),
                metadata = new Dictionary<string, object>()
            };

            // Detect TypeScript projects
            var projects = await _tsAnalysis.DetectProjectsAsync(workspacePath, cancellationToken);
            results = results with { projects = projects };

            // Search based on mode
            if (mode == "definition" || mode == "both")
            {
                var definition = await _tsAnalysis.FindDefinitionAsync(symbolName, workspacePath, cancellationToken);
                if (definition != null)
                {
                    results = results with { definition = definition };
                }
            }

            if (mode == "references" || mode == "both")
            {
                var references = await _tsAnalysis.FindReferencesAsync(symbolName, workspacePath, cancellationToken);
                
                // Limit results
                if (references.Count > maxResults)
                {
                    references = references.Take(maxResults).ToList();
                    results.metadata["truncated"] = true;
                    results.metadata["totalFound"] = references.Count;
                }
                
                results = results with { references = references };
            }

            // Group references by file
            if (results.references.Any())
            {
                var groupedByFile = results.references
                    .GroupBy(r => r.FilePath)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(r => new
                        {
                            line = r.Line,
                            column = r.Column,
                            symbolType = r.SymbolType,
                            lineText = r.LineText
                        }).ToList()
                    );
                
                results.metadata["groupedByFile"] = groupedByFile;
                results.metadata["filesAffected"] = groupedByFile.Count;
            }

            results.metadata["projectsFound"] = projects.Count;
            results.metadata["hasTypeScriptConfig"] = projects.Any(p => !p.IsInferred);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching TypeScript for {Symbol}", symbolName);
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}