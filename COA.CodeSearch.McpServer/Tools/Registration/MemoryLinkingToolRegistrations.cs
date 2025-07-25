using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Protocol;
using System.Text.Json;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

public static class MemoryLinkingToolRegistrations
{
    public static void RegisterAll(ToolRegistry registry, MemoryLinkingTools linkingTools)
    {
        RegisterLinkMemoriesTool(registry, linkingTools);
        RegisterGetRelatedMemoriesTool(registry, linkingTools);
        RegisterUnlinkMemoriesTool(registry, linkingTools);
    }
    
    private static void RegisterLinkMemoriesTool(ToolRegistry registry, MemoryLinkingTools tool)
    {
        registry.RegisterTool<LinkMemoriesParams>(
            name: ToolNames.LinkMemories,
            description: "Create a relationship between two memories. Supports various relationship types like 'blockedBy', 'implements', 'supersedes', etc.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    sourceId = new { type = "string", description = "ID of the source memory" },
                    targetId = new { type = "string", description = "ID of the target memory" },
                    relationshipType = new 
                    { 
                        type = "string", 
                        description = "Type of relationship (default: relatedTo). Common types: blockedBy, implements, supersedes, dependsOn, parentOf, references, causes, resolves, duplicates",
                        @default = "relatedTo"
                    },
                    bidirectional = new 
                    { 
                        type = "boolean", 
                        description = "Create relationship in both directions (default: false)",
                        @default = false
                    }
                },
                required = new[] { "sourceId", "targetId" }
            },
            handler: async (parameters, ct) =>
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - parameters are marked as required
                var result = await tool.LinkMemoriesAsync(
                    parameters.SourceId ?? throw new ArgumentNullException(nameof(parameters.SourceId)),
                    parameters.TargetId ?? throw new ArgumentNullException(nameof(parameters.TargetId)),
                    parameters.RelationshipType ?? "relatedTo",
                    parameters.Bidirectional ?? false
                );
#pragma warning restore CS8602
                
                if (result.Success)
                {
                    return ToolRegistrationHelper.CreateSuccessResult(new
                    {
                        success = true,
                        message = result.Message,
                        sourceId = result.SourceId,
                        targetId = result.TargetId,
                        relationshipType = result.RelationshipType
                    });
                }
                else
                {
                    return ToolRegistrationHelper.CreateErrorResult(result.Message);
                }
            }
        );
    }
    
    private static void RegisterGetRelatedMemoriesTool(ToolRegistry registry, MemoryLinkingTools tool)
    {
        registry.RegisterTool<GetRelatedMemoriesParams>(
            name: ToolNames.GetRelatedMemories,
            description: "Get all memories related to a given memory, traversing relationships up to specified depth. Returns a graph of connected memories.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    memoryId = new { type = "string", description = "ID of the memory to find relationships for" },
                    maxDepth = new 
                    { 
                        type = "integer", 
                        description = "Maximum depth to traverse relationships (default: 2)",
                        @default = 2
                    },
                    relationshipTypes = new 
                    { 
                        type = "array",
                        items = new { type = "string" },
                        description = "Filter by specific relationship types. If not specified, all types are included"
                    }
                },
                required = new[] { "memoryId" }
            },
            handler: async (parameters, ct) =>
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - parameters are marked as required
                var result = await tool.GetRelatedMemoriesAsync(
                    parameters.MemoryId ?? throw new ArgumentNullException(nameof(parameters.MemoryId)),
                    parameters.MaxDepth ?? 2,
                    parameters.RelationshipTypes
                );
#pragma warning restore CS8602
                
                if (result.Success)
                {
                    return ToolRegistrationHelper.CreateSuccessResult(new
                    {
                        success = true,
                        rootMemory = result.RootMemory,
                        relatedMemories = result.RelatedMemories.Select(rm => new
                        {
                            memory = rm.Memory,
                            depth = rm.Depth,
                            relationshipType = rm.RelationshipType,
                            path = rm.Path
                        }),
                        totalFound = result.TotalFound,
                        message = $"Found {result.TotalFound} related memories"
                    });
                }
                else
                {
                    return ToolRegistrationHelper.CreateErrorResult(result.Message);
                }
            }
        );
    }
    
    private static void RegisterUnlinkMemoriesTool(ToolRegistry registry, MemoryLinkingTools tool)
    {
        registry.RegisterTool<UnlinkMemoriesParams>(
            name: ToolNames.UnlinkMemories,
            description: "Remove a relationship between two memories",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    sourceId = new { type = "string", description = "ID of the source memory" },
                    targetId = new { type = "string", description = "ID of the target memory" },
                    relationshipType = new 
                    { 
                        type = "string", 
                        description = "Type of relationship to remove (default: relatedTo)",
                        @default = "relatedTo"
                    },
                    bidirectional = new 
                    { 
                        type = "boolean", 
                        description = "Remove relationship in both directions (default: false)",
                        @default = false
                    }
                },
                required = new[] { "sourceId", "targetId" }
            },
            handler: async (parameters, ct) =>
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - parameters are marked as required
                var result = await tool.UnlinkMemoriesAsync(
                    parameters.SourceId ?? throw new ArgumentNullException(nameof(parameters.SourceId)),
                    parameters.TargetId ?? throw new ArgumentNullException(nameof(parameters.TargetId)),
                    parameters.RelationshipType ?? "relatedTo",
                    parameters.Bidirectional ?? false
                );
#pragma warning restore CS8602
                
                if (result.Success)
                {
                    return ToolRegistrationHelper.CreateSuccessResult(new
                    {
                        success = true,
                        message = result.Message
                    });
                }
                else
                {
                    return ToolRegistrationHelper.CreateErrorResult(result.Message);
                }
            }
        );
    }
}

// Parameter classes
public class LinkMemoriesParams
{
    public required string SourceId { get; set; }
    public required string TargetId { get; set; }
    public string? RelationshipType { get; set; }
    public bool? Bidirectional { get; set; }
}

public class GetRelatedMemoriesParams
{
    public required string MemoryId { get; set; }
    public int? MaxDepth { get; set; }
    public string[]? RelationshipTypes { get; set; }
}

public class UnlinkMemoriesParams
{
    public required string SourceId { get; set; }
    public required string TargetId { get; set; }
    public string? RelationshipType { get; set; }
    public bool? Bidirectional { get; set; }
}