namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Example of a strongly-typed tool implementation
/// This shows how new tools should be implemented with proper typing
/// </summary>
public class ExampleTypedTool : IExecutableTool<ExampleToolParams, ExampleToolResult>
{
    public string ToolName => "example_typed";
    public string Description => "Example of strongly-typed tool implementation pattern";
    public ToolCategory Category => ToolCategory.Infrastructure;
    
    public async Task<ExampleToolResult> ExecuteAsync(
        ExampleToolParams parameters, 
        CancellationToken cancellationToken = default)
    {
        // Tool implementation
        await Task.Delay(100, cancellationToken); // Simulate work
        
        return new ExampleToolResult
        {
            Success = true,
            Message = $"Processed: {parameters.Input}",
            ProcessedAt = DateTime.UtcNow
        };
    }
}

public class ExampleToolParams
{
    public string Input { get; set; } = "";
    public int? MaxResults { get; set; }
    public bool IncludeDetails { get; set; }
}

public class ExampleToolResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public DateTime ProcessedAt { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}