using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Mock implementation of IIndexingMetricsService for testing
/// </summary>
public class MockIndexingMetricsService : IIndexingMetricsService
{
    public IDisposable StartOperation(string operationType, string workspacePath, int? expectedFileCount = null)
    {
        return new NoOpDisposable();
    }

    public void RecordFileIndexed(string filePath, long fileSizeBytes, TimeSpan duration, bool success = true, string? error = null)
    {
        // No-op for testing
    }

    public void RecordSearchOperation(string queryType, TimeSpan duration, int resultCount, bool success = true)
    {
        // No-op for testing
    }

    public void RecordMemoryUsage(long workingSetBytes, long privateMemoryBytes)
    {
        // No-op for testing
    }

    public Task<IndexingMetricsSnapshot> GetCurrentMetricsAsync()
    {
        return Task.FromResult(new IndexingMetricsSnapshot
        {
            MetricsEnabled = false,
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<IndexingMetricsReport> GetMetricsReportAsync(TimeSpan period)
    {
        return Task.FromResult(new IndexingMetricsReport
        {
            MetricsEnabled = false,
            Period = period,
            StartTime = DateTime.UtcNow.Subtract(period),
            EndTime = DateTime.UtcNow
        });
    }

    public Task<string> ExportMetricsAsync(TimeSpan? period = null)
    {
        return Task.FromResult("{}");
    }

    public void ResetMetrics()
    {
        // No-op for testing
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}