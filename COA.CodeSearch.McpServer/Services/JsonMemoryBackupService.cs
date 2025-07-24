using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
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
        IPathResolutionService pathResolutionService)
    {
        _logger = logger;
        _luceneService = luceneService;
        _pathResolutionService = pathResolutionService;
        
        _backupDirectory = Path.Combine(_pathResolutionService.GetBasePath(), PathConstants.BackupsDirectoryName);
        Directory.CreateDirectory(_backupDirectory);
    }
    
    /// <summary>
    /// Backup memories to JSON files
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
            
            // Default types if not specified
            types ??= new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            
            if (includeLocal)
            {
                types = types.Concat(new[] { "WorkSession", "LocalInsight" }).ToArray();
            }
            
            var allMemories = new List<MemoryBackupItem>();
            
            // Collect memories from each workspace
            foreach (var type in types)
            {
                var workspace = GetWorkspaceForType(type);
                var memories = await CollectMemoriesAsync(workspace, type, cancellationToken);
                allMemories.AddRange(memories);
                _logger.LogInformation("Collected {Count} memories of type {Type}", memories.Count, type);
            }
            
            // Write to JSON file
            var backup = new MemoryBackup
            {
                Version = "1.0",
                BackupTime = DateTime.UtcNow,
                TotalMemories = allMemories.Count,
                Memories = allMemories
            };
            
            var json = JsonSerializer.Serialize(backup, _jsonOptions);
            await File.WriteAllTextAsync(backupPath, json, cancellationToken);
            
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
            return new BackupResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _backupLock.Release();
        }
    }
    
    /// <summary>
    /// Restore memories from JSON file
    /// </summary>
    public async Task<RestoreResult> RestoreMemoriesAsync(
        string? backupFile = null,
        string[]? types = null,
        bool includeLocal = false,
        CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken);
        try
        {
            var result = new RestoreResult();
            
            // If no file specified, use the most recent backup
            if (string.IsNullOrEmpty(backupFile))
            {
                var files = Directory.GetFiles(_backupDirectory, "memories_*.json")
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
            
            // Read and parse backup
            var json = await File.ReadAllTextAsync(backupFile, cancellationToken);
            var backup = JsonSerializer.Deserialize<MemoryBackup>(json, _jsonOptions);
            
            if (backup?.Memories == null)
            {
                throw new InvalidOperationException("Invalid backup file format");
            }
            
            _logger.LogInformation("Restoring from backup with {Count} memories", backup.Memories.Count);
            
            // Default types if not specified
            types ??= new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            
            if (includeLocal)
            {
                types = types.Concat(new[] { "WorkSession", "LocalInsight" }).ToArray();
            }
            
            // Filter memories by type
            var memoriesToRestore = backup.Memories
                .Where(m => types.Contains(m.Type))
                .ToList();
            
            // Group by workspace and restore
            var groupedMemories = memoriesToRestore.GroupBy(m => GetWorkspaceForType(m.Type));
            
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
            
            result.Success = true;
            result.RestoreTime = DateTime.UtcNow;
            
            _logger.LogInformation("Restored {Count} memories from {Path}", 
                result.DocumentsRestored, backupFile);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore memories");
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
        var files = Directory.GetFiles(_backupDirectory, "memories_*.json")
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
        string type,
        CancellationToken cancellationToken)
    {
        var memories = new List<MemoryBackupItem>();
        
        try
        {
            var searcher = await _luceneService.GetIndexSearcherAsync(workspace, cancellationToken);
            var query = new TermQuery(new Term("type", type));
            var collector = TopScoreDocCollector.Create(10000, true);
            searcher.Search(query, collector);
            
            var hits = collector.GetTopDocs().ScoreDocs;
            
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var memory = DocumentToMemory(doc);
                memories.Add(memory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect memories of type {Type} from {Workspace}", 
                type, workspace);
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
        // Project-level types (shared with team)
        var isProjectType = type is "ArchitecturalDecision" 
                                 or "CodePattern" 
                                 or "SecurityRule" 
                                 or "ProjectInsight";
        
        return isProjectType
            ? _pathResolutionService.GetProjectMemoryPath()
            : _pathResolutionService.GetLocalMemoryPath();
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

// Using BackupResult and RestoreResult from MemoryBackupService