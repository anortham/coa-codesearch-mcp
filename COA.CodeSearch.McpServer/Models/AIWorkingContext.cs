using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Models;

public class AIWorkingContext
{
    public string SessionId { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; }
    public List<FlexibleMemoryEntry> PrimaryMemories { get; set; } = new();
    public List<FlexibleMemoryEntry> SecondaryMemories { get; set; } = new();
    public List<FlexibleMemoryEntry> AvailableMemories { get; set; } = new();
    public List<string> SuggestedActions { get; set; } = new();
}