using Microsoft.Build.Locator;
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
/// <remarks>
/// This service is thread-safe and manages a cache of Roslyn workspaces with automatic eviction.
/// Workspaces are cached for performance and reused across multiple analysis operations.
/// The service handles MSBuild workspace loading, NuGet package restoration, and MEF composition.
/// 
/// Key features:
/// - Automatic workspace caching with LRU eviction
/// - Configurable maximum workspaces and timeout values
/// - Automatic NuGet package restoration before loading
/// - Fallback loading strategy for problematic solutions
/// - Thread-safe operations using semaphore locking
/// </remarks>
public class CodeAnalysisService : IDisposable
{
    private readonly ILogger<CodeAnalysisService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, WorkspaceEntry> _workspaces = new();
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly int _maxWorkspaces;
    private readonly TimeSpan _workspaceTimeout;
    private readonly Timer _cleanupTimer;
    
    // Thread safety: All public methods that access _workspaces are protected by _workspaceLock
    // to ensure thread-safe operations. The ConcurrentDictionary provides additional safety
    // for concurrent reads, while writes are serialized through the semaphore.

    /// <summary>
    /// Initializes a new instance of the CodeAnalysisService
    /// </summary>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="configuration">Configuration for workspace settings (McpServer:MaxWorkspaces, McpServer:WorkspaceTimeout)</param>
    /// <remarks>
    /// Assumes MSBuildLocator has already been registered by the application startup.
    /// Starts a background timer for cleaning up expired workspaces every 5 minutes.
    /// </remarks>
    public CodeAnalysisService(ILogger<CodeAnalysisService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _maxWorkspaces = configuration.GetValue("McpServer:MaxWorkspaces", 5);
        _workspaceTimeout = configuration.GetValue("McpServer:WorkspaceTimeout", TimeSpan.FromMinutes(30));
        
        // MSBuild is already registered in Program.cs before any services are created
        _logger.LogInformation("MSBuildLocator.IsRegistered: {IsRegistered}", MSBuildLocator.IsRegistered);
        if (MSBuildLocator.IsRegistered && MSBuildLocator.QueryVisualStudioInstances().Any())
        {
            var registered = MSBuildLocator.QueryVisualStudioInstances().First();
            _logger.LogInformation("Using MSBuild: {Name} {Version} at {Path}", 
                registered.Name, registered.Version, registered.MSBuildPath);
        }
        
        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupExpiredWorkspaces, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Gets all required MEF assemblies for Roslyn workspace composition
    /// </summary>
    /// <remarks>
    /// MEF (Managed Extensibility Framework) composition is required for Roslyn to discover
    /// and load language services, particularly the C# language support. Without proper MEF
    /// composition, workspace operations will fail with "language 'C#' is not supported" errors.
    /// This method scans for and loads all Microsoft.CodeAnalysis and Microsoft.Build assemblies
    /// to ensure complete MEF composition.
    /// </remarks>
    /// <returns>Collection of assemblies containing MEF exports for Roslyn</returns>
    private IEnumerable<Assembly> GetMefAssemblies()
    {
        var assemblies = new List<Assembly>();
        
        _logger.LogInformation("Loading MEF assemblies for Roslyn workspace composition...");
        
        // Core assemblies
        assemblies.Add(typeof(Workspace).Assembly);                   // Microsoft.CodeAnalysis.Workspaces
        assemblies.Add(typeof(MSBuildWorkspace).Assembly);           // Microsoft.CodeAnalysis.Workspaces.MSBuild
        assemblies.Add(typeof(CSharpCompilation).Assembly);          // Microsoft.CodeAnalysis.CSharp
        assemblies.Add(typeof(SyntaxNode).Assembly);                 // Microsoft.CodeAnalysis
        
        _logger.LogInformation("Loaded core assemblies: {Count}", assemblies.Count);
        
        // Critical: Load all assemblies from the current directory that might contain MEF exports
        var baseDir = AppContext.BaseDirectory;
        _logger.LogInformation("Scanning for MEF assemblies in: {BaseDir}", baseDir);
        
        var assemblyPatterns = new[]
        {
            "Microsoft.CodeAnalysis.*.dll",
            "Microsoft.Build.*.dll"
        };
        
        foreach (var pattern in assemblyPatterns)
        {
            var files = Directory.GetFiles(baseDir, pattern);
            _logger.LogInformation("Found {Count} files matching pattern {Pattern}", files.Length, pattern);
            
            foreach (var file in files)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    if (!assemblies.Contains(assembly))
                    {
                        assemblies.Add(assembly);
                        _logger.LogInformation("Loaded MEF assembly: {AssemblyName} from {File}", assembly.GetName().Name, file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load assembly: {File}", file);
                }
            }
        }
        
        // Ensure we have the critical CSharp.Workspaces assembly
        var csharpWorkspacesName = "Microsoft.CodeAnalysis.CSharp.Workspaces";
        var hasCSharpWorkspaces = assemblies.Any(a => a.GetName().Name == csharpWorkspacesName);
        
        if (!hasCSharpWorkspaces)
        {
            _logger.LogError("CRITICAL: {AssemblyName} not found - C# language support will fail!", csharpWorkspacesName);
        }
        else
        {
            _logger.LogInformation("SUCCESS: Found critical assembly {AssemblyName}", csharpWorkspacesName);
        }
        
        _logger.LogInformation("Total MEF assemblies loaded: {Count}", assemblies.Distinct().Count());
        
        return assemblies.Distinct();
    }

    /// <summary>
    /// Gets or creates a workspace for the specified solution
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The loaded workspace, or null if loading fails</returns>
    /// <remarks>
    /// This method implements caching to avoid reloading the same solution multiple times.
    /// If the workspace is already cached, it updates the last accessed time and returns it.
    /// For new workspaces, it performs the following steps:
    /// 1. Ensures workspace capacity by evicting LRU entries if needed
    /// 2. Loads MEF assemblies for proper language support
    /// 3. Configures MSBuild properties for framework and package resolution
    /// 4. Attempts NuGet package restoration
    /// 5. Opens the solution with fallback to individual project loading if needed
    /// 6. Validates the workspace can resolve basic types
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
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
            
            // Get all required MEF assemblies
            var mefAssemblies = GetMefAssemblies();
            
            // Create host services with all MEF assemblies
            var hostServices = MefHostServices.Create(mefAssemblies);
            
            // Create workspace with enhanced properties for better framework and package resolution
            var properties = new Dictionary<string, string>
            {
                // Build configuration
                ["Configuration"] = "Debug",
                ["Platform"] = "AnyCPU",
                ["DesignTimeBuild"] = "true",
                ["BuildingInsideVisualStudio"] = "true",
                ["SkipCompilerExecution"] = "true", 
                ["ProvideCommandLineArgs"] = "true",
                ["ContinueOnError"] = "ErrorAndContinue",
                ["UseSharedCompilation"] = "false",
                ["BuildInParallel"] = "false",
                ["RunAnalyzersDuringBuild"] = "false",
                
                // Framework resolution - CRITICAL for resolving System.Object
                ["TargetFrameworkRootPath"] = GetDotNetInstallDirectory(),
                ["FrameworkPathOverride"] = GetReferenceAssemblyPath(),
                ["MSBuildExtensionsPath"] = GetMSBuildExtensionsPath(),
                ["MSBuildSDKsPath"] = GetMSBuildSDKsPath(),
                
                // NuGet and package reference resolution
                ["RestorePackages"] = "true",
                ["EnableNuGetPackageRestore"] = "true", 
                ["RestoreProjectStyle"] = "PackageReference",
                ["NuGetPackageRoot"] = GetNuGetPackageRoot(),
                ["NuGetProps"] = "true",
                ["ImportProjectExtensionProps"] = "true",
                ["ImportNuGetBuildTargets"] = "true",
                
                // Assembly resolution
                ["ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch"] = "None",
                ["ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch"] = "true",
                ["AutoGenerateBindingRedirects"] = "true",
                ["GenerateBindingRedirectsOutputType"] = "true",
                
                // Additional properties for better resolution
                ["DisableRarCache"] = "true",
                ["LoadAllFilesAsReadOnly"] = "true"
            };
            
            var workspace = MSBuildWorkspace.Create(properties, hostServices);
            
            // Configure workspace
            workspace.WorkspaceFailed += (sender, args) =>
            {
                var severity = args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? LogLevel.Error : LogLevel.Warning;
                _logger.Log(severity, "Workspace diagnostic: {Kind} - {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);
                
                // Log critical failures with more detail
                if (args.Diagnostic.Message.Contains("language 'C#' is not supported", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("CRITICAL: C# language support not available! This usually means MEF composition failed.");
                    _logger.LogError("Loaded assemblies: {Assemblies}", string.Join(", ", mefAssemblies.Select(a => a.GetName().Name)));
                }
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
                        using (var restoreProcess = new System.Diagnostics.Process
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
                        })
                        {
                            restoreProcess.Start();
                            
                            // Read output streams to prevent deadlock
                            var outputTask = restoreProcess.StandardOutput.ReadToEndAsync();
                            var errorTask = restoreProcess.StandardError.ReadToEndAsync();
                            
                            // Wait with timeout (5 minutes max for restore)
                            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                cts.CancelAfter(TimeSpan.FromMinutes(5));
                                try
                                {
                                    await restoreProcess.WaitForExitAsync(cts.Token);
                                }
                                catch (OperationCanceledException)
                                {
                                    _logger.LogWarning("NuGet restore timed out after 5 minutes");
                                    try { restoreProcess.Kill(); } catch { }
                                    throw;
                                }
                            }
                            
                            var output = await outputTask;
                            var error = await errorTask;
                            
                            if (restoreProcess.ExitCode != 0)
                            {
                                _logger.LogWarning("NuGet restore failed with exit code {ExitCode}. Error: {Error}", 
                                    restoreProcess.ExitCode, error);
                            }
                            else
                            {
                                _logger.LogDebug("NuGet restore completed successfully");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to restore NuGet packages, continuing anyway");
                    }
                }
                
                var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully loaded solution with {ProjectCount} projects", solution.Projects.Count());
                
                // Validate the workspace
                await ValidateWorkspaceAsync(workspace, solution);
                
                // Log solution details through proper logging
                _logger.LogDebug("Loaded solution: {SolutionPath} with {ProjectCount} projects", 
                    solutionPath, solution.Projects.Count());
                
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    foreach (var project in solution.Projects)
                    {
                        _logger.LogDebug("  Project: {ProjectName} at {ProjectPath}", 
                            project.Name, project.FilePath);
                    }
                }
                
                // If no projects loaded, try loading individual projects as fallback
                if (!solution.Projects.Any())
                {
                    _logger.LogWarning("No projects loaded from solution, trying individual project loading");
                    
                    // Find all .csproj files in the solution directory
                    var fallbackSolutionDir = Path.GetDirectoryName(solutionPath);
                    if (fallbackSolutionDir != null)
                    {
                        var csprojFiles = Directory.GetFiles(fallbackSolutionDir, "*.csproj", SearchOption.AllDirectories);
                        
                        _logger.LogDebug("Fallback: Found {ProjectCount} .csproj files", csprojFiles.Length);
                        
                        // Try to load each project individually
                        foreach (var csprojPath in csprojFiles.Take(4)) // Limit to 4 projects to avoid issues
                        {
                            try
                            {
                                _logger.LogInformation("Attempting to load project: {ProjectPath}", csprojPath);
                                var project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken);
                                
                                _logger.LogDebug("Fallback loaded project: {ProjectName}", project.Name);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to load project: {ProjectPath}", csprojPath);
                                
                                _logger.LogDebug("Fallback failed to load {ProjectPath}: {Error}", 
                                    csprojPath, ex.Message);
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
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Solution file not found: {SolutionPath}", solutionPath);
                workspace.Dispose();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid solution format or MSBuild configuration issue: {SolutionPath}", solutionPath);
                workspace.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error loading solution: {SolutionPath}", solutionPath);
                workspace.Dispose();
                return null;
            }
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Gets a project from cache or loads it as a standalone project
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The loaded project, or null if loading fails</returns>
    /// <remarks>
    /// First searches for the project in all cached workspaces. If not found,
    /// loads it as a standalone project with the same MEF and MSBuild configuration
    /// as solution loading. Standalone projects are also cached for reuse.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
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
            
            // Get all required MEF assemblies (same as solution loading)
            var mefAssemblies = GetMefAssemblies();
            
            // Create host services with all MEF assemblies
            var hostServices = MefHostServices.Create(mefAssemblies);
            
            // Create workspace with same enhanced properties
            var properties = new Dictionary<string, string>
            {
                // Build configuration
                ["Configuration"] = "Debug",
                ["Platform"] = "AnyCPU",
                ["DesignTimeBuild"] = "true",
                ["BuildingInsideVisualStudio"] = "true",
                ["SkipCompilerExecution"] = "true", 
                ["ProvideCommandLineArgs"] = "true",
                ["ContinueOnError"] = "ErrorAndContinue",
                ["UseSharedCompilation"] = "false",
                ["BuildInParallel"] = "false",
                ["RunAnalyzersDuringBuild"] = "false",
                
                // Framework resolution - CRITICAL for resolving System.Object
                ["TargetFrameworkRootPath"] = GetDotNetInstallDirectory(),
                ["FrameworkPathOverride"] = GetReferenceAssemblyPath(),
                ["MSBuildExtensionsPath"] = GetMSBuildExtensionsPath(),
                ["MSBuildSDKsPath"] = GetMSBuildSDKsPath(),
                
                // NuGet and package reference resolution
                ["RestorePackages"] = "true",
                ["EnableNuGetPackageRestore"] = "true", 
                ["RestoreProjectStyle"] = "PackageReference",
                ["NuGetPackageRoot"] = GetNuGetPackageRoot(),
                
                // Assembly resolution
                ["ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch"] = "None",
                ["ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch"] = "true",
                ["AutoGenerateBindingRedirects"] = "true",
                ["GenerateBindingRedirectsOutputType"] = "true",
                
                // Additional properties for better resolution
                ["DisableRarCache"] = "true",
                ["LoadAllFilesAsReadOnly"] = "true"
            };
            
            var workspace = MSBuildWorkspace.Create(properties, hostServices);
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
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Project file not found: {ProjectPath}", projectPath);
                workspace.Dispose();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid project format or MSBuild configuration issue: {ProjectPath}", projectPath);
                workspace.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error loading project: {ProjectPath}", projectPath);
                workspace.Dispose();
                return null;
            }
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Gets a document from any cached workspace or loads its containing project
    /// </summary>
    /// <param name="filePath">Path to the source file</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The document, or null if not found in any workspace</returns>
    /// <remarks>
    /// Searches all cached workspaces for the document. If not found, attempts to
    /// locate the containing project by walking up the directory tree looking for
    /// .csproj files, then loads that project to access the document.
    /// </remarks>
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

    /// <summary>
    /// Ensures workspace cache capacity by evicting least recently used entries
    /// </summary>
    /// <remarks>
    /// Called before adding new workspaces to ensure we don't exceed the configured
    /// maximum. Uses LRU (Least Recently Used) eviction strategy based on LastAccessed
    /// timestamps. Disposed workspaces are properly cleaned up to free resources.
    /// </remarks>
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

    /// <summary>
    /// Timer callback that removes workspaces that haven't been accessed recently
    /// </summary>
    /// <param name="state">Timer state (unused)</param>
    /// <remarks>
    /// Runs every 5 minutes to check for workspaces that exceed the configured timeout.
    /// This prevents memory leaks from long-running services that load many solutions.
    /// </remarks>
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

    /// <summary>
    /// Updates a solution in its workspace with changes
    /// </summary>
    /// <param name="newSolution">The modified solution to apply</param>
    /// <param name="cancellationToken">Cancellation token (currently unused)</param>
    /// <returns>A completed task</returns>
    /// <exception cref="InvalidOperationException">Thrown when workspace not found or changes cannot be applied</exception>
    /// <remarks>
    /// Finds the workspace containing the solution and applies the changes using
    /// TryApplyChanges. This is used for operations like renaming symbols that
    /// modify the solution structure.
    /// </remarks>
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

    /// <summary>
    /// Disposes all cached workspaces and releases resources
    /// </summary>
    /// <remarks>
    /// Properly disposes the cleanup timer, all cached workspaces, and the semaphore.
    /// This should be called when the service is no longer needed to ensure all
    /// Roslyn resources are properly released.
    /// </remarks>
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

    /// <summary>
    /// Represents a cached workspace entry with access tracking
    /// </summary>
    private class WorkspaceEntry
    {
        /// <summary>
        /// The Roslyn workspace instance
        /// </summary>
        public required Workspace Workspace { get; init; }
        
        /// <summary>
        /// Path to the solution or project file this workspace represents
        /// </summary>
        public required string SolutionPath { get; init; }
        
        /// <summary>
        /// Last time this workspace was accessed, used for LRU eviction
        /// </summary>
        public DateTime LastAccessed { get; set; }
    }

    /// <summary>
    /// Gets the .NET installation directory for framework resolution
    /// </summary>
    /// <remarks>
    /// Attempts to locate the .NET installation in the following order:
    /// 1. DOTNET_ROOT environment variable
    /// 2. Program Files\dotnet directory
    /// 3. Fallback to C:\Program Files\dotnet
    /// This path is critical for MSBuild to resolve framework assemblies.
    /// </remarks>
    /// <returns>Path to the .NET installation directory</returns>
    private string GetDotNetInstallDirectory()
    {
        // Try environment variable first
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }
        
        // Try program files
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var dotnetDir = Path.Combine(programFiles, "dotnet");
        if (Directory.Exists(dotnetDir))
        {
            return dotnetDir;
        }
        
        // Fallback
        return @"C:\Program Files\dotnet";
    }

    /// <summary>
    /// Gets the reference assembly path for the current framework
    /// </summary>
    /// <remarks>
    /// Locates reference assemblies which contain type metadata for compilation:
    /// 1. Traditional .NET Framework reference assemblies
    /// 2. .NET Core/5+ targeting packs
    /// These assemblies are used by the compiler to resolve types but are not
    /// loaded at runtime. Critical for resolving System types.
    /// </remarks>
    /// <returns>Path to reference assemblies or targeting packs</returns>
    private string GetReferenceAssemblyPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(programFiles))
        {
            programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }
        
