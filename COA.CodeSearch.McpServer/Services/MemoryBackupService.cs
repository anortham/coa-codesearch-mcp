using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides SQLite backup and restore functionality for Lucene-based memory indexes.
/// This enables version control, cross-machine sharing, and disaster recovery.
/// </summary>
public class MemoryBackupService : IDisposable
{
    private readonly ILogger<MemoryBackupService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly string _backupDbPath;
    private readonly SemaphoreSlim _backupLock = new(1, 1);
    
    public MemoryBackupService(
        ILogger<MemoryBackupService> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneService,
        IPathResolutionService pathResolutionService)
    {
        _logger = logger;
        _configuration = configuration;
        _luceneService = luceneService;
        _pathResolutionService = pathResolutionService;
        
        // Get backup database path using PathResolutionService
        _backupDbPath = Path.Combine(_pathResolutionService.GetBackupPath(), "memories.db");
        
        // Ensure database exists with proper schema
        InitializeDatabase();
    }
    
    /// <summary>
    /// Initialize SQLite database with proper schema
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_backupDbPath}");
            connection.Open();
            
            // Enable WAL mode for better performance and concurrency
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaCmd.ExecuteNonQuery();
            }
            
            // Create memories table
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS memories (
                    id TEXT PRIMARY KEY,
                    scope TEXT NOT NULL,
                    content TEXT NOT NULL,
                    timestamp INTEGER NOT NULL,
                    last_modified INTEGER NOT NULL,
                    json_data TEXT NOT NULL
                );
                
                CREATE INDEX IF NOT EXISTS idx_scope ON memories(scope);
                CREATE INDEX IF NOT EXISTS idx_last_modified ON memories(last_modified);
                CREATE INDEX IF NOT EXISTS idx_timestamp ON memories(timestamp);
                
                CREATE TABLE IF NOT EXISTS backup_metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );";
            
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("Memory backup database initialized at {Path}", _backupDbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize backup database");
            throw;
        }
    }
    
    /// <summary>
    /// Backup memories from Lucene to SQLite (incremental)
    /// </summary>
    public async Task<BackupResult> BackupMemoriesAsync(string[] scopes, CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken);
        try
        {
            var result = new BackupResult();
            var lastBackupTime = await GetLastBackupTimeAsync();
            
            using var connection = new SqliteConnection($"Data Source={_backupDbPath}");
            await connection.OpenAsync(cancellationToken);
            
            using var transaction = connection.BeginTransaction();
            
            _logger.LogInformation("BackupMemoriesAsync: Processing {ScopeCount} scopes", scopes.Length);
            foreach (var scope in scopes)
            {
                var workspace = GetWorkspaceForScope(scope);
                _logger.LogInformation("BackupMemoriesAsync: Processing scope '{Scope}' with workspace '{Workspace}'", scope, workspace);
                var backedUp = await BackupScopeAsync(
                    connection, 
                    transaction, 
                    scope, 
                    workspace, 
                    lastBackupTime,
                    cancellationToken);
                
                result.DocumentsBackedUp += backedUp;
            }
            
            // Update last backup timestamp
            await UpdateLastBackupTimeAsync(connection, transaction);
            
            await transaction.CommitAsync(cancellationToken);
            
            result.Success = true;
            result.BackupPath = _backupDbPath;
            result.BackupTime = DateTime.UtcNow;
            
            _logger.LogInformation("Backed up {Count} memories to {Path}", 
                result.DocumentsBackedUp, _backupDbPath);
            
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
    /// Restore memories from SQLite to Lucene
    /// </summary>
    public async Task<RestoreResult> RestoreMemoriesAsync(string[] scopes, CancellationToken cancellationToken = default)
    {
        await _backupLock.WaitAsync(cancellationToken);
        try
        {
            var result = new RestoreResult();
            
            if (!File.Exists(_backupDbPath))
            {
                _logger.LogInformation("No backup database found at {Path}", _backupDbPath);
                result.Success = true;
                return result;
            }
            
            using var connection = new SqliteConnection($"Data Source={_backupDbPath}");
            await connection.OpenAsync(cancellationToken);
            
            foreach (var scope in scopes)
            {
                var workspace = GetWorkspaceForScope(scope);
                var restored = await RestoreScopeAsync(
                    connection, 
                    scope, 
                    workspace,
                    cancellationToken);
                
                result.DocumentsRestored += restored;
            }
            
            result.Success = true;
            result.RestoreTime = DateTime.UtcNow;
            
            _logger.LogInformation("Restored {Count} memories from {Path}", 
                result.DocumentsRestored, _backupDbPath);
            
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
    
    private async Task<int> BackupScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string scope,
        string workspace,
        DateTime lastBackupTime,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("BackupScopeAsync: Starting backup for scope '{Scope}' with workspace '{Workspace}'", scope, workspace);
            
            var searcher = await _luceneService.GetIndexSearcherAsync(workspace, cancellationToken);
            _logger.LogInformation("BackupScopeAsync: Got IndexSearcher for workspace '{Workspace}', Reader has {NumDocs} documents", 
                workspace, searcher.IndexReader.NumDocs);
        
        // Query for all documents modified since last backup
        Query timeQuery;
        if (lastBackupTime == DateTime.MinValue)
        {
            // First backup - get everything
            timeQuery = new MatchAllDocsQuery();
            _logger.LogInformation("BackupScopeAsync: Using MatchAllDocsQuery for initial backup");
        }
        else
        {
            // Incremental - only get modified documents
            var ticks = lastBackupTime.Ticks;
            timeQuery = NumericRangeQuery.NewInt64Range("timestamp_ticks", ticks, long.MaxValue, false, true);
            _logger.LogInformation("BackupScopeAsync: Using incremental backup from {LastBackup} (ticks: {Ticks})", lastBackupTime, ticks);
        }
        
        // Combine with scope filter
        var boolQuery = new BooleanQuery();
        boolQuery.Add(timeQuery, Occur.MUST);
        boolQuery.Add(new TermQuery(new Term("scope", scope)), Occur.MUST);
        
        var query = boolQuery;
        _logger.LogInformation("BackupScopeAsync: Filtering for scope '{Scope}'", scope);
        
        var collector = TopScoreDocCollector.Create(10000, true);
        searcher.Search(query, collector);
        var hits = collector.GetTopDocs().ScoreDocs;
        
        _logger.LogInformation("BackupScopeAsync: Found {HitCount} documents to backup for scope '{Scope}'", hits.Length, scope);
        
        // Debug: Let's check what scopes actually exist in the index
        if (hits.Length == 0)
        {
            _logger.LogInformation("BackupScopeAsync: No hits found. Let's check what scopes exist in the index...");
            var allDocsQuery = new MatchAllDocsQuery();
            var debugCollector = TopScoreDocCollector.Create(100, true);
            searcher.Search(allDocsQuery, debugCollector);
            var debugHits = debugCollector.GetTopDocs().ScoreDocs;
            
            _logger.LogInformation("BackupScopeAsync: Total documents in index: {DocCount}", debugHits.Length);
            
            var scopesFound = new HashSet<string>();
            var sampleDocs = new List<string>();
            foreach (var hit in debugHits.Take(20)) // Check first 20 docs
            {
                var doc = searcher.Doc(hit.Doc);
                var docScope = doc.Get("scope");
                var docContent = doc.Get("content");
                if (!string.IsNullOrEmpty(docScope))
                {
                    scopesFound.Add($"'{docScope}'");
                    sampleDocs.Add($"scope='{docScope}', content='{docContent?.Substring(0, Math.Min(50, docContent?.Length ?? 0))}'");
                }
            }
            
            _logger.LogInformation("BackupScopeAsync: Found scopes in index: {Scopes}", string.Join(", ", scopesFound));
            _logger.LogInformation("BackupScopeAsync: Sample documents: {Docs}", string.Join("; ", sampleDocs.Take(3)));
            _logger.LogInformation("BackupScopeAsync: We are searching for scope: '{Scope}'", scope);
            _logger.LogInformation("BackupScopeAsync: Workspace path: '{Workspace}'", workspace);
        }
        
        if (hits.Length == 0)
            return 0;
        
        // Prepare insert/update command
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO memories (id, scope, content, timestamp, last_modified, json_data)
            VALUES (@id, @scope, @content, @timestamp, @modified, @json)";
        
        var idParam = cmd.Parameters.Add("@id", SqliteType.Text);
        var scopeParam = cmd.Parameters.Add("@scope", SqliteType.Text);
        var contentParam = cmd.Parameters.Add("@content", SqliteType.Text);
        var timestampParam = cmd.Parameters.Add("@timestamp", SqliteType.Integer);
        var modifiedParam = cmd.Parameters.Add("@modified", SqliteType.Integer);
        var jsonParam = cmd.Parameters.Add("@json", SqliteType.Text);
        
        scopeParam.Value = scope;
        
        var count = 0;
        foreach (var hit in hits)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var doc = searcher.Doc(hit.Doc);
            
            // Extract fields
            var id = doc.Get("id") ?? Guid.NewGuid().ToString();
            var content = doc.Get("content") ?? "";
            var timestamp = doc.GetField("timestamp_ticks")?.GetInt64Value() ?? DateTime.UtcNow.Ticks;
            
            // Build JSON representation of all fields
            var jsonData = JsonSerializer.Serialize(DocumentToDict(doc));
            
            // Set parameters
            idParam.Value = id;
            contentParam.Value = content;
            timestampParam.Value = timestamp;
            modifiedParam.Value = DateTime.UtcNow.Ticks;
            jsonParam.Value = jsonData;
            
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            count++;
        }
        
        return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup scope '{Scope}' with workspace '{Workspace}'", scope, workspace);
            throw;
        }
    }
    
    private async Task<int> RestoreScopeAsync(
        SqliteConnection connection,
        string scope,
        string workspace,
        CancellationToken cancellationToken)
    {
        // Get all memories for this scope
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, json_data FROM memories WHERE scope = @scope";
        cmd.Parameters.AddWithValue("@scope", scope);
        
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!reader.HasRows)
            return 0;
        
        var writer = await _luceneService.GetIndexWriterAsync(workspace, cancellationToken);
        var count = 0;
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var jsonData = reader.GetString(1);
            
            try
            {
                // Check if document already exists in Lucene
                var term = new Term("id", id);
                var searcher = await _luceneService.GetIndexSearcherAsync(workspace, cancellationToken);
                var query = new TermQuery(term);
                var hits = searcher.Search(query, 1);
                
                if (hits.TotalHits > 0)
                    continue; // Already exists
                
                // Deserialize and restore document
                var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonData);
                if (fields != null)
                {
                    var doc = new Document();
                    foreach (var field in fields)
                    {
                        // Reconstruct field based on type
                        if (field.Value is JsonElement element)
                        {
                            AddFieldFromJson(doc, field.Key, element);
                        }
                    }
                    
                    writer.AddDocument(doc);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore memory {Id}", id);
            }
        }
        
        if (count > 0)
        {
            await _luceneService.CommitAsync(workspace, cancellationToken);
        }
        
        return count;
    }
    
    private void AddFieldFromJson(Document doc, string name, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                doc.Add(new TextField(name, element.GetString() ?? "", Field.Store.YES));
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    doc.Add(new Int64Field(name, longValue, Field.Store.YES));
                }
                else if (element.TryGetDouble(out var doubleValue))
                {
                    doc.Add(new DoubleField(name, doubleValue, Field.Store.YES));
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                doc.Add(new StringField(name, element.GetBoolean().ToString(), Field.Store.YES));
                break;
        }
    }
    
    private Dictionary<string, object> DocumentToDict(Document doc)
    {
        var dict = new Dictionary<string, object>();
        foreach (var field in doc.Fields)
        {
            var name = field.Name;
            if (field.GetStringValue() != null)
            {
                dict[name] = field.GetStringValue();
            }
            else if (field.GetInt64Value() != null)
            {
                dict[name] = field.GetInt64Value()!;
            }
            else if (field.GetDoubleValue() != null)
            {
                dict[name] = field.GetDoubleValue()!;
            }
        }
        return dict;
    }
    
    private string GetWorkspaceForScope(string scope)
    {
        // Build the workspace path that will be resolved by ILuceneIndexService
        // This must match the logic in ClaudeMemoryService.IsProjectScope for consistency
        
        // Project-level scopes (shared with team via version control)
        // Must match ClaudeMemoryService.IsProjectScope() logic exactly
        var isProjectScope = scope is "ArchitecturalDecision" 
                                   or "CodePattern" 
                                   or "SecurityRule" 
                                   or "ProjectInsight";
        
        // Return just the memory path names - PathResolutionService will handle the full path resolution
        return isProjectScope
            ? _configuration["ClaudeMemory:ProjectMemoryPath"] ?? "project-memory"
            : _configuration["ClaudeMemory:LocalMemoryPath"] ?? "local-memory";
    }
    
    private async Task<DateTime> GetLastBackupTimeAsync()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_backupDbPath}");
            await connection.OpenAsync();
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM backup_metadata WHERE key = 'last_backup_time'";
            
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && long.TryParse(result.ToString(), out var ticks))
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }
        catch { }
        
        return DateTime.MinValue;
    }
    
    private async Task UpdateLastBackupTimeAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO backup_metadata (key, value)
            VALUES ('last_backup_time', @value)";
        cmd.Parameters.AddWithValue("@value", DateTime.UtcNow.Ticks.ToString());
        
        await cmd.ExecuteNonQueryAsync();
    }
    
    public void Dispose()
    {
        _backupLock?.Dispose();
    }
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