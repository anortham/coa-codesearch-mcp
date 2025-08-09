using System.Text.Json;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Collects and tracks indexing performance metrics for monitoring and optimization
/// </summary>
public interface IIndexingMetricsService
{
    /// <summary>
    /// Start tracking an indexing operation
    /// </summary>
    IDisposable StartOperation(string operationType, string workspacePath, int? expectedFileCount = null);
    
    /// <summary>
    /// Record a file indexing operation
    /// </summary>
    void RecordFileIndexed(string filePath, long fileSizeBytes, TimeSpan duration, bool success = true, string? error = null);
    
    /// <summary>
    /// Record a search operation
    /// </summary>
    void RecordSearchOperation(string queryType, TimeSpan duration, int resultCount, bool success = true);
    
    /// <summary>
    /// Record memory usage at a point in time
    /// </summary>
    void RecordMemoryUsage(long workingSetBytes, long privateMemoryBytes);
    
    /// <summary>
    /// Get current performance metrics
    /// </summary>
    Task<IndexingMetricsSnapshot> GetCurrentMetricsAsync();
    
    /// <summary>
    /// Get metrics for a specific time period
    /// </summary>
    Task<IndexingMetricsReport> GetMetricsReportAsync(TimeSpan period);
    
    /// <summary>
    /// Export metrics to JSON for external monitoring systems
    /// </summary>
    Task<string> ExportMetricsAsync(TimeSpan? period = null);
    
    /// <summary>
    /// Reset all collected metrics (useful for testing)
    /// </summary>
    void ResetMetrics();
}

// Response models
public class IndexingMetricsSnapshot
{
    public bool MetricsEnabled { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Totals
    public long TotalOperations { get; set; }
    public long TotalFileOperations { get; set; }
    public long TotalSearchOperations { get; set; }
    public long TotalBytesIndexed { get; set; }
    
    // Recent performance (last 5 minutes)
    public int RecentFileOperationsCount { get; set; }
    public double RecentFileOperationsPerSecond { get; set; }
    public TimeSpan RecentAverageFileIndexingTime { get; set; }
    public int RecentSearchOperationsCount { get; set; }
    public TimeSpan RecentAverageSearchTime { get; set; }
    
    // Hourly performance
    public int HourlyFileOperationsCount { get; set; }
    public double HourlyFileOperationsPerSecond { get; set; }
    public TimeSpan HourlyAverageFileIndexingTime { get; set; }
    public int HourlySearchOperationsCount { get; set; }
    public TimeSpan HourlyAverageSearchTime { get; set; }
    
    // Error rates
    public double RecentFileErrorRate { get; set; }
    public double RecentSearchErrorRate { get; set; }
    
    // Memory
    public double CurrentWorkingSetMB { get; set; }
    public double CurrentPrivateMemoryMB { get; set; }
    
    // Analysis
    public Dictionary<string, int> TopFileExtensionsByCount { get; set; } = new();
    public Dictionary<string, long> TopFileExtensionsBySize { get; set; } = new();
}

public class IndexingMetricsReport
{
    public bool MetricsEnabled { get; set; }
    public TimeSpan Period { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    
    // File operations
    public int TotalFileOperations { get; set; }
    public int SuccessfulFileOperations { get; set; }
    public int FailedFileOperations { get; set; }
    public long TotalBytesProcessed { get; set; }
    public TimeSpan AverageFileIndexingTime { get; set; }
    public TimeSpan MedianFileIndexingTime { get; set; }
    public TimeSpan P95FileIndexingTime { get; set; }
    
    // Search operations
    public int TotalSearchOperations { get; set; }
    public int SuccessfulSearchOperations { get; set; }
    public int FailedSearchOperations { get; set; }
    public TimeSpan AverageSearchTime { get; set; }
    public TimeSpan MedianSearchTime { get; set; }
    public TimeSpan P95SearchTime { get; set; }
    
    // Memory
    public double PeakMemoryUsageMB { get; set; }
    public double AverageMemoryUsageMB { get; set; }
    
    // Analysis
    public Dictionary<string, int> TopFileErrors { get; set; } = new();
    public Dictionary<string, FileTypePerformance> PerformanceByFileType { get; set; } = new();
}

public class FileTypePerformance
{
    public int Count { get; set; }
    public long TotalSizeBytes { get; set; }
    public TimeSpan AverageTime { get; set; }
    public TimeSpan MedianTime { get; set; }
}