using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Resource provider that exposes indexed workspace files as browsable resources
/// and provides rich context about workspaces for AI agents.
/// </summary>
public class WorkspaceResourceProvider : IResourceProvider
{
    private readonly ILogger<WorkspaceResourceProvider> _logger;
    private readonly ILuceneIndexService _indexService;
    private readonly IPathResolutionService _pathResolution;

    public string Scheme => "codesearch-workspace";
    public string Name => "Workspace Files";
    public string Description => "Provides access to indexed workspace files and directories";

    public WorkspaceResourceProvider(
        ILogger<WorkspaceResourceProvider> logger,
        ILuceneIndexService indexService,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _indexService = indexService;
        _pathResolution = pathResolution;
    }

    /// <inheritdoc />
    public async Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<Resource>();

        try
        {
            // Get all indexed workspaces
            var workspaceMappings = await _indexService.GetAllIndexMappingsAsync();

            foreach (var mapping in workspaceMappings)
            {
                var workspacePath = mapping.Key;
                var workspaceName = Path.GetFileName(workspacePath) ?? "Unknown Workspace";

                // Add workspace root resource
                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://{Uri.EscapeDataString(workspacePath)}",
                    Name = $"Workspace: {workspaceName}",
                    Description = $"Browse indexed files in {workspacePath}",
                    MimeType = "application/json"
                });

                // Add workspace context resources
                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://{Uri.EscapeDataString(workspacePath)}/context/overview",
                    Name = $"Context: {workspaceName} Overview",
                    Description = $"Workspace overview including file counts, languages, and structure",
                    MimeType = "application/json"
                });

                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://{Uri.EscapeDataString(workspacePath)}/context/languages",
                    Name = $"Context: {workspaceName} Languages",
                    Description = $"Programming languages used in the workspace with statistics",
                    MimeType = "application/json"
                });

                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://{Uri.EscapeDataString(workspacePath)}/context/dependencies",
                    Name = $"Context: {workspaceName} Dependencies",
                    Description = $"Project dependencies and package information",
                    MimeType = "application/json"
                });

                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://{Uri.EscapeDataString(workspacePath)}/context/recent-changes",
                    Name = $"Context: {workspaceName} Recent Changes",
                    Description = $"Recently modified files and areas of active development",
                    MimeType = "application/json"
                });

                // Add directories as resources
                if (Directory.Exists(workspacePath))
                {
                    var directories = Directory.GetDirectories(workspacePath, "*", SearchOption.TopDirectoryOnly)
                        .Take(10); // Limit to avoid overwhelming clients

                    foreach (var directory in directories)
                    {
                        var dirName = Path.GetFileName(directory);
                        if (ShouldIncludeDirectory(dirName))
                        {
                            resources.Add(new Resource
                            {
                                Uri = $"{Scheme}://{Uri.EscapeDataString(directory)}",
                                Name = $"Directory: {dirName}",
                                Description = $"Browse files in {directory}",
                                MimeType = "application/json"
                            });
                        }
                    }
                }
            }

            _logger.LogDebug("Listed {Count} workspace resources", resources.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing workspace resources");
        }

        return resources;
    }

    /// <inheritdoc />
    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return null;

        try
        {
            // Extract path from URI
            // Check if this is a context resource
            if (uri.Contains("/context/"))
            {
                return await ReadContextResourceAsync(uri, cancellationToken);
            }

            var path = ExtractPathFromUri(uri);
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Could not extract path from URI: {Uri}", uri);
                return null;
            }

            if (Directory.Exists(path))
            {
                return await ReadDirectoryAsync(path, cancellationToken);
            }
            else if (File.Exists(path))
            {
                return await ReadFileAsync(path, cancellationToken);
            }

            _logger.LogWarning("Path does not exist: {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading workspace resource {Uri}", uri);
            return null;
        }
    }

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        return uri.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase);
    }

    private string? ExtractPathFromUri(string uri)
    {
        if (!uri.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase))
            return null;

        var pathPart = uri.Substring($"{Scheme}://".Length);
        return Uri.UnescapeDataString(pathPart);
    }

    private async Task<ReadResourceResult> ReadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var result = new ReadResourceResult();
        var content = new List<object>();

        // Add directory info
        var dirInfo = new DirectoryInfo(path);
        content.Add(new
        {
            type = "directory",
            name = dirInfo.Name,
            path = path,
            lastModified = dirInfo.LastWriteTimeUtc,
            itemCount = Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length
        });

        // Add subdirectories
        var directories = Directory.GetDirectories(path)
            .Where(d => ShouldIncludeDirectory(Path.GetFileName(d)))
            .Take(50)
            .Select(d => new
            {
                type = "directory",
                name = Path.GetFileName(d),
                path = d,
                uri = $"{Scheme}://{Uri.EscapeDataString(d)}"
            });

        content.AddRange(directories);

        // Add files
        var files = Directory.GetFiles(path)
            .Where(f => ShouldIncludeFile(f))
            .Take(100)
            .Select(f => new
            {
                type = "file",
                name = Path.GetFileName(f),
                path = f,
                uri = $"{Scheme}://{Uri.EscapeDataString(f)}",
                size = new FileInfo(f).Length,
                extension = Path.GetExtension(f)
            });

        content.AddRange(files);

        result.Contents.Add(new ResourceContent
        {
            Uri = $"{Scheme}://{Uri.EscapeDataString(path)}",
            MimeType = "application/json",
            Text = System.Text.Json.JsonSerializer.Serialize(content, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            })
        });

        return result;
    }

    private async Task<ReadResourceResult> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var result = new ReadResourceResult();
        var fileInfo = new FileInfo(path);

        // Check file size limit (1MB)
        if (fileInfo.Length > 1024 * 1024)
        {
            result.Contents.Add(new ResourceContent
            {
                Uri = $"{Scheme}://{Uri.EscapeDataString(path)}",
                MimeType = "text/plain",
                Text = $"File too large to display ({fileInfo.Length:N0} bytes). Use a text editor to view this file."
            });
            return result;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var mimeType = GetMimeTypeFromExtension(Path.GetExtension(path));

            result.Contents.Add(new ResourceContent
            {
                Uri = $"{Scheme}://{Uri.EscapeDataString(path)}",
                MimeType = mimeType,
                Text = content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {Path}", path);
            result.Contents.Add(new ResourceContent
            {
                Uri = $"{Scheme}://{Uri.EscapeDataString(path)}",
                MimeType = "text/plain",
                Text = $"Error reading file: {ex.Message}"
            });
        }

        return result;
    }

    private bool ShouldIncludeDirectory(string directoryName)
    {
        var excludedDirs = new[] { ".git", ".vs", "bin", "obj", "node_modules", ".codesearch" };
        return !excludedDirs.Contains(directoryName, StringComparer.OrdinalIgnoreCase);
    }

    private bool ShouldIncludeFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var allowedExtensions = new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".md", ".txt", ".json", ".xml", ".yml", ".yaml" };
        return allowedExtensions.Contains(extension);
    }

    private string GetMimeTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "text/x-csharp",
            ".js" => "text/javascript",
            ".ts" => "text/typescript",
            ".py" => "text/x-python",
            ".java" => "text/x-java",
            ".cpp" => "text/x-c++src",
            ".h" => "text/x-chdr",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".yml" or ".yaml" => "text/yaml",
            _ => "text/plain"
        };
    }

    private async Task<ReadResourceResult> ReadContextResourceAsync(string uri, CancellationToken cancellationToken)
    {
        var result = new ReadResourceResult();

        try
        {
            // Extract workspace path and context type from URI
            var parts = uri.Split("/context/");
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid context resource URI format: {Uri}", uri);
                return result;
            }

            var workspacePathEncoded = parts[0].Replace($"{Scheme}://", "");
            var contextType = parts[1];
            var workspacePath = Uri.UnescapeDataString(workspacePathEncoded);

            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("Workspace path does not exist: {Path}", workspacePath);
                return result;
            }

            object? contextData = contextType switch
            {
                "overview" => await GetWorkspaceOverviewAsync(workspacePath, cancellationToken),
                "languages" => await GetWorkspaceLanguagesAsync(workspacePath, cancellationToken),
                "dependencies" => await GetWorkspaceDependenciesAsync(workspacePath, cancellationToken),
                "recent-changes" => await GetRecentChangesAsync(workspacePath, cancellationToken),
                _ => null
            };

            if (contextData == null)
            {
                _logger.LogWarning("Unknown context type: {ContextType}", contextType);
                return result;
            }

            result.Contents.Add(new ResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(contextData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading context resource: {Uri}", uri);
        }

        return result;
    }

    private async Task<object> GetWorkspaceOverviewAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories);
        var languages = new Dictionary<string, int>();
        var totalSize = 0L;
        var fileCount = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (ShouldIncludeFile(file))
            {
                fileCount++;
                var fileInfo = new FileInfo(file);
                totalSize += fileInfo.Length;

                if (!string.IsNullOrEmpty(extension))
                {
                    languages[extension] = languages.GetValueOrDefault(extension, 0) + 1;
                }
            }
        }

        var directories = Directory.GetDirectories(workspacePath, "*", SearchOption.AllDirectories)
            .Where(d => ShouldIncludeDirectory(Path.GetFileName(d)))
            .Count();

        return new
        {
            workspacePath = workspacePath,
            workspaceName = Path.GetFileName(workspacePath),
            fileCount = fileCount,
            directoryCount = directories,
            totalSizeBytes = totalSize,
            totalSizeMB = totalSize / (1024.0 * 1024.0),
            languages = languages.OrderByDescending(kvp => kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            primaryLanguage = languages.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? "unknown",
            indexed = (await _indexService.GetAllIndexMappingsAsync()).ContainsKey(workspacePath),
            lastScanned = DateTime.UtcNow
        };
    }

    private async Task<object> GetWorkspaceLanguagesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Satisfy async requirement
        var languageStats = new Dictionary<string, LanguageStats>();
        var files = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (ShouldIncludeFile(file))
            {
                var language = GetLanguageFromExtension(extension);
                if (!languageStats.ContainsKey(language))
                {
                    languageStats[language] = new LanguageStats { Language = language };
                }

                var stats = languageStats[language];
                stats.FileCount++;
                stats.TotalBytes += new FileInfo(file).Length;
                stats.Extensions.Add(extension);
            }
        }

        return new
        {
            languages = languageStats.Values.OrderByDescending(l => l.FileCount).ToList(),
            totalLanguages = languageStats.Count,
            primaryLanguage = languageStats.Values.OrderByDescending(l => l.FileCount).FirstOrDefault()?.Language ?? "unknown"
        };
    }

    private async Task<object> GetWorkspaceDependenciesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var dependencies = new List<object>();

        // Check for .NET projects
        var csprojFiles = Directory.GetFiles(workspacePath, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Any())
        {
            dependencies.Add(new
            {
                type = ".NET",
                projectFiles = csprojFiles.Select(Path.GetFileName).ToList(),
                count = csprojFiles.Length
            });
        }

        // Check for Node.js projects
        var packageJsonFiles = Directory.GetFiles(workspacePath, "package.json", SearchOption.AllDirectories);
        if (packageJsonFiles.Any())
        {
            dependencies.Add(new
            {
                type = "Node.js",
                projectFiles = packageJsonFiles.Select(Path.GetFileName).ToList(),
                count = packageJsonFiles.Length
            });
        }

        // Check for Python projects
        var requirementsFiles = Directory.GetFiles(workspacePath, "requirements.txt", SearchOption.AllDirectories);
        if (requirementsFiles.Any())
        {
            dependencies.Add(new
            {
                type = "Python",
                projectFiles = requirementsFiles.Select(Path.GetFileName).ToList(),
                count = requirementsFiles.Length
            });
        }

        await Task.CompletedTask;

        return new
        {
            workspacePath = workspacePath,
            dependencies = dependencies,
            hasMultipleProjectTypes = dependencies.Count > 1
        };
    }

    private async Task<object> GetRecentChangesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var cutoffTime = DateTime.UtcNow.AddDays(-7);
        var files = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories);
        
        // Create strongly-typed collection to avoid dynamic casts
        var recentFilesList = new List<(string path, string relativePath, DateTime lastModified, long sizeBytes, string extension)>();

        foreach (var file in files.Take(1000)) // Limit to prevent overwhelming
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (ShouldIncludeFile(file))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc >= cutoffTime)
                {
                    recentFilesList.Add((
                        path: file,
                        relativePath: Path.GetRelativePath(workspacePath, file),
                        lastModified: fileInfo.LastWriteTimeUtc,
                        sizeBytes: fileInfo.Length,
                        extension: fileInfo.Extension
                    ));
                }
            }
        }

        var orderedFiles = recentFilesList
            .OrderByDescending(f => f.lastModified)
            .Take(50)
            .Select(f => new
            {
                path = f.path,
                relativePath = f.relativePath,
                lastModified = f.lastModified,
                sizeBytes = f.sizeBytes,
                extension = f.extension
            })
            .ToList();

        var hotspots = orderedFiles
            .GroupBy(f => Path.GetDirectoryName(f.relativePath))
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { directory = g.Key, changeCount = g.Count() })
            .ToList();

        await Task.CompletedTask;

        return new
        {
            workspacePath = workspacePath,
            timeRange = "last7days",
            totalChangedFiles = recentFilesList.Count,
            recentFiles = orderedFiles,
            hotspots = hotspots
        };
    }

    private string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".cpp" or ".cc" or ".cxx" => "C++",
            ".c" => "C",
            ".h" or ".hpp" => "C/C++ Header",
            ".go" => "Go",
            ".rs" => "Rust",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".swift" => "Swift",
            ".kt" => "Kotlin",
            ".scala" => "Scala",
            ".r" => "R",
            ".m" => "Objective-C",
            ".lua" => "Lua",
            ".pl" => "Perl",
            ".sh" => "Shell",
            ".ps1" => "PowerShell",
            ".md" => "Markdown",
            ".json" => "JSON",
            ".xml" => "XML",
            ".yml" or ".yaml" => "YAML",
            ".html" => "HTML",
            ".css" => "CSS",
            ".scss" or ".sass" => "SASS/SCSS",
            ".sql" => "SQL",
            _ => "Other"
        };
    }

    private class LanguageStats
    {
        public string Language { get; set; } = "";
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public HashSet<string> Extensions { get; set; } = new();
    }
}