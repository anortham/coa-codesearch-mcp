using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.Roslyn.McpServer.Services;

public class RoslynWorkspaceService : IDisposable
{
    private readonly ILogger<RoslynWorkspaceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, WorkspaceEntry> _workspaces = new();
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly int _maxWorkspaces;
    private readonly TimeSpan _workspaceTimeout;
    private readonly Timer _cleanupTimer;

    public RoslynWorkspaceService(ILogger<RoslynWorkspaceService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _maxWorkspaces = configuration.GetValue("McpServer:MaxWorkspaces", 5);
        _workspaceTimeout = configuration.GetValue("McpServer:WorkspaceTimeout", TimeSpan.FromMinutes(30));
        
        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupExpiredWorkspaces, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

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
            
            var workspace = MSBuildWorkspace.Create();
            
            // Configure workspace
            workspace.WorkspaceFailed += (sender, args) =>
            {
                _logger.LogWarning("Workspace diagnostic: {Kind} - {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);
            };

            try
            {
                var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully loaded solution with {ProjectCount} projects", solution.Projects.Count());
                
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

    public async Task UpdateSolutionAsync(Solution newSolution, CancellationToken cancellationToken = default)
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