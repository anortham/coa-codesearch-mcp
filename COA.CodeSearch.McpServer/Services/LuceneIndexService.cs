using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Services;

public class LuceneIndexService : IDisposable
{
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, IndexEntry> _indexes = new();
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private const int MaxIndexes = 5;
    private const int CleanupIntervalMinutes = 30;

    public LuceneIndexService(ILogger<LuceneIndexService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _cleanupTimer = new Timer(CleanupExpiredIndexes, null, TimeSpan.FromMinutes(CleanupIntervalMinutes), TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    public async Task<IndexWriter> GetIndexWriterAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(workspacePath, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.IndexWriter;
            }

            await EnsureCapacityAsync();

            var indexPath = GetIndexPath(workspacePath);
            var directory = FSDirectory.Open(indexPath);
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                RAMBufferSizeMB = 256.0
            };

            var indexWriter = new IndexWriter(directory, config);
            
            _indexes[workspacePath] = new IndexEntry
            {
                IndexWriter = indexWriter,
                Directory = directory,
                Analyzer = analyzer,
                WorkspacePath = workspacePath,
                LastAccessed = DateTime.UtcNow
            };

            _logger.LogInformation("Created new Lucene index for workspace: {WorkspacePath}", workspacePath);
            return indexWriter;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<IndexSearcher> GetIndexSearcherAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var indexWriter = await GetIndexWriterAsync(workspacePath, cancellationToken);
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(workspacePath, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                
                // Always get a fresh reader for up-to-date results
                var reader = indexWriter.GetReader(applyAllDeletes: true);
                return new IndexSearcher(reader);
            }
            
            throw new InvalidOperationException($"Index not found for workspace: {workspacePath}");
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(workspacePath, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Analyzer;
            }
            
            // If index doesn't exist yet, create it
            await GetIndexWriterAsync(workspacePath, cancellationToken);
            
            if (_indexes.TryGetValue(workspacePath, out entry))
            {
                return entry.Analyzer;
            }
            
            throw new InvalidOperationException($"Failed to get analyzer for workspace: {workspacePath}");
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var indexWriter = await GetIndexWriterAsync(workspacePath, cancellationToken);
        await Task.Run(() => indexWriter.Commit(), cancellationToken);
        _logger.LogDebug("Committed changes to Lucene index for workspace: {WorkspacePath}", workspacePath);
    }

    public async Task OptimizeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var indexWriter = await GetIndexWriterAsync(workspacePath, cancellationToken);
        await Task.Run(() => indexWriter.ForceMerge(1), cancellationToken);
        _logger.LogInformation("Optimized Lucene index for workspace: {WorkspacePath}", workspacePath);
    }

    public async Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var indexWriter = await GetIndexWriterAsync(workspacePath, cancellationToken);
        await Task.Run(() => indexWriter.DeleteAll(), cancellationToken);
        await CommitAsync(workspacePath, cancellationToken);
        _logger.LogInformation("Cleared Lucene index for workspace: {WorkspacePath}", workspacePath);
    }

    private string GetIndexPath(string workspacePath)
    {
        // Use .codesearch folder in the project root
        var projectRoot = FindProjectRoot(workspacePath);
        var indexPath = Path.Combine(projectRoot, ".codesearch", "index");
        
        // Ensure directory exists
        System.IO.Directory.CreateDirectory(indexPath);
        
        // Ensure .gitignore includes .codesearch
        EnsureGitIgnoreEntry(projectRoot);
        
        return indexPath;
    }
    
    private string FindProjectRoot(string workspacePath)
    {
        // If it's a solution or project file, get its directory
        if (File.Exists(workspacePath))
        {
            return Path.GetDirectoryName(workspacePath) ?? workspacePath;
        }
        
        // If it's already a directory, use it
        if (System.IO.Directory.Exists(workspacePath))
        {
            return workspacePath;
        }
        
        // Fallback to workspace path
        return workspacePath;
    }
    
    private void EnsureGitIgnoreEntry(string projectRoot)
    {
        try
        {
            var gitIgnorePath = Path.Combine(projectRoot, ".gitignore");
            var codesearchEntry = ".codesearch/";
            
            if (File.Exists(gitIgnorePath))
            {
                var content = File.ReadAllText(gitIgnorePath);
                if (!content.Contains(codesearchEntry))
                {
                    // Add .codesearch/ to gitignore
                    File.AppendAllText(gitIgnorePath, $"{Environment.NewLine}{codesearchEntry}{Environment.NewLine}");
                    _logger.LogInformation("Added .codesearch/ to .gitignore");
                }
            }
            else
            {
                // Create .gitignore with .codesearch/ entry
                File.WriteAllText(gitIgnorePath, $"{codesearchEntry}{Environment.NewLine}");
                _logger.LogInformation("Created .gitignore with .codesearch/ entry");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update .gitignore");
        }
    }

    private async Task EnsureCapacityAsync()
    {
        if (_indexes.Count >= MaxIndexes)
        {
            var oldestEntry = _indexes.Values
                .OrderBy(e => e.LastAccessed)
                .FirstOrDefault();

            if (oldestEntry != null)
            {
                await CloseIndexAsync(oldestEntry.WorkspacePath);
            }
        }
    }

    private async Task CloseIndexAsync(string workspacePath)
    {
        if (_indexes.TryRemove(workspacePath, out var entry))
        {
            try
            {
                await Task.Run(() => entry.IndexWriter.Dispose());
                entry.Directory.Dispose();
                _logger.LogInformation("Closed Lucene index for workspace: {WorkspacePath}", workspacePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing Lucene index for workspace: {WorkspacePath}", workspacePath);
            }
        }
    }

    private void CleanupExpiredIndexes(object? state)
    {
        _logger.LogDebug("Running Lucene index cleanup");
        var expiredEntries = _indexes.Values
            .Where(e => DateTime.UtcNow - e.LastAccessed > TimeSpan.FromHours(2))
            .ToList();

        foreach (var entry in expiredEntries)
        {
            _ = CloseIndexAsync(entry.WorkspacePath).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        
        foreach (var workspacePath in _indexes.Keys.ToList())
        {
            CloseIndexAsync(workspacePath).GetAwaiter().GetResult();
        }
        
        _indexLock?.Dispose();
    }

    private class IndexEntry
    {
        public required IndexWriter IndexWriter { get; init; }
        public required FSDirectory Directory { get; init; }
        public required Analyzer Analyzer { get; init; }
        public required string WorkspacePath { get; init; }
        public DateTime LastAccessed { get; set; }
    }
}