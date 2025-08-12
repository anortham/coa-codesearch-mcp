using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Collects and tracks indexing performance metrics for monitoring and optimization
/// </summary>
public class IndexingMetricsService : IIndexingMetricsService, IDisposable
{
    private readonly ILogger<IndexingMetricsService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPathResolutionService _pathResolution;
    
    // Thread-safe collections for metrics
    private readonly ConcurrentQueue<OperationMetric> _operations = new();
    private readonly ConcurrentQueue<FileIndexingMetric> _fileOperations = new();
    private readonly ConcurrentQueue<SearchMetric> _searchOperations = new();
    private readonly ConcurrentQueue<MemoryMetric> _memorySnapshots = new();
    
    // Configuration
    private readonly bool _metricsEnabled;
    private readonly int _maxMetricsAge;
    private readonly int _maxMetricsCount;
    private readonly Timer? _cleanupTimer;
    private readonly Timer? _memoryTimer;
    
    // Performance counters
    private long _totalOperations;
    private long _totalFileOperations;
    private long _totalSearchOperations;
    private long _totalBytesIndexed;
    private readonly object _countersLock = new();

    public IndexingMetricsService(
        ILogger<IndexingMetricsService> logger,
        IConfiguration configuration,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _configuration = configuration;
        _pathResolution = pathResolution;
        
        // Load configuration
        _metricsEnabled = configuration.GetValue("Metrics:Enabled", true);
        _maxMetricsAge = configuration.GetValue("Metrics:MaxAgeMinutes", 60); // Keep 1 hour of metrics by default
        _maxMetricsCount = configuration.GetValue("Metrics:MaxCount", 10000); // Limit memory usage
        
        if (_metricsEnabled)
        {
            // Start periodic cleanup of old metrics
            _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            // Start periodic memory monitoring
            _memoryTimer = new Timer(RecordCurrentMemoryUsage, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            
            _logger.LogInformation("IndexingMetricsService started - metrics collection enabled");
        }
        else
        {
            _logger.LogInformation("IndexingMetricsService started - metrics collection disabled");
        }
    }

    public IDisposable StartOperation(string operationType, string workspacePath, int? expectedFileCount = null)
    {
        if (!_metricsEnabled) return new NoOpDisposable();
        
        var operation = new OperationTracker(this, operationType, workspacePath, expectedFileCount);
        return operation;
    }
    
    public void RecordFileIndexed(string filePath, long fileSizeBytes, TimeSpan duration, bool success = true, string? error = null)
    {
        if (!_metricsEnabled) return;
        
        var metric = new FileIndexingMetric
        {
            Timestamp = DateTime.UtcNow,
            FilePath = filePath,
            FileSizeBytes = fileSizeBytes,
            Duration = duration,
            Success = success,
            Error = error,
            FileExtension = Path.GetExtension(filePath).ToLowerInvariant()
        };
        
        _fileOperations.Enqueue(metric);
        
        lock (_countersLock)
        {
            _totalFileOperations++;
            if (success)
            {
                _totalBytesIndexed += fileSizeBytes;
            }
        }
        
        // Log slow file operations
        if (duration.TotalSeconds > 5)
        {
            _logger.LogWarning("Slow file indexing detected: {FilePath} took {Duration:F2}s ({SizeKB} KB)",
                filePath, duration.TotalSeconds, fileSizeBytes / 1024);
        }
    }
    
    public void RecordSearchOperation(string queryType, TimeSpan duration, int resultCount, bool success = true)
    {
        if (!_metricsEnabled) return;
        
        var metric = new SearchMetric
        {
            Timestamp = DateTime.UtcNow,
            QueryType = queryType,
            Duration = duration,
            ResultCount = resultCount,
            Success = success
        };
        
        _searchOperations.Enqueue(metric);
        
        lock (_countersLock)
        {
            _totalSearchOperations++;
        }
        
        // Log slow search operations
        if (duration.TotalMilliseconds > 1000)
        {
            _logger.LogWarning("Slow search operation detected: {QueryType} took {Duration:F2}ms (results: {ResultCount})",
                queryType, duration.TotalMilliseconds, resultCount);
        }
    }
    
    public void RecordMemoryUsage(long workingSetBytes, long privateMemoryBytes)
    {
        if (!_metricsEnabled) return;
        
        var metric = new MemoryMetric
        {
            Timestamp = DateTime.UtcNow,
            WorkingSetBytes = workingSetBytes,
            PrivateMemoryBytes = privateMemoryBytes
        };
        
        _memorySnapshots.Enqueue(metric);
    }
    
    public async Task<IndexingMetricsSnapshot> GetCurrentMetricsAsync()
    {
        if (!_metricsEnabled)
        {
            return new IndexingMetricsSnapshot { MetricsEnabled = false };
        }
        
        var now = DateTime.UtcNow;
        var last5Minutes = now.AddMinutes(-5);
        var last1Hour = now.AddHours(-1);
        
        // Get recent operations
        var recentFileOps = _fileOperations.Where(op => op.Timestamp >= last5Minutes).ToList();
        var hourlyFileOps = _fileOperations.Where(op => op.Timestamp >= last1Hour).ToList();
        var recentSearchOps = _searchOperations.Where(op => op.Timestamp >= last5Minutes).ToList();
        var hourlySearchOps = _searchOperations.Where(op => op.Timestamp >= last1Hour).ToList();
        
        // Get latest memory snapshot
        var latestMemory = _memorySnapshots.LastOrDefault();
        
        var snapshot = new IndexingMetricsSnapshot
        {
            MetricsEnabled = true,
            Timestamp = now,
            
            // Totals
            TotalOperations = _totalOperations,
            TotalFileOperations = _totalFileOperations,
            TotalSearchOperations = _totalSearchOperations,
            TotalBytesIndexed = _totalBytesIndexed,
            
            // Recent performance (last 5 minutes)
            RecentFileOperationsCount = recentFileOps.Count,
            RecentFileOperationsPerSecond = recentFileOps.Count / 300.0, // 5 minutes = 300 seconds
            RecentAverageFileIndexingTime = recentFileOps.Any() 
                ? TimeSpan.FromMilliseconds(recentFileOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            RecentSearchOperationsCount = recentSearchOps.Count,
            RecentAverageSearchTime = recentSearchOps.Any()
                ? TimeSpan.FromMilliseconds(recentSearchOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            
            // Hourly performance
            HourlyFileOperationsCount = hourlyFileOps.Count,
            HourlyFileOperationsPerSecond = hourlyFileOps.Count / 3600.0, // 1 hour = 3600 seconds
            HourlyAverageFileIndexingTime = hourlyFileOps.Any()
                ? TimeSpan.FromMilliseconds(hourlyFileOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            HourlySearchOperationsCount = hourlySearchOps.Count,
            HourlyAverageSearchTime = hourlySearchOps.Any()
                ? TimeSpan.FromMilliseconds(hourlySearchOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            
            // Error rates
            RecentFileErrorRate = recentFileOps.Any() 
                ? recentFileOps.Count(op => !op.Success) / (double)recentFileOps.Count
                : 0.0,
            RecentSearchErrorRate = recentSearchOps.Any()
                ? recentSearchOps.Count(op => !op.Success) / (double)recentSearchOps.Count
                : 0.0,
            
            // Memory
            CurrentWorkingSetMB = latestMemory?.WorkingSetBytes / 1024.0 / 1024.0 ?? 0,
            CurrentPrivateMemoryMB = latestMemory?.PrivateMemoryBytes / 1024.0 / 1024.0 ?? 0,
            
            // Top file extensions by volume
            TopFileExtensionsByCount = hourlyFileOps
                .GroupBy(op => op.FileExtension)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count()),
                
            TopFileExtensionsBySize = hourlyFileOps
                .GroupBy(op => op.FileExtension)
                .OrderByDescending(g => g.Sum(op => op.FileSizeBytes))
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Sum(op => op.FileSizeBytes))
        };
        
        return await Task.FromResult(snapshot);
    }
    
    public async Task<IndexingMetricsReport> GetMetricsReportAsync(TimeSpan period)
    {
        if (!_metricsEnabled)
        {
            return new IndexingMetricsReport { MetricsEnabled = false };
        }
        
        var cutoff = DateTime.UtcNow.Subtract(period);
        
        var fileOps = _fileOperations.Where(op => op.Timestamp >= cutoff).ToList();
        var searchOps = _searchOperations.Where(op => op.Timestamp >= cutoff).ToList();
        var memorySnapshots = _memorySnapshots.Where(m => m.Timestamp >= cutoff).ToList();
        
        var report = new IndexingMetricsReport
        {
            MetricsEnabled = true,
            Period = period,
            StartTime = cutoff,
            EndTime = DateTime.UtcNow,
            
            // File operations analysis
            TotalFileOperations = fileOps.Count,
            SuccessfulFileOperations = fileOps.Count(op => op.Success),
            FailedFileOperations = fileOps.Count(op => !op.Success),
            TotalBytesProcessed = fileOps.Where(op => op.Success).Sum(op => op.FileSizeBytes),
            AverageFileIndexingTime = fileOps.Any() 
                ? TimeSpan.FromMilliseconds(fileOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            MedianFileIndexingTime = fileOps.Any() 
                ? TimeSpan.FromMilliseconds(CalculateMedian(fileOps.Select(op => op.Duration.TotalMilliseconds)))
                : TimeSpan.Zero,
            P95FileIndexingTime = fileOps.Any()
                ? TimeSpan.FromMilliseconds(CalculatePercentile(fileOps.Select(op => op.Duration.TotalMilliseconds), 0.95))
                : TimeSpan.Zero,
            
            // Search operations analysis  
            TotalSearchOperations = searchOps.Count,
            SuccessfulSearchOperations = searchOps.Count(op => op.Success),
            FailedSearchOperations = searchOps.Count(op => !op.Success),
            AverageSearchTime = searchOps.Any()
                ? TimeSpan.FromMilliseconds(searchOps.Average(op => op.Duration.TotalMilliseconds))
                : TimeSpan.Zero,
            MedianSearchTime = searchOps.Any()
                ? TimeSpan.FromMilliseconds(CalculateMedian(searchOps.Select(op => op.Duration.TotalMilliseconds)))
                : TimeSpan.Zero,
            P95SearchTime = searchOps.Any()
                ? TimeSpan.FromMilliseconds(CalculatePercentile(searchOps.Select(op => op.Duration.TotalMilliseconds), 0.95))
                : TimeSpan.Zero,
            
            // Memory analysis
            PeakMemoryUsageMB = memorySnapshots.Any() 
                ? memorySnapshots.Max(m => m.WorkingSetBytes) / 1024.0 / 1024.0
                : 0,
            AverageMemoryUsageMB = memorySnapshots.Any()
                ? memorySnapshots.Average(m => m.WorkingSetBytes) / 1024.0 / 1024.0
                : 0,
            
            // Error analysis
            TopFileErrors = fileOps
                .Where(op => !op.Success && !string.IsNullOrEmpty(op.Error))
                .GroupBy(op => op.Error!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count()),
                
            // Performance by file type
            PerformanceByFileType = fileOps
                .Where(op => op.Success)
                .GroupBy(op => op.FileExtension)
                .Where(g => g.Count() >= 5) // Only include types with sufficient data
                .ToDictionary(
                    g => g.Key,
                    g => new FileTypePerformance
                    {
                        Count = g.Count(),
                        TotalSizeBytes = g.Sum(op => op.FileSizeBytes),
                        AverageTime = TimeSpan.FromMilliseconds(g.Average(op => op.Duration.TotalMilliseconds)),
                        MedianTime = TimeSpan.FromMilliseconds(CalculateMedian(g.Select(op => op.Duration.TotalMilliseconds)))
                    })
        };
        
        return await Task.FromResult(report);
    }
    
    public async Task<string> ExportMetricsAsync(TimeSpan? period = null)
    {
        var exportPeriod = period ?? TimeSpan.FromHours(1);
        var report = await GetMetricsReportAsync(exportPeriod);
        var snapshot = await GetCurrentMetricsAsync();
        
        var export = new
        {
            ExportedAt = DateTime.UtcNow,
            Period = exportPeriod,
            Snapshot = snapshot,
            Report = report
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        return JsonSerializer.Serialize(export, options);
    }
    
    public void ResetMetrics()
    {
        if (!_metricsEnabled) return;
        
        // Clear all collections
        while (_operations.TryDequeue(out _)) { }
        while (_fileOperations.TryDequeue(out _)) { }
        while (_searchOperations.TryDequeue(out _)) { }
        while (_memorySnapshots.TryDequeue(out _)) { }
        
        lock (_countersLock)
        {
            _totalOperations = 0;
            _totalFileOperations = 0;
            _totalSearchOperations = 0;
            _totalBytesIndexed = 0;
        }
        
        _logger.LogInformation("All metrics have been reset");
    }
    
    private void CleanupOldMetrics(object? state)
    {
        if (!_metricsEnabled) return;
        
        var cutoff = DateTime.UtcNow.AddMinutes(-_maxMetricsAge);
        
        // Clean up old metrics to prevent memory leaks
        CleanupQueue(_fileOperations, cutoff, _maxMetricsCount);
        CleanupQueue(_searchOperations, cutoff, _maxMetricsCount);
        CleanupQueue(_memorySnapshots, cutoff, _maxMetricsCount);
        CleanupQueue(_operations, cutoff, _maxMetricsCount);
    }
    
    private void CleanupQueue<T>(ConcurrentQueue<T> queue, DateTime cutoff, int maxCount) where T : ITimestamped
    {
        var temp = new List<T>();
        
        // Dequeue all items
        while (queue.TryDequeue(out var item))
        {
            if (item.Timestamp >= cutoff)
            {
                temp.Add(item);
            }
        }
        
        // Keep only the most recent items if we're over the limit
        if (temp.Count > maxCount)
        {
            temp = temp.OrderByDescending(item => item.Timestamp).Take(maxCount).ToList();
        }
        
        // Re-enqueue the items we want to keep
        foreach (var item in temp.OrderBy(item => item.Timestamp))
        {
            queue.Enqueue(item);
        }
    }
    
    private void RecordCurrentMemoryUsage(object? state)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            RecordMemoryUsage(process.WorkingSet64, process.PrivateMemorySize64);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record memory usage");
        }
    }
    
    private static double CalculateMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return 0;
        
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 
            ? (sorted[mid - 1] + sorted[mid]) / 2.0 
            : sorted[mid];
    }
    
    private static double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return 0;
        
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
    
    internal void RecordOperation(string operationType, string workspacePath, TimeSpan duration, 
        bool success, int? fileCount = null, string? error = null)
    {
        if (!_metricsEnabled) return;
        
        var metric = new OperationMetric
        {
            Timestamp = DateTime.UtcNow,
            OperationType = operationType,
            WorkspacePath = workspacePath,
            Duration = duration,
            Success = success,
            FileCount = fileCount,
            Error = error
        };
        
        _operations.Enqueue(metric);
        
        lock (_countersLock)
        {
            _totalOperations++;
        }
    }
    
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _memoryTimer?.Dispose();
    }
}

// Helper classes for operation tracking
internal class OperationTracker : IDisposable
{
    private readonly IndexingMetricsService _metricsService;
    private readonly string _operationType;
    private readonly string _workspacePath;
    private readonly int? _expectedFileCount;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public OperationTracker(IndexingMetricsService metricsService, string operationType, 
        string workspacePath, int? expectedFileCount)
    {
        _metricsService = metricsService;
        _operationType = operationType;
        _workspacePath = workspacePath;
        _expectedFileCount = expectedFileCount;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _stopwatch.Stop();
        _metricsService.RecordOperation(_operationType, _workspacePath, _stopwatch.Elapsed, 
            true, _expectedFileCount);
        
        _disposed = true;
    }
}

internal class NoOpDisposable : IDisposable
{
    public void Dispose() { }
}

// Interfaces and data models
internal interface ITimestamped
{
    DateTime Timestamp { get; }
}

// Metric data models
public class OperationMetric : ITimestamped
{
    public DateTime Timestamp { get; set; }
    public string OperationType { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public int? FileCount { get; set; }
    public string? Error { get; set; }
}

public class FileIndexingMetric : ITimestamped
{
    public DateTime Timestamp { get; set; }
    public string FilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string FileExtension { get; set; } = "";
}

public class SearchMetric : ITimestamped
{
    public DateTime Timestamp { get; set; }
    public string QueryType { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int ResultCount { get; set; }
    public bool Success { get; set; }
}

public class MemoryMetric : ITimestamped
{
    public DateTime Timestamp { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
}