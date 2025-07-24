using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Claude-optimized version of ProjectStructureAnalysisTool with progressive disclosure
/// </summary>
public class ProjectStructureAnalysisToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "project_structure_analysis_v2";
    public override string Description => "AI-optimized project analysis";
    public override ToolCategory Category => ToolCategory.Analysis;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public ProjectStructureAnalysisToolV2(
        ILogger<ProjectStructureAnalysisToolV2> logger,
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
        string workspacePath,
        bool includeMetrics = true,
        bool includeFiles = false,
        bool includeNuGetPackages = false,
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

            Logger.LogInformation("ProjectStructureAnalysis request for: {WorkspacePath}", workspacePath);

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
                return CreateErrorResponse<object>($"Could not load workspace: {workspacePath}");
            }

            var solution = workspace.CurrentSolution;
            var structureData = new ProjectStructureData
            {
                WorkspacePath = workspacePath,
                IncludeMetrics = includeMetrics,
                IncludeFiles = includeFiles,
                IncludeNuGetPackages = includeNuGetPackages
            };

            // Analyze all projects
            foreach (var project in solution.Projects)
            {
                var projectAnalysis = await AnalyzeProject(project, includeFiles, includeNuGetPackages, includeMetrics, cancellationToken);
                structureData.Projects.Add(projectAnalysis);
            }

            // Calculate solution-level metrics
            if (includeMetrics)
            {
                structureData.SolutionMetrics = CalculateSolutionMetrics(structureData.Projects);
            }

            // Create AI-optimized response
            var response = CreateAiOptimizedResponse(structureData, mode, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ProjectStructureAnalysisV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private ClaudeSummaryData CreateSummaryData(ProjectStructureData data)
    {
        var insights = new List<string>();
        
        // Solution-level insights
        if (data.SolutionMetrics != null)
        {
            if (data.SolutionMetrics.TotalProjects > 20)
            {
                insights.Add($"Large solution with {data.SolutionMetrics.TotalProjects} projects - consider solution filtering or modularization");
            }
            
            if (data.SolutionMetrics.TotalLines > 100000)
            {
                insights.Add($"Very large codebase ({data.SolutionMetrics.TotalLines:N0} lines) - performance optimizations recommended");
            }
            
            if (data.SolutionMetrics.Languages.Count > 1)
            {
                insights.Add($"Multi-language solution: {string.Join(", ", data.SolutionMetrics.Languages)}");
            }
            
            // Complexity insights
            if (data.SolutionMetrics.TotalMethods > 0)
            {
                var avgMethodsPerClass = data.SolutionMetrics.TotalClasses > 0 
                    ? (double)data.SolutionMetrics.TotalMethods / data.SolutionMetrics.TotalClasses 
                    : 0;
                    
                if (avgMethodsPerClass > 20)
                {
                    insights.Add($"High average methods per class ({avgMethodsPerClass:F1}) - consider refactoring large classes");
                }
            }
        }

        // Project-level insights
        var projectTypes = data.Projects.GroupBy(p => p.OutputType).ToDictionary(g => g.Key, g => g.Count());
        if (projectTypes.Count > 1)
        {
            var typeBreakdown = string.Join(", ", projectTypes.Select(kvp => $"{kvp.Value} {kvp.Key}"));
            insights.Add($"Mixed project types: {typeBreakdown}");
        }

        // Dependency analysis
        var projectsWithManyRefs = data.Projects.Where(p => (p.ProjectReferences?.Count ?? 0) > 10).ToList();
        if (projectsWithManyRefs.Any())
        {
            insights.Add($"{projectsWithManyRefs.Count} project(s) with >10 project dependencies - review for circular dependencies");
        }

        // NuGet insights
        if (data.IncludeNuGetPackages)
        {
            var allPackages = data.Projects
                .SelectMany(p => p.NuGetPackages ?? new List<NuGetPackage>())
                .GroupBy(pkg => pkg.Name)
                .Where(g => g.Select(p => p.Version).Distinct().Count() > 1)
                .ToList();
                
            if (allPackages.Any())
            {
                insights.Add($"{allPackages.Count} NuGet packages with version conflicts - consider consolidation");
            }
        }

        // Identify project hotspots (complexity)
        var projectHotspots = data.Projects
            .Where(p => p.Metrics != null)
            .OrderByDescending(p => p.Metrics!.TotalLines)
            .Take(5)
            .Select(p => new Hotspot
            {
                File = p.Name,
                Occurrences = p.Metrics!.TotalLines,
                Complexity = p.Metrics.TotalLines > 10000 ? "high" : p.Metrics.TotalLines > 5000 ? "medium" : "low",
                Reason = $"{p.Metrics.TotalClasses} classes, {p.Metrics.TotalMethods} methods"
            })
            .ToList();

        // Categorize projects
        var categories = new Dictionary<string, CategorySummary>();
        
        // By output type
        var byType = data.Projects.GroupBy(p => p.OutputType);
        foreach (var group in byType)
        {
            categories[group.Key] = new CategorySummary
            {
                Files = group.Count(),
                Occurrences = group.Sum(p => p.Metrics?.TotalLines ?? 0),
                PrimaryPattern = $"{group.Count()} {group.Key} projects"
            };
        }

        // Framework breakdown
        var frameworkBreakdown = data.Projects
            .Where(p => !string.IsNullOrEmpty(p.TargetFramework))
            .GroupBy(p => p.TargetFramework)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = data.Projects.Count,
                AffectedFiles = data.SolutionMetrics?.TotalFiles ?? 0,
                EstimatedFullResponseTokens = SizeEstimator.EstimateTokens(data),
                KeyInsights = insights
            },
            ByCategory = categories,
            Hotspots = projectHotspots,
            Preview = new ChangePreview
            {
                TopChanges = data.Projects.Take(5).Select(p => new PreviewItem
                {
                    File = p.FilePath,
                    Line = 0,
                    Preview = $"{p.Name}: {p.OutputType} - {p.Language} - {p.Metrics?.TotalLines ?? 0} lines",
                    Context = $"{p.ProjectReferences?.Count ?? 0} project refs, {p.AssemblyReferences?.Count ?? 0} assembly refs"
                }).ToList(),
                FullContext = false,
                GetFullContextCommand = new { detailLevel = "projects", maxProjects = 10 }
            }
        };
    }

    private object CreateAiOptimizedResponse(ProjectStructureData data, ResponseMode mode, CancellationToken cancellationToken)
    {
        // Calculate key metrics
        var metrics = data.SolutionMetrics;
        var totalProjects = data.Projects.Count;
        var totalLines = metrics?.TotalLines ?? 0;
        var totalFiles = metrics?.TotalFiles ?? 0;
        
        // Project type breakdown
        var projectTypes = data.Projects
            .GroupBy(p => p.OutputType)
            .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.Count());
        
        // Framework breakdown
        var frameworks = data.Projects
            .Where(p => !string.IsNullOrEmpty(p.TargetFramework))
            .GroupBy(p => p.TargetFramework)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Language breakdown
        var languages = data.Projects
            .GroupBy(p => p.Language)
            .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.Count());
        
        // Hotspots (largest projects)
        var hotspots = data.Projects
            .Where(p => p.Metrics != null)
            .OrderByDescending(p => p.Metrics!.TotalLines)
            .Take(5)
            .Select(p => new { 
                project = p.Name, 
                lines = p.Metrics!.TotalLines,
                classes = p.Metrics.TotalClasses,
                methods = p.Metrics.TotalMethods
            })
            .ToList();
        
        // Dependency analysis
        var projectsWithManyRefs = data.Projects
            .Where(p => (p.ProjectReferences?.Count ?? 0) > 10)
            .Select(p => new { name = p.Name, dependencies = p.ProjectReferences?.Count ?? 0 })
            .ToList();
        
        // NuGet analysis
        var nugetIssues = new List<object>();
        if (data.IncludeNuGetPackages)
        {
            var versionConflicts = data.Projects
                .SelectMany(p => p.NuGetPackages ?? new List<NuGetPackage>())
                .GroupBy(pkg => pkg.Name)
                .Where(g => g.Select(p => p.Version).Distinct().Count() > 1)
                .Select(g => new { 
                    package = g.Key, 
                    versions = g.Select(p => p.Version).Distinct().Count() 
                })
                .Take(5)
                .ToList();
            
            if (versionConflicts.Any())
            {
                nugetIssues.AddRange(versionConflicts);
            }
        }
        
        // Generate insights
        var insights = GenerateProjectStructureInsights(data, totalLines, projectsWithManyRefs.Count);
        
        // Assess health
        var health = AssessProjectStructureHealth(data, totalLines, projectsWithManyRefs.Count);
        
        // Generate actions
        var actions = new List<object>();
        
        if (hotspots.Any(h => h.lines > 10000))
        {
            actions.Add(new 
            { 
                id = "refactor_large", 
                cmd = new { detailLevel = "hotspots", threshold = 10000 }, 
                tokens = 2500,
                priority = "high"
            });
        }
        
        if (projectsWithManyRefs.Count > 0)
        {
            actions.Add(new 
            { 
                id = "analyze_dependencies", 
                cmd = new { detailLevel = "dependencies", minRefs = 10 }, 
                tokens = 2000,
                priority = "recommended"
            });
        }
        
        if (nugetIssues.Any())
        {
            actions.Add(new 
            { 
                id = "fix_nuget_conflicts", 
                cmd = new { detailLevel = "nuget", showConflicts = true }, 
                tokens = 1500,
                priority = "normal"
            });
        }
        
        if (mode == ResponseMode.Summary && totalProjects < 50)
        {
            actions.Add(new 
            { 
                id = "full_details", 
                cmd = new { responseMode = "full", includeFiles = true }, 
                tokens = EstimateFullResponseTokens(data),
                priority = "available"
            });
        }
        
        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (totalProjects > 0)
            {
                actions.Add(new 
                { 
                    id = "explore_structure", 
                    cmd = new { detailLevel = "overview" }, 
                    tokens = 1500,
                    priority = "available"
                });
            }
            else
            {
                actions.Add(new 
                { 
                    id = "initialize_project", 
                    cmd = new { createSample = true }, 
                    tokens = 1000,
                    priority = "available"
                });
            }
        }

        return new
        {
            success = true,
            operation = "project_structure_analysis",
            workspace = new
            {
                path = data.WorkspacePath,
                type = data.WorkspacePath.EndsWith(".sln") ? "solution" :
                       data.WorkspacePath.EndsWith(".csproj") ? "project" : "directory"
            },
            overview = new
            {
                projects = totalProjects,
                files = totalFiles,
                lines = totalLines,
                classes = metrics?.TotalClasses ?? 0,
                methods = metrics?.TotalMethods ?? 0
            },
            breakdown = new
            {
                types = projectTypes,
                languages = languages,
                frameworks = frameworks
            },
            health = health,
            hotspots = hotspots,
            issues = new
            {
                highDependencyProjects = projectsWithManyRefs,
                nugetConflicts = nugetIssues,
                versionConflicts = nugetIssues
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                includeMetrics = data.IncludeMetrics,
                includeFiles = data.IncludeFiles,
                includeNuGet = data.IncludeNuGetPackages,
                tokens = SizeEstimator.EstimateTokens(new { projects = data.Projects.Take(5) }),
                cached = $"struct_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private string AssessProjectStructureHealth(ProjectStructureData data, int totalLines, int projectsWithManyRefs)
    {
        var score = 100;
        
        // Size issues
        if (totalLines > 500000)
        {
            score -= 20;
        }
        else if (totalLines > 100000)
        {
            score -= 10;
        }
        
        // Complexity issues
        if (data.Projects.Count > 50)
        {
            score -= 15;
        }
        else if (data.Projects.Count > 20)
        {
            score -= 5;
        }
        
        // Dependency issues
        if (projectsWithManyRefs > 5)
        {
            score -= 20;
        }
        else if (projectsWithManyRefs > 2)
        {
            score -= 10;
        }
        
        // Large project issues
        var largeProjects = data.Projects.Count(p => p.Metrics?.TotalLines > 20000);
        if (largeProjects > 3)
        {
            score -= 15;
        }
        else if (largeProjects > 1)
        {
            score -= 5;
        }
        
        if (score >= 85) return "excellent";
        if (score >= 70) return "good";
        if (score >= 50) return "fair";
        return "needs-attention";
    }

    private List<string> GenerateProjectStructureInsights(ProjectStructureData data, int totalLines, int projectsWithManyRefs)
    {
        var insights = new List<string>();
        
        // Size insights
        if (data.Projects.Count > 20)
        {
            insights.Add($"Large solution with {data.Projects.Count} projects - consider solution filtering");
        }
        
        if (totalLines > 100000)
        {
            insights.Add($"Large codebase ({totalLines:N0} lines) - ensure build performance is optimized");
        }
        
        // Language insights
        var languages = data.Projects.Select(p => p.Language).Distinct().ToList();
        if (languages.Count > 1)
        {
            insights.Add($"Multi-language solution: {string.Join(", ", languages)}");
        }
        
        // Framework insights
        var frameworks = data.Projects
            .Where(p => !string.IsNullOrEmpty(p.TargetFramework))
            .Select(p => p.TargetFramework)
            .Distinct()
            .Count();
        
        if (frameworks > 2)
        {
            insights.Add($"Multiple target frameworks ({frameworks}) - consider consolidation");
        }
        
        // Complexity insights
        if (data.SolutionMetrics != null && data.SolutionMetrics.TotalClasses > 0)
        {
            var avgMethodsPerClass = (double)data.SolutionMetrics.TotalMethods / data.SolutionMetrics.TotalClasses;
            if (avgMethodsPerClass > 15)
            {
                insights.Add($"High avg methods/class ({avgMethodsPerClass:F1}) - review for SRP violations");
            }
        }
        
        // Dependency insights
        if (projectsWithManyRefs > 0)
        {
            insights.Add($"{projectsWithManyRefs} project(s) with >10 dependencies - check for circular references");
        }
        
        // NuGet insights
        if (data.IncludeNuGetPackages)
        {
            var allPackages = data.Projects
                .SelectMany(p => p.NuGetPackages ?? new List<NuGetPackage>())
                .GroupBy(pkg => pkg.Name)
                .Where(g => g.Select(p => p.Version).Distinct().Count() > 1)
                .Count();
                
            if (allPackages > 0)
            {
                insights.Add($"{allPackages} packages with version conflicts - consolidate versions");
            }
        }
        
        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (data.Projects.Count == 0)
            {
                insights.Add("No projects found in workspace");
            }
            else
            {
                insights.Add($"Solution contains {data.Projects.Count} project(s) with {totalLines:N0} total lines of code");
            }
        }
        
        return insights;
    }

    private int EstimateFullResponseTokens(ProjectStructureData data)
    {
        // Estimate ~200 tokens per project with full details
        return Math.Min(25000, data.Projects.Count * 200);
    }

    private async Task<ProjectAnalysis> AnalyzeProject(Project project, bool includeFiles, bool includeNuGetPackages, bool includeMetrics, CancellationToken cancellationToken)
    {
        var analysis = new ProjectAnalysis
        {
            Name = project.Name,
            Id = project.Id.ToString(),
            FilePath = project.FilePath ?? "",
            Language = project.Language,
            AssemblyName = project.AssemblyName ?? "",
            DefaultNamespace = project.DefaultNamespace ?? "",
            OutputType = GetOutputType(project),
            TargetFramework = GetTargetFramework(project)
        };

        // Analyze project references
        analysis.ProjectReferences = project.ProjectReferences
            .Select(pr => new ProjectReference
            {
                ProjectId = pr.ProjectId.ToString(),
                ProjectName = project.Solution.GetProject(pr.ProjectId)?.Name ?? "Unknown"
            })
            .ToList();

        // Analyze assembly references
        analysis.AssemblyReferences = project.MetadataReferences
            .Where(mr => mr is PortableExecutableReference)
            .Cast<PortableExecutableReference>()
            .Select(per => new AssemblyReference
            {
                Name = Path.GetFileNameWithoutExtension(per.FilePath ?? "Unknown"),
                FilePath = per.FilePath ?? "",
                IsFrameworkReference = IsFrameworkReference(per.FilePath ?? "")
            })
            .ToList();

        // Include NuGet packages if requested
        if (includeNuGetPackages)
        {
            analysis.NuGetPackages = ExtractNuGetPackages(project);
        }

        // Include files if requested
        if (includeFiles)
        {
            analysis.SourceFiles = project.Documents
                .Select(doc => new SourceFile
                {
                    Name = Path.GetFileName(doc.FilePath ?? doc.Name),
                    FilePath = doc.FilePath ?? "",
                    Folders = doc.Folders.ToList()
                })
                .ToList();
        }

        // Calculate metrics if requested
        if (includeMetrics)
        {
            analysis.Metrics = await CalculateProjectMetrics(project, cancellationToken);
        }

        return analysis;
    }

    private async Task<ProjectMetrics> CalculateProjectMetrics(Project project, CancellationToken cancellationToken)
    {
        var metrics = new ProjectMetrics();
        var totalLines = 0;
        var totalClasses = 0;
        var totalMethods = 0;
        var totalProperties = 0;

        foreach (var document in project.Documents)
        {
            try
            {
                var text = await document.GetTextAsync(cancellationToken);
                totalLines += text.Lines.Count;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);

                if (semanticModel != null && syntaxRoot != null)
                {
                    // Count symbols in this document
                    var symbols = semanticModel.GetDeclaredSymbols(syntaxRoot, cancellationToken);
                    
                    totalClasses += symbols.Count(s => s.Kind == SymbolKind.NamedType);
                    totalMethods += symbols.Count(s => s.Kind == SymbolKind.Method);
                    totalProperties += symbols.Count(s => s.Kind == SymbolKind.Property);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error calculating metrics for document {DocumentName}", document.Name);
            }
        }

        metrics.TotalFiles = project.Documents.Count();
        metrics.TotalLines = totalLines;
        metrics.TotalClasses = totalClasses;
        metrics.TotalMethods = totalMethods;
        metrics.TotalProperties = totalProperties;
        metrics.ProjectReferences = project.ProjectReferences.Count();
        metrics.AssemblyReferences = project.MetadataReferences.Count();

        return metrics;
    }

    private SolutionMetrics CalculateSolutionMetrics(List<ProjectAnalysis> projects)
    {
        return new SolutionMetrics
        {
            TotalProjects = projects.Count,
            TotalFiles = projects.Sum(p => p.Metrics?.TotalFiles ?? 0),
            TotalLines = projects.Sum(p => p.Metrics?.TotalLines ?? 0),
            TotalClasses = projects.Sum(p => p.Metrics?.TotalClasses ?? 0),
            TotalMethods = projects.Sum(p => p.Metrics?.TotalMethods ?? 0),
            TotalProperties = projects.Sum(p => p.Metrics?.TotalProperties ?? 0),
            Languages = projects.Select(p => p.Language).Distinct().ToList(),
            TargetFrameworks = projects.Select(p => p.TargetFramework).Where(tf => !string.IsNullOrEmpty(tf)).Distinct().ToList()
        };
    }

    private string GetOutputType(Project project)
    {
        var compilation = project.GetCompilationAsync().Result;
        return compilation?.Options.OutputKind.ToString() ?? "Unknown";
    }

    private string GetTargetFramework(Project project)
    {
        // Try to extract from project file path or compilation options
        return project.ParseOptions?.DocumentationMode.ToString() ?? "";
    }

    private bool IsFrameworkReference(string filePath)
    {
        return filePath.Contains("Microsoft.NETCore.App") ||
               filePath.Contains("Microsoft.WindowsDesktop.App") ||
               filePath.Contains("Microsoft.AspNetCore.App") ||
               filePath.Contains("\\dotnet\\") ||
               filePath.Contains("Program Files\\dotnet\\");
    }

    private List<NuGetPackage> ExtractNuGetPackages(Project project)
    {
        var packages = new List<NuGetPackage>();
        
        // This is a simplified extraction - in a real implementation,
        // you'd parse the project file or use NuGet APIs
        foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>())
        {
            var filePath = reference.FilePath ?? "";
            if (filePath.Contains("\\.nuget\\packages\\"))
            {
                var parts = filePath.Split('\\');
                var packagesIndex = Array.FindIndex(parts, p => p.Equals("packages", StringComparison.OrdinalIgnoreCase));
                
                if (packagesIndex >= 0 && packagesIndex + 2 < parts.Length)
                {
                    var packageName = parts[packagesIndex + 1];
                    var version = parts[packagesIndex + 2];
                    
                    if (!packages.Any(p => p.Name == packageName && p.Version == version))
                    {
                        packages.Add(new NuGetPackage
                        {
                            Name = packageName,
                            Version = version,
                            Path = filePath
                        });
                    }
                }
            }
        }

        return packages;
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is ProjectStructureData structureData)
        {
            if (structureData.IncludeFiles)
            {
                return structureData.Projects.Sum(p => p.SourceFiles?.Count ?? 0);
            }
            return structureData.Projects.Count;
        }
        return 0;
    }

    protected override NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = base.GenerateNextActions(data, currentMode, metadata);
        
        if (currentMode == ResponseMode.Summary && data is ProjectStructureData structureData)
        {
            actions.Recommended.Clear();
            
            // Recommend viewing largest projects
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "view_largest_projects",
                Description = "View details of the largest projects",
                Reason = "Focus on projects with the most code",
                EstimatedTokens = 3000,
                Priority = "high",
                Command = new 
                { 
                    detailLevel = "projects",
                    detailRequestToken = metadata.DetailRequestToken,
                    sortBy = "lines",
                    maxProjects = 5
                }
            });

            // If files were requested but truncated
            if (structureData.IncludeFiles)
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "browse_project_files",
                    Description = "Browse files in specific projects",
                    Reason = "File listing was truncated in summary",
                    EstimatedTokens = 2500,
                    Priority = "medium",
                    Command = new
                    {
                        detailLevel = "files",
                        detailRequestToken = metadata.DetailRequestToken,
                        projectName = "specify-project-name"
                    }
                });
            }

            // If NuGet analysis is available
            if (structureData.IncludeNuGetPackages)
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "analyze_dependencies",
                    Description = "Analyze NuGet package dependencies",
                    Reason = "Check for version conflicts and updates",
                    EstimatedTokens = 2000,
                    Priority = "low",
                    Command = new
                    {
                        detailLevel = "nuget",
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
        
        if (data is ProjectStructureData structureData)
        {
            // Assess complexity
            var totalLines = structureData.SolutionMetrics?.TotalLines ?? 0;
            context.Impact = totalLines switch
            {
                > 100000 => "Very Large Solution",
                > 50000 => "Large Solution",
                > 10000 => "Medium Solution",
                _ => "Small Solution"
            };

            // Risk factors
            context.RiskFactors = new List<string>();
            
            if (structureData.Projects.Count > 20)
            {
                context.RiskFactors.Add("High project count may impact build times");
            }
            
            var circularRefs = DetectPotentialCircularReferences(structureData.Projects);
            if (circularRefs.Any())
            {
                context.RiskFactors.Add($"Potential circular references detected: {string.Join(", ", circularRefs.Take(3))}");
            }

            // Suggestions
            context.Suggestions = new List<string>();
            
            if (structureData.SolutionMetrics?.Languages.Count > 2)
            {
                context.Suggestions.Add("Consider standardizing on fewer languages to reduce complexity");
            }
            
            var avgLinesPerProject = structureData.Projects.Count > 0 
                ? totalLines / structureData.Projects.Count 
                : 0;
                
            if (avgLinesPerProject > 10000)
            {
                context.Suggestions.Add("Consider breaking down large projects into smaller, focused libraries");
            }
        }
        
        return context;
    }

    private List<string> DetectPotentialCircularReferences(List<ProjectAnalysis> projects)
    {
        var circular = new List<string>();
        var projectMap = projects.ToDictionary(p => p.Id, p => p);
        
        foreach (var project in projects)
        {
            if (project.ProjectReferences == null) continue;
            
            foreach (var reference in project.ProjectReferences)
            {
                // Check if referenced project also references this project
                if (projectMap.TryGetValue(reference.ProjectId, out var referencedProject))
                {
                    if (referencedProject.ProjectReferences?.Any(r => r.ProjectId == project.Id) == true)
                    {
                        circular.Add($"{project.Name} <-> {referencedProject.Name}");
                    }
                }
            }
        }
        
        return circular.Distinct().ToList();
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        if (DetailCache == null || string.IsNullOrEmpty(request.DetailRequestToken))
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Detail request token is required"));
        }

        var cachedData = DetailCache.GetDetailData<ProjectStructureData>(request.DetailRequestToken);
        if (cachedData == null)
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Invalid or expired detail request token"));
        }

        var result = request.DetailLevelId switch
        {
            "projects" => GetProjectDetails(cachedData, request),
            "files" => GetFileDetails(cachedData, request),
            "nuget" => GetNuGetDetails(cachedData, request),
            "metrics" => GetMetricsDetails(cachedData, request),
            _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}")
        };
        
        return Task.FromResult(result);
    }

    private object GetProjectDetails(ProjectStructureData data, DetailRequest request)
    {
        var sortBy = request.AdditionalInfo?.GetValueOrDefault("sortBy")?.ToString() ?? "name";
        var maxProjects = Convert.ToInt32(request.AdditionalInfo?.GetValueOrDefault("maxProjects") ?? 10);
        
        var projects = data.Projects.AsEnumerable();
        
        // Apply sorting
        projects = sortBy switch
        {
            "lines" => projects.OrderByDescending(p => p.Metrics?.TotalLines ?? 0),
            "classes" => projects.OrderByDescending(p => p.Metrics?.TotalClasses ?? 0),
            "dependencies" => projects.OrderByDescending(p => p.ProjectReferences?.Count ?? 0),
            _ => projects.OrderBy(p => p.Name)
        };
        
        var selectedProjects = projects.Take(maxProjects).ToList();
        
        return new
        {
            success = true,
            detailLevel = "projects",
            sortedBy = sortBy,
            projects = selectedProjects.Select(p => new
            {
                name = p.Name,
                filePath = p.FilePath,
                language = p.Language,
                outputType = p.OutputType,
                targetFramework = p.TargetFramework,
                metrics = p.Metrics,
                projectReferences = p.ProjectReferences,
                assemblyReferenceCount = p.AssemblyReferences?.Count ?? 0,
                nugetPackageCount = p.NuGetPackages?.Count ?? 0
            }),
            metadata = new ResponseMetadata
            {
                TotalResults = data.Projects.Count,
                ReturnedResults = selectedProjects.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(selectedProjects)
            }
        };
    }

    private object GetFileDetails(ProjectStructureData data, DetailRequest request)
    {
        var projectName = request.AdditionalInfo?.GetValueOrDefault("projectName")?.ToString();
        
        if (string.IsNullOrEmpty(projectName))
        {
            return CreateErrorResponse<object>("Project name is required for file details");
        }
        
        var project = data.Projects.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        if (project == null)
        {
            return CreateErrorResponse<object>($"Project not found: {projectName}");
        }
        
        var files = project.SourceFiles ?? new List<SourceFile>();
        var maxFiles = Convert.ToInt32(request.MaxResults ?? 100);
        
        var selectedFiles = files.Take(maxFiles).ToList();
        
        // Group by folder
        var filesByFolder = selectedFiles
            .GroupBy(f => string.Join("/", f.Folders))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                folder = string.IsNullOrEmpty(g.Key) ? "(root)" : g.Key,
                files = g.Select(f => new { name = f.Name, path = f.FilePath }).ToList()
            })
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "files",
            projectName = project.Name,
            totalFiles = files.Count,
            filesByFolder = filesByFolder,
            metadata = new ResponseMetadata
            {
                TotalResults = files.Count,
                ReturnedResults = selectedFiles.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(filesByFolder)
            }
        };
    }

    private object GetNuGetDetails(ProjectStructureData data, DetailRequest request)
    {
        var allPackages = data.Projects
            .SelectMany(p => (p.NuGetPackages ?? new List<NuGetPackage>())
                .Select(pkg => new { Project = p.Name, Package = pkg }))
            .GroupBy(x => x.Package.Name)
            .Select(g => new
            {
                packageName = g.Key,
                versions = g.GroupBy(x => x.Package.Version)
                    .Select(vg => new
                    {
                        version = vg.Key,
                        projects = vg.Select(x => x.Project).ToList()
                    })
                    .ToList(),
                hasVersionConflict = g.Select(x => x.Package.Version).Distinct().Count() > 1
            })
            .OrderByDescending(p => p.hasVersionConflict)
            .ThenBy(p => p.packageName)
            .ToList();
        
        return new
        {
            success = true,
            detailLevel = "nuget",
            totalPackages = allPackages.Count,
            packagesWithConflicts = allPackages.Count(p => p.hasVersionConflict),
            packages = allPackages,
            metadata = new ResponseMetadata
            {
                TotalResults = allPackages.Count,
                ReturnedResults = allPackages.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(allPackages)
            }
        };
    }

    private object GetMetricsDetails(ProjectStructureData data, DetailRequest request)
    {
        var metricsBreakdown = new
        {
            byLanguage = data.Projects
                .GroupBy(p => p.Language)
                .Select(g => new
                {
                    language = g.Key,
                    projectCount = g.Count(),
                    totalLines = g.Sum(p => p.Metrics?.TotalLines ?? 0),
                    totalClasses = g.Sum(p => p.Metrics?.TotalClasses ?? 0),
                    totalMethods = g.Sum(p => p.Metrics?.TotalMethods ?? 0)
                })
                .ToList(),
            byOutputType = data.Projects
                .GroupBy(p => p.OutputType)
                .Select(g => new
                {
                    outputType = g.Key,
                    projectCount = g.Count(),
                    totalLines = g.Sum(p => p.Metrics?.TotalLines ?? 0),
                    avgLinesPerProject = g.Average(p => p.Metrics?.TotalLines ?? 0)
                })
                .ToList(),
            complexityMetrics = new
            {
                avgMethodsPerClass = data.SolutionMetrics?.TotalClasses > 0 
                    ? (double)data.SolutionMetrics.TotalMethods / data.SolutionMetrics.TotalClasses 
                    : 0,
                avgLinesPerMethod = data.SolutionMetrics?.TotalMethods > 0 
                    ? (double)data.SolutionMetrics.TotalLines / data.SolutionMetrics.TotalMethods 
                    : 0,
                avgLinesPerFile = data.SolutionMetrics?.TotalFiles > 0 
                    ? (double)data.SolutionMetrics.TotalLines / data.SolutionMetrics.TotalFiles 
                    : 0
            }
        };
        
        return new
        {
            success = true,
            detailLevel = "metrics",
            solutionMetrics = data.SolutionMetrics,
            breakdown = metricsBreakdown,
            metadata = new ResponseMetadata
            {
                TotalResults = 1,
                ReturnedResults = 1,
                EstimatedTokens = SizeEstimator.EstimateTokens(metricsBreakdown)
            }
        };
    }

    // Data structures
    private class ProjectStructureData
    {
        public string WorkspacePath { get; set; } = "";
        public bool IncludeMetrics { get; set; }
        public bool IncludeFiles { get; set; }
        public bool IncludeNuGetPackages { get; set; }
        public List<ProjectAnalysis> Projects { get; set; } = new();
        public SolutionMetrics? SolutionMetrics { get; set; }
    }
}