using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Documents;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Mock implementation of IBatchIndexingService for testing
/// </summary>
public class MockBatchIndexingService : IBatchIndexingService
{
    public Task AddDocumentAsync(string workspacePath, Document document, string documentId, CancellationToken cancellationToken = default)
    {
        // No-op for testing - just return completed task
        return Task.CompletedTask;
    }

    public Task FlushBatchAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // No-op for testing
        return Task.CompletedTask;
    }

    public BatchIndexingStats GetStats(string workspacePath)
    {
        return new BatchIndexingStats
        {
            PendingDocuments = 0,
            TotalBatches = 0,
            TotalDocuments = 0,
            AverageBatchTime = TimeSpan.Zero,
            LastCommit = DateTime.UtcNow
        };
    }

    public Task CommitAllAsync(CancellationToken cancellationToken = default)
    {
        // No-op for testing
        return Task.CompletedTask;
    }
}