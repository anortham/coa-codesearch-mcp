using System.Collections.Generic;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Models specific to AI-optimized response building
/// </summary>

/// <summary>
/// Base class for memory data items
/// </summary>
public class MemorySearchData
{
    public List<MemorySummaryItem> Items { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> Distribution { get; set; } = new();
    public List<FileHotspot> Hotspots { get; set; } = new();
    public MemorySearchSummary Summary { get; set; } = new();
}

/// <summary>
/// Memory summary item
/// </summary>
public class MemorySummaryItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Created { get; set; }
    public bool IsShared { get; set; }
    public float Score { get; set; }
    public List<string> Files { get; set; } = new();
}

/// <summary>
/// File hotspot information
/// </summary>
public class FileHotspot
{
    public string File { get; set; } = "";
    public string Path { get; set; } = "";
    public int MemoryCount { get; set; }
}

/// <summary>
/// Memory search summary
/// </summary>
public class MemorySearchSummary
{
    public int TotalFound { get; set; }
    public int Returned { get; set; }
    public string PrimaryType { get; set; } = "";
}

/// <summary>
/// Convenience aliases for Priority enum
/// </summary>
public static class Priority
{
    public const ActionPriority High = ActionPriority.High;
    public const ActionPriority Medium = ActionPriority.Medium;
    public const ActionPriority Low = ActionPriority.Low;
    public const ActionPriority Recommended = ActionPriority.Medium; // Map to Medium
    public const ActionPriority Available = ActionPriority.Low; // Map to Low
}

/// <summary>
/// Alias for AIActionCommand for backward compatibility
/// </summary>
public class AICommand : AIActionCommand
{
    public AICommand() : base() { }
}