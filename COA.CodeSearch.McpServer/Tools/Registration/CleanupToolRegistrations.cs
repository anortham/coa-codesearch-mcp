using COA.CodeSearch.McpServer.Services;
using COA.Directus.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers cleanup and maintenance tools
/// </summary>
public static class CleanupToolRegistrations
{
    public static void RegisterCleanupTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        var cleanupTool = serviceProvider.GetRequiredService<CleanupMemoryIndexesTool>();
        
        // TODO: Remove this tool after migration is complete - no longer needed
        // RegisterCleanupMemoryIndexes(registry, cleanupTool);
    }
    
    private static void RegisterCleanupMemoryIndexes(ToolRegistry registry, CleanupMemoryIndexesTool tool)
    {
        registry.RegisterTool<CleanupMemoryIndexesParams>(
            name: "cleanup_memory_indexes",
            description: "Clean up incorrectly created memory index directories with hash suffixes and migrate data to correct locations. Finds directories like project-memory_hash and migrates them to proper project-memory location.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    dryRun = new { 
                        type = "boolean", 
                        description = "If true, only report what would be done without making changes (default: true)", 
                        @default = true 
                    }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.CleanupMemoryIndexesAsync(parameters?.DryRun ?? true);
                return CreateSuccessResult(result);
            }
        );
    }
}

public class CleanupMemoryIndexesParams
{
    public bool? DryRun { get; set; }
}