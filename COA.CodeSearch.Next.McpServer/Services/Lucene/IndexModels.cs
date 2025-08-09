using Lucene.Net.Documents;
using Lucene.Net.Search;

namespace COA.CodeSearch.Next.McpServer.Services.Lucene;

/// <summary>
/// Result of index initialization
/// </summary>
public class IndexInitResult
{
    public bool Success { get; set; }
    public string WorkspaceHash { get; set; } = string.Empty;
    public string IndexPath { get; set; } = string.Empty;
    public bool IsNewIndex { get; set; }
    public int ExistingDocumentCount { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Search result from Lucene
/// </summary>
public class SearchResult
{
    public int TotalHits { get; set; }
    public List<SearchHit> Hits { get; set; } = new();
    public TimeSpan SearchTime { get; set; }
    public string? Query { get; set; }
}

/// <summary>
/// Individual search hit
/// </summary>
public class SearchHit
{
    public string FilePath { get; set; } = string.Empty;
    public float Score { get; set; }
    public string? Content { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<string>? HighlightedFragments { get; set; }
}

/// <summary>
/// Index health status
/// </summary>
public class IndexHealthStatus
{
    public enum HealthLevel { Healthy, Degraded, Unhealthy, Missing }
    
    public HealthLevel Level { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsCorrupted { get; set; }
    public int DocumentCount { get; set; }
    public long IndexSizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Index statistics
/// </summary>
public class IndexStatistics
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string WorkspaceHash { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public int DeletedDocumentCount { get; set; }
    public long IndexSizeBytes { get; set; }
    public int SegmentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime? LastCommit { get; set; }
    public DateTime? LastOptimization { get; set; }
    public Dictionary<string, int> FileTypeDistribution { get; set; } = new();
}

/// <summary>
/// Options for index repair operations
/// </summary>
public class IndexRepairOptions
{
    public bool CreateBackup { get; set; } = true;
    public bool RemoveBadSegments { get; set; } = true;
    public bool ValidateAfterRepair { get; set; } = true;
    public string? BackupPath { get; set; }
}

/// <summary>
/// Result of an index repair operation
/// </summary>
public class IndexRepairResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RemovedSegments { get; set; }
    public int LostDocuments { get; set; }
    public string? BackupPath { get; set; }
    public Exception? Exception { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}