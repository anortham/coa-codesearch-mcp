using COA.CodeSearch.McpServer.Services.Analysis;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Index.Extensions;
using Lucene.Net.Search;
using Lucene.Net.Store;
using LockFactory = Lucene.Net.Store.LockFactory;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services.Lucene;

/// <summary>
/// Thread-safe Lucene index service with centralized architecture support
/// Manages multiple workspace indexes with proper lifecycle management
/// </summary>
public class LuceneIndexService : ILuceneIndexService, IAsyncDisposable
{
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    private const int DEFAULT_MAX_RESULTS = 100;
    private const int LOCK_TIMEOUT_SECONDS = 60; // Increased from 30 for better reliability
    
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPathResolutionService _pathResolution;
    private readonly ICircuitBreakerService _circuitBreaker;
    private readonly IMemoryPressureService _memoryPressure;
    private readonly LineAwareSearchService _lineAwareSearchService;
    private readonly SmartSnippetService _snippetService;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly IWriteLockManager _writeLockManager;
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
        IMemoryPressureService memoryPressure,
        LineAwareSearchService lineAwareSearchService,
        SmartSnippetService snippetService,
        IWriteLockManager writeLockManager,
        CodeAnalyzer codeAnalyzer)
    {
        _logger = logger;
        _configuration = configuration;
        _pathResolution = pathResolution;
        _circuitBreaker = circuitBreaker;
        _memoryPressure = memoryPressure;
        _lineAwareSearchService = lineAwareSearchService;
        _snippetService = snippetService;
        _writeLockManager = writeLockManager;
        _codeAnalyzer = codeAnalyzer;
        
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
            return await _circuitBreaker.ExecuteAsync($"init-index-{workspaceHash}", async () =>
            {
                var indexPath = _pathResolution.GetIndexPath(workspacePath);
                var isNewIndex = !System.IO.Directory.Exists(indexPath) || !System.IO.Directory.GetFiles(indexPath).Any();
                
                // Create directory with explicit SimpleFSLockFactory for cross-platform consistency
                global::Lucene.Net.Store.Directory directory;
                if (_useRamDirectory)
                {
                    directory = new RAMDirectory();
                }
                else
                {
                    // Use SimpleFSLockFactory for consistent cross-platform behavior
                    // This avoids platform-specific issues with NativeFSLockFactory
                    var lockFactory = new SimpleFSLockFactory(indexPath);
                    directory = FSDirectory.Open(indexPath, lockFactory);
                    _logger.LogDebug("Created FSDirectory with SimpleFSLockFactory for {Path}", indexPath);
                }
                
                // Create context
                var context = new IndexContext(workspacePath, workspaceHash, indexPath, directory);
                
                try
                {
                    // Initialize writer with configuration
                    var ramBuffer = _configuration.GetValue("CodeSearch:Lucene:RAMBufferSizeMB", 256.0);
                    var maxBufferedDocs = _configuration.GetValue("CodeSearch:Lucene:MaxBufferedDocs", 1000);
                    var maxThreadStates = _configuration.GetValue("CodeSearch:Lucene:MaxThreadStates", 8);
                    
                    var config = new IndexWriterConfig(LUCENE_VERSION, _codeAnalyzer)
                    {
                        OpenMode = OpenMode.CREATE_OR_APPEND,
                        RAMBufferSizeMB = ramBuffer,
                        MaxBufferedDocs = maxBufferedDocs
                    };
                    
                    // Configure merge policy
                    var mergePolicyType = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:Type", "TieredMergePolicy");
                    if (mergePolicyType == "TieredMergePolicy")
                    {
                        var mergePolicy = new TieredMergePolicy
                        {
                            MaxMergeAtOnce = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:MaxMergeAtOnce", 10),
                            SegmentsPerTier = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:SegmentsPerTier", 10.0),
                            MaxMergedSegmentMB = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:MaxMergedSegmentMB", 5120.0)
                        };
                        config.MergePolicy = mergePolicy;
                    }
                    
                    // Configure merge scheduler
                    config.MaxThreadStates = maxThreadStates;
                    
                    // Try to create IndexWriter with lock recovery
                    try
                    {
                        context.Writer = new IndexWriter(directory, config);
                    }
                    catch (global::Lucene.Net.Store.LockObtainFailedException lockEx)
                    {
                        _logger.LogWarning(lockEx, "Lock obtain failed for {WorkspacePath}, attempting recovery", workspacePath);
                        
                        // Try to force remove the lock and retry once
                        var removed = await _writeLockManager.ForceRemoveLockAsync(indexPath);
                        if (removed)
                        {
                            _logger.LogInformation("Successfully removed stale lock, retrying IndexWriter creation");
                            context.Writer = new IndexWriter(directory, config);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Could not obtain write lock for {workspacePath}", lockEx);
                        }
                    }
                    
                    // Add to dictionary
                    if (!_indexes.TryAdd(workspaceHash, context))
                    {
                        context.Dispose();
                        throw new InvalidOperationException($"Failed to add index context for workspace {workspaceHash}");
                    }
                }
                catch
                {
                    // Critical: Ensure context is disposed if IndexWriter creation fails
                    // This prevents lock files from being left orphaned
                    context.Dispose();
                    throw;
                }
                
                var docCount = context.Writer.NumDocs;
                
                _logger.LogInformation("Initialized index for workspace {Hash} - New: {IsNew}, Docs: {DocCount}", 
                    workspaceHash, isNewIndex, docCount);
                
                return new IndexInitResult
                {
                    Success = true,
                    WorkspaceHash = workspaceHash,
                    IndexPath = indexPath,
                    IsNewIndex = isNewIndex,
                    ExistingDocumentCount = docCount
                };
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
        return await SearchAsync(workspacePath, query, maxResults, false, cancellationToken);
    }

    public async Task<SearchResult> SearchAsync(string workspacePath, Query query, int maxResults, bool includeSnippets, CancellationToken cancellationToken = default)
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
            
            var searcher = context.GetSearcher(context.Writer);
            
            // Perform search
            var topDocs = searcher.Search(query, maxResults);
            
            var hits = new List<SearchHit>();
            for (int i = 0; i < topDocs.ScoreDocs.Length; i++)
            {
                var scoreDoc = topDocs.ScoreDocs[i];
                var doc = searcher.Doc(scoreDoc.Doc);
                
                var hit = new SearchHit
                {
                    FilePath = doc.Get("path") ?? string.Empty,
                    Score = scoreDoc.Score,
                    // Content included in Fields for LineSearchTool
                    Fields = new Dictionary<string, string>()
                };
                
                // Add all stored fields except path (path already set above)
                foreach (var field in doc.Fields)
                {
                    if (field.Name != "path" && field.Name != "content_tv" && field.Name != "line_breaks")
                    {
                        // For stored fields, we need to get the value differently
                        var value = field.GetStringValue();
                        if (value == null && field.Name == "type_info")
                        {
                            // StoredField might need special handling
                            value = doc.Get(field.Name);
                        }
                        hit.Fields[field.Name] = value ?? string.Empty;
                    }
                }
                
                // Parse modified date if available
                if (hit.Fields.TryGetValue("modified", out var modifiedTicks) && 
                    long.TryParse(modifiedTicks, out var ticks))
                {
                    hit.LastModified = new DateTime(ticks, DateTimeKind.Utc);
                }
                
                // NEW: Calculate line number using line-aware data (with fallback to legacy approach)
                // Extract the original query text from MultiFactorScoreQuery if wrapped
                string queryText = query.ToString();
                if (query is COA.CodeSearch.McpServer.Scoring.MultiFactorScoreQuery multiFactorQuery)
                {
                    // Extract the base query part from the wrapper format
                    // Format: "MultiFactorScore(content:term, factors=[...])"
                    var startIdx = queryText.IndexOf('(') + 1;
                    var endIdx = queryText.IndexOf(", factors=");
                    if (startIdx > 0 && endIdx > startIdx)
                    {
                        queryText = queryText.Substring(startIdx, endIdx - startIdx);
                    }
                }
                
                var lineResult = _lineAwareSearchService.GetLineNumber(doc, queryText, searcher, scoreDoc.Doc);
                hit.LineNumber = lineResult.LineNumber;
                
                // Store line context if available for snippet generation
                if (lineResult.Context != null)
                {
                    hit.ContextLines = lineResult.Context.ContextLines;
                    hit.StartLine = lineResult.Context.StartLine;
                    hit.EndLine = lineResult.Context.EndLine;
                }
                
                // Add debug info to fields
                hit.Fields["line_accurate"] = lineResult.IsAccurate.ToString().ToLowerInvariant();
                hit.Fields["line_from_cache"] = lineResult.IsFromCache.ToString().ToLowerInvariant();
                if (lineResult.IsAccurate)
                {
                    hit.Fields["precise_location"] = "true";
                }
                
                // Deserialize type_info JSON into TypeContext if available
                if (hit.Fields.TryGetValue("type_info", out var typeInfoJson) && !string.IsNullOrEmpty(typeInfoJson))
                {
                    try
                    {
                        _logger.LogDebug("Attempting to deserialize type_info for {FilePath}, JSON length: {Length}", 
                            hit.FilePath, typeInfoJson.Length);
                        
                        var typeData = JsonSerializer.Deserialize<StoredTypeInfo>(typeInfoJson);
                        if (typeData != null)
                        {
                            _logger.LogDebug("Deserialized type_info: {TypeCount} types, {MethodCount} methods, Language: {Language}", 
                                typeData.types?.Count ?? 0, typeData.methods?.Count ?? 0, typeData.language);
                            
                            hit.TypeContext = new COA.CodeSearch.McpServer.Models.TypeContext
                            {
                                Language = typeData.language,
                                NearbyTypes = typeData.types ?? new List<COA.CodeSearch.McpServer.Services.TypeExtraction.TypeInfo>(),
                                NearbyMethods = typeData.methods ?? new List<COA.CodeSearch.McpServer.Services.TypeExtraction.MethodInfo>()
                            };
                            
                            _logger.LogDebug("Created TypeContext with {TypeCount} types, {MethodCount} methods", 
                                hit.TypeContext.NearbyTypes.Count, hit.TypeContext.NearbyMethods.Count);
                            
                            // Determine containing type based on line number
                            if (hit.LineNumber > 0 && typeData.types != null)
                            {
                                // Find the type that contains this line
                                // Since we only have start line, we'll use proximity
                                var nearestType = typeData.types
                                    .Where(t => t.Line <= hit.LineNumber)
                                    .OrderBy(t => hit.LineNumber - t.Line)
                                    .FirstOrDefault();
                                    
                                if (nearestType != null)
                                {
                                    hit.TypeContext.ContainingType = $"{nearestType.Kind} {nearestType.Name}";
                                    _logger.LogDebug("Set ContainingType to: {ContainingType}", hit.TypeContext.ContainingType);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Deserialized type_info was null for {FilePath}", hit.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize type_info for {FilePath}, JSON: {Json}", 
                            hit.FilePath, typeInfoJson.Substring(0, Math.Min(500, typeInfoJson.Length)));
                    }
                }
                else
                {
                    _logger.LogDebug("No type_info field found for {FilePath}", hit.FilePath);
                }
                
                hits.Add(hit);
            }
            
            stopwatch.Stop();
            
            var searchResult = new SearchResult
            {
                TotalHits = topDocs.TotalHits,
                Hits = hits,
                SearchTime = stopwatch.Elapsed,
                Query = query.ToString()
            };

            // Generate snippets if requested (for VS Code visualization)
            if (includeSnippets)
            {
                searchResult = await _snippetService.EnhanceWithSnippetsAsync(
                    searchResult, 
                    query, 
                    searcher, 
                    forVisualization: true, 
                    cancellationToken);
            }

            return searchResult;
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

    /// <summary>
    /// Force rebuild the index with new schema - properly recreates the index structure
    /// This is the correct method to use when schema changes require a complete rebuild
    /// </summary>
    public async Task ForceRebuildIndexAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
        
        await _globalLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting force rebuild for workspace {Path}", workspacePath);
            
            // Step 1: Close and dispose existing context if it exists
            if (_indexes.TryGetValue(workspaceHash, out var existingContext))
            {
                await DisposeContextAsync(existingContext);
                _indexes.TryRemove(workspaceHash, out _);
                _logger.LogDebug("Disposed existing index context for force rebuild");
            }
            
            // Step 2: Create new IndexWriter with OpenMode.CREATE to rebuild schema
            var indexPath = _pathResolution.GetIndexPath(workspacePath);
            var directory = _useRamDirectory
                ? new RAMDirectory() as global::Lucene.Net.Store.Directory
                : FSDirectory.Open(indexPath);
            
            // Create new context
            var context = new IndexContext(workspacePath, workspaceHash, indexPath, directory);
            
            try
            {
                // Configure IndexWriter for complete rebuild
                var ramBuffer = _configuration.GetValue("CodeSearch:Lucene:RAMBufferSizeMB", 256.0);
                var maxBufferedDocs = _configuration.GetValue("CodeSearch:Lucene:MaxBufferedDocs", 1000);
                var maxThreadStates = _configuration.GetValue("CodeSearch:Lucene:MaxThreadStates", 8);
                
                var config = new IndexWriterConfig(LUCENE_VERSION, _codeAnalyzer)
                {
                    // CRITICAL: Use CREATE mode to rebuild schema completely
                    OpenMode = OpenMode.CREATE,
                    RAMBufferSizeMB = ramBuffer,
                    MaxBufferedDocs = maxBufferedDocs
                };
                
                // Configure merge policy
                var mergePolicyType = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:Type", "TieredMergePolicy");
                if (mergePolicyType == "TieredMergePolicy")
                {
                    var mergePolicy = new TieredMergePolicy
                    {
                        MaxMergeAtOnce = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:MaxMergeAtOnce", 10),
                        SegmentsPerTier = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:SegmentsPerTier", 10.0),
                        MaxMergedSegmentMB = _configuration.GetValue("CodeSearch:Lucene:MergePolicy:MaxMergedSegmentMB", 5120.0)
                    };
                    config.MergePolicy = mergePolicy;
                }
                
                config.MaxThreadStates = maxThreadStates;
                
                // Create new IndexWriter with CREATE mode and lock recovery
                try
                {
                    context.Writer = new IndexWriter(directory, config);
                }
                catch (global::Lucene.Net.Store.LockObtainFailedException lockEx)
                {
                    _logger.LogWarning(lockEx, "Lock obtain failed during force rebuild for {WorkspacePath}, attempting recovery", workspacePath);
                    
                    // Force remove the lock for rebuild
                    var removed = await _writeLockManager.ForceRemoveLockAsync(indexPath);
                    if (removed)
                    {
                        _logger.LogInformation("Successfully removed lock during force rebuild, retrying");
                        context.Writer = new IndexWriter(directory, config);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Could not obtain write lock for force rebuild of {workspacePath}", lockEx);
                    }
                }
                
                // Step 3: Register the new context
                if (!_indexes.TryAdd(workspaceHash, context))
                {
                    context.Dispose();
                    throw new InvalidOperationException($"Failed to add rebuilt index context for workspace {workspaceHash}");
                }
            }
            catch
            {
                // Critical: Ensure context is disposed if IndexWriter creation fails
                // This prevents lock files from being left orphaned
                context.Dispose();
                throw;
            }
            
            _logger.LogInformation("Force rebuild completed for workspace {Path} - new schema ready", workspacePath);
        }
        finally
        {
            _globalLock.Release();
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
            
            // CRITICAL: Invalidate the cached reader after commit to ensure NRT visibility
            context.InvalidateReader();
            
            // Optional: Force refresh immediately if we expect searches soon
            // This trades memory for latency by eagerly creating the new reader
            if (_configuration.GetValue("CodeSearch:Lucene:EagerReaderRefresh", false))
            {
                context.ForceReaderRefresh();
            }
            
            _logger.LogDebug("Committed changes to index for workspace {Path}, reader invalidated", workspacePath);
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
            var readerStats = context.GetReaderStats();
            
            return new IndexStatistics
            {
                WorkspacePath = workspacePath,
                WorkspaceHash = workspaceHash,
                DocumentCount = context.Writer?.NumDocs ?? 0,
                DeletedDocumentCount = 0, // Not directly available in Lucene.NET 4.8
                IndexSizeBytes = dirInfo.Exists ? dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0,
                SegmentCount = 1, // Default for simplicity
                CreatedAt = dirInfo.CreationTimeUtc,
                LastModified = dirInfo.LastWriteTimeUtc,
                ReaderAge = readerStats.Age,
                LastReaderUpdate = readerStats.LastUpdate
            };
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    /// <summary>
    /// Get detailed reader diagnostics for troubleshooting NRT issues
    /// </summary>
    public async Task<ReaderDiagnostics> GetReaderDiagnosticsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(workspacePath, cancellationToken);
        
        await context.Lock.WaitAsync(TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), cancellationToken);
        try
        {
            var readerStats = context.GetReaderStats();
            var writerGeneration = context.Writer?.MaxDoc ?? 0;
            
            return new ReaderDiagnostics
            {
                WorkspacePath = workspacePath,
                WorkspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath),
                HasReader = readerStats.HasReader,
                ReaderAge = readerStats.Age,
                LastReaderUpdate = readerStats.LastUpdate,
                ReaderGeneration = readerStats.Generation,
                WriterGeneration = writerGeneration,
                IsReaderStale = readerStats.Generation < writerGeneration,
                RecommendRefresh = readerStats.Age > TimeSpan.FromSeconds(30) || readerStats.Generation < writerGeneration
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
            // Use a shorter timeout for disposal to avoid hanging
            var lockAcquired = await context.Lock.WaitAsync(TimeSpan.FromSeconds(5));
            
            if (!lockAcquired)
            {
                _logger.LogWarning("Could not acquire lock for disposal of context {Hash}, forcing disposal", context.WorkspaceHash);
            }
            
            try
            {
                if (context.Writer != null)
                {
                    try
                    {
                        // Check for uncommitted changes before disposal
                        if (context.Writer.HasUncommittedChanges())
                        {
                            try
                            {
                                // Verify index directory state before commit
                                var directoryFiles = context.Writer.Directory.ListAll();
                                if (directoryFiles.Length > 0)
                                {
                                    context.Writer.Commit();
                                }
                                else
                                {
                                    _logger.LogDebug("Index directory empty during disposal for {Hash} - skipping commit", context.WorkspaceHash);
                                }
                            }
                            catch (FileNotFoundException ex) when (ex.Message.Contains(".cfs"))
                            {
                                // macOS-specific: Compound file segments cleaned up externally by aggressive file system optimization
                                _logger.LogDebug("Compound file segment missing during disposal for {Hash} (common on macOS) - continuing with disposal", context.WorkspaceHash);
                            }
                            catch (System.IO.DirectoryNotFoundException)
                            {
                                // Index directory was removed externally - safe to continue
                                _logger.LogDebug("Index directory removed externally during disposal for {Hash} - continuing", context.WorkspaceHash);
                            }
                            catch (global::Lucene.Net.Index.CorruptIndexException)
                            {
                                // Index corruption during disposal - log and continue
                                _logger.LogDebug("Index corruption detected during disposal for {Hash} - continuing with cleanup", context.WorkspaceHash);
                            }
                        }
                        context.Writer.Dispose();
                    }
                    catch (global::Lucene.Net.Store.LockObtainFailedException lockEx)
                    {
                        _logger.LogWarning(lockEx, "Lock exception during writer disposal for {Hash}", context.WorkspaceHash);
                    }
                    catch (Exception writerEx)
                    {
                        // Enhanced error logging with macOS context
                        var isMacOSFileSystemError = writerEx is FileNotFoundException && writerEx.Message.Contains(".cfs");
                        var logLevel = isMacOSFileSystemError ? LogLevel.Debug : LogLevel.Error;
                        
                        _logger.Log(logLevel, writerEx, "Error disposing writer for {Hash}{MacOSNote}", 
                            context.WorkspaceHash, 
                            isMacOSFileSystemError ? " (macOS file system race condition)" : "");
                    }
                }
            }
            finally
            {
                // Always release the lock if we acquired it
                if (lockAcquired)
                {
                    context.Lock.Release();
                }
            }
            
            // Dispose the context (which handles its own cleanup)
            context.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing index context for {Hash}", context.WorkspaceHash);
            // Force dispose even on error
            try { context.Dispose(); } catch { }
        }
    }
    
    public async Task<IndexRepairResult> RepairIndexAsync(string workspacePath, IndexRepairOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new IndexRepairOptions();
        var result = new IndexRepairResult { StartTime = DateTime.UtcNow };
        
        try
        {
            var indexPath = _pathResolution.GetIndexPath(workspacePath);
            if (!System.IO.Directory.Exists(indexPath))
            {
                result.Success = false;
                result.Message = "Index does not exist";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
            
            // Create backup if requested
            if (options.CreateBackup)
            {
                var backupPath = options.BackupPath ?? $"{indexPath}.backup_{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    DirectoryCopy(indexPath, backupPath, true);
                    result.BackupPath = backupPath;
                    _logger.LogInformation("Created backup at {BackupPath}", backupPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create backup");
                }
            }
            
            // Close existing writer if any
            var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
            if (_indexes.TryGetValue(workspaceHash, out var context))
            {
                await DisposeContextAsync(context);
                _indexes.TryRemove(workspaceHash, out _);
            }
            
            // Use CheckIndex to repair
            using var directory = FSDirectory.Open(indexPath);
            var checkIndex = new CheckIndex(directory);
            
            var status = checkIndex.DoCheckIndex();
            if (!status.Clean && options.RemoveBadSegments)
            {
                _logger.LogWarning("Index is corrupted, attempting repair");
                checkIndex.FixIndex(status);
                result.RemovedSegments = status.TotLoseDocCount > 0 ? 1 : 0;
                result.LostDocuments = (int)status.TotLoseDocCount;
            }
            
            // Validate after repair if requested
            if (options.ValidateAfterRepair)
            {
                var validationStatus = checkIndex.DoCheckIndex();
                result.Success = validationStatus.Clean;
                result.Message = validationStatus.Clean ? "Index repaired successfully" : "Index still has issues after repair";
            }
            else
            {
                result.Success = true;
                result.Message = "Repair completed";
            }
            
            result.EndTime = DateTime.UtcNow;
            _logger.LogInformation("Index repair completed in {Duration}ms - Success: {Success}", 
                result.Duration.TotalMilliseconds, result.Success);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair index for {WorkspacePath}", workspacePath);
            result.Success = false;
            result.Message = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }
    
    public async Task<bool> OptimizeIndexAsync(string workspacePath, int maxSegments = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspaceHash = _pathResolution.ComputeWorkspaceHash(workspacePath);
            
            // Ensure index is initialized
            if (!_indexes.TryGetValue(workspaceHash, out var context))
            {
                var initResult = await InitializeIndexAsync(workspacePath, cancellationToken);
                if (!initResult.Success)
                {
                    return false;
                }
                context = _indexes[workspaceHash];
            }
            
            await context.Lock.WaitAsync(cancellationToken);
            try
            {
                if (context.Writer != null)
                {
                    // Force merge to optimize
                    context.Writer.ForceMerge(maxSegments, doWait: true);
                    context.Writer.Commit();
                    
                    _logger.LogInformation("Optimized index for {WorkspacePath} to {MaxSegments} segments", 
                        workspacePath, maxSegments);
                    return true;
                }
                return false;
            }
            finally
            {
                context.Lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize index for {WorkspacePath}", workspacePath);
            return false;
        }
    }
    
    private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
    {
        var dir = new DirectoryInfo(sourceDirName);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDirName}");
        }

        var dirs = dir.GetDirectories();
        
        System.IO.Directory.CreateDirectory(destDirName);
        
        var files = dir.GetFiles();
        foreach (var file in files)
        {
            var tempPath = Path.Combine(destDirName, file.Name);
            file.CopyTo(tempPath, false);
        }

        if (copySubDirs)
        {
            foreach (var subdir in dirs)
            {
                var tempPath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
            }
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
    
    // Helper class for deserializing type_info JSON from stored format
    private class StoredTypeInfo
    {
        public List<COA.CodeSearch.McpServer.Services.TypeExtraction.TypeInfo> types { get; set; } = new();
        public List<COA.CodeSearch.McpServer.Services.TypeExtraction.MethodInfo> methods { get; set; } = new();
        public string? language { get; set; }
    }
}