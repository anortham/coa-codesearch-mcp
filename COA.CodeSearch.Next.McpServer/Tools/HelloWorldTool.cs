using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// A simple greeting tool that demonstrates basic MCP tool structure.
/// </summary>
public class HelloWorldTool : McpToolBase<HelloWorldParameters, HelloWorldResult>
{
    public override string Name => "hello_world";
    public override string Description => "Simple greeting tool that demonstrates basic MCP tool structure";
    public override ToolCategory Category => ToolCategory.Utility;

    protected override async Task<HelloWorldResult> ExecuteInternalAsync(
        HelloWorldParameters parameters, 
        CancellationToken cancellationToken)
    {
        var name = parameters.Name ?? "World";
        var greeting = $"Hello, {name}!";
        
        if (parameters.IncludeTime == true)
        {
            greeting += $" The current UTC time is {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }

        return await Task.FromResult(new HelloWorldResult
        {
            Greeting = greeting,
            Timestamp = DateTime.UtcNow
        });
    }
}

/// <summary>
/// Parameters for the HelloWorld tool.
/// </summary>
public class HelloWorldParameters
{
    /// <summary>
    /// Name of the person to greet
    /// </summary>
    [Description("Name of the person to greet")]
    public string? Name { get; set; }

    /// <summary>
    /// Include current UTC time in greeting
    /// </summary>
    [Description("Include current UTC time in greeting")]
    public bool? IncludeTime { get; set; }
}

/// <summary>
/// Result from the HelloWorld tool.
/// </summary>
public class HelloWorldResult : ToolResultBase
{
    public override string Operation => "hello_world";
    
    /// <summary>
    /// The greeting message
    /// </summary>
    public string Greeting { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when greeting was generated
    /// </summary>
    public DateTime Timestamp { get; set; }
}