        var refAssembliesPath = Path.Combine(programFiles, "Reference Assemblies", "Microsoft", "Framework");
        if (Directory.Exists(refAssembliesPath))
        {
            return refAssembliesPath;
        }
        
        // Try .NET Core/5+ packs
        var dotnetRoot = GetDotNetInstallDirectory();
        var packsPath = Path.Combine(dotnetRoot, "packs");
        if (Directory.Exists(packsPath))
        {
            return packsPath;
        }
        
        return refAssembliesPath;
    }

    /// <summary>
    /// Gets the MSBuild extensions path
    /// </summary>
    /// <remarks>
    /// Searches for MSBuild in the following locations:
    /// 1. Visual Studio 2022 installation (all editions)
    /// 2. .NET SDK installation
    /// 3. Fallback to Program Files\MSBuild
    /// This path contains MSBuild targets and tasks required for project evaluation.
    /// </remarks>
    /// <returns>Path to MSBuild extensions</returns>
    private string GetMSBuildExtensionsPath()
    {
        // Try VS installation first
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var vsPath = Path.Combine(programFiles, "Microsoft Visual Studio", "2022");
        
        if (Directory.Exists(vsPath))
        {
            var editions = new[] { "Enterprise", "Professional", "Community", "Preview" };
            foreach (var edition in editions)
            {
                var msbuildPath = Path.Combine(vsPath, edition, "MSBuild");
                if (Directory.Exists(msbuildPath))
                {
                    return msbuildPath;
                }
            }
        }
        
        // Fallback to .NET SDK
        var dotnetRoot = GetDotNetInstallDirectory();
        var sdkPath = Path.Combine(dotnetRoot, "sdk");
        if (Directory.Exists(sdkPath))
        {
            var latestSdk = Directory.GetDirectories(sdkPath)
                .OrderByDescending(d => d)
                .FirstOrDefault();
            if (latestSdk != null)
            {
                return latestSdk;
            }
        }
        
        return Path.Combine(programFiles, "MSBuild");
    }

    /// <summary>
    /// Gets the MSBuild SDKs path
    /// </summary>
    /// <remarks>
    /// Locates MSBuild SDKs which define project templates and build logic:
    /// 1. MSBuildSDKsPath environment variable
    /// 2. Latest .NET SDK's Sdks directory
    /// SDKs are essential for modern .NET project files that use SDK-style format.
    /// </remarks>
    /// <returns>Path to MSBuild SDKs directory</returns>
    private string GetMSBuildSDKsPath()
    {
        // Check environment variable first
        var sdksPath = Environment.GetEnvironmentVariable("MSBuildSDKsPath");
        if (!string.IsNullOrEmpty(sdksPath) && Directory.Exists(sdksPath))
        {
            return sdksPath;
        }
        
        // Get from .NET SDK
        var dotnetRoot = GetDotNetInstallDirectory();
        var sdkPath = Path.Combine(dotnetRoot, "sdk");
        if (Directory.Exists(sdkPath))
        {
            var latestSdk = Directory.GetDirectories(sdkPath)
                .OrderByDescending(d => d)
                .FirstOrDefault();
            if (latestSdk != null)
            {
                var sdksDir = Path.Combine(latestSdk, "Sdks");
                if (Directory.Exists(sdksDir))
                {
                    return sdksDir;
                }
            }
        }
        
        return "";
    }

    /// <summary>
    /// Gets the NuGet package root directory
    /// </summary>
    /// <remarks>
    /// Determines where NuGet packages are cached:
    /// 1. NUGET_PACKAGES environment variable
    /// 2. Default location: %USERPROFILE%\.nuget\packages
    /// This path is used by MSBuild to resolve package references without
    /// requiring a full restore operation.
    /// </remarks>
    /// <returns>Path to NuGet package cache</returns>
    private string GetNuGetPackageRoot()
    {
        // Check environment variable
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPackages) && Directory.Exists(nugetPackages))
        {
            return nugetPackages;
        }
        
        // Default location
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultPath = Path.Combine(userProfile, ".nuget", "packages");
        if (Directory.Exists(defaultPath))
        {
            return defaultPath;
        }
        
        return "";
    }

    /// <summary>
    /// Validates that the workspace can resolve basic types
    /// </summary>
    /// <summary>
    /// Validates that the workspace can resolve basic types and framework references
    /// </summary>
    /// <param name="workspace">The workspace to validate</param>
    /// <param name="solution">The loaded solution</param>
    /// <remarks>
    /// Performs critical validation to ensure the workspace is properly configured:
    /// - Checks if System.Object can be resolved (indicates framework references work)
    /// - Validates other critical types like String, Dictionary, and Task
    /// - Logs detailed diagnostics if resolution fails to aid troubleshooting
    /// - Reports compilation error counts
    /// 
    /// This validation helps detect configuration issues early, particularly with
    /// framework resolution which is a common source of Roslyn workspace problems.
    /// </remarks>
    private async Task ValidateWorkspaceAsync(Workspace workspace, Solution solution)
    {
        try
        {
            _logger.LogInformation("Validating workspace...");
            
            // Pick the first C# project
            var project = solution.Projects.FirstOrDefault(p => p.Language == LanguageNames.CSharp);
            if (project == null)
            {
                _logger.LogWarning("No C# projects found to validate");
                return;
            }
            
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                _logger.LogError("Failed to get compilation for validation");
                return;
            }
            
            // Check if we can resolve System.Object
            var objectType = compilation.GetTypeByMetadataName("System.Object");
            if (objectType == null)
            {
                _logger.LogError("CRITICAL: Cannot resolve System.Object - framework references are broken!");
                _logger.LogError("Project: {ProjectName}, Framework: {TargetFramework}", project.Name, project.CompilationOptions?.Platform);
                
                // Log diagnostic information
                var diagnostics = compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .GroupBy(d => d.Id)
                    .OrderByDescending(g => g.Count())
                    .Take(5);
                    
                _logger.LogError("Top compilation errors:");
                foreach (var diagGroup in diagnostics)
                {
                    _logger.LogError("  {Id}: {Count} occurrences - {Message}", 
                        diagGroup.Key, 
                        diagGroup.Count(), 
                        diagGroup.First().GetMessage());
                }
                
                // Log assembly references
                _logger.LogError("Assembly references ({Count} total):", compilation.References.Count());
                foreach (var reference in compilation.References.OfType<MetadataReference>().Take(10))
                {
                    _logger.LogError("  Reference: {Display}", reference.Display);
                }
                
                // Log framework paths
                _logger.LogError("Framework paths:");
                _logger.LogError("  TargetFrameworkRootPath: {Path}", GetDotNetInstallDirectory());
                _logger.LogError("  FrameworkPathOverride: {Path}", GetReferenceAssemblyPath());
                _logger.LogError("  MSBuildExtensionsPath: {Path}", GetMSBuildExtensionsPath());
                _logger.LogError("  MSBuildSDKsPath: {Path}", GetMSBuildSDKsPath());
                _logger.LogError("  NuGetPackageRoot: {Path}", GetNuGetPackageRoot());
            }
            else
            {
                _logger.LogInformation("Workspace validation successful - System.Object resolved");
                
                // Check a few more critical types
                var criticalTypes = new[] 
                { 
                    "System.String",
                    "System.Collections.Generic.Dictionary`2",
                    "System.Threading.Tasks.Task"
                };
                
                foreach (var typeName in criticalTypes)
                {
                    var type = compilation.GetTypeByMetadataName(typeName);
                    if (type == null)
                    {
                        _logger.LogWarning("Could not resolve type: {TypeName}", typeName);
                    }
                }
                
                // Log total error count
                var errorCount = compilation.GetDiagnostics()
                    .Count(d => d.Severity == DiagnosticSeverity.Error);
                    
                if (errorCount > 0)
                {
                    _logger.LogWarning("Compilation has {ErrorCount} errors", errorCount);
                }
                else
                {
                    _logger.LogInformation("Compilation is error-free");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during workspace validation");
        }
    }
}