namespace COA.Roslyn.McpServer.Models;

public class LocationInfo
{
    public required string FilePath { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string? PreviewText { get; init; }
}