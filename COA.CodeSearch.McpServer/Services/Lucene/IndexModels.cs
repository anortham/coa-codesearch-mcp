using Lucene.Net.Documents;
using Lucene.Net.Search;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Services.Lucene;

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
    public long ProcessingTimeMs => (long)SearchTime.TotalMilliseconds;
}

/// <summary>
/// Individual search hit
/// </summary>
public class SearchHit
{
    public string FilePath { get; set; } = string.Empty;
    public float Score { get; set; }
    // Content removed - loaded on-demand when context is needed
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<string>? HighlightedFragments { get; set; }
    public string? Snippet { get; set; }
    public DateTime? LastModified { get; set; }
    public int? LineNumber { get; set; }
    
    // NEW: Rich context for VS Code visualization
    public List<string>? ContextLines { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    
    
    // Type information from Tree-sitter extraction
    public TypeContext? TypeContext { get; set; }

    // Helper properties for common fields
    public string? FileName => Fields.GetValueOrDefault("filename");
    public string? RelativePath => Fields.GetValueOrDefault("relativePath");
    public string? Extension => Fields.GetValueOrDefault("extension");
}

/// <summary>
/// Type context information for search hits
/// </summary>
public class TypeContext
{
    public string? ContainingType { get; set; }
    public List<TypeExtraction.TypeInfo>? NearbyTypes { get; set; }
    public List<TypeExtraction.MethodInfo>? NearbyMethods { get; set; }
    public string? Language { get; set; }
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
    public TimeSpan ReaderAge { get; set; }
    public DateTime LastReaderUpdate { get; set; }
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

/// <summary>
/// Detailed diagnostics for NRT reader troubleshooting
/// </summary>
public class ReaderDiagnostics
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string WorkspaceHash { get; set; } = string.Empty;
    public bool HasReader { get; set; }
    public TimeSpan ReaderAge { get; set; }
    public DateTime LastReaderUpdate { get; set; }
    public long ReaderGeneration { get; set; }
    public long WriterGeneration { get; set; }
    public bool IsReaderStale { get; set; }
    public bool RecommendRefresh { get; set; }
    
    /// <summary>
    /// Human-readable description of reader state
    /// </summary>
    public string StatusDescription
    {
        get
        {
            if (!HasReader) return "No reader initialized";
            if (IsReaderStale) return $"Reader is stale (generation {ReaderGeneration} vs {WriterGeneration})";
            if (RecommendRefresh) return $"Reader age {ReaderAge.TotalSeconds:F1}s exceeds refresh threshold";
            return "Reader is current";
        }
    }
}