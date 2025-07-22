using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
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
        // Currently no cleanup tools are registered
        // This registration class is kept for future cleanup tools
    }
}