using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class ProjectStructureAnalysisTool
{
    private readonly ILogger<ProjectStructureAnalysisTool> _logger;
    private readonly CodeAnalysisService _workspaceService;
    private readonly IConfiguration _configuration;

    public ProjectStructureAnalysisTool(ILogger<ProjectStructureAnalysisTool> logger, CodeAnalysisService workspaceService, IConfiguration configuration)
    {
        _logger = logger;
        _workspaceService = workspaceService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        bool includeMetrics = true,
        bool includeFiles = false,
        bool includeNuGetPackages = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ProjectStructureAnalysis request for: {WorkspacePath}", workspacePath);

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
            var analysis = new ProjectStructureAnalysis();

            // Analyze all projects
            foreach (var project in solution.Projects)
            {
                var projectAnalysis = await AnalyzeProject(project, includeFiles, includeNuGetPackages, includeMetrics, cancellationToken);
                analysis.Projects.Add(projectAnalysis);
            }

            // Calculate solution-level metrics
            if (includeMetrics)
            {
                analysis.SolutionMetrics = CalculateSolutionMetrics(analysis.Projects);
            }

            return new
            {
                success = true,
                workspacePath = workspacePath,
                includeMetrics = includeMetrics,
                includeFiles = includeFiles,
                includeNuGetPackages = includeNuGetPackages,
                analysis = analysis.ToResult()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in project structure analysis");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
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
                _logger.LogWarning(ex, "Error calculating metrics for document {DocumentName}", document.Name);
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
}

// Extension method to get declared symbols from syntax root
public static class SemanticModelExtensions
{
    public static IEnumerable<ISymbol> GetDeclaredSymbols(this SemanticModel semanticModel, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        
        foreach (var node in syntaxRoot.DescendantNodes())
        {
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol != null)
            {
                symbols.Add(symbol);
            }
        }
        
        return symbols;
    }
}

public class ProjectStructureAnalysis
{
    public List<ProjectAnalysis> Projects { get; set; } = new();
    public SolutionMetrics? SolutionMetrics { get; set; }

    public object ToResult()
    {
        return new
        {
            projects = Projects.Select(p => new
            {
                name = p.Name,
                id = p.Id,
                filePath = p.FilePath,
                language = p.Language,
                assemblyName = p.AssemblyName,
                defaultNamespace = p.DefaultNamespace,
                outputType = p.OutputType,
                targetFramework = p.TargetFramework,
                projectReferences = p.ProjectReferences?.Select(pr => new { projectId = pr.ProjectId, projectName = pr.ProjectName }),
                assemblyReferences = p.AssemblyReferences?.Select(ar => new { name = ar.Name, filePath = ar.FilePath, isFrameworkReference = ar.IsFrameworkReference }),
                nugetPackages = p.NuGetPackages?.Select(np => new { name = np.Name, version = np.Version, path = np.Path }),
                sourceFiles = p.SourceFiles?.Select(sf => new { name = sf.Name, filePath = sf.FilePath, folders = sf.Folders }),
                metrics = p.Metrics != null ? new
                {
                    totalFiles = p.Metrics.TotalFiles,
                    totalLines = p.Metrics.TotalLines,
                    totalClasses = p.Metrics.TotalClasses,
                    totalMethods = p.Metrics.TotalMethods,
                    totalProperties = p.Metrics.TotalProperties,
                    projectReferences = p.Metrics.ProjectReferences,
                    assemblyReferences = p.Metrics.AssemblyReferences
                } : null
            }).ToArray(),
            solutionMetrics = SolutionMetrics != null ? new
            {
                totalProjects = SolutionMetrics.TotalProjects,
                totalFiles = SolutionMetrics.TotalFiles,
                totalLines = SolutionMetrics.TotalLines,
                totalClasses = SolutionMetrics.TotalClasses,
                totalMethods = SolutionMetrics.TotalMethods,
                totalProperties = SolutionMetrics.TotalProperties,
                languages = SolutionMetrics.Languages,
                targetFrameworks = SolutionMetrics.TargetFrameworks
            } : null
        };
    }
}

public class ProjectAnalysis
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Language { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string DefaultNamespace { get; set; } = "";
    public string OutputType { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public List<ProjectReference>? ProjectReferences { get; set; }
    public List<AssemblyReference>? AssemblyReferences { get; set; }
    public List<NuGetPackage>? NuGetPackages { get; set; }
    public List<SourceFile>? SourceFiles { get; set; }
    public ProjectMetrics? Metrics { get; set; }
}

public class ProjectReference
{
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
}

public class AssemblyReference
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsFrameworkReference { get; set; }
}

public class NuGetPackage
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
}

public class SourceFile
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public List<string> Folders { get; set; } = new();
}

public class ProjectMetrics
{
    public int TotalFiles { get; set; }
    public int TotalLines { get; set; }
    public int TotalClasses { get; set; }
    public int TotalMethods { get; set; }
    public int TotalProperties { get; set; }
    public int ProjectReferences { get; set; }
    public int AssemblyReferences { get; set; }
}

public class SolutionMetrics
{
    public int TotalProjects { get; set; }
    public int TotalFiles { get; set; }
    public int TotalLines { get; set; }
    public int TotalClasses { get; set; }
    public int TotalMethods { get; set; }
    public int TotalProperties { get; set; }
    public List<string> Languages { get; set; } = new();
    public List<string> TargetFrameworks { get; set; } = new();
}