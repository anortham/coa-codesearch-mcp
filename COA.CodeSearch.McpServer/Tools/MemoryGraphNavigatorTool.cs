using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized tool that explores memory relationships with visual understanding.
/// Designed to help AI agents understand the connections and dependencies between memories.
/// </summary>
public class MemoryGraphNavigatorTool : ClaudeOptimizedToolBase
{
    public override string ToolName => "memory_graph_navigator";
    public override string Description => "Explores memory relationships and dependencies with graph visualization";
    public override ToolCategory Category => ToolCategory.Memory;

    private readonly FlexibleMemorySearchToolV2 _memorySearchTool;
    private readonly MemoryLinkingTools _linkingTools;
    private readonly FlexibleMemoryService _memoryService;
    private readonly IErrorRecoveryService _errorRecoveryService;

    public MemoryGraphNavigatorTool(
        ILogger<MemoryGraphNavigatorTool> logger,
        FlexibleMemorySearchToolV2 memorySearchTool,
        MemoryLinkingTools linkingTools,
        FlexibleMemoryService memoryService,
        IErrorRecoveryService errorRecoveryService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memorySearchTool = memorySearchTool;
        _linkingTools = linkingTools;
        _memoryService = memoryService;
        _errorRecoveryService = errorRecoveryService;
    }

    public async Task<object> ExecuteAsync(
        string startPoint,
        int depth = 2,
        string[]? filterTypes = null,
        bool includeOrphans = false,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                var cachedData = DetailCache.GetDetailData<object>(detailRequest.DetailRequestToken);
                if (cachedData != null)
                {
                    return cachedData;
                }
            }

            Logger.LogInformation("Memory graph navigator starting for: {StartPoint} with depth {Depth}", startPoint, depth);

            // Validate input
            if (string.IsNullOrWhiteSpace(startPoint))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Start point cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("startPoint", "memory ID or search query"));
            }

            if (depth < 1 || depth > 5)
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Depth must be between 1 and 5",
                    _errorRecoveryService.GetValidationErrorRecovery("depth", "integer between 1 and 5"));
            }

            var resourceUri = $"codesearch-memory-graph://{Guid.NewGuid():N}";
            var graph = new MemoryGraph();
            var clusters = new List<MemoryCluster>();
            var insights = new List<string>();

            // Step 1: Determine if startPoint is a memory ID or search query
            var startMemory = await ResolveStartPointAsync(startPoint);
            if (startMemory == null)
            {
                return UnifiedToolResponse<object>.CreateError(
                    "NOT_FOUND",
                    $"Could not find memory or memories for start point: {startPoint}",
                    _errorRecoveryService.GetValidationErrorRecovery("startPoint", "valid memory ID or search terms"));
            }

            // Step 2: Build the graph starting from the resolved memory
            await BuildMemoryGraphAsync(graph, startMemory, depth, filterTypes, cancellationToken);

            // Step 3: Add orphaned memories if requested
            if (includeOrphans)
            {
                await AddOrphanedMemoriesAsync(graph, filterTypes, cancellationToken);
            }

            // Step 4: Identify clusters and themes
            clusters = IdentifyMemoryClusters(graph);

            // Step 5: Generate insights about the graph structure
            insights = GenerateGraphInsights(graph, clusters);

            var result = new MemoryGraphResult
            {
                Graph = graph,
                Clusters = clusters,
                Insights = insights,
                ResourceUri = resourceUri,
                Success = true,
                Operation = "memory_graph_navigator",
                Query = new { startPoint, depth, filterTypes, includeOrphans }
            };

            Logger.LogInformation("Memory graph navigation completed. Found {NodeCount} nodes, {EdgeCount} edges",
                graph.Nodes.Count, graph.Edges.Count);

            // Return Claude-optimized response
            return await CreateClaudeResponseAsync(
                result,
                mode,
                GenerateMemoryGraphSummary,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Memory graph navigation failed for start point: {StartPoint}", startPoint);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                ex.Message,
                _errorRecoveryService.GetValidationErrorRecovery("memory_graph_navigator", "Check start point and try again"));
        }
    }

    private async Task<MemoryNode?> ResolveStartPointAsync(string startPoint)
    {
        try
        {
            // First try to treat it as a memory ID
            var memory = await _memoryService.GetMemoryByIdAsync(startPoint);
            if (memory != null)
            {
                return CreateMemoryNodeFromMemory(memory);
            }

            // If not a valid ID, search for memories matching the start point
            var searchResult = await _memorySearchTool.ExecuteAsync(
                query: startPoint, 
                maxResults: 1);

            if (searchResult != null)
            {
                var searchData = ExtractSearchData(searchResult);
                if (searchData.Any())
                {
                    var firstResult = searchData.First();
                    return CreateMemoryNodeFromSearchResult(firstResult);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to resolve start point: {StartPoint}", startPoint);
            return null;
        }
    }

    private async Task BuildMemoryGraphAsync(
        MemoryGraph graph, 
        MemoryNode startNode, 
        int depth, 
        string[]? filterTypes,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<(MemoryNode node, int currentDepth)>();

        // Add start node
        graph.Nodes.Add(startNode);
        visited.Add(startNode.Id);
        queue.Enqueue((startNode, 0));

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentNode, currentDepth) = queue.Dequeue();

            if (currentDepth >= depth)
                continue;

            try
            {
                // Get related memories for current node
                var relatedResult = await _linkingTools.GetRelatedMemoriesAsync(
                    currentNode.Id, 
                    maxDepth: 1); // Only get direct relationships

                if (relatedResult?.Success == true && relatedResult.RelatedMemories?.Any() == true)
                {
                    foreach (var relatedMemory in relatedResult.RelatedMemories)
                    {
                        // Skip if we've already visited this memory
                        if (visited.Contains(relatedMemory.Memory.Id))
                            continue;

                        // Apply type filtering if specified
                        if (filterTypes != null && filterTypes.Length > 0 && 
                            !filterTypes.Contains(relatedMemory.Memory.Type, StringComparer.OrdinalIgnoreCase))
                            continue;

                        // Create node for related memory
                        var relatedNode = CreateMemoryNodeFromMemory(relatedMemory.Memory);
                        graph.Nodes.Add(relatedNode);
                        visited.Add(relatedNode.Id);

                        // Create edge between current and related node
                        var edge = new RelationshipEdge
                        {
                            SourceId = currentNode.Id,
                            TargetId = relatedNode.Id,
                            RelationshipType = relatedMemory.RelationshipType ?? "relatedTo",
                            Strength = CalculateRelationshipStrength(currentNode, relatedNode)
                        };
                        graph.Edges.Add(edge);

                        // Add to queue for further exploration
                        queue.Enqueue((relatedNode, currentDepth + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to get relationships for memory: {MemoryId}", currentNode.Id);
            }
        }
    }

    private async Task AddOrphanedMemoriesAsync(MemoryGraph graph, string[]? filterTypes, CancellationToken cancellationToken)
    {
        try
        {
            // Search for memories without relationships
            var searchResult = await _memorySearchTool.ExecuteAsync(
                query: "*", 
                types: filterTypes,
                maxResults: 50);

            if (searchResult != null)
            {
                var searchData = ExtractSearchData(searchResult);
                var existingIds = new HashSet<string>(graph.Nodes.Select(n => n.Id));

                foreach (var memory in searchData.Take(20)) // Limit orphans to prevent overwhelming response
                {
                    var memoryId = ExtractMemoryId(memory);
                    if (!string.IsNullOrEmpty(memoryId) && !existingIds.Contains(memoryId))
                    {
                        // Check if this memory truly has no relationships
                        var relatedResult = await _linkingTools.GetRelatedMemoriesAsync(memoryId, maxDepth: 1);
                        if (relatedResult?.Success == true && 
                            (relatedResult.RelatedMemories == null || !relatedResult.RelatedMemories.Any()))
                        {
                            var orphanNode = CreateMemoryNodeFromSearchResult(memory);
                            orphanNode.IsOrphan = true;
                            graph.Nodes.Add(orphanNode);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to add orphaned memories to graph");
        }
    }

    private List<MemoryCluster> IdentifyMemoryClusters(MemoryGraph graph)
    {
        var clusters = new List<MemoryCluster>();
        var clusteredNodes = new HashSet<string>();

        // Group by memory type
        var typeGroups = graph.Nodes
            .Where(n => !clusteredNodes.Contains(n.Id))
            .GroupBy(n => n.Type)
            .Where(g => g.Count() > 1);

        foreach (var typeGroup in typeGroups)
        {
            clusters.Add(new MemoryCluster
            {
                Theme = $"{typeGroup.Key} Cluster",
                MemberIds = typeGroup.Select(n => n.Id).ToList(),
                ClusterType = "type-based"
            });

            foreach (var node in typeGroup)
            {
                clusteredNodes.Add(node.Id);
            }
        }

        // Group by file associations
        var fileGroups = graph.Nodes
            .Where(n => !clusteredNodes.Contains(n.Id) && n.Files.Any())
            .SelectMany(n => n.Files.Select(f => new { Node = n, File = f }))
            .GroupBy(x => x.File)
            .Where(g => g.Count() > 1);

        foreach (var fileGroup in fileGroups.Take(5)) // Limit file clusters
        {
            clusters.Add(new MemoryCluster
            {
                Theme = $"File: {Path.GetFileName(fileGroup.Key)}",
                MemberIds = fileGroup.Select(x => x.Node.Id).Distinct().ToList(),
                ClusterType = "file-based"
            });

            foreach (var item in fileGroup)
            {
                clusteredNodes.Add(item.Node.Id);
            }
        }

        return clusters;
    }

    private List<string> GenerateGraphInsights(MemoryGraph graph, List<MemoryCluster> clusters)
    {
        var insights = new List<string>();

        if (graph.Nodes.Count == 0)
        {
            insights.Add("No memories found in the graph");
            return insights;
        }

        // Graph structure insights
        var orphanCount = graph.Nodes.Count(n => n.IsOrphan);
        if (orphanCount > 0)
        {
            insights.Add($"Found {orphanCount} orphaned memories with no relationships");
        }

        var mostConnectedNode = graph.Nodes
            .Select(n => new { Node = n, Connections = graph.Edges.Count(e => e.SourceId == n.Id || e.TargetId == n.Id) })
            .OrderByDescending(x => x.Connections)
            .FirstOrDefault();

        if (mostConnectedNode != null && mostConnectedNode.Connections > 0)
        {
            insights.Add($"Most connected memory: {mostConnectedNode.Node.Title} ({mostConnectedNode.Connections} connections)");
        }

        // Cluster insights
        if (clusters.Count > 0)
        {
            insights.Add($"Identified {clusters.Count} memory clusters suggesting organized knowledge areas");
        }

        // Type distribution insights
        var typeDistribution = graph.Nodes.GroupBy(n => n.Type).ToDictionary(g => g.Key, g => g.Count());
        var dominantType = typeDistribution.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        if (dominantType.Value > graph.Nodes.Count / 2)
        {
            insights.Add($"Graph is dominated by {dominantType.Key} memories ({dominantType.Value}/{graph.Nodes.Count})");
        }

        return insights;
    }

    private MemoryNode CreateMemoryNodeFromMemory(FlexibleMemoryEntry memory)
    {
        return new MemoryNode
        {
            Id = memory.Id,
            Title = memory.Content ?? "Untitled",
            Type = memory.Type ?? "Unknown",
            Files = memory.FilesInvolved?.ToList() ?? new List<string>(),
            CreatedAt = memory.Created,
            IsOrphan = false
        };
    }

    private List<object> ExtractSearchData(object searchResult)
    {
        try
        {
            // Try to extract data from the search result
            var resultType = searchResult.GetType();
            var dataProperty = resultType.GetProperty("Data");
            if (dataProperty != null)
            {
                var data = dataProperty.GetValue(searchResult);
                if (data != null)
                {
                    var resultsProperty = data.GetType().GetProperty("Results");
                    if (resultsProperty != null && resultsProperty.GetValue(data) is IEnumerable<object> results)
                    {
                        return results.ToList();
                    }
                }
            }
            return new List<object>();
        }
        catch
        {
            return new List<object>();
        }
    }

    private MemoryNode CreateMemoryNodeFromSearchResult(dynamic searchResult)
    {
        return new MemoryNode
        {
            Id = ExtractMemoryId(searchResult) ?? "",
            Title = ExtractContent(searchResult) ?? "Untitled",
            Type = ExtractType(searchResult) ?? "Unknown",
            Files = ExtractFiles(searchResult),
            CreatedAt = ExtractDate(searchResult, "Created") ?? DateTime.MinValue,
            IsOrphan = false
        };
    }

    private MemoryNode CreateMemoryNodeFromRelated(dynamic relatedMemory)
    {
        return new MemoryNode
        {
            Id = relatedMemory.Id?.ToString() ?? "",
            Title = relatedMemory.Content?.ToString() ?? "Untitled",
            Type = relatedMemory.Type?.ToString() ?? "Unknown",
            Files = ExtractFiles(relatedMemory),
            CreatedAt = ExtractDate(relatedMemory, "Created") ?? DateTime.MinValue,
            IsOrphan = false
        };
    }

    private double CalculateRelationshipStrength(MemoryNode source, MemoryNode target)
    {
        var strength = 0.5; // Base strength

        // Increase strength for same type
        if (source.Type == target.Type)
            strength += 0.2;

        // Increase strength for shared files
        var sharedFiles = source.Files.Intersect(target.Files).Count();
        if (sharedFiles > 0)
            strength += Math.Min(0.3, sharedFiles * 0.1);

        return Math.Min(1.0, strength);
    }

    // Helper methods to extract data from dynamic objects
    private string? ExtractMemoryId(dynamic obj)
    {
        try
        {
            return obj.Id?.ToString() ?? obj.id?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractContent(dynamic obj)
    {
        try
        {
            return obj.Content?.ToString() ?? obj.content?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractType(dynamic obj)
    {
        try
        {
            return obj.MemoryType?.ToString() ?? obj.Type?.ToString() ?? obj.type?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private List<string> ExtractFiles(dynamic obj)
    {
        try
        {
            if (obj.Files != null)
            {
                if (obj.Files is IEnumerable<string> stringList)
                    return stringList.ToList();
                if (obj.Files is IEnumerable<object> objList)
                    return objList.Select(f => f.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()!;
            }
            return new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private DateTime? ExtractDate(dynamic obj, string fieldName)
    {
        try
        {
            var dateField = obj.GetType().GetProperty(fieldName)?.GetValue(obj);
            if (dateField is DateTime dt)
                return dt;
            if (dateField is string str && DateTime.TryParse(str, out var parsed))
                return parsed;
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Override required base class methods
    protected override int GetTotalResults<T>(T data)
    {
        if (data is MemoryGraphResult result)
        {
            return result.Graph.Nodes.Count;
        }
        return 0;
    }

    protected override List<string> GenerateKeyInsights<T>(T data)
    {
        var insights = base.GenerateKeyInsights(data);

        if (data is MemoryGraphResult result)
        {
            insights.AddRange(result.Insights.Take(3));
        }

        return insights;
    }

    private ClaudeSummaryData GenerateMemoryGraphSummary(MemoryGraphResult result)
    {
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = result.Graph.Nodes.Count,
                AffectedFiles = result.Graph.Nodes.SelectMany(n => n.Files).Distinct().Count(),
                EstimatedFullResponseTokens = (result.Graph.Nodes.Count + result.Graph.Edges.Count) * 80,
                KeyInsights = result.Insights.Take(3).ToList()
            },
            ByCategory = new Dictionary<string, CategorySummary>
            {
                ["nodes"] = new CategorySummary
                {
                    Files = result.Graph.Nodes.Count,
                    Occurrences = result.Graph.Nodes.Count,
                    PrimaryPattern = result.Graph.Nodes.GroupBy(n => n.Type).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "Mixed"
                },
                ["relationships"] = new CategorySummary
                {
                    Files = result.Graph.Edges.Count,
                    Occurrences = result.Graph.Edges.Count,
                    PrimaryPattern = result.Graph.Edges.GroupBy(e => e.RelationshipType).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "Mixed"
                }
            },
            Hotspots = result.Clusters.Take(3).Select(cluster => new Hotspot
            {
                File = cluster.Theme,
                Occurrences = cluster.MemberIds.Count,
                Complexity = cluster.MemberIds.Count > 5 ? "high" : "medium",
                Reason = $"Memory cluster with {cluster.MemberIds.Count} related items"
            }).ToList()
        };
    }
}

// Data Models
public class MemoryGraphResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Operation { get; set; } = "memory_graph_navigator";
    public object? Query { get; set; }
    public MemoryGraph Graph { get; set; } = new();
    public List<MemoryCluster> Clusters { get; set; } = new();
    public List<string> Insights { get; set; } = new();
    public string ResourceUri { get; set; } = string.Empty;
}

public class MemoryGraph
{
    public List<MemoryNode> Nodes { get; set; } = new();
    public List<RelationshipEdge> Edges { get; set; } = new();
}

public class MemoryNode
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public bool IsOrphan { get; set; }
}

public class RelationshipEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public double Strength { get; set; }
}

public class MemoryCluster
{
    public string Theme { get; set; } = string.Empty;
    public List<string> MemberIds { get; set; } = new();
    public string ClusterType { get; set; } = string.Empty;
}