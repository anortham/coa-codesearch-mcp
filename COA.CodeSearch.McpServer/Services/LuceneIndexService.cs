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
        // Normalize the workspace path to the project root
        var projectRoot = FindProjectRoot(workspacePath);
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            // Use the normalized project root as the cache key
            if (_indexes.TryGetValue(projectRoot, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.IndexWriter;
            }

            await EnsureCapacityAsync();

            // GetIndexPath will also use FindProjectRoot internally, but we already have it
            var indexPath = Path.Combine(projectRoot, ".codesearch", "index");
            System.IO.Directory.CreateDirectory(indexPath);
            
            // Ensure .gitignore includes .codesearch
            EnsureGitIgnoreEntry(projectRoot);
            
            var directory = FSDirectory.Open(indexPath);
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                RAMBufferSizeMB = 256.0
            };

            var indexWriter = new IndexWriter(directory, config);
            
            _indexes[projectRoot] = new IndexEntry
            {
                IndexWriter = indexWriter,
                Directory = directory,
                Analyzer = analyzer,
                WorkspacePath = projectRoot,
                LastAccessed = DateTime.UtcNow
            };

            _logger.LogInformation("Created new Lucene index at: {IndexPath} for project root: {ProjectRoot} (original path: {WorkspacePath})", 
                indexPath, projectRoot, workspacePath);
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
        
        // Normalize to project root for cache lookup
        var projectRoot = FindProjectRoot(workspacePath);
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(projectRoot, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                
                // Always get a fresh reader for up-to-date results
                var reader = indexWriter.GetReader(applyAllDeletes: true);
                return new IndexSearcher(reader);
            }
            
            throw new InvalidOperationException($"Index not found for project: {projectRoot} (workspace: {workspacePath})");
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // Normalize to project root for cache lookup
        var projectRoot = FindProjectRoot(workspacePath);
        
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(projectRoot, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Analyzer;
            }
            
            // If index doesn't exist yet, create it
            await GetIndexWriterAsync(workspacePath, cancellationToken);
            
            if (_indexes.TryGetValue(projectRoot, out entry))
            {
                return entry.Analyzer;
            }
            
            throw new InvalidOperationException($"Failed to get analyzer for project: {projectRoot} (workspace: {workspacePath})");
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
        // Start from the workspace path
        string currentPath = workspacePath;
        
        // If it's a file, start from its directory
        if (File.Exists(workspacePath))
        {
            currentPath = Path.GetDirectoryName(workspacePath) ?? workspacePath;
        }
        
        // If the path doesn't exist, return the original workspace path
        if (!System.IO.Directory.Exists(currentPath))
        {
            _logger.LogWarning("Path does not exist: {Path}, using as-is", currentPath);
            return workspacePath;
        }
        
        // Walk up the directory tree looking for project root indicators
        while (!string.IsNullOrEmpty(currentPath))
        {
            // Check for .git directory (most reliable indicator)
            if (System.IO.Directory.Exists(Path.Combine(currentPath, ".git")))
            {
                _logger.LogDebug("Found project root at {Path} (.git directory)", currentPath);
                return currentPath;
            }
            
            // Check for solution files
            var slnFiles = System.IO.Directory.GetFiles(currentPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                _logger.LogDebug("Found project root at {Path} (solution file)", currentPath);
                return currentPath;
            }
            
            // Check for common project root files
            if (File.Exists(Path.Combine(currentPath, ".gitignore")) ||
                File.Exists(Path.Combine(currentPath, "package.json")) ||
                File.Exists(Path.Combine(currentPath, "Directory.Build.props")) ||
                File.Exists(Path.Combine(currentPath, "global.json")))
            {
                _logger.LogDebug("Found project root at {Path} (project root file)", currentPath);
                return currentPath;
            }
            
            // Move up one directory
            var parent = System.IO.Directory.GetParent(currentPath);
            if (parent == null || parent.FullName == currentPath)
            {
                break;
            }
            currentPath = parent.FullName;
        }
        
        // If we couldn't find a project root, use the original directory
        if (System.IO.Directory.Exists(workspacePath))
        {
            _logger.LogWarning("Could not find project root, using workspace path: {Path}", workspacePath);
            return workspacePath;
        }
        else if (File.Exists(workspacePath))
        {
            var dir = Path.GetDirectoryName(workspacePath) ?? workspacePath;
            _logger.LogWarning("Could not find project root, using file directory: {Path}", dir);
            return dir;
        }
        
        return workspacePath;
    }
    
    private void EnsureGitIgnoreEntry(string projectRoot)
    {
        try
        {
            var gitIgnorePath = Path.Combine(projectRoot, ".gitignore");
            var codesearchEntry = ".codesearch/";
            
            // Only update .gitignore if it exists - we shouldn't create new .gitignore files
            if (File.Exists(gitIgnorePath))
            {
                var lines = File.ReadAllLines(gitIgnorePath);
                var hasCodesearchEntry = lines.Any(line => 
                    line.Trim() == codesearchEntry || 
                    line.Trim() == ".codesearch" ||
                    line.Trim() == "/.codesearch/" ||
                    line.Trim() == "/.codesearch");
                
                if (!hasCodesearchEntry)
                {
                    // Ensure we don't add duplicate empty lines
                    var lastLine = lines.LastOrDefault();
                    var needsNewLine = !string.IsNullOrWhiteSpace(lastLine);
                    
                    using (var writer = File.AppendText(gitIgnorePath))
                    {
                        if (needsNewLine)
                        {
                            writer.WriteLine();
                        }
                        writer.WriteLine(codesearchEntry);
                    }
                    
                    _logger.LogInformation("Added .codesearch/ to existing .gitignore at {Path}", gitIgnorePath);
                }
            }
            else
            {
                // Log that we're NOT creating a .gitignore
                _logger.LogDebug(".gitignore not found at {Path}, skipping update", gitIgnorePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update .gitignore at {Path}", projectRoot);
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