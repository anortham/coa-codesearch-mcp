using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for memory lifecycle management operations
/// </summary>
public interface IMemoryLifecycleService
{
    /// <summary>
    /// Manually trigger a check for stale memories
    /// </summary>
    Task<StaleMemoryCheckResult> ManuallyCheckStaleMemoriesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get pending resolution memories
    /// </summary>
    Task<PendingResolutionResult> GetPendingResolutionsAsync(int maxResults = 50, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Record feedback on a resolution decision
    /// </summary>
    Task RecordResolutionFeedbackAsync(string memoryId, bool wasCorrect, string? feedback = null);
    
    /// <summary>
    /// Archive memories based on criteria
    /// </summary>
    Task<ArchiveMemoriesResult> ArchiveMemoriesAsync(ArchiveMemoriesRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get confidence score for a memory
    /// </summary>
    Task<MemoryConfidenceResult> GetMemoryConfidenceAsync(string memoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of stale memory check
/// </summary>
public class StaleMemoryCheckResult
{
    public bool Success { get; set; }
    public int StaleMemoriesFound { get; set; }
    public int MemoriesMarkedStale { get; set; }
    public List<FlexibleMemoryEntry> StaleMemories { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of pending resolutions query
/// </summary>
public class PendingResolutionResult
{
    public bool Success { get; set; }
    public int TotalCount { get; set; }
    public List<PendingResolutionEntry> PendingResolutions { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Pending resolution entry with confidence and related memory info
/// </summary>
public class PendingResolutionEntry
{
    public string Id { get; set; } = string.Empty;
    public string OriginalMemoryId { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Request to archive memories
/// </summary>
public class ArchiveMemoriesRequest
{
    public string? MemoryType { get; set; }
    public int? OlderThanDays { get; set; }
    public string? Status { get; set; }
    public bool IncludeResolved { get; set; }
    public int MaxToArchive { get; set; } = 100;
}

/// <summary>
/// Result of archive operation
/// </summary>
public class ArchiveMemoriesResult
{
    public bool Success { get; set; }
    public int MemoriesArchived { get; set; }
    public List<string> ArchivedMemoryIds { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Memory confidence information
/// </summary>
public class MemoryConfidenceResult
{
    public bool Success { get; set; }
    public string MemoryId { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public DateTime CalculatedAt { get; set; }
    public Dictionary<string, float> ConfidenceFactors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}