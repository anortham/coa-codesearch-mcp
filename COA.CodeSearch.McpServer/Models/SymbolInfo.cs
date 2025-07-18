namespace COA.CodeSearch.McpServer.Models;

public class SymbolInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string ContainerName { get; init; }
    public string? Documentation { get; init; }
    public required LocationInfo Location { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}