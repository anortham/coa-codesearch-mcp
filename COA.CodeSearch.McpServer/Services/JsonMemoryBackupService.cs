using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Simple JSON-based backup and restore for memory indexes.
/// Replaces the complex SQLite approach with straightforward JSON serialization.
/// </summary>
public class JsonMemoryBackupService : IDisposable
{
    private readonly ILogger<JsonMemoryBackupService> _logger;
    private readonly ILuceneIndexService _luceneService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ICircuitBreakerService _circuitBreakerService;
    private readonly string _backupDirectory;
    private readonly SemaphoreSlim _backupLock = new(1, 1);
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
    
    public JsonMemoryBackupService(
        ILogger<JsonMemoryBackupService> logger,
        ILuceneIndexService luceneService,
        IPathResolutionService pathResolutionService,
        ICircuitBreakerService circuitBreakerService)
    {
        _logger = logger;
        _luceneService = luceneService;
        _pathResolutionService = pathResolutionService;
        _circuitBreakerService = circuitBreakerService;
        
        // Validate backup directory path to prevent path traversal
        var basePath = _pathResolutionService.GetBasePath();
        _backupDirectory = Path.Combine(basePath, PathConstants.BackupsDirectoryName);
        
        // Security check: Ensure backup directory is within the base path
        var normalizedBackupPath = Path.GetFullPath(_backupDirectory);
        var normalizedBasePath = Path.GetFullPath(basePath);
        
        if (!normalizedBackupPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Backup directory path '{normalizedBackupPath}' is outside the allowed base path '{normalizedBasePath}'");
        }
        
        System.IO.Directory.CreateDirectory(_backupDirectory);
    }
    
    /// <summary>
    /// Backup memories to JSON files with atomic transaction support
    /// </summary>
    public async Task<BackupResult> BackupMemoriesAsync(
        string[]? types = null, 
        bool includeLocal = false,
        CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken);
        try
        {
            var result = new BackupResult();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(_backupDirectory, $"memories_{timestamp}.json");
            var tempBackupPath = backupPath + ".tmp";
            
            // Default types if not specified
            types ??= new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight", "TechnicalDebt" };
            
            if (includeLocal)
            {
                types = types.Concat(new[] { "WorkSession", "LocalInsight" }).ToArray();
            }
            
            var allMemories = new List<MemoryBackupItem>();
            
            // Collect memories from workspaces based on what types were requested
            var workspacesToBackup = new HashSet<string>();
            
            // Determine which workspaces to backup based on types
            foreach (var type in types)
            {
                var workspace = GetWorkspaceForType(type);
                workspacesToBackup.Add(workspace);
            }
            
            // Collect ALL memories from each workspace (no type filtering)
            foreach (var workspace in workspacesToBackup)
            {
                var memories = await CollectMemoriesAsync(workspace, cancellationToken);
                allMemories.AddRange(memories);
                _logger.LogInformation("Collected {Count} memories from workspace {Workspace}", memories.Count, workspace);
            }
            
            // Write to temporary file first (atomic transaction)
            var backup = new MemoryBackup
            {
                Version = "1.0",
                BackupTime = DateTime.UtcNow,
                TotalMemories = allMemories.Count,
                Memories = allMemories
            };
            
            var json = JsonSerializer.Serialize(backup, _jsonOptions);
            
            // Write backup file with circuit breaker protection
            await _circuitBreakerService.ExecuteAsync("BackupFileWrite", async () =>
            {
                await File.WriteAllTextAsync(tempBackupPath, json, cancellationToken);
            }, cancellationToken);
            
            // Verify the backup was written correctly
            await VerifyBackupIntegrityAsync(tempBackupPath, allMemories.Count);
            
            // Atomic move to final location with circuit breaker protection
            await _circuitBreakerService.ExecuteAsync("BackupFileMove", async () =>
            {
                await Task.Run(() => File.Move(tempBackupPath, backupPath), cancellationToken);
            }, cancellationToken);
            
            result.Success = true;
            result.DocumentsBackedUp = allMemories.Count;
            result.BackupPath = backupPath;
            result.BackupTime = DateTime.UtcNow;
            
            _logger.LogInformation("Backed up {Count} memories to {Path}", allMemories.Count, backupPath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup memories");
            
            // Clean up temp file on error
            var tempPath = Path.Combine(_backupDirectory, $"memories_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json.tmp");
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Best effort cleanup */ }
            }
            
            return new BackupResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _backupLock.Release();
        }
    }
    
    /// <summary>
    /// Restore memories from JSON file with atomic transaction support
    /// </summary>
    public async Task<RestoreResult> RestoreMemoriesAsync(
        string? backupFile = null,
        string[]? types = null,
        bool includeLocal = false,
        CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken);
        var snapshotTrackers = new List<WorkspaceSnapshotTracker>();
        
        try
        {
            var result = new RestoreResult();
            
            // If no file specified, use the most recent backup
            if (string.IsNullOrEmpty(backupFile))
            {
                var files = System.IO.Directory.GetFiles(_backupDirectory, "memories_*.json")
                    .OrderByDescending(f => f)
                    .ToArray();
                    
                if (files.Length == 0)
                {
                    _logger.LogInformation("No backup files found");
                    result.Success = true;
                    return result;
                }
                
                backupFile = files[0];
            }
            
            if (!File.Exists(backupFile))
            {
                throw new FileNotFoundException($"Backup file not found: {backupFile}");
            }
            
            // Verify backup integrity before proceeding
            await VerifyBackupIntegrityAsync(backupFile);
            
            // Read and parse backup with circuit breaker protection
            var json = await _circuitBreakerService.ExecuteAsync("BackupFileRead", async () =>
            {
                return await File.ReadAllTextAsync(backupFile, cancellationToken);
            }, cancellationToken);
            var backup = JsonSerializer.Deserialize<MemoryBackup>(json, _jsonOptions);
            
            if (backup?.Memories == null)
            {
                throw new InvalidOperationException("Invalid backup file format");
            }
            
            _logger.LogInformation("Restoring from backup with {Count} memories", backup.Memories.Count);
            
            // Determine which memories to restore based on workspace
            var memoriesToRestore = backup.Memories.ToList();
            
            // If types are specified, filter by them
            if (types != null && types.Length > 0)
            {
                memoriesToRestore = memoriesToRestore.Where(m => types.Contains(m.Type)).ToList();
            }
            else
            {
                // No types specified - restore based on includeLocal flag
                if (!includeLocal)
                {
                    // Exclude local types by checking workspace
                    var localWorkspace = _pathResolutionService.GetLocalMemoryPath();
                    memoriesToRestore = memoriesToRestore.Where(m => 
                        GetWorkspaceForType(m.Type) != localWorkspace
                    ).ToList();
                }
                // If includeLocal is true, restore everything
            }
            
            // Group by workspace and create snapshots for rollback
            var groupedMemories = memoriesToRestore.GroupBy(m => GetWorkspaceForType(m.Type));
            
            foreach (var group in groupedMemories)
            {
                var workspace = group.Key;
                var snapshotTracker = await CreateWorkspaceSnapshotAsync(workspace, group.Select(m => m.Id).ToList(), cancellationToken);
                snapshotTrackers.Add(snapshotTracker);
            }
            
            // Perform the actual restore
            foreach (var group in groupedMemories)
            {
                var workspace = group.Key;
                var writer = await _luceneService.GetIndexWriterAsync(workspace, cancellationToken);
                
                foreach (var memory in group)
                {
                    // Check if memory already exists
                    var existingQuery = new TermQuery(new Term("id", memory.Id));
                    writer.DeleteDocuments(existingQuery);
                    
                    // Create Lucene document
                    var doc = CreateDocument(memory);
                    writer.AddDocument(doc);
                    result.DocumentsRestored++;
                }
                
                await _luceneService.CommitAsync(workspace, cancellationToken);
                _logger.LogInformation("Restored {Count} memories to workspace {Workspace}", 
                    group.Count(), workspace);
            }
            
            // Clean up snapshots on success
            foreach (var tracker in snapshotTrackers)
            {
                tracker.CleanupSnapshot();
            }
            
            result.Success = true;
            result.RestoreTime = DateTime.UtcNow;
            
            _logger.LogInformation("Restored {Count} memories from {Path}", 
                result.DocumentsRestored, backupFile);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore memories, rolling back changes");
            
            // Rollback all changes
            foreach (var tracker in snapshotTrackers)
            {
                await tracker.RollbackAsync(_luceneService, cancellationToken);
            }
            
            return new RestoreResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _backupLock.Release();
        }
    }
    
    /// <summary>
    /// List available backup files
    /// </summary>
    public Task<List<BackupFileInfo>> ListBackupsAsync()
    {
        var files = System.IO.Directory.GetFiles(_backupDirectory, "memories_*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupFileInfo
            {
                FileName = f.Name,
                FilePath = f.FullName,
                Size = f.Length,
                Created = f.CreationTimeUtc,
                MemoryCount = GetMemoryCount(f.FullName)
            })
            .ToList();
            
        return Task.FromResult(files);
    }
    
    private async Task<List<MemoryBackupItem>> CollectMemoriesAsync(
        string workspace, 
        CancellationToken cancellationToken)
    {
        var memories = new List<MemoryBackupItem>();
        
        try
        {
            // Check if index exists before trying to read from it
            var indexPath = workspace;
            if (System.IO.Directory.Exists(indexPath))
            {
                using var directory = FSDirectory.Open(indexPath);
                if (!DirectoryReader.IndexExists(directory))
                {
                    _logger.LogDebug("Index does not exist at {Workspace}, skipping", workspace);
                    return memories;
                }
            }
            else
            {
                _logger.LogDebug("Index directory does not exist at {Workspace}, skipping", workspace);
                return memories;
            }
            
            var searcher = await _luceneService.GetIndexSearcherAsync(workspace, cancellationToken);
            var query = new MatchAllDocsQuery(); // Get ALL memories, not filtered by type
            var collector = TopScoreDocCollector.Create(10000, true);
            searcher.Search(query, collector);
            
            var hits = collector.GetTopDocs().ScoreDocs;
            
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var memory = DocumentToMemory(doc);
                memories.Add(memory);
            }
            
            _logger.LogDebug("Successfully collected {Count} memories from {Workspace}", 
                memories.Count, workspace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect memories from {Workspace}", 
                workspace);
        }
        
        return memories;
    }
    
    private MemoryBackupItem DocumentToMemory(Document doc)
    {
        var memory = new MemoryBackupItem
        {
            Id = doc.Get("id") ?? Guid.NewGuid().ToString(),
            Type = doc.Get("type") ?? "Unknown",
            Content = doc.Get("content") ?? "",
            Created = ParseDateTime(doc.Get("created")),
            Modified = ParseDateTime(doc.Get("modified")),
            IsShared = bool.Parse(doc.Get("is_shared") ?? "false"),
            SessionId = doc.Get("session_id"),
            AccessCount = int.Parse(doc.Get("access_count") ?? "0"),
            Fields = new Dictionary<string, object>()
        };
        
        // Collect files
        var files = doc.GetValues("file");
        if (files?.Length > 0)
        {
            memory.Files = files.ToList();
        }
        
        // Collect custom fields
        foreach (var field in doc.Fields)
        {
            var name = field.Name;
            
            // Skip standard fields
            if (name is "id" or "type" or "content" or "created" or "modified" 
                or "is_shared" or "session_id" or "access_count" or "file"
                or "timestamp_ticks" or "content_vector")
                continue;
                
            memory.Fields[name] = field.GetStringValue() ?? field.ToString() ?? "";
        }
        
        return memory;
    }
    
    private Document CreateDocument(MemoryBackupItem memory)
    {
        var doc = new Document();
        
        // Standard fields
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        // Add raw field for precise searching (not analyzed)
        doc.Add(new StringField("content.raw", memory.Content, Field.Store.NO));
        doc.Add(new StringField("created", memory.Created.Ticks.ToString(), Field.Store.YES));
        doc.Add(new StringField("modified", memory.Modified.Ticks.ToString(), Field.Store.YES));
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.YES));
        
        // Numeric fields for range queries
        var dateFieldType = new FieldType { IsIndexed = true, IsStored = false, NumericType = NumericType.INT64 };
        doc.Add(new Int64Field("created", memory.Created.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("created", memory.Created.Ticks));
        doc.Add(new Int64Field("modified", memory.Modified.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("modified", memory.Modified.Ticks));
        doc.Add(new Int64Field("timestamp_ticks", memory.Modified.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("timestamp_ticks", memory.Modified.Ticks));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        // Files
        if (memory.Files != null)
        {
            foreach (var file in memory.Files)
            {
                doc.Add(new StringField("file", file, Field.Store.YES));
            }
        }
        
        // Custom fields
        if (memory.Fields != null)
        {
            foreach (var (key, value) in memory.Fields)
            {
                var stringValue = value?.ToString() ?? "";
                doc.Add(new TextField(key, stringValue, Field.Store.YES));
            }
        }
        
        return doc;
    }
    
    private DateTime ParseDateTime(string? ticksStr)
    {
        if (string.IsNullOrEmpty(ticksStr) || !long.TryParse(ticksStr, out var ticks))
        {
            return DateTime.UtcNow;
        }
        return new DateTime(ticks, DateTimeKind.Utc);
    }
    
    private string GetWorkspaceForType(string type)
    {
        // Local-only types (developer-specific, not shared)
        var isLocalType = type is "WorkSession" 
                               or "LocalInsight"
                               or "SearchContext"
                               or "WorkingMemory";
        
        // Everything else goes to project memory (shared with team)
        return isLocalType
            ? _pathResolutionService.GetLocalMemoryPath()
            : _pathResolutionService.GetProjectMemoryPath();
    }
    
    private int GetMemoryCount(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("totalMemories").GetInt32();
        }
        catch
        {
            return -1;
        }
    }
    
    /// <summary>
    /// Verify backup file integrity
    /// </summary>
    private async Task VerifyBackupIntegrityAsync(string backupPath, int? expectedCount = null)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: {backupPath}");
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(backupPath);
            var backup = JsonSerializer.Deserialize<MemoryBackup>(json, _jsonOptions);
            
            if (backup?.Memories == null)
            {
                throw new InvalidOperationException("Backup file contains no memories data");
            }
            
            if (backup.TotalMemories != backup.Memories.Count)
            {
                throw new InvalidOperationException($"Backup integrity check failed: totalMemories ({backup.TotalMemories}) doesn't match actual count ({backup.Memories.Count})");
            }
            
            if (expectedCount.HasValue && backup.TotalMemories != expectedCount.Value)
            {
                throw new InvalidOperationException($"Backup integrity check failed: expected {expectedCount.Value} memories, found {backup.TotalMemories}");
            }
            
            // Verify each memory has required fields
            foreach (var memory in backup.Memories)
            {
                if (string.IsNullOrEmpty(memory.Id))
                {
                    throw new InvalidOperationException("Backup contains memory with empty ID");
                }
                
                if (string.IsNullOrEmpty(memory.Type))
                {
                    throw new InvalidOperationException($"Backup contains memory {memory.Id} with empty Type");
                }
            }
            
            _logger.LogDebug("Backup integrity verified: {Count} memories in {Path}", backup.TotalMemories, backupPath);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Backup file is not valid JSON: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Create a snapshot of workspace state for rollback capability
    /// </summary>
    private async Task<WorkspaceSnapshotTracker> CreateWorkspaceSnapshotAsync(
        string workspace, 
        List<string> memoryIds, 
        CancellationToken cancellationToken)
    {
        var tracker = new WorkspaceSnapshotTracker(workspace, _logger);
        
        try
        {
            var searcher = await _luceneService.GetIndexSearcherAsync(workspace, cancellationToken);
            
            // Snapshot existing memories that will be affected
            foreach (var memoryId in memoryIds)
            {
                var query = new TermQuery(new Term("id", memoryId));
                var collector = TopScoreDocCollector.Create(1, true);
                searcher.Search(query, collector);
                
                var hits = collector.GetTopDocs().ScoreDocs;
                if (hits.Length > 0)
                {
                    var doc = searcher.Doc(hits[0].Doc);
                    var memory = DocumentToMemory(doc);
                    tracker.AddOriginalMemory(memory);
                }
            }
            
            _logger.LogDebug("Created snapshot for workspace {Workspace} with {Count} existing memories", 
                workspace, tracker.OriginalMemories.Count);
            
            return tracker;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace snapshot for {Workspace}", workspace);
            throw;
        }
    }
    
    public void Dispose()
    {
        _backupLock?.Dispose();
    }
}

// Data models
public class MemoryBackup
{
    public string Version { get; set; } = "1.0";
    public DateTime BackupTime { get; set; }
    public int TotalMemories { get; set; }
    public List<MemoryBackupItem> Memories { get; set; } = new();
}

public class MemoryBackupItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public bool IsShared { get; set; }
    public string? SessionId { get; set; }
    public int AccessCount { get; set; }
    public List<string>? Files { get; set; }
    public Dictionary<string, object>? Fields { get; set; }
}

public class BackupFileInfo
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long Size { get; set; }
    public DateTime Created { get; set; }
    public int MemoryCount { get; set; }
}

public class BackupResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int DocumentsBackedUp { get; set; }
    public string BackupPath { get; set; } = "";
    public DateTime BackupTime { get; set; }
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int DocumentsRestored { get; set; }
    public DateTime RestoreTime { get; set; }
}

/// <summary>
/// Tracks workspace state for atomic rollback capability
/// </summary>
internal class WorkspaceSnapshotTracker
{
    public string Workspace { get; }
    public List<MemoryBackupItem> OriginalMemories { get; } = new();
    private readonly ILogger _logger;
    private readonly string? _snapshotPath;
    
    public WorkspaceSnapshotTracker(string workspace, ILogger logger)
    {
        Workspace = workspace;
        _logger = logger;
        
        // Create a temporary snapshot file for very large workspaces
        _snapshotPath = Path.Combine(Path.GetTempPath(), $"codesearch_snapshot_{Guid.NewGuid():N}.json");
    }
    
    public void AddOriginalMemory(MemoryBackupItem memory)
    {
        OriginalMemories.Add(memory);
    }
    
    /// <summary>
    /// Rollback changes by restoring original memories
    /// </summary>
    public async Task RollbackAsync(ILuceneIndexService luceneService, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Rolling back {Count} memories in workspace {Workspace}", OriginalMemories.Count, Workspace);
            
            var writer = await luceneService.GetIndexWriterAsync(Workspace, cancellationToken);
            
            // Remove all memories that were restored
            foreach (var memory in OriginalMemories)
            {
                var deleteQuery = new TermQuery(new Term("id", memory.Id));
                writer.DeleteDocuments(deleteQuery);
            }
            
            // Restore original memories
            foreach (var memory in OriginalMemories)
            {
                var doc = CreateDocumentFromBackupItem(memory);
                writer.AddDocument(doc);
            }
            
            await luceneService.CommitAsync(Workspace, cancellationToken);
            _logger.LogInformation("Successfully rolled back {Count} memories in workspace {Workspace}", 
                OriginalMemories.Count, Workspace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback workspace {Workspace}", Workspace);
            throw;
        }
        finally
        {
            CleanupSnapshot();
        }
    }
    
    /// <summary>
    /// Clean up temporary snapshot resources
    /// </summary>
    public void CleanupSnapshot()
    {
        if (!string.IsNullOrEmpty(_snapshotPath) && File.Exists(_snapshotPath))
        {
            try
            {
                File.Delete(_snapshotPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup snapshot file {Path}", _snapshotPath);
            }
        }
    }
    
    private Document CreateDocumentFromBackupItem(MemoryBackupItem memory)
    {
        var doc = new Document();
        
        // Standard fields
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        // Add raw field for precise searching (not analyzed)
        doc.Add(new StringField("content.raw", memory.Content, Field.Store.NO));
        doc.Add(new StringField("created", memory.Created.Ticks.ToString(), Field.Store.YES));
        doc.Add(new StringField("modified", memory.Modified.Ticks.ToString(), Field.Store.YES));
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.YES));
        
        // Numeric fields for range queries
        var dateFieldType = new FieldType { IsIndexed = true, IsStored = false, NumericType = NumericType.INT64 };
        doc.Add(new Int64Field("created", memory.Created.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("created", memory.Created.Ticks));
        doc.Add(new Int64Field("modified", memory.Modified.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("modified", memory.Modified.Ticks));
        doc.Add(new Int64Field("timestamp_ticks", memory.Modified.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("timestamp_ticks", memory.Modified.Ticks));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        // Files
        if (memory.Files != null)
        {
            foreach (var file in memory.Files)
            {
                doc.Add(new StringField("file", file, Field.Store.YES));
            }
        }
        
        // Custom fields
        if (memory.Fields != null)
        {
            foreach (var (key, value) in memory.Fields)
            {
                var stringValue = value?.ToString() ?? "";
                doc.Add(new TextField(key, stringValue, Field.Store.YES));
            }
        }
        
        return doc;
    }
}