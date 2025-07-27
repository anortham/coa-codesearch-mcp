using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class IndexWorkspaceTool : ITool
{
    public string ToolName => "index_workspace";
    public string Description => "Build search index for high-performance searches";
    public ToolCategory Category => ToolCategory.Infrastructure;
    private readonly ILogger<IndexWorkspaceTool> _logger;
    private readonly FileIndexingService _fileIndexingService;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly FileWatcherService? _fileWatcherService;
    private readonly INotificationService? _notificationService;

    public IndexWorkspaceTool(
        ILogger<IndexWorkspaceTool> logger,
        FileIndexingService fileIndexingService,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        FileWatcherService? fileWatcherService = null,
        INotificationService? notificationService = null)
    {
        _logger = logger;
        _fileIndexingService = fileIndexingService;
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _fileWatcherService = fileWatcherService;
        _notificationService = notificationService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        bool forceRebuild = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Index workspace request for: {WorkspacePath}, Force: {Force}", workspacePath, forceRebuild);

            // Validate input
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = "Workspace path cannot be empty"
                };
            }

            // CRITICAL: Protect memory indexes from being indexed as code
            var pathToCheck = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathSegments = pathToCheck.Split(Path.DirectorySeparatorChar);
            
            // Check if any segment in the path is exactly ".codesearch" or exactly one of the memory directories
            if (pathSegments.Any(segment => 
                segment.Equals(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(PathConstants.ProjectMemoryDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(PathConstants.LocalMemoryDirectoryName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Attempted to index protected path as workspace: {WorkspacePath}", workspacePath);
                return new
                {
                    success = false,
                    error = $"Cannot index {PathConstants.BaseDirectoryName} directories or memory paths. These are managed internally by the indexing system."
                };
            }

            if (!Directory.Exists(workspacePath))
            {
                return new
                {
                    success = false,
                    error = $"Workspace path does not exist: {workspacePath}"
                };
            }

            // Check if this path is already covered by a parent index
            var normalizedRequestPath = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var existingIndexes = await GetExistingIndexesAsync();
            
            foreach (var existingIndex in existingIndexes)
            {
                var normalizedExistingPath = Path.GetFullPath(existingIndex.OriginalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                
                // Check if the requested path is a subdirectory of an existing indexed path
                if (normalizedRequestPath.StartsWith(normalizedExistingPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedRequestPath.Equals(normalizedExistingPath, StringComparison.OrdinalIgnoreCase))
                {
                    // This path is already covered by a parent index
                    if (!normalizedRequestPath.Equals(normalizedExistingPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Path {RequestPath} is already covered by parent index at {ParentPath}", workspacePath, existingIndex.OriginalPath);
                        return new
                        {
                            success = true,
                            message = $"This path is already indexed as part of parent workspace: {existingIndex.OriginalPath}",
                            parentWorkspace = existingIndex.OriginalPath,
                            action = "skipped_redundant"
                        };
                    }
                }
            }

            // Check if index exists and get document count
            var indexExists = false;
            var documentCount = 0;
            
            try
            {
                var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
                var indexReader = searcher.IndexReader;
                documentCount = indexReader.NumDocs;
                indexExists = documentCount > 0;
            }
            catch
            {
                // Index doesn't exist yet
                indexExists = false;
            }

            // Determine if we should index
            var shouldIndex = !indexExists || forceRebuild;
            
            if (!shouldIndex)
            {
                _logger.LogInformation("Index already exists with {Count} documents, skipping", documentCount);
                return new
                {
                    success = true,
                    message = $"Index already exists with {documentCount} documents. Use forceRebuild=true to rebuild.",
                    documentCount = documentCount,
                    action = "skipped"
                };
            }

            // Clear existing index if force rebuild
            if (forceRebuild && indexExists)
            {
                _logger.LogInformation("Force rebuild requested, clearing existing index");
                try
                {
                    // Use the service's built-in method to clear the index safely
                    // This will ONLY clear the specific index directory, not memories or backups
                    await _luceneIndexService.ClearIndexAsync(workspacePath);
                    _logger.LogInformation("Cleared existing index for workspace {WorkspacePath}", workspacePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clear existing index, will overwrite");
                }
            }

            // Perform indexing
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Starting index build for {WorkspacePath}", workspacePath);
            
            // Generate a unique progress token for this operation
            var progressToken = $"index-workspace-{Guid.NewGuid():N}";
            
            // Send initial progress notification
            if (_notificationService != null)
            {
                await _notificationService.SendProgressAsync(
                    progressToken, 
                    0, 
                    null, 
                    $"Starting workspace indexing for {Path.GetFileName(workspacePath)}",
                    cancellationToken);
            }
            
            var indexedCount = await _fileIndexingService.IndexDirectoryAsync(
                workspacePath, 
                workspacePath, 
                cancellationToken);
            
            // Send completion progress notification
            if (_notificationService != null)
            {
                await _notificationService.SendProgressAsync(
                    progressToken, 
                    100, 
                    100, 
                    $"Indexed {indexedCount} files successfully",
                    cancellationToken);
            }
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Indexed {Count} files in {Duration:F2} seconds", indexedCount, duration.TotalSeconds);

            // Start file watching for this workspace
            if (_fileWatcherService != null)
            {
                try
                {
                    _fileWatcherService.StartWatching(workspacePath);
                    _logger.LogInformation("Started file watching for workspace: {WorkspacePath}", workspacePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start file watching for workspace: {WorkspacePath}", workspacePath);
                    // Don't fail the entire operation if file watching fails
                }
            }

            // Detect project type and provide helpful info
            var projectInfo = DetectProjectType(workspacePath);
            
            var response = new
            {
                success = true,
                message = $"Successfully indexed {indexedCount} files",
                workspacePath = workspacePath,
                filesIndexed = indexedCount,
                duration = $"{duration.TotalSeconds:F2} seconds",
                action = forceRebuild ? "rebuilt" : "created",
                fileWatching = _fileWatcherService != null ? "enabled" : "disabled",
                progressToken = progressToken,
                projectInfo = projectInfo
            };
            
            // Log response size for debugging
            var responseJson = System.Text.Json.JsonSerializer.Serialize(response);
            _logger.LogDebug("Index workspace response size: {Size} characters", responseJson.Length);
            if (responseJson.Length > 50000) // Roughly 10k tokens
            {
                _logger.LogWarning("Index workspace response is very large: {Size} characters. This may exceed token limits.", responseJson.Length);
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing workspace: {WorkspacePath}", workspacePath);
            
            // Limit error message size to prevent token overflow
            var errorMessage = ex.Message;
            if (errorMessage.Length > 500)
            {
                errorMessage = errorMessage.Substring(0, 497) + "...";
            }
            
            return new
            {
                success = false,
                error = $"Failed to index workspace: {errorMessage}",
                errorType = ex.GetType().Name
            };
        }
    }
    
    private object DetectProjectType(string workspacePath)
    {
        var projectTypes = new List<string>();
        var primaryExtensions = new HashSet<string>();
        var tips = new List<string>();
        
        try
        {
            // Check for .csproj files in root and immediate subdirectories only
            var csprojFiles = Directory.GetFiles(workspacePath, "*.csproj", SearchOption.TopDirectoryOnly);
            if (!csprojFiles.Any())
            {
                // Check one level down for common project structures
                var subdirs = Directory.GetDirectories(workspacePath)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .Take(5); // Limit subdirectories checked
                    
                foreach (var subdir in subdirs)
                {
                    csprojFiles = Directory.GetFiles(subdir, "*.csproj", SearchOption.TopDirectoryOnly);
                    if (csprojFiles.Any()) break;
                }
            }
            
            // Quick .NET detection - just check first project file
            if (csprojFiles.Any())
            {
                projectTypes.Add(".NET");
                primaryExtensions.Add(".cs");
                
                try
                {
                    var content = File.ReadAllText(csprojFiles.First());
                    if (content.Contains("Microsoft.NET.Sdk.Web"))
                    {
                        projectTypes.Add("ASP.NET");
                        primaryExtensions.Add(".cshtml");
                    }
                    if (content.Contains("Microsoft.NET.Sdk.BlazorWebAssembly"))
                    {
                        projectTypes.Add("Web Assembly");
                        tips.Add("For Web Assembly projects: use extensions .cs,.html to search code and markup");
                    }
                }
                catch { /* Ignore read errors */ }
            }
            
            // Check for various project types
            DetectJavaScriptProjects(workspacePath, projectTypes, primaryExtensions);
            DetectPythonProjects(workspacePath, projectTypes, primaryExtensions);
            DetectGoProjects(workspacePath, projectTypes, primaryExtensions);
            DetectRustProjects(workspacePath, projectTypes, primaryExtensions);
            DetectJavaProjects(workspacePath, projectTypes, primaryExtensions);
            DetectPhpProjects(workspacePath, projectTypes, primaryExtensions);
            DetectRubyProjects(workspacePath, projectTypes, primaryExtensions);
            DetectMobileProjects(workspacePath, projectTypes, primaryExtensions);
            DetectDevOpsProjects(workspacePath, projectTypes, primaryExtensions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting project type for {WorkspacePath}", workspacePath);
        }
        
        // Default tip if no specific project type detected
        if (!tips.Any())
        {
            tips.Add("Use text_search without filePattern to search all file types");
        }
        
        return new
        {
            type = projectTypes.Any() ? string.Join(", ", projectTypes) : "Unknown",
            primaryExtensions = primaryExtensions.Take(10).ToArray(), // Limit array size
            tips = tips.Take(5).ToArray() // Limit tips
        };
    }
    
    private void DetectJavaScriptProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        var packageJsonPath = Path.Combine(workspacePath, "package.json");
        if (!File.Exists(packageJsonPath)) return;
        
        try
        {
            var content = File.ReadAllText(packageJsonPath);
            var deps = content.ToLower();
            
            if (deps.Contains("@angular/"))
            {
                projectTypes.Add("Angular");
                primaryExtensions.Add(".ts");
                primaryExtensions.Add(".html");
            }
            else if (deps.Contains("\"react\""))
            {
                projectTypes.Add("React");
                primaryExtensions.Add(".tsx");
                primaryExtensions.Add(".jsx");
            }
            else if (deps.Contains("\"vue\""))
            {
                projectTypes.Add("Vue");
                primaryExtensions.Add(".vue");
            }
            else if (deps.Contains("\"next\"") || deps.Contains("next.config"))
            {
                projectTypes.Add("Next.js");
                primaryExtensions.Add(".tsx");
                primaryExtensions.Add(".jsx");
            }
            else if (deps.Contains("\"nuxt\""))
            {
                projectTypes.Add("Nuxt.js");
                primaryExtensions.Add(".vue");
            }
            else if (deps.Contains("\"svelte\""))
            {
                projectTypes.Add("Svelte");
                primaryExtensions.Add(".svelte");
            }
            else
            {
                projectTypes.Add("Node.js");
                primaryExtensions.Add(".js");
            }
        }
        catch { /* Ignore read errors */ }
    }
    
    private void DetectPythonProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        var indicators = new[]
        {
            ("requirements.txt", "Python"),
            ("pyproject.toml", "Python"),
            ("setup.py", "Python"),
            ("manage.py", "Django"),
            ("app.py", "Flask")
        };
        
        foreach (var (file, type) in indicators)
        {
            if (File.Exists(Path.Combine(workspacePath, file)))
            {
                projectTypes.Add(type);
                primaryExtensions.Add(".py");
                break;
            }
        }
    }
    
    private void DetectGoProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        if (File.Exists(Path.Combine(workspacePath, "go.mod")))
        {
            projectTypes.Add("Go");
            primaryExtensions.Add(".go");
        }
    }
    
    private void DetectRustProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        if (File.Exists(Path.Combine(workspacePath, "Cargo.toml")))
        {
            projectTypes.Add("Rust");
            primaryExtensions.Add(".rs");
        }
    }
    
    private void DetectJavaProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        if (File.Exists(Path.Combine(workspacePath, "pom.xml")))
        {
            projectTypes.Add("Maven/Java");
            primaryExtensions.Add(".java");
        }
        else if (File.Exists(Path.Combine(workspacePath, "build.gradle")))
        {
            projectTypes.Add("Gradle/Java");
            primaryExtensions.Add(".java");
        }
    }
    
    private void DetectPhpProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        if (File.Exists(Path.Combine(workspacePath, "composer.json")))
        {
            try
            {
                var content = File.ReadAllText(Path.Combine(workspacePath, "composer.json"));
                if (content.Contains("laravel/framework"))
                {
                    projectTypes.Add("Laravel");
                }
                else if (content.Contains("symfony/symfony"))
                {
                    projectTypes.Add("Symfony");
                }
                else
                {
                    projectTypes.Add("PHP");
                }
                primaryExtensions.Add(".php");
            }
            catch { /* Ignore read errors */ }
        }
    }
    
    private void DetectRubyProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        if (File.Exists(Path.Combine(workspacePath, "Gemfile")))
        {
            try
            {
                var content = File.ReadAllText(Path.Combine(workspacePath, "Gemfile"));
                if (content.Contains("rails"))
                {
                    projectTypes.Add("Ruby on Rails");
                }
                else
                {
                    projectTypes.Add("Ruby");
                }
                primaryExtensions.Add(".rb");
            }
            catch { /* Ignore read errors */ }
        }
    }
    
    private void DetectMobileProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        // Flutter
        if (File.Exists(Path.Combine(workspacePath, "pubspec.yaml")))
        {
            projectTypes.Add("Flutter");
            primaryExtensions.Add(".dart");
        }
        
        // iOS
        if (Directory.GetFiles(workspacePath, "*.xcodeproj", SearchOption.TopDirectoryOnly).Any() ||
            File.Exists(Path.Combine(workspacePath, "Info.plist")))
        {
            projectTypes.Add("iOS");
            primaryExtensions.Add(".swift");
            primaryExtensions.Add(".m");
        }
        
        // Android
        if (File.Exists(Path.Combine(workspacePath, "AndroidManifest.xml")) ||
            Directory.GetDirectories(workspacePath).Any(d => Path.GetFileName(d) == "app" && 
                File.Exists(Path.Combine(d, "build.gradle"))))
        {
            projectTypes.Add("Android");
            primaryExtensions.Add(".java");
            primaryExtensions.Add(".kt");
        }
    }
    
    private void DetectDevOpsProjects(string workspacePath, List<string> projectTypes, HashSet<string> primaryExtensions)
    {
        // Docker
        if (File.Exists(Path.Combine(workspacePath, "Dockerfile")) ||
            File.Exists(Path.Combine(workspacePath, "docker-compose.yml")))
        {
            projectTypes.Add("Docker");
        }
        
        // Kubernetes
        if (Directory.GetFiles(workspacePath, "*.yaml", SearchOption.TopDirectoryOnly)
            .Any(f => File.ReadAllText(f).Contains("apiVersion")))
        {
            projectTypes.Add("Kubernetes");
            primaryExtensions.Add(".yaml");
        }
        
        // Terraform
        if (Directory.GetFiles(workspacePath, "*.tf", SearchOption.TopDirectoryOnly).Any())
        {
            projectTypes.Add("Terraform");
            primaryExtensions.Add(".tf");
        }
    }
    
    private async Task<List<WorkspaceIndexInfo>> GetExistingIndexesAsync()
    {
        var indexes = new List<WorkspaceIndexInfo>();
        
        try
        {
            // Get the metadata file path
            var metadataPath = _pathResolutionService.GetWorkspaceMetadataPath();
            
            if (File.Exists(metadataPath))
            {
                var metadataJson = await File.ReadAllTextAsync(metadataPath);
                var metadata = System.Text.Json.JsonSerializer.Deserialize<WorkspaceMetadata>(metadataJson);
                
                if (metadata?.Indexes != null)
                {
                    foreach (var kvp in metadata.Indexes)
                    {
                        indexes.Add(kvp.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read workspace metadata");
        }
        
        return indexes;
    }
    
    private class WorkspaceMetadata
    {
        public Dictionary<string, WorkspaceIndexInfo> Indexes { get; set; } = new();
    }
    
    private class WorkspaceIndexInfo
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string HashPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
    }
}