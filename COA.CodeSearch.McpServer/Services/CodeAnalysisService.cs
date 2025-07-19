using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Composition.Hosting;
using System.Reflection;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing Roslyn workspaces and code analysis operations.
/// Provides caching, lifecycle management, and access to compilation and semantic models.
/// </summary>
public class CodeAnalysisService : IDisposable
{
    private readonly ILogger<CodeAnalysisService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, WorkspaceEntry> _workspaces = new();
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly int _maxWorkspaces;
    private readonly TimeSpan _workspaceTimeout;
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// Initializes a new instance of the CodeAnalysisService
    /// </summary>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="configuration">Configuration for workspace settings</param>
    public CodeAnalysisService(ILogger<CodeAnalysisService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _maxWorkspaces = configuration.GetValue("McpServer:MaxWorkspaces", 5);
        _workspaceTimeout = configuration.GetValue("McpServer:WorkspaceTimeout", TimeSpan.FromMinutes(30));
        
        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupExpiredWorkspaces, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Gets or creates a workspace for the specified solution
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The workspace, or null if loading fails</returns>
    public async Task<Workspace?> GetWorkspaceAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
        {
            _logger.LogError("Solution file not found: {SolutionPath}", solutionPath);
            return null;
        }

        // Check cache first
        if (_workspaces.TryGetValue(solutionPath, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            return entry.Workspace;
        }

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_workspaces.TryGetValue(solutionPath, out entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Workspace;
            }

            // Ensure we don't exceed max workspaces
            await EnsureCapacityAsync();

            // Load the workspace
            _logger.LogInformation("Loading workspace for solution: {SolutionPath}", solutionPath);
            
            // Get all C# and workspace assemblies for MEF composition
            var csharpWorkspaceAssemblies = new[]
            {
                // Core assemblies
                typeof(Workspace).Assembly,                                    // Microsoft.CodeAnalysis.Workspaces
                typeof(MSBuildWorkspace).Assembly,                            // Microsoft.CodeAnalysis.Workspaces.MSBuild
                typeof(CSharpCompilation).Assembly,                           // Microsoft.CodeAnalysis.CSharp
                typeof(CSharpSyntaxTree).Assembly,                           // Microsoft.CodeAnalysis.CSharp
                typeof(SyntaxNode).Assembly,                                  // Microsoft.CodeAnalysis
                
                // Try to load CSharp.Workspaces if available
                Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Microsoft.CodeAnalysis.CSharp.Workspaces.dll"))
            }.Where(a => a != null).Distinct();

            // Create host services with explicit C# support
            var hostServices = MefHostServices.Create(csharpWorkspaceAssemblies);
            
            // Create workspace with enhanced properties for NuGet support
            var properties = new Dictionary<string, string>
            {
                ["Configuration"] = "Release",
                ["Platform"] = "AnyCPU",
                ["DesignTimeBuild"] = "true",
                ["BuildingInsideVisualStudio"] = "true",
                ["SkipCompilerExecution"] = "true", 
                ["ProvideCommandLineArgs"] = "true",
                ["ContinueOnError"] = "ErrorAndContinue",
                ["UseSharedCompilation"] = "false",
                ["BuildInParallel"] = "false",
                ["RunAnalyzersDuringBuild"] = "false",
                
                // NuGet and package reference resolution
                ["RestorePackages"] = "true",
                ["EnableNuGetPackageRestore"] = "true", 
                ["RestoreProjectStyle"] = "PackageReference",
                ["MSBuildSDKsPath"] = Environment.GetEnvironmentVariable("MSBuildSDKsPath") ?? "",
                ["NuGetProps"] = "true",
                ["ImportProjectExtensionProps"] = "true",
                ["ImportNuGetBuildTargets"] = "true",
                
                // Assembly resolution
                ["ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch"] = "None",
                ["ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch"] = "true",
                ["AutoGenerateBindingRedirects"] = "true",
                ["GenerateBindingRedirectsOutputType"] = "true"
            };
            
            var workspace = MSBuildWorkspace.Create(properties, hostServices);
            
            // Configure workspace
            workspace.WorkspaceFailed += (sender, args) =>
            {
                _logger.LogWarning("Workspace diagnostic: {Kind} - {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);
                
                // Temporary debug logging to file
                var logPath = Path.Combine(Path.GetTempPath(), "mcp-workspace-debug.log");
                try
                {
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Workspace diagnostic: {args.Diagnostic.Kind} - {args.Diagnostic.Message}\n");
                }
                catch { }
            };

            try
            {
                // Ensure NuGet packages are restored first
                var solutionDir = Path.GetDirectoryName(solutionPath);
                if (solutionDir != null)
                {
                    try
                    {
                        // Check if dotnet is available and run restore
                        var restoreProcess = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "dotnet",
                                Arguments = $"restore \"{solutionPath}\"",
                                WorkingDirectory = solutionDir,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        
                        restoreProcess.Start();
                        await restoreProcess.WaitForExitAsync(cancellationToken);
                        
                        // Log restore result
                        var restoreLogPath = @"C:\temp\mcp-workspace-debug.log";
                        try
                        {
                            System.IO.File.AppendAllText(restoreLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NuGet restore exit code: {restoreProcess.ExitCode}\n");
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to restore NuGet packages, continuing anyway");
                        
                        var restoreLogPath = @"C:\temp\mcp-workspace-debug.log";
                        try
                        {
                            System.IO.File.AppendAllText(restoreLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NuGet restore failed: {ex.Message}\n");
                        }
                        catch { }
                    }
                }
                
                var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully loaded solution with {ProjectCount} projects", solution.Projects.Count());
                
                // Temporary debug logging to file
                var logPath = Path.Combine(Path.GetTempPath(), "mcp-workspace-debug.log");
                try
                {
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Loaded solution: {solutionPath}\n");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Project count: {solution.Projects.Count()}\n");
                    
                    foreach (var project in solution.Projects)
                    {
                        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Project: {project.Name} at {project.FilePath}\n");
                    }
                }
                catch { }
                
                // If no projects loaded, try loading individual projects as fallback
                if (!solution.Projects.Any())
                {
                    _logger.LogWarning("No projects loaded from solution, trying individual project loading");
                    
                    // Find all .csproj files in the solution directory
                    var fallbackSolutionDir = Path.GetDirectoryName(solutionPath);
                    if (fallbackSolutionDir != null)
                    {
                        var csprojFiles = Directory.GetFiles(fallbackSolutionDir, "*.csproj", SearchOption.AllDirectories);
                        
                        try
                        {
                            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fallback: Found {csprojFiles.Length} .csproj files\n");
                        }
                        catch { }
                        
                        // Try to load each project individually
                        foreach (var csprojPath in csprojFiles.Take(4)) // Limit to 4 projects to avoid issues
                        {
                            try
                            {
                                _logger.LogInformation("Attempting to load project: {ProjectPath}", csprojPath);
                                var project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken);
                                
                                try
                                {
                                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fallback loaded: {project.Name}\n");
                                }
                                catch { }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to load project: {ProjectPath}", csprojPath);
                                
                                try
                                {
                                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fallback failed: {csprojPath} - {ex.Message}\n");
                                }
                                catch { }
                            }
                        }
                        
                        // Get the updated solution
                        solution = workspace.CurrentSolution;
                    }
                }
                
                // Log project details
                foreach (var project in solution.Projects)
                {
                    _logger.LogDebug("Loaded project: {ProjectName} at {ProjectPath}", project.Name, project.FilePath);
                }
                
                // Cache the workspace
                _workspaces[solutionPath] = new WorkspaceEntry
                {
                    Workspace = workspace,
                    SolutionPath = solutionPath,
                    LastAccessed = DateTime.UtcNow
                };

                return workspace;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
                workspace.Dispose();
                return null;
            }
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<Project?> GetProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
        {
            _logger.LogError("Project file not found: {ProjectPath}", projectPath);
            return null;
        }

        // Try to find the project in existing workspaces
        foreach (var entry in _workspaces.Values)
        {
            var solution = entry.Workspace.CurrentSolution;
            var project = solution.Projects.FirstOrDefault(p => 
                StringComparer.OrdinalIgnoreCase.Equals(p.FilePath, projectPath));
            
            if (project != null)
            {
                entry.LastAccessed = DateTime.UtcNow;
                return project;
            }
        }

        // Load as standalone project
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCapacityAsync();

            _logger.LogInformation("Loading standalone project: {ProjectPath}", projectPath);
            
            var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (sender, args) =>
            {
                _logger.LogWarning("Workspace diagnostic: {Kind} - {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);
            };

            try
            {
                var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully loaded project: {ProjectName}", project.Name);
                
                // Cache the workspace
                _workspaces[projectPath] = new WorkspaceEntry
                {
                    Workspace = workspace,
                    SolutionPath = projectPath,
                    LastAccessed = DateTime.UtcNow
                };

                return project;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load project: {ProjectPath}", projectPath);
                workspace.Dispose();
                return null;
            }
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {FilePath}", filePath);
            return null;
        }

        // Search in all cached workspaces
        foreach (var entry in _workspaces.Values)
        {
            var solution = entry.Workspace.CurrentSolution;
            var document = solution.GetDocumentIdsWithFilePath(filePath)
                .Select(id => solution.GetDocument(id))
                .FirstOrDefault(d => d != null);
            
            if (document != null)
            {
                entry.LastAccessed = DateTime.UtcNow;
                return document;
            }
        }

        // Try to find the containing project
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(directory))
        {
            var projectFiles = Directory.GetFiles(directory, "*.csproj");
            if (projectFiles.Length > 0)
            {
                var project = await GetProjectAsync(projectFiles[0], cancellationToken);
                if (project != null)
                {
                    return project.Documents.FirstOrDefault(d => 
                        StringComparer.OrdinalIgnoreCase.Equals(d.FilePath, filePath));
                }
            }
            
            directory = Path.GetDirectoryName(directory);
        }

        _logger.LogWarning("Could not find document in any workspace: {FilePath}", filePath);
        return null;
    }

    private Task EnsureCapacityAsync()
    {
        while (_workspaces.Count >= _maxWorkspaces)
        {
            // Find and remove the least recently used workspace
            var lru = _workspaces.Values
                .OrderBy(e => e.LastAccessed)
                .FirstOrDefault();
            
            if (lru != null && _workspaces.TryRemove(lru.SolutionPath, out var removed))
            {
                _logger.LogInformation("Evicting workspace: {SolutionPath}", lru.SolutionPath);
                removed.Workspace.Dispose();
            }
            else
            {
                break;
            }
        }
        
        return Task.CompletedTask;
    }

    private void CleanupExpiredWorkspaces(object? state)
    {
        var now = DateTime.UtcNow;
        var expired = _workspaces
            .Where(kvp => now - kvp.Value.LastAccessed > _workspaceTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            if (_workspaces.TryRemove(key, out var entry))
            {
                _logger.LogInformation("Removing expired workspace: {SolutionPath}", key);
                entry.Workspace.Dispose();
            }
        }
    }

    public Task UpdateSolutionAsync(Solution newSolution, CancellationToken cancellationToken = default)
    {
        // Find which workspace this solution belongs to
        Workspace? targetWorkspace = null;
        
        foreach (var entry in _workspaces.Values)
        {
            if (entry.Workspace.CurrentSolution.Id == newSolution.Id)
            {
                targetWorkspace = entry.Workspace;
                break;
            }
        }

        if (targetWorkspace == null)
        {
            throw new InvalidOperationException("Could not find workspace for solution");
        }

        // Apply the solution changes
        if (!targetWorkspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("Failed to apply changes to workspace");
        }

        _logger.LogInformation("Applied solution changes to workspace");
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        
        foreach (var entry in _workspaces.Values)
        {
            entry.Workspace.Dispose();
        }
        
        _workspaces.Clear();
        _workspaceLock.Dispose();
    }

    private class WorkspaceEntry
    {
        public required Workspace Workspace { get; init; }
        public required string SolutionPath { get; init; }
        public DateTime LastAccessed { get; set; }
    }
}