using COA.CodeSearch.Next.McpServer.Services.Analysis;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace COA.CodeSearch.Next.McpServer.Services.Lucene;

/// <summary>
/// Thread-safe Lucene index service with centralized architecture support
/// Manages multiple workspace indexes with proper lifecycle management
/// </summary>
public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable
{
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    private const int DEFAULT_MAX_RESULTS = 100;
    private const int LOCK_TIMEOUT_SECONDS = 30;
    
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPathResolutionService _pathResolution;
    private readonly ICircuitBreakerService _circuitBreaker;
    private readonly IMemoryPressureService _memoryPressure;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly ConcurrentDictionary<string, IndexContext> _indexes = new();
    private readonly SemaphoreSlim _globalLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private bool _disposed;
    
    // Configuration
    private readonly bool _useRamDirectory;
    private readonly TimeSpan _inactivityThreshold;
    private readonly int _maxConcurrentIndexes;
    
    public LuceneIndexService(
        ILogger<LuceneIndexService> logger,
        IConfiguration configuration,
        IPathResolutionService pathResolution,
        ICircuitBreakerService circuitBreaker,
        IMemoryPressureService memoryPressure)
    {
        _logger = logger;
        _configuration = configuration;
        _pathResolution = pathResolution;
        _circuitBreaker = circuitBreaker;
        _memoryPressure = memoryPressure;
        _codeAnalyzer = new CodeAnalyzer(LUCENE_VERSION);
        
        // Load configuration
        _useRamDirectory = configuration.GetValue("CodeSearch:Lucene:UseRamDirectory", false);
        _inactivityThreshold = TimeSpan.FromMinutes(configuration.GetValue("CodeSearch:Lucene:InactivityThresholdMinutes", 30));
        _maxConcurrentIndexes = configuration.GetValue("CodeSearch:Lucene:MaxConcurrentIndexes", 10);
        
        // Start cleanup timer for inactive indexes
        _cleanupTimer = new Timer(CleanupInactiveIndexes, null, _inactivityThreshold, _inactivityThreshold);
        
        _logger.LogInformation("LuceneIndexService initialized - UseRam: {UseRam}, MaxIndexes: {MaxIndexes}", 
            _useRamDirectory, _maxConcurrentIndexes);
    }
    
    public async Task<IndexInitResult> InitializeIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        
        // Check if already initialized
        if (_indexes.ContainsKey(workspaceHash))
        {
            var existingContext = _indexes[workspaceHash];
            return new IndexInitResult
            {
                Success = true,
                WorkspaceHash = workspaceHash,
                IndexPath = existingContext.IndexPath,
                IsNewIndex = false,
                ExistingDocumentCount = await GetDocumentCountAsync(workspacePath, cancellationToken)
            };
        }
        
        // Ensure we don't exceed max concurrent indexes
        await EnforceMaxIndexesAsync(cancellationToken);
        
        try
        {
            return await _circuitBreaker.ExecuteAsync($"init-index-{workspaceHash}", () =>
            {
                var indexPath = _pathResolution.GetIndexPath(workspacePath);
                var isNewIndex = !System.IO.Directory.Exists(indexPath) || !System.IO.Directory.GetFiles(indexPath).Any();
                
                // Create directory
                var directory = _useRamDirectory
                    ? new RAMDirectory() as global::Lucene.Net.Store.Directory
                    : FSDirectory.Open(indexPath);
                
                // Create context
                var context = new IndexContext(workspacePath, workspaceHash, indexPath, directory);
                
                // Initialize writer
                var config = new IndexWriterConfig(LUCENE_VERSION, _codeAnalyzer)
                {
                    OpenMode = OpenMode.CREATE_OR_APPEND,
                    RAMBufferSizeMB = 16.0,
                    MaxBufferedDocs = 1000
                };
                
                context.Writer = new IndexWriter(directory, config);
                
                // Add to dictionary
                if (!_indexes.TryAdd(workspaceHash, context))
                {
                    context.Dispose();
                    throw new InvalidOperationException($"Failed to add index context for workspace {workspaceHash}");
                }
                
                var docCount = context.Writer.NumDocs;
                
                _logger.LogInformation("Initialized index for workspace {Hash} - New: {IsNew}, Docs: {DocCount}", 
                    workspaceHash, isNewIndex, docCount);
                
                return Task.FromResult(new IndexInitResult
                {
                    Success = true,
                    WorkspaceHash = workspaceHash,
                    IndexPath = indexPath,
                    IsNewIndex = isNewIndex,
                    ExistingDocumentCount = docCount
                });
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize index for workspace {Path}", workspacePath);
            return new IndexInitResult
            {
                Success = false,
                WorkspaceHash = workspaceHash,
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task IndexDocumentAsync(string workspacePath, Document document, CancellationToken cancellationToken = default)
    {
        await IndexDocumentsAsync(workspacePath, new[] { document }, cancellationToken);
    }
    
    public async Task IndexDocumentsAsync(string workspacePath, IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            if (context.Writer == null)
            {
                throw new InvalidOperationException($"No writer available for workspace {workspacePath}");
            }
            
            foreach (var doc in documents)
            {
                // Check for file path to enable updates
                var pathField = doc.GetField("path");
                if (pathField != null)
                {
                    // Update existing or add new
                    context.Writer.UpdateDocument(new Term("path", pathField.GetStringValue()), doc);
                }
                else
                {
                    // Just add if no path field
                    context.Writer.AddDocument(doc);
                }
                
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            // Report memory usage
            var ramUsedBytes = context.Writer.MaxDoc * 1024; // Estimate based on docs
            var ramUsedMB = ramUsedBytes / (1024 * 1024);
            _memoryPressure.ReportMemoryUsage($"lucene-{context.WorkspaceHash}", ramUsedBytes);
            
            if (ramUsedMB > 50)
            {
                _logger.LogDebug("Index writer using {RamMB}MB RAM for workspace {Hash}", ramUsedMB, context.WorkspaceHash);
            }
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task DeleteDocumentAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            if (context.Writer == null)
            {
                throw new InvalidOperationException($"No writer available for workspace {workspacePath}");
            }
            
            context.Writer.DeleteDocuments(new Term("path", filePath));
            _logger.LogDebug("Deleted document {Path} from index", filePath);
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task<SearchResult> SearchAsync(string workspacePath, Query query, int maxResults = DEFAULT_MAX_RESULTS, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            if (context.Writer == null)
            {
                throw new InvalidOperationException($"No writer available for workspace {workspacePath}");
            }
            
            var reader = context.GetReader(context.Writer);
            var searcher = new IndexSearcher(reader);
            
            // Perform search
            var topDocs = searcher.Search(query, maxResults);
            
            var hits = new List<SearchHit>();
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var hit = new SearchHit
                {
                    FilePath = doc.Get("path") ?? string.Empty,
                    Score = scoreDoc.Score,
                    Content = doc.Get("content"),
                    Fields = new Dictionary<string, string>()
                };
                
                // Add all fields
                foreach (var field in doc.Fields)
                {
                    if (field.Name != "path" && field.Name != "content")
                    {
                        hit.Fields[field.Name] = field.GetStringValue() ?? string.Empty;
                    }
                }
                
                hits.Add(hit);
            }
            
            stopwatch.Stop();
            
            return new SearchResult
            {
                TotalHits = topDocs.TotalHits,
                Hits = hits,
                SearchTime = stopwatch.Elapsed,
                Query = query.ToString()
            };
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task<int> GetDocumentCountAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            return context.Writer?.NumDocs ?? 0;
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task ClearIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            if (context.Writer == null)
            {
                throw new InvalidOperationException($"No writer available for workspace {workspacePath}");
            }
            
            context.Writer.DeleteAll();
            context.Writer.Commit();
            
            _logger.LogInformation("Cleared all documents from index for workspace {Path}", workspacePath);
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task CommitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            if (context.Writer == null)
            {
                throw new InvalidOperationException($"No writer available for workspace {workspacePath}");
            }
            
            context.Writer.Commit();
            _logger.LogDebug("Committed changes to index for workspace {Path}", workspacePath);
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    public async Task<bool> IndexExistsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        
        if (_indexes.ContainsKey(workspaceHash))
        {
            return true;
        }
        
        var indexPath = _pathResolution.GetIndexPath(workspacePath);
        return await Task.FromResult(System.IO.Directory.Exists(indexPath) && 
                                     System.IO.Directory.GetFiles(indexPath).Any());
    }
    
    public async Task<IndexHealthStatus> GetHealthAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
            var indexPath = _pathResolution.GetIndexPath(workspacePath);
            
            if (!_indexes.TryGetValue(workspaceHash, out var context))
            {
                if (!System.IO.Directory.Exists(indexPath))
                {
                    return new IndexHealthStatus
                    {
                        Level = IndexHealthStatus.HealthLevel.Missing,
                        Description = "Index does not exist"
                    };
                }
                
                // Index exists on disk but not loaded
                return new IndexHealthStatus
                {
                    Level = IndexHealthStatus.HealthLevel.Healthy,
                    Description = "Index exists but not loaded"
                };
            }
            
            await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
            try
            {
                var docCount = context.Writer?.NumDocs ?? 0;
                var dirInfo = new DirectoryInfo(indexPath);
                var sizeBytes = dirInfo.Exists ? dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
                
                return new IndexHealthStatus
                {
                    Level = IndexHealthStatus.HealthLevel.Healthy,
                    Description = "Index is healthy",
                    DocumentCount = docCount,
                    IndexSizeBytes = sizeBytes,
                    LastModified = dirInfo.LastWriteTimeUtc
                };
            }
            finally
            {
                context.Lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for workspace {Path}", workspacePath);
            return new IndexHealthStatus
            {
                Level = IndexHealthStatus.HealthLevel.Unhealthy,
                Description = $"Error checking health: {ex.Message}",
                Issues = new List<string> { ex.Message }
            };
        }
    }
    
    public async Task<IndexStatistics> GetStatisticsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        var indexPath = _pathResolution.GetIndexPath(workspacePath);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            var dirInfo = new DirectoryInfo(indexPath);
            
            return new IndexStatistics
            {
                WorkspacePath = workspacePath,
                WorkspaceHash = workspaceHash,
                DocumentCount = context.Writer?.NumDocs ?? 0,
                DeletedDocumentCount = 0, // Not directly available in Lucene.NET 4.8
                IndexSizeBytes = dirInfo.Exists ? dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0,
                SegmentCount = 1, // Default for simplicity
                CreatedAt = dirInfo.CreationTimeUtc,
                LastModified = dirInfo.LastWriteTimeUtc
            };
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    private async Task<IndexContext> GetOrCreateContextAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        
        if (_indexes.TryGetValue(workspaceHash, out var context))
        {
            return context;
        }
        
        // Initialize if not exists
        var result = await InitializeIndexAsync(workspacePath, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to initialize index: {result.ErrorMessage}");
        }
        
        return _indexes[workspaceHash];
    }
    
    private async Task EnforceMaxIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexes.Count >= _maxConcurrentIndexes)
        {
            // Find least recently used index
            var lru = _indexes.Values
                .OrderBy(c => c.LastAccess)
                .FirstOrDefault();
            
            if (lru != null && _indexes.TryRemove(lru.WorkspaceHash, out var removed))
            {
                await DisposeContextAsync(removed);
                _logger.LogInformation("Evicted LRU index {Hash} to stay within limit", lru.WorkspaceHash);
            }
        }
    }
    
    private void CleanupInactiveIndexes(object? state)
    {
        try
        {
            var toRemove = _indexes
                .Where(kvp => kvp.Value.ShouldEvict(_inactivityThreshold))
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var hash in toRemove)
            {
                if (_indexes.TryRemove(hash, out var context))
                {
                    _ = DisposeContextAsync(context);
                    _logger.LogInformation("Removed inactive index {Hash}", hash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of inactive indexes");
        }
    }
    
    private async Task DisposeContextAsync(IndexContext context)
    {
        try
        {
            await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS));
            try
            {
                if (context.Writer != null)
                {
                    context.Writer.Commit();
                    context.Writer.Dispose();
                }
                
                context.Dispose();
            }
            finally
            {
                context.Lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing index context for {Hash}", context.WorkspaceHash);
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _cleanupTimer?.Dispose();
        
        // Dispose all contexts
        var disposeTasks = _indexes.Values.Select(DisposeContextAsync);
        await Task.WhenAll(disposeTasks);
        
        _indexes.Clear();
        _codeAnalyzer?.Dispose();
        
        _disposed = true;
        _logger.LogInformation("LuceneIndexService disposed");
    }
}