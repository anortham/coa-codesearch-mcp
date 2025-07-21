using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool to migrate memories from old format to new FlexibleMemoryEntry format
/// Old format: id, scope, content, timestamp, last_modified, json_data
/// New format: id, type, content, created, modified, is_shared, access_count, timestamp_ticks, fields
/// </summary>
public class MigrateOldMemoriesTool
{
    private readonly ILogger<MigrateOldMemoriesTool> _logger;
    private readonly ILuceneIndexService _indexService;
    private readonly IPathResolutionService _pathResolution;
    private readonly FlexibleMemoryService _memoryService;
    private readonly StandardAnalyzer _analyzer;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public MigrateOldMemoriesTool(
        ILogger<MigrateOldMemoriesTool> logger,
        ILuceneIndexService indexService,
        IPathResolutionService pathResolution,
        FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _indexService = indexService;
        _pathResolution = pathResolution;
        _memoryService = memoryService;
        _analyzer = new StandardAnalyzer(LUCENE_VERSION);
    }

    public async Task<MigrationResult> MigrateOldMemoriesAsync(bool dryRun = true)
    {
        var result = new MigrationResult { DryRun = dryRun };
        
        try
        {
            // Check both project and local memory workspaces
            var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
            var localMemoryPath = _pathResolution.GetLocalMemoryPath();
            
            _logger.LogInformation("Starting memory migration (DryRun: {DryRun})", dryRun);
            _logger.LogInformation("Project memory path: {Path}", projectMemoryPath);
            _logger.LogInformation("Local memory path: {Path}", localMemoryPath);
            
            // Migrate project memories
            var projectResult = await MigrateWorkspaceAsync(projectMemoryPath, true, dryRun);
            result.ProjectMemories = projectResult;
            
            // Migrate local memories
            var localResult = await MigrateWorkspaceAsync(localMemoryPath, false, dryRun);
            result.LocalMemories = localResult;
            
            result.Success = true;
            
            _logger.LogInformation("Migration completed. Total old format: {OldCount}, Migrated: {MigratedCount}, Failed: {FailedCount}",
                result.TotalOldFormatCount, result.TotalMigratedCount, result.TotalFailedCount);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private async Task<WorkspaceMigrationResult> MigrateWorkspaceAsync(string workspace, bool isShared, bool dryRun)
    {
        var result = new WorkspaceMigrationResult { Workspace = workspace };
        
        // Only get searcher for reading - don't create writer until we need it
        var indexSearcher = await _indexService.GetIndexSearcherAsync(workspace);
        if (indexSearcher == null)
        {
            _logger.LogWarning("Could not open index for workspace {Workspace}", workspace);
            return result;
        }
        
        try
        {
            // Search for all documents
            var allDocsQuery = new MatchAllDocsQuery();
            var topDocs = indexSearcher.Search(allDocsQuery, int.MaxValue);
            
            _logger.LogInformation("Found {Count} documents in {Workspace}", topDocs.TotalHits, workspace);
            
            var migratedMemories = new List<FlexibleMemoryEntry>();
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = indexSearcher.Doc(scoreDoc.Doc);
                
                // Check if this is old format
                if (IsOldFormat(doc))
                {
                    result.OldFormatCount++;
                    
                    try
                    {
                        var migrated = ConvertToNewFormat(doc, isShared);
                        if (migrated != null)
                        {
                            migratedMemories.Add(migrated);
                            result.MigratedIds.Add(migrated.Id);
                            
                            _logger.LogDebug("Migrated memory {Id} from old format", migrated.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        var id = doc.Get("id") ?? "unknown";
                        result.FailedIds.Add(id);
                        _logger.LogError(ex, "Failed to migrate memory {Id}", id);
                    }
                }
                else
                {
                    result.NewFormatCount++;
                }
            }
            
            if (!dryRun && migratedMemories.Count > 0)
            {
                _logger.LogInformation("Storing {Count} migrated memories in {Workspace}", migratedMemories.Count, workspace);
                
                // Store all migrated memories using the memory service
                foreach (var memory in migratedMemories)
                {
                    await _memoryService.StoreMemoryAsync(memory);
                }
                
                _logger.LogInformation("Successfully stored {Count} migrated memories", migratedMemories.Count);
            }
            
            return result;
        }
        finally
        {
            // No need to release index with current ILuceneIndexService interface
        }
    }

    private bool IsOldFormat(Document doc)
    {
        // Old format has: id, scope, content, timestamp, last_modified, json_data
        // New format has: id, type, content, created, modified, is_shared, access_count, timestamp_ticks, fields
        
        // Check for old format fields
        var hasScope = doc.Get("scope") != null;
        var hasTimestamp = doc.Get("timestamp") != null;
        var hasJsonData = doc.Get("json_data") != null;
        
        // Check for new format fields
        var hasType = doc.Get("type") != null;
        var hasCreated = doc.Get("created") != null;
        var hasTimestampTicks = doc.Get("timestamp_ticks") != null;
        
        // If it has old format fields and lacks new format fields, it's old
        return hasScope && hasTimestamp && hasJsonData && !hasType && !hasCreated && !hasTimestampTicks;
    }

    private FlexibleMemoryEntry? ConvertToNewFormat(Document doc, bool isShared)
    {
        try
        {
            var id = doc.Get("id");
            var scope = doc.Get("scope");
            var content = doc.Get("content");
            var timestampStr = doc.Get("timestamp");
            var lastModifiedStr = doc.Get("last_modified");
            var jsonData = doc.Get("json_data");
            
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Memory missing required fields: id={Id}, content={HasContent}", 
                    id ?? "null", !string.IsNullOrEmpty(content));
                return null;
            }
            
            // Parse timestamps
            DateTime created = DateTime.UtcNow;
            DateTime modified = DateTime.UtcNow;
            
            if (long.TryParse(timestampStr, out var timestampTicks))
            {
                created = new DateTime(timestampTicks, DateTimeKind.Utc);
            }
            
            if (long.TryParse(lastModifiedStr, out var lastModifiedTicks))
            {
                modified = new DateTime(lastModifiedTicks, DateTimeKind.Utc);
            }
            
            // Map scope to type
            var type = MapScopeToType(scope);
            
            // Create new format memory
            var memory = new FlexibleMemoryEntry
            {
                Id = id,
                Type = type,
                Content = content,
                Created = created,
                Modified = modified,
                IsShared = isShared,
                AccessCount = 0,
                // TimestampTicks is a computed property, not settable
            };
            
            // Parse json_data if present
            if (!string.IsNullOrEmpty(jsonData))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(jsonData);
                    var root = jsonDoc.RootElement;
                    
                    // Extract known fields from old format
                    if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
                    {
                        var files = filesElement.EnumerateArray()
                            .Select(f => f.GetString())
                            .Where(f => !string.IsNullOrEmpty(f))
                            .ToList();
                        
                        if (files.Count > 0)
                        {
                            memory.FilesInvolved = files!.ToArray();
                        }
                    }
                    
                    // Store any other fields in the Fields dictionary
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name != "files" && prop.Name != "id" && prop.Name != "content")
                        {
                            memory.SetField(prop.Name, prop.Value.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse json_data for memory {Id}", id);
                }
            }
            
            return memory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert memory to new format");
            return null;
        }
    }

    private string MapScopeToType(string? scope)
    {
        // Map old scope values to new memory types
        return scope?.ToLowerInvariant() switch
        {
            "architecturaldecision" => MemoryTypes.ArchitecturalDecision,
            "codepattern" => MemoryTypes.CodePattern,
            "securityrule" => MemoryTypes.SecurityRule,
            "projectinsight" => MemoryTypes.ProjectInsight,
            "worksession" => MemoryTypes.WorkSession,
            "localinsight" => MemoryTypes.PersonalContext, // LocalInsight mapped to PersonalContext
            "technicaldebt" => MemoryTypes.TechnicalDebt,
            "question" => MemoryTypes.Question,
            "deferredtask" => MemoryTypes.DeferredTask,
            "temporarynote" => MemoryTypes.TemporaryNote,
            _ => MemoryTypes.ProjectInsight // Default fallback
        };
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public string? ErrorMessage { get; set; }
    public WorkspaceMigrationResult ProjectMemories { get; set; } = new();
    public WorkspaceMigrationResult LocalMemories { get; set; } = new();
    
    public int TotalOldFormatCount => ProjectMemories.OldFormatCount + LocalMemories.OldFormatCount;
    public int TotalNewFormatCount => ProjectMemories.NewFormatCount + LocalMemories.NewFormatCount;
    public int TotalMigratedCount => ProjectMemories.MigratedIds.Count + LocalMemories.MigratedIds.Count;
    public int TotalFailedCount => ProjectMemories.FailedCount + LocalMemories.FailedCount;
}

public class WorkspaceMigrationResult
{
    public string Workspace { get; set; } = "";
    public int OldFormatCount { get; set; }
    public int NewFormatCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> MigratedIds { get; set; } = new();
    public List<string> FailedIds { get; set; } = new();
}