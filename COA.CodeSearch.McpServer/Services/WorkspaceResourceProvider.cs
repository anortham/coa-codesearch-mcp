using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Resource provider that exposes indexed workspace files as browsable resources.
/// Allows clients to discover and read files that have been indexed by CodeSearch.
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
}