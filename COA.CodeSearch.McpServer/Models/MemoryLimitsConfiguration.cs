using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Configuration for memory limits and backpressure control
/// Critical for preventing OOM and ensuring system stability
/// </summary>
public class MemoryLimitsConfiguration
{
    /// <summary>
    /// Maximum file size to index (in bytes)
    /// Default: 10MB - Files larger than this are skipped to prevent memory pressure
    /// </summary>
    [Range(1024, 100 * 1024 * 1024)]
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Threshold for using memory-mapped files vs loading into memory (in bytes)
    /// Default: 1MB - Above this size, use memory-mapped files to prevent LOH allocation
    /// </summary>
    [Range(1024, 50 * 1024 * 1024)]
    public long LargeFileThreshold { get; set; } = 1024 * 1024;

    /// <summary>
    /// Maximum number of search results to return
    /// Default: 10000 - Above this number, results are truncated to prevent OOM
    /// </summary>
    [Range(100, 100000)]
    public int MaxAllowedResults { get; set; } = 10000;

    /// <summary>
    /// Maximum content length for memory entries (in characters)
    /// Default: 100000 - Above this size, content is truncated
    /// </summary>
    [Range(1000, 1000000)]
    public int MaxMemoryContentLength { get; set; } = 100000;

    /// <summary>
    /// Maximum number of files to process in parallel during indexing
    /// Default: 8 - Higher values increase memory usage but improve performance
    /// </summary>
    [Range(1, 32)]
    public int MaxIndexingConcurrency { get; set; } = 8;

    /// <summary>
    /// Maximum queue size for file indexing backpressure
    /// Default: 80 - When queue is full, indexing will wait to prevent memory exhaustion
    /// </summary>
    [Range(10, 1000)]
    public int MaxIndexingQueueSize { get; set; } = 80;

    /// <summary>
    /// Maximum number of indexes to keep in memory simultaneously
    /// Default: 100 - Older indexes are evicted using LRU when this limit is exceeded
    /// </summary>
    [Range(10, 500)]
    public int MaxActiveIndexes { get; set; } = 100;

    /// <summary>
    /// Time after which idle indexes are cleaned up (in minutes)
    /// Default: 15 minutes - Helps prevent memory leaks from unused indexes
    /// </summary>
    [Range(1, 1440)]
    public int IdleIndexCleanupMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum memory usage percentage before backpressure kicks in
    /// Default: 85% - When system memory usage exceeds this, operations are throttled
    /// </summary>
    [Range(50, 95)]
    public int MaxMemoryUsagePercent { get; set; } = 85;

    /// <summary>
    /// Maximum number of concurrent batch operations
    /// Default: 4 - Limits concurrent batch indexing to prevent memory spikes
    /// </summary>
    [Range(1, 16)]
    public int MaxConcurrentBatches { get; set; } = 4;

    /// <summary>
    /// Maximum batch size for document indexing
    /// Default: 500 - Larger batches improve performance but use more memory
    /// </summary>
    [Range(10, 5000)]
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Enable automatic garbage collection when memory pressure is detected
    /// Default: true - Helps prevent OOM by forcing GC when memory usage is high
    /// </summary>
    public bool EnableMemoryPressureGC { get; set; } = true;

    /// <summary>
    /// Enable backpressure control for indexing operations
    /// Default: true - Prevents system overload by throttling operations when memory is low
    /// </summary>
    public bool EnableBackpressure { get; set; } = true;
}