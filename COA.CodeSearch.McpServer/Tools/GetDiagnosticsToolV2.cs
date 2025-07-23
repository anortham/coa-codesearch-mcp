using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Claude-optimized version of GetDiagnosticsTool with progressive disclosure
/// </summary>
public class GetDiagnosticsToolV2 : ClaudeOptimizedToolBase
{
    private readonly CodeAnalysisService _workspaceService;

    public GetDiagnosticsToolV2(
        ILogger<GetDiagnosticsToolV2> logger,
        CodeAnalysisService workspaceService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string path,
        string[]? severities = null,
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

            Logger.LogInformation("GetDiagnostics request for {Path}", path);

            var severityFilter = ParseSeverities(severities);
            var diagnosticData = await CollectDiagnosticsAsync(path, severityFilter, cancellationToken);

            if (!diagnosticData.Success)
            {
                return CreateErrorResponse<object>(diagnosticData.Error ?? "Failed to collect diagnostics");
            }

            // Create AI-optimized response
            var response = CreateAiOptimizedResponse(diagnosticData, mode, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in GetDiagnosticsV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<DiagnosticsData> CollectDiagnosticsAsync(
        string path,
        HashSet<DiagnosticSeverity> severityFilter,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var projectDiagnostics = new Dictionary<string, List<DiagnosticInfo>>();

        try
        {
            // Handle solution file
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var workspace = await _workspaceService.GetWorkspaceAsync(path, cancellationToken);
                if (workspace == null)
                {
                    return new DiagnosticsData { Success = false, Error = $"Could not load solution: {path}" };
                }

                foreach (var project in workspace.CurrentSolution.Projects)
                {
                    var projectDiags = new List<DiagnosticInfo>();
                    await AddProjectDiagnosticsAsync(project, severityFilter, projectDiags, cancellationToken);
                    
                    if (projectDiags.Any())
                    {
                        projectDiagnostics[project.Name] = projectDiags;
                        diagnostics.AddRange(projectDiags);
                    }
                }
            }
            // Handle project file
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await _workspaceService.GetProjectAsync(path, cancellationToken);
                if (project == null)
                {
                    return new DiagnosticsData { Success = false, Error = $"Could not load project: {path}" };
                }

                var projectDiags = new List<DiagnosticInfo>();
                await AddProjectDiagnosticsAsync(project, severityFilter, projectDiags, cancellationToken);
                
                if (projectDiags.Any())
                {
                    projectDiagnostics[project.Name] = projectDiags;
                    diagnostics.AddRange(projectDiags);
                }
            }
            // Handle single file
            else
            {
                var document = await _workspaceService.GetDocumentAsync(path, cancellationToken);
                if (document == null)
                {
                    return new DiagnosticsData { Success = false, Error = $"Could not find document: {path}" };
                }

                await AddDocumentDiagnosticsAsync(document, severityFilter, diagnostics, cancellationToken);
                if (diagnostics.Any())
                {
                    projectDiagnostics[document.Project.Name] = diagnostics;
                }
            }

            return new DiagnosticsData
            {
                Success = true,
                Path = path,
                Diagnostics = diagnostics,
                ProjectDiagnostics = projectDiagnostics,
                GroupedByFile = diagnostics.GroupBy(d => d.FilePath).ToDictionary(g => g.Key, g => g.ToList()),
                GroupedBySeverity = diagnostics.GroupBy(d => d.Severity).ToDictionary(g => g.Key, g => g.ToList()),
                GroupedByCategory = diagnostics.GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.ToList())
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsData { Success = false, Error = ex.Message };
        }
    }

    private ClaudeSummaryData CreateSummaryData(DiagnosticsData data)
    {
        var insights = new List<string>();
        
        // Analyze severity distribution
        var errorCount = data.GroupedBySeverity.GetValueOrDefault("Error")?.Count ?? 0;
        var warningCount = data.GroupedBySeverity.GetValueOrDefault("Warning")?.Count ?? 0;
        var infoCount = data.GroupedBySeverity.GetValueOrDefault("Info")?.Count ?? 0;
        
        if (errorCount > 0)
        {
            insights.Add($"🚨 {errorCount} error{(errorCount > 1 ? "s" : "")} preventing compilation - must fix immediately");
        }
        
        if (warningCount > 50)
        {
            insights.Add($"⚠️ High warning count ({warningCount}) - code quality concerns");
        }
        else if (warningCount > 20)
        {
            insights.Add($"⚠️ Moderate warning count ({warningCount}) - consider cleanup");
        }
        
        // Analyze patterns
        var topCategories = data.GroupedByCategory
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(3)
            .ToList();
            
        if (topCategories.Any())
        {
            var categoryInsights = topCategories
                .Select(kvp => $"{kvp.Key}: {kvp.Value.Count}")
                .ToList();
            insights.Add($"Top issue categories: {string.Join(", ", categoryInsights)}");
        }
        
        // Identify common issues
        var commonIssues = data.Diagnostics
            .GroupBy(d => d.Id)
            .Where(g => g.Count() > 3)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .ToList();
            
        if (commonIssues.Any())
        {
            insights.Add($"Recurring issues: {string.Join(", ", commonIssues.Select(g => $"{g.Key} ({g.Count()}x)"))}");
        }
        
        // File hotspots
        var hotspots = IdentifyHotspots(
            data.Diagnostics,
            d => d.FilePath,
            g => g.Count(),
            maxHotspots: 5);
        
        // Project-level insights
        if (data.ProjectDiagnostics.Count > 1)
        {
            var projectSummary = data.ProjectDiagnostics
                .OrderByDescending(kvp => kvp.Value.Count)
                .Take(3)
                .Select(kvp => $"{kvp.Key}: {kvp.Value.Count}")
                .ToList();
            insights.Add($"Projects with most issues: {string.Join(", ", projectSummary)}");
        }
        
        // Create preview of critical issues
        var criticalIssues = data.Diagnostics
            .Where(d => d.Severity == "Error")
            .Take(5)
            .Select(d => new PreviewItem
            {
                File = d.FilePath,
                Line = d.Line,
                Preview = d.PreviewText,
                Context = $"{d.Id}: {d.Message}"
            })
            .ToList();
            
        if (!criticalIssues.Any())
        {
            // Show top warnings if no errors
            criticalIssues = data.Diagnostics
                .Where(d => d.Severity == "Warning")
                .Take(5)
                .Select(d => new PreviewItem
                {
                    File = d.FilePath,
                    Line = d.Line,
                    Preview = d.PreviewText,
                    Context = $"{d.Id}: {d.Message}"
                })
                .ToList();
        }
        
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = data.Diagnostics.Count,
                AffectedFiles = data.GroupedByFile.Count,
                EstimatedFullResponseTokens = SizeEstimator.EstimateTokens(data),
                KeyInsights = insights
            },
            ByCategory = data.GroupedBySeverity
                .ToDictionary(
                    kvp => kvp.Key.ToLowerInvariant(),
                    kvp => new CategorySummary
                    {
                        Files = kvp.Value.Select(d => d.FilePath).Distinct().Count(),
                        Occurrences = kvp.Value.Count,
                        PrimaryPattern = kvp.Value.GroupBy(d => d.Id).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key
                    }
                ),
            Hotspots = hotspots,
            Preview = new ChangePreview
            {
                TopChanges = criticalIssues,
                FullContext = false,
                GetFullContextCommand = new { detailLevel = "errors", maxItems = 20 }
            }
        };
    }

    private object CreateAiOptimizedResponse(DiagnosticsData data, ResponseMode mode, CancellationToken cancellationToken)
    {
        // Calculate severity breakdown
        var severityCounts = data.GroupedBySeverity
            .ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => kvp.Value.Count);
        
        // Identify most common issues
        var topIssues = data.Diagnostics
            .GroupBy(d => d.Id)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { id = g.Key, count = g.Count(), example = g.First().Message })
            .ToList();

        // Get hotspots
        var hotspots = data.GroupedByFile
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(5)
            .Select(kvp => new { file = kvp.Key, issues = kvp.Value.Count })
            .ToList();

        // Generate insights
        var insights = GenerateDiagnosticInsights(data);
        
        // Priority assessment
        var priority = AssessDiagnosticPriority(data);
        
        // Generate actions
        var actions = new List<object>();
        
        if (severityCounts.ContainsKey("error") && severityCounts["error"] > 0)
        {
            actions.Add(new 
            { 
                id = "fix_errors", 
                cmd = new { detailLevel = "errors", maxItems = 20 }, 
                tokens = Math.Min(3000, severityCounts["error"] * 150),
                priority = "critical"
            });
        }
        
        if (hotspots.Any())
        {
            actions.Add(new 
            { 
                id = "review_hotspots", 
                cmd = new { detailLevel = "hotspots", maxFiles = 5 }, 
                tokens = Math.Min(2500, hotspots.Count * 500),
                priority = severityCounts.ContainsKey("error") ? "high" : "recommended"
            });
        }

        if (topIssues.Any(i => i.count > 5))
        {
            actions.Add(new 
            { 
                id = "fix_recurring", 
                cmd = new { detailLevel = "recurring", threshold = 5 }, 
                tokens = 2000,
                priority = "normal"
            });
        }

        if (mode == ResponseMode.Summary && data.Diagnostics.Count < 200)
        {
            actions.Add(new 
            { 
                id = "full_details", 
                cmd = new { responseMode = "full" }, 
                tokens = EstimateFullResponseTokens(data),
                priority = "available"
            });
        }
        
        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (data.Diagnostics.Count > 0)
            {
                actions.Add(new 
                { 
                    id = "analyze_issues", 
                    cmd = new { detailLevel = "summary" }, 
                    tokens = 1500,
                    priority = "available"
                });
            }
            else
            {
                actions.Add(new 
                { 
                    id = "code_health_check", 
                    cmd = new { includeInfo = true }, 
                    tokens = 1000,
                    priority = "available"
                });
            }
        }

        return new
        {
            success = true,
            operation = "get_diagnostics",
            scope = new
            {
                path = data.Path,
                type = data.Path.EndsWith(".sln") ? "solution" :
                       data.Path.EndsWith(".csproj") ? "project" : "file",
                projects = data.ProjectDiagnostics.Count
            },
            summary = new
            {
                total = data.Diagnostics.Count,
                files = data.GroupedByFile.Count,
                severity = severityCounts,
                priority = priority
            },
            topIssues = topIssues,
            hotspots = hotspots,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokens = SizeEstimator.EstimateTokens(new { diagnostics = data.Diagnostics.Take(10) }),
                cached = $"diag_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private string AssessDiagnosticPriority(DiagnosticsData data)
    {
        var errorCount = data.GroupedBySeverity.ContainsKey("Error") ? 
            data.GroupedBySeverity["Error"].Count : 0;
        
        if (errorCount > 20) return "critical";
        if (errorCount > 5) return "high";
        if (errorCount > 0) return "medium";
        
        var warningCount = data.GroupedBySeverity.ContainsKey("Warning") ? 
            data.GroupedBySeverity["Warning"].Count : 0;
        
        if (warningCount > 50) return "medium";
        if (warningCount > 20) return "low";
        
        return "minimal";
    }

    private List<string> GenerateDiagnosticInsights(DiagnosticsData data)
    {
        var insights = new List<string>();
        
        // Severity insights
        var errorCount = data.GroupedBySeverity.ContainsKey("Error") ? 
            data.GroupedBySeverity["Error"].Count : 0;
        var warningCount = data.GroupedBySeverity.ContainsKey("Warning") ? 
            data.GroupedBySeverity["Warning"].Count : 0;
        
        if (errorCount > 0)
        {
            insights.Add($"{errorCount} build error(s) - must fix before deployment");
        }
        
        if (warningCount > 50)
        {
            insights.Add($"High warning count ({warningCount}) - consider cleanup");
        }
        
        // Common issue patterns
        var commonIssues = data.Diagnostics
            .GroupBy(d => d.Id)
            .Where(g => g.Count() > 5)
            .OrderByDescending(g => g.Count())
            .Take(3);
        
        if (commonIssues.Any())
        {
            var issueList = string.Join(", ", commonIssues.Select(g => $"{g.Key} ({g.Count()}x)"));
            insights.Add($"Recurring: {issueList}");
        }
        
        // File concentration
        var filesWithManyIssues = data.GroupedByFile
            .Where(kvp => kvp.Value.Count > 10)
            .Count();
        
        if (filesWithManyIssues > 0)
        {
            insights.Add($"{filesWithManyIssues} file(s) with 10+ issues - focus areas");
        }
        
        // Category insights
        var nullabilityIssues = data.Diagnostics.Count(d => d.Id.StartsWith("CS8"));
        if (nullabilityIssues > 20)
        {
            insights.Add($"{nullabilityIssues} nullability warnings - enable nullable reference types");
        }
        
        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (data.Diagnostics.Count == 0)
            {
                insights.Add("No diagnostics found - code appears clean");
            }
            else
            {
                insights.Add($"Found {data.Diagnostics.Count} diagnostic(s) in {data.GroupedByFile.Count} file(s)");
            }
        }
        
        return insights;
    }

    private int EstimateFullResponseTokens(DiagnosticsData data)
    {
        // Estimate ~100 tokens per diagnostic with full details
        return Math.Min(25000, data.Diagnostics.Count * 100);
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
                    HelpLink = diagnostic.Descriptor.HelpLinkUri,
                    ProjectName = project.Name
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
                HelpLink = diagnostic.Descriptor.HelpLinkUri,
                ProjectName = document.Project.Name
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

    protected override int GetTotalResults<T>(T data)
    {
        if (data is DiagnosticsData diagData)
        {
            return diagData.Diagnostics.Count;
        }
        return 0;
    }

    protected override NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = base.GenerateNextActions(data, currentMode, metadata);
        
        if (currentMode == ResponseMode.Summary && data is DiagnosticsData diagData)
        {
            actions.Recommended.Clear();
            
            // Prioritize errors
            var errorCount = diagData.GroupedBySeverity.GetValueOrDefault("Error")?.Count ?? 0;
            if (errorCount > 0)
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "review_errors",
                    Description = "Review all compilation errors",
                    Reason = "Errors prevent compilation and must be fixed first",
                    EstimatedTokens = Math.Min(errorCount * 100, 5000),
                    Priority = "critical",
                    Command = new 
                    { 
                        detailLevel = "severity",
                        severity = "error",
                        detailRequestToken = metadata.DetailRequestToken
                    }
                });
            }
            
            // Review hotspot files
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "review_hotspots",
                Description = "Review files with most issues",
                Reason = "Focus cleanup efforts on problematic files",
                EstimatedTokens = 3000,
                Priority = errorCount > 0 ? "high" : "critical",
                Command = new
                {
                    detailLevel = "hotspots",
                    detailRequestToken = metadata.DetailRequestToken,
                    maxFiles = 5
                }
            });
            
            // Review by category
            var topCategory = diagData.GroupedByCategory
                .OrderByDescending(kvp => kvp.Value.Count)
                .FirstOrDefault();
                
            if (topCategory.Value != null)
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "review_category",
                    Description = $"Review {topCategory.Key} issues",
                    Reason = $"Most common category with {topCategory.Value.Count} issues",
                    EstimatedTokens = 2000,
                    Priority = "medium",
                    Command = new
                    {
                        detailLevel = "category",
                        category = topCategory.Key,
                        detailRequestToken = metadata.DetailRequestToken
                    }
                });
            }
        }
        
        return actions;
    }

    protected override ResultContext AnalyzeResultContext<T>(T data)
    {
        var context = base.AnalyzeResultContext(data);
        
        if (data is DiagnosticsData diagData)
        {
            // Assess code health
            var errorCount = diagData.GroupedBySeverity.GetValueOrDefault("Error")?.Count ?? 0;
            var warningCount = diagData.GroupedBySeverity.GetValueOrDefault("Warning")?.Count ?? 0;
            
            if (errorCount > 0)
            {
                context.Impact = "critical";
                context.RiskFactors.Add($"{errorCount} compilation errors blocking build");
            }
            else if (warningCount > 50)
            {
                context.Impact = "high";
                context.RiskFactors.Add("High warning count indicates code quality issues");
            }
            else if (warningCount > 20)
            {
                context.Impact = "moderate";
            }
            else
            {
                context.Impact = "low";
            }
            
            // Add specific risk factors
            var nullabilityWarnings = diagData.Diagnostics.Count(d => d.Id.StartsWith("CS8"));
            if (nullabilityWarnings > 10)
            {
                context.RiskFactors.Add($"{nullabilityWarnings} nullable reference warnings - potential NullReferenceExceptions");
            }
            
            var obsoleteWarnings = diagData.Diagnostics.Count(d => d.Id == "CS0618" || d.Id == "CS0619");
            if (obsoleteWarnings > 0)
            {
                context.RiskFactors.Add($"{obsoleteWarnings} obsolete API usages - technical debt");
            }
            
            // Suggestions
            context.Suggestions = new List<string>();
            
            if (errorCount > 0)
            {
                context.Suggestions.Add("Fix all errors before proceeding with other development");
            }
            
            if (warningCount > 20)
            {
                context.Suggestions.Add("Consider enabling warnings as errors to maintain code quality");
            }
            
            var commonIssues = diagData.Diagnostics
                .GroupBy(d => d.Id)
                .Where(g => g.Count() > 5)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();
                
            foreach (var issue in commonIssues)
            {
                context.Suggestions.Add($"Address recurring {issue.Key} ({issue.Count()} occurrences) - consider project-wide fix");
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

        var cachedData = DetailCache.GetDetailData<DiagnosticsData>(request.DetailRequestToken);
        if (cachedData == null)
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Invalid or expired detail request token"));
        }

        var result = request.DetailLevelId switch
        {
            "severity" => GetSeverityDetails(cachedData, request),
            "category" => GetCategoryDetails(cachedData, request),
            "hotspots" => GetHotspotDetails(cachedData, request),
            "project" => GetProjectDetails(cachedData, request),
            "errors" => GetErrorDetails(cachedData, request),
            _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}")
        };
        
        return Task.FromResult(result);
    }

    private object GetSeverityDetails(DiagnosticsData data, DetailRequest request)
    {
        var severity = request.AdditionalInfo?["severity"]?.ToString() ?? "error";
        
        var severityDiagnostics = data.GroupedBySeverity
            .GetValueOrDefault(severity.Substring(0, 1).ToUpper() + severity.Substring(1).ToLower())
            ?? new List<DiagnosticInfo>();
        
        var maxResults = Convert.ToInt32(request.MaxResults ?? 50);
        var diagnostics = severityDiagnostics.Take(maxResults).ToList();
        
        return new
        {
            success = true,
            detailLevel = "severity",
            severity = severity,
            diagnostics = diagnostics,
            byFile = diagnostics
                .GroupBy(d => d.FilePath)
                .Select(g => new
                {
                    filePath = g.Key,
                    count = g.Count(),
                    diagnostics = g.ToList()
                })
                .ToList(),
            summary = new
            {
                total = severityDiagnostics.Count,
                returned = diagnostics.Count,
                truncated = severityDiagnostics.Count > maxResults
            },
            metadata = new ResponseMetadata
            {
                TotalResults = severityDiagnostics.Count,
                ReturnedResults = diagnostics.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(diagnostics)
            }
        };
    }

    private object GetCategoryDetails(DiagnosticsData data, DetailRequest request)
    {
        var category = request.AdditionalInfo?["category"]?.ToString() ?? "Code Quality";
        
        var categoryDiagnostics = data.GroupedByCategory.GetValueOrDefault(category) ?? new List<DiagnosticInfo>();
        var maxResults = Convert.ToInt32(request.MaxResults ?? 50);
        var diagnostics = categoryDiagnostics.Take(maxResults).ToList();
        
        return new
        {
            success = true,
            detailLevel = "category",
            category = category,
            diagnostics = diagnostics,
            byId = diagnostics
                .GroupBy(d => d.Id)
                .OrderByDescending(g => g.Count())
                .Select(g => new
                {
                    id = g.Key,
                    count = g.Count(),
                    message = g.First().Message,
                    locations = g.Select(d => new { file = d.FilePath, line = d.Line }).Take(5).ToList()
                })
                .ToList(),
            metadata = new ResponseMetadata
            {
                TotalResults = categoryDiagnostics.Count,
                ReturnedResults = diagnostics.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(diagnostics)
            }
        };
    }

    private object GetHotspotDetails(DiagnosticsData data, DetailRequest request)
    {
        var maxFiles = Convert.ToInt32(request.AdditionalInfo?["maxFiles"] ?? 5);
        
        var hotspotFiles = data.GroupedByFile
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(maxFiles)
            .Select(kvp => new
            {
                filePath = kvp.Key,
                diagnostics = kvp.Value.OrderBy(d => d.Line).ToList(),
                summary = new
                {
                    total = kvp.Value.Count,
                    bySeverity = kvp.Value.GroupBy(d => d.Severity).ToDictionary(g => g.Key, g => g.Count()),
                    byCategory = kvp.Value.GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.Count())
                }
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "hotspots",
            files = hotspotFiles,
            metadata = new ResponseMetadata
            {
                TotalResults = hotspotFiles.Sum(f => f.diagnostics.Count),
                ReturnedResults = hotspotFiles.Sum(f => f.diagnostics.Count),
                EstimatedTokens = SizeEstimator.EstimateTokens(hotspotFiles)
            }
        };
    }

    private object GetProjectDetails(DiagnosticsData data, DetailRequest request)
    {
        var projectName = request.AdditionalInfo?["project"]?.ToString();
        
        var projectData = string.IsNullOrEmpty(projectName) 
            ? data.ProjectDiagnostics
            : data.ProjectDiagnostics.Where(kvp => kvp.Key == projectName).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        var projectSummaries = projectData
            .Select(kvp => new
            {
                project = kvp.Key,
                diagnostics = kvp.Value.Take(20).ToList(),
                summary = new
                {
                    total = kvp.Value.Count,
                    bySeverity = kvp.Value.GroupBy(d => d.Severity).ToDictionary(g => g.Key, g => g.Count()),
                    topIssues = kvp.Value.GroupBy(d => d.Id)
                        .OrderByDescending(g => g.Count())
                        .Take(3)
                        .Select(g => new { id = g.Key, count = g.Count() })
                        .ToList()
                }
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "project",
            projects = projectSummaries,
            metadata = new ResponseMetadata
            {
                TotalResults = projectSummaries.Sum(p => p.summary.total),
                ReturnedResults = projectSummaries.Sum(p => p.diagnostics.Count),
                EstimatedTokens = SizeEstimator.EstimateTokens(projectSummaries)
            }
        };
    }

    private object GetErrorDetails(DiagnosticsData data, DetailRequest request)
    {
        var maxItems = Convert.ToInt32(request.MaxResults ?? 20);
        
        var errors = data.Diagnostics
            .Where(d => d.Severity == "Error")
            .Take(maxItems)
            .Select(d => new
            {
                file = d.FilePath,
                line = d.Line,
                column = d.Column,
                id = d.Id,
                message = d.Message,
                preview = d.PreviewText,
                helpLink = d.HelpLink
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "errors",
            errors = errors,
            byId = errors
                .GroupBy(e => e.id)
                .Select(g => new
                {
                    id = g.Key,
                    count = g.Count(),
                    message = g.First().message,
                    files = g.Select(e => e.file).Distinct().ToList()
                })
                .ToList(),
            metadata = new ResponseMetadata
            {
                TotalResults = data.GroupedBySeverity.GetValueOrDefault("Error")?.Count ?? 0,
                ReturnedResults = errors.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(errors)
            }
        };
    }

    // Data structures
    private class DiagnosticsData
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Path { get; set; } = "";
        public List<DiagnosticInfo> Diagnostics { get; set; } = new();
        public Dictionary<string, List<DiagnosticInfo>> ProjectDiagnostics { get; set; } = new();
        public Dictionary<string, List<DiagnosticInfo>> GroupedByFile { get; set; } = new();
        public Dictionary<string, List<DiagnosticInfo>> GroupedBySeverity { get; set; } = new();
        public Dictionary<string, List<DiagnosticInfo>> GroupedByCategory { get; set; } = new();
    }

    private class DiagnosticInfo
    {
        public string Id { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string PreviewText { get; set; } = "";
        public string Category { get; set; } = "";
        public string? HelpLink { get; set; }
        public string ProjectName { get; set; } = "";
    }
}