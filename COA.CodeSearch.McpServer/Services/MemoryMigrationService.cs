using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service to migrate existing memories to the new flexible schema
/// </summary>
public class MemoryMigrationService
{
    private readonly ILogger<MemoryMigrationService> _logger;
    private readonly ClaudeMemoryService _oldMemoryService;
    private readonly IPathResolutionService _pathResolution;
    private readonly string _basePath;

    public MemoryMigrationService(
        ILogger<MemoryMigrationService> logger,
        ClaudeMemoryService oldMemoryService,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _oldMemoryService = oldMemoryService;
        _pathResolution = pathResolution;
        _basePath = _pathResolution.GetBasePath();
    }

    /// <summary>
    /// Migrate all existing memories to the new flexible format
    /// </summary>
    public async Task<MigrationResult> MigrateAllMemoriesAsync()
    {
        var result = new MigrationResult();
        
        try
        {
            _logger.LogInformation("Starting memory migration to flexible schema");
            
            // First, backup existing memories to ensure we don't lose anything
            var backupPath = await CreateBackupAsync();
            result.BackupPath = backupPath;
            _logger.LogInformation($"Created backup at: {backupPath}");
            
            // Get all existing memories
            var allMemories = await GetAllExistingMemoriesAsync();
            result.TotalMemories = allMemories.Count;
            _logger.LogInformation($"Found {allMemories.Count} memories to migrate");
            
            // Convert each memory to the new format
            var flexibleMemories = new List<FlexibleMemoryEntry>();
            foreach (var oldMemory in allMemories)
            {
                try
                {
                    var flexibleMemory = ConvertToFlexibleMemory(oldMemory);
                    flexibleMemories.Add(flexibleMemory);
                    result.SuccessfulMigrations++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to migrate memory {oldMemory.Id}");
                    result.FailedMigrations++;
                    result.Errors.Add($"Memory {oldMemory.Id}: {ex.Message}");
                }
            }
            
            // Store migrated memories in new index
            await StoreFlexibleMemoriesAsync(flexibleMemories);
            
            _logger.LogInformation($"Migration complete. Success: {result.SuccessfulMigrations}, Failed: {result.FailedMigrations}");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory migration failed");
            result.Errors.Add($"Migration failed: {ex.Message}");
            return result;
        }
    }
    
    /// <summary>
    /// Create a backup of existing memories
    /// </summary>
    private async Task<string> CreateBackupAsync()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupDir = _pathResolution.GetBackupPath($"pre_migration_{timestamp}");
        
        // Copy existing index directories
        var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
        var localMemoryPath = _pathResolution.GetLocalMemoryPath();
        
        if (System.IO.Directory.Exists(projectMemoryPath))
        {
            CopyDirectory(projectMemoryPath, Path.Combine(backupDir, "project-memory"));
        }
        
        if (System.IO.Directory.Exists(localMemoryPath))
        {
            CopyDirectory(localMemoryPath, Path.Combine(backupDir, "local-memory"));
        }
        
        // Also create a JSON backup for safety
        var allMemories = await GetAllExistingMemoriesAsync();
        var jsonBackup = JsonSerializer.Serialize(allMemories, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(backupDir, "memories.json"), jsonBackup);
        
        return backupDir;
    }
    
    /// <summary>
    /// Get all existing memories from both project and local storage
    /// </summary>
    private async Task<List<MemoryEntry>> GetAllExistingMemoriesAsync()
    {
        var allMemories = new List<MemoryEntry>();
        
        // Get memories from all scopes
        foreach (MemoryScope scope in Enum.GetValues<MemoryScope>())
        {
            try
            {
                var memories = await _oldMemoryService.SearchMemoriesAsync("*", scope);
                allMemories.AddRange(memories.Memories);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not retrieve memories for scope {scope}");
            }
        }
        
        return allMemories;
    }
    
    /// <summary>
    /// Convert old memory format to new flexible format
    /// </summary>
    private FlexibleMemoryEntry ConvertToFlexibleMemory(MemoryEntry oldMemory)
    {
        var flexibleMemory = new FlexibleMemoryEntry
        {
            Id = oldMemory.Id,
            Type = oldMemory.Scope.ToString(),
            Content = oldMemory.Content,
            Created = oldMemory.Timestamp,
            Modified = oldMemory.Timestamp,
            FilesInvolved = oldMemory.FilesInvolved,
            SessionId = oldMemory.SessionId,
            IsShared = IsSharedScope(oldMemory.Scope)
        };
        
        // Migrate existing fields to the new extended fields
        if (!string.IsNullOrEmpty(oldMemory.Category))
        {
            flexibleMemory.SetField(MemoryFields.Category, oldMemory.Category);
        }
        
        if (!string.IsNullOrEmpty(oldMemory.Reasoning))
        {
            flexibleMemory.SetField(MemoryFields.Reasoning, oldMemory.Reasoning);
        }
        
        if (oldMemory.Tags.Length > 0)
        {
            flexibleMemory.SetField(MemoryFields.Tags, oldMemory.Tags);
        }
        
        if (oldMemory.Confidence != 100)
        {
            flexibleMemory.SetField(MemoryFields.Confidence, oldMemory.Confidence);
        }
        
        // Add default status based on memory type
        switch (oldMemory.Scope)
        {
            case MemoryScope.TemporaryNote:
                flexibleMemory.SetField(MemoryFields.Status, MemoryStatus.Pending);
                flexibleMemory.SetField(MemoryFields.Priority, MemoryPriority.Low);
                break;
            case MemoryScope.WorkSession:
                flexibleMemory.SetField(MemoryFields.Status, MemoryStatus.Done);
                break;
            case MemoryScope.ArchitecturalDecision:
            case MemoryScope.SecurityRule:
                flexibleMemory.SetField(MemoryFields.Status, MemoryStatus.Approved);
                flexibleMemory.SetField(MemoryFields.Priority, MemoryPriority.High);
                break;
        }
        
        // Extract keywords for better searchability
        if (oldMemory.Keywords.Length > 0)
        {
            // Store keywords as tags if not already present
            var existingTags = flexibleMemory.Tags ?? Array.Empty<string>();
            var allTags = existingTags.Union(oldMemory.Keywords).Distinct().ToArray();
            flexibleMemory.SetField(MemoryFields.Tags, allTags);
        }
        
        return flexibleMemory;
    }
    
    /// <summary>
    /// Determine if a scope should be shared (version controlled)
    /// </summary>
    private bool IsSharedScope(MemoryScope scope)
    {
        return scope switch
        {
            MemoryScope.ArchitecturalDecision => true,
            MemoryScope.CodePattern => true,
            MemoryScope.SecurityRule => true,
            MemoryScope.ProjectInsight => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Store flexible memories in the new index structure
    /// </summary>
    private async Task StoreFlexibleMemoriesAsync(List<FlexibleMemoryEntry> memories)
    {
        // Group by shared vs local
        var sharedMemories = memories.Where(m => m.IsShared).ToList();
        var localMemories = memories.Where(m => !m.IsShared).ToList();
        
        // Store in appropriate indexes using PathResolutionService
        if (sharedMemories.Any())
        {
            await StoreMemoriesToIndexAsync(sharedMemories, _pathResolution.GetProjectMemoryPath());
        }
        
        if (localMemories.Any())
        {
            await StoreMemoriesToIndexAsync(localMemories, _pathResolution.GetLocalMemoryPath());
        }
    }
    
    /// <summary>
    /// Store memories to a specific Lucene index
    /// </summary>
    private Task StoreMemoriesToIndexAsync(List<FlexibleMemoryEntry> memories, string indexPath)
    {
        
        using var directory = FSDirectory.Open(indexPath);
        var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
        var config = new IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer);
        
        using var writer = new IndexWriter(directory, config);
        
        foreach (var memory in memories)
        {
            var doc = CreateFlexibleDocument(memory);
            writer.AddDocument(doc);
        }
        
        writer.Commit();
        writer.Flush(true, true);
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Create a Lucene document from a flexible memory entry
    /// </summary>
    private Document CreateFlexibleDocument(FlexibleMemoryEntry memory)
    {
        var doc = new Document();
        
        // Core fields (stored and indexed)
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        doc.Add(new Int64Field("created", memory.Created.Ticks, Field.Store.YES));
        doc.Add(new Int64Field("modified", memory.Modified.Ticks, Field.Store.YES));
        doc.Add(new Int64Field("timestamp_ticks", memory.TimestampTicks, Field.Store.YES));
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.YES));
        
        // Session ID
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        // Files involved
        foreach (var file in memory.FilesInvolved)
        {
            doc.Add(new StringField("file", file, Field.Store.YES));
        }
        
        // Extended fields - store as JSON for flexibility
        if (memory.Fields.Any())
        {
            var fieldsJson = JsonSerializer.Serialize(memory.Fields);
            doc.Add(new StoredField("extended_fields", fieldsJson));
            
            // Also index specific fields for searching
            foreach (var (key, value) in memory.Fields)
            {
                var fieldName = $"field_{key}";
                
                // Try to index the field appropriately based on its type
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        doc.Add(new StringField(fieldName, value.GetString() ?? "", Field.Store.NO));
                        break;
                    case JsonValueKind.Number:
                        if (value.TryGetInt32(out var intVal))
                            doc.Add(new Int32Field(fieldName, intVal, Field.Store.NO));
                        else if (value.TryGetInt64(out var longVal))
                            doc.Add(new Int64Field(fieldName, longVal, Field.Store.NO));
                        break;
                    case JsonValueKind.Array:
                        // For arrays, index each element
                        foreach (var element in value.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String)
                            {
                                doc.Add(new StringField(fieldName, element.GetString() ?? "", Field.Store.NO));
                            }
                        }
                        break;
                }
            }
        }
        
        // Create searchable content combining all text fields
        var searchableContent = string.Join(" ", 
            memory.Content,
            memory.Type,
            string.Join(" ", memory.FilesInvolved),
            string.Join(" ", memory.Tags ?? Array.Empty<string>())
        );
        doc.Add(new TextField("_all", searchableContent, Field.Store.NO));
        
        return doc;
    }
    
    /// <summary>
    /// Copy directory recursively
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir)
    {
        System.IO.Directory.CreateDirectory(destDir); // This is OK - it's for backup subdirectories
        
        foreach (var file in System.IO.Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }
        
        foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}

/// <summary>
/// Result of memory migration operation
/// </summary>
public class MigrationResult
{
    public int TotalMemories { get; set; }
    public int SuccessfulMigrations { get; set; }
    public int FailedMigrations { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}