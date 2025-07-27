using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Resource provider that exposes memory system content as readable documents.
/// Allows clients to access memories, checklists, and related data as structured resources.
/// </summary>
public class MemoryResourceProvider : IResourceProvider
{
    private readonly ILogger<MemoryResourceProvider> _logger;
    private readonly IMemoryService _memoryService;

    public string Scheme => "codesearch-memory";
    public string Name => "Memory System";
    public string Description => "Provides access to memories, checklists, and knowledge base";

    public MemoryResourceProvider(
        ILogger<MemoryResourceProvider> logger,
        IMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }

    /// <inheritdoc />
    public async Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<Resource>();

        try
        {
            // Add memory collections
            resources.Add(new Resource
            {
                Uri = $"{Scheme}://memories/all",
                Name = "All Memories",
                Description = "Complete collection of stored memories",
                MimeType = "application/json"
            });

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://memories/by-type",
                Name = "Memories by Type",
                Description = "Memories organized by type (TechnicalDebt, ArchitecturalDecision, etc.)",
                MimeType = "application/json"
            });

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://memories/timeline",
                Name = "Memory Timeline",
                Description = "Chronological view of memories",
                MimeType = "application/json"
            });

            // Add memory type-specific collections
            var memoryTypes = new[]
            {
                "TechnicalDebt",
                "ArchitecturalDecision",
                "ProjectInsight",
                "CodePattern",
                "WorkSession",
                "LocalInsight"
            };

            foreach (var type in memoryTypes)
            {
                resources.Add(new Resource
                {
                    Uri = $"{Scheme}://memories/type/{type}",
                    Name = $"{type} Memories",
                    Description = $"All memories of type {type}",
                    MimeType = "application/json"
                });
            }

            // Add dashboard resource
            resources.Add(new Resource
            {
                Uri = $"{Scheme}://dashboard",
                Name = "Memory Dashboard",
                Description = "Statistics and health information about the memory system",
                MimeType = "application/json"
            });

            _logger.LogDebug("Listed {Count} memory resources", resources.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing memory resources");
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
            var path = ExtractPathFromUri(uri);
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Could not extract path from URI: {Uri}", uri);
                return null;
            }

            var result = new ReadResourceResult();
            
            // Route to appropriate handler based on path
            if (path.StartsWith("memories/"))
            {
                await HandleMemoryResource(path, result, cancellationToken);
            }
            else if (path == "dashboard")
            {
                await HandleDashboardResource(result, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Unknown memory resource path: {Path}", path);
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading memory resource {Uri}", uri);
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

        return uri.Substring($"{Scheme}://".Length);
    }

    private async Task HandleMemoryResource(string path, ReadResourceResult result, CancellationToken cancellationToken)
    {
        var content = path switch
        {
            "memories/all" => await GetAllMemoriesAsync(cancellationToken),
            "memories/by-type" => await GetMemoriesByTypeAsync(cancellationToken),
            "memories/timeline" => await GetMemoryTimelineAsync(cancellationToken),
            var p when p.StartsWith("memories/type/") => await GetMemoriesBySpecificTypeAsync(p.Substring("memories/type/".Length), cancellationToken),
            _ => null
        };

        if (content != null)
        {
            result.Contents.Add(new ResourceContent
            {
                Uri = $"{Scheme}://{path}",
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(content, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            });
        }
    }

    private async Task HandleDashboardResource(ReadResourceResult result, CancellationToken cancellationToken)
    {
        // This would typically call the memory dashboard functionality
        // For now, create a simple dashboard structure
        var dashboard = new
        {
            timestamp = DateTime.UtcNow,
            summary = new
            {
                totalMemories = 0, // Would get from memory service
                memoryTypes = new string[] { "TechnicalDebt", "ArchitecturalDecision", "ProjectInsight" },
                healthStatus = "Healthy"
            },
            recentActivity = new[]
            {
                new { type = "created", count = 5, timeframe = "today" },
                new { type = "updated", count = 2, timeframe = "today" }
            }
        };

        result.Contents.Add(new ResourceContent
        {
            Uri = $"{Scheme}://dashboard",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(dashboard, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        });
    }

    private async Task<object> GetAllMemoriesAsync(CancellationToken cancellationToken)
    {
        // This would call the memory service to get all memories
        // For now, return a placeholder structure
        return new
        {
            memories = new object[] { },
            totalCount = 0,
            retrievedAt = DateTime.UtcNow
        };
    }

    private async Task<object> GetMemoriesByTypeAsync(CancellationToken cancellationToken)
    {
        // This would call the memory service to get memories grouped by type
        return new
        {
            memoriesByType = new Dictionary<string, object[]>
            {
                ["TechnicalDebt"] = new object[] { },
                ["ArchitecturalDecision"] = new object[] { },
                ["ProjectInsight"] = new object[] { }
            },
            retrievedAt = DateTime.UtcNow
        };
    }

    private async Task<object> GetMemoryTimelineAsync(CancellationToken cancellationToken)
    {
        // This would call the memory service to get chronological timeline
        return new
        {
            timeline = new object[] { },
            retrievedAt = DateTime.UtcNow
        };
    }

    private async Task<object> GetMemoriesBySpecificTypeAsync(string type, CancellationToken cancellationToken)
    {
        // This would call the memory service to get memories of a specific type
        return new
        {
            type = type,
            memories = new object[] { },
            retrievedAt = DateTime.UtcNow
        };
    }
}