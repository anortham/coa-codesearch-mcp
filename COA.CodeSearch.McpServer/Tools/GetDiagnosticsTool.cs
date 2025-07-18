using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class GetDiagnosticsTool
{
    private readonly ILogger<GetDiagnosticsTool> _logger;
    private readonly CodeAnalysisService _workspaceService;

    public GetDiagnosticsTool(ILogger<GetDiagnosticsTool> logger, CodeAnalysisService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string path,
        string[]? severities = null,
        int maxResults = 100,
        bool summaryOnly = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetDiagnostics request for {Path}", path);

            var severityFilter = ParseSeverities(severities);
            var diagnosticResults = new List<DiagnosticInfo>();

            // Handle solution file
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var workspace = await _workspaceService.GetWorkspaceAsync(path, cancellationToken);
                if (workspace == null)
                {
                    return new { success = false, error = $"Could not load solution: {path}" };
                }

                foreach (var project in workspace.CurrentSolution.Projects)
                {
                    await AddProjectDiagnosticsAsync(project, severityFilter, diagnosticResults, cancellationToken);
                }
            }
            // Handle project file
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await _workspaceService.GetProjectAsync(path, cancellationToken);
                if (project == null)
                {
                    return new { success = false, error = $"Could not load project: {path}" };
                }

                await AddProjectDiagnosticsAsync(project, severityFilter, diagnosticResults, cancellationToken);
            }
            // Handle single file
            else
            {
                var document = await _workspaceService.GetDocumentAsync(path, cancellationToken);
                if (document == null)
                {
                    return new { success = false, error = $"Could not find document: {path}" };
                }

                await AddDocumentDiagnosticsAsync(document, severityFilter, diagnosticResults, cancellationToken);
            }

            // Count by severity
            var severityCounts = diagnosticResults
                .GroupBy(d => d.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            // Group by file
            var groupedDiagnostics = diagnosticResults
                .GroupBy(d => d.FilePath)
                .Select(g => new
                {
                    filePath = g.Key,
                    count = g.Count(),
                    diagnostics = g.OrderBy(d => d.Line).ThenBy(d => d.Column).ToArray()
                })
                .OrderBy(g => g.filePath)
                .ToArray();

            // If summary only, don't include the actual diagnostics
            if (summaryOnly)
            {
                return new
                {
                    success = true,
                    path = path,
                    totalDiagnostics = diagnosticResults.Count,
                    severityCounts = severityCounts,
                    fileCount = groupedDiagnostics.Length,
                    fileSummary = groupedDiagnostics.Select(g => new
                    {
                        filePath = g.filePath,
                        count = g.count
                    }).ToArray()
                };
            }

            // Apply maxResults limit
            var limitedResults = diagnosticResults.Take(maxResults).ToList();
            var truncated = diagnosticResults.Count > maxResults;

            var limitedGroupedDiagnostics = limitedResults
                .GroupBy(d => d.FilePath)
                .Select(g => new
                {
                    filePath = g.Key,
                    diagnostics = g.OrderBy(d => d.Line).ThenBy(d => d.Column).ToArray()
                })
                .OrderBy(g => g.filePath)
                .ToArray();

            return new
            {
                success = true,
                path = path,
                totalDiagnostics = diagnosticResults.Count,
                severityCounts = severityCounts,
                fileCount = groupedDiagnostics.Length,
                resultsReturned = limitedResults.Count,
                truncated = truncated,
                diagnosticsByFile = limitedGroupedDiagnostics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetDiagnostics");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task AddProjectDiagnosticsAsync(
        Project project,
        HashSet<DiagnosticSeverity> severityFilter,
        List<DiagnosticInfo> results,
        CancellationToken cancellationToken)
    {
        if (!project.SupportsCompilation)
            return;

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
            return;

        var diagnostics = compilation.GetDiagnostics(cancellationToken);
        
        foreach (var diagnostic in diagnostics)
        {
            if (!severityFilter.Contains(diagnostic.Severity))
                continue;

            if (diagnostic.Location.Kind != LocationKind.SourceFile)
                continue;

            var location = diagnostic.Location.GetLineSpan();
            var document = project.GetDocument(diagnostic.Location.SourceTree);
            
            if (document != null)
            {
                var text = await document.GetTextAsync(cancellationToken);
                var lineText = text.Lines[location.StartLinePosition.Line].ToString();

                results.Add(new DiagnosticInfo
                {
                    Id = diagnostic.Id,
                    Severity = diagnostic.Severity.ToString(),
                    Message = diagnostic.GetMessage(),
                    FilePath = document.FilePath ?? location.Path,
                    Line = location.StartLinePosition.Line + 1,
                    Column = location.StartLinePosition.Character + 1,
                    EndLine = location.EndLinePosition.Line + 1,
                    EndColumn = location.EndLinePosition.Character + 1,
                    PreviewText = lineText.Trim(),
                    Category = diagnostic.Descriptor.Category,
                    HelpLink = diagnostic.Descriptor.HelpLinkUri
                });
            }
        }
    }

    private async Task AddDocumentDiagnosticsAsync(
        Document document,
        HashSet<DiagnosticSeverity> severityFilter,
        List<DiagnosticInfo> results,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
            return;

        var diagnostics = semanticModel.GetDiagnostics();
        var text = await document.GetTextAsync(cancellationToken);

        foreach (var diagnostic in diagnostics)
        {
            if (!severityFilter.Contains(diagnostic.Severity))
                continue;

            var lineSpan = diagnostic.Location.GetLineSpan();
            var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

            results.Add(new DiagnosticInfo
            {
                Id = diagnostic.Id,
                Severity = diagnostic.Severity.ToString(),
                Message = diagnostic.GetMessage(),
                FilePath = document.FilePath ?? "",
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                PreviewText = lineText.Trim(),
                Category = diagnostic.Descriptor.Category,
                HelpLink = diagnostic.Descriptor.HelpLinkUri
            });
        }
    }

    private static HashSet<DiagnosticSeverity> ParseSeverities(string[]? severities)
    {
        var result = new HashSet<DiagnosticSeverity>();

        if (severities == null || severities.Length == 0)
        {
            // Default to all severities
            result.Add(DiagnosticSeverity.Error);
            result.Add(DiagnosticSeverity.Warning);
            result.Add(DiagnosticSeverity.Info);
            result.Add(DiagnosticSeverity.Hidden);
            return result;
        }

        foreach (var severity in severities)
        {
            switch (severity.ToLowerInvariant())
            {
                case "error":
                    result.Add(DiagnosticSeverity.Error);
                    break;
                case "warning":
                    result.Add(DiagnosticSeverity.Warning);
                    break;
                case "info":
                    result.Add(DiagnosticSeverity.Info);
                    break;
                case "hidden":
                    result.Add(DiagnosticSeverity.Hidden);
                    break;
            }
        }

        return result;
    }

    private class DiagnosticInfo
    {
        public string Id { get; init; } = "";
        public string Severity { get; init; } = "";
        public string Message { get; init; } = "";
        public string FilePath { get; init; } = "";
        public int Line { get; init; }
        public int Column { get; init; }
        public int EndLine { get; init; }
        public int EndColumn { get; init; }
        public string PreviewText { get; init; } = "";
        public string Category { get; init; } = "";
        public string? HelpLink { get; init; }
    }
}