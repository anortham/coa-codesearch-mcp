using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool to migrate existing memories to add the timestamp_ticks field
/// This is needed for memories created before commit 434c694
/// </summary>
public class MigrateMemoriesTool
{
    private readonly ILogger<MigrateMemoriesTool> _logger;
    private readonly ILuceneIndexService _luceneService;
    private readonly IConfiguration _configuration;
    
    public MigrateMemoriesTool(
        ILogger<MigrateMemoriesTool> logger, 
        ILuceneIndexService luceneService,
        IConfiguration configuration)
    {
        _logger = logger;
        _luceneService = luceneService;
        _configuration = configuration;
    }
    
    public class MigrationResult
    {
        public int TotalMemories { get; set; }
        public int MigratedMemories { get; set; }
        public int SkippedMemories { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> MigratedScopes { get; set; } = new();
    }
    
    public async Task<MigrationResult> MigrateAsync()
    {
        var result = new MigrationResult();
        
        try
        {
            // Get project memory workspace path
            var projectMemoryPath = _configuration["ClaudeMemory:ProjectMemoryPath"] ?? "project-memory";
            
            // Get the index searcher
            var searcher = await _luceneService.GetIndexSearcherAsync(projectMemoryPath);
            if (searcher == null)
            {
                throw new InvalidOperationException("Could not access project memory index");
            }
            
            // Find all documents
            var allDocsQuery = new MatchAllDocsQuery();
            var collector = TopScoreDocCollector.Create(1000, true);
            searcher.Search(allDocsQuery, collector);
            var hits = collector.GetTopDocs().ScoreDocs;
            
            result.TotalMemories = hits.Length;
            _logger.LogInformation("Found {Count} total memories to check for migration", hits.Length);
            
            // Process each document
            var docsToUpdate = new List<Document>();
            var docsToDelete = new List<string>();
            
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var timestampStr = doc.Get("timestamp");
                var timestampTicks = doc.Get("timestamp_ticks");
                var id = doc.Get("id");
                
                // Skip if already has timestamp_ticks
                if (!string.IsNullOrEmpty(timestampTicks))
                {
                    result.SkippedMemories++;
                    continue;
                }
                
                // Parse timestamp and add ticks field
                if (!string.IsNullOrEmpty(timestampStr) && !string.IsNullOrEmpty(id))
                {
                    try
                    {
                        var timestamp = DateTime.Parse(timestampStr, null, DateTimeStyles.RoundtripKind);
                        
                        // Create new document with all existing fields plus timestamp_ticks
                        var newDoc = new Document();
                        
                        // Copy all existing fields
                        foreach (var field in doc.Fields)
                        {
                            if (field.Name == "timestamp_ticks") continue; // Skip if somehow it exists
                            
                            // Add field based on its type
                            switch (field.Name)
                            {
                                case "id":
                                case "scope":
                                case "type":
                                case "timestamp":
                                    newDoc.Add(new StringField(field.Name, field.GetStringValue(), Field.Store.YES));
                                    break;
                                case "content":
                                case "metadata":
                                    newDoc.Add(new TextField(field.Name, field.GetStringValue(), Field.Store.YES));
                                    break;
                                default:
                                    // For any other fields, preserve as stored
                                    newDoc.Add(new StoredField(field.Name, field.GetStringValue()));
                                    break;
                            }
                        }
                        
                        // Add the missing timestamp_ticks field
                        newDoc.Add(new NumericDocValuesField("timestamp_ticks", timestamp.Ticks));
                        newDoc.Add(new StoredField("timestamp_ticks", timestamp.Ticks.ToString()));
                        
                        docsToUpdate.Add(newDoc);
                        docsToDelete.Add(id);
                        
                        var scope = doc.Get("scope");
                        if (!string.IsNullOrEmpty(scope) && !result.MigratedScopes.Contains(scope))
                        {
                            result.MigratedScopes.Add(scope);
                        }
                        
                        _logger.LogDebug("Prepared migration for document {DocId} with scope '{Scope}' and timestamp {Timestamp}",
                            id, scope, timestampStr);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to parse timestamp for doc {id}: {ex.Message}");
                        _logger.LogError(ex, "Failed to parse timestamp '{Timestamp}' for document {DocId}", 
                            timestampStr, id);
                    }
                }
            }
            
            // Apply updates if any
            if (docsToUpdate.Count > 0)
            {
                _logger.LogInformation("Migrating {Count} memories by adding timestamp_ticks field", docsToUpdate.Count);
                
                // Get the index writer
                var writer = await _luceneService.GetIndexWriterAsync(projectMemoryPath);
                
                // Update the index
                foreach (var (newDoc, oldId) in docsToUpdate.Zip(docsToDelete))
                {
                    // Delete old document and add new one
                    writer.DeleteDocuments(new Term("id", oldId));
                    writer.AddDocument(newDoc);
                    result.MigratedMemories++;
                }
                
                // Commit changes
                await _luceneService.CommitAsync(projectMemoryPath);
                _logger.LogInformation("Successfully committed {Count} migrated memories", result.MigratedMemories);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate memories");
            result.Errors.Add($"Migration failed: {ex.Message}");
            return result;
        }
    }
}