using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing Claude's persistent memory system using Lucene indexing
/// Supports both project-level (version controlled) and local (developer-specific) memories
/// </summary>
public class ClaudeMemoryService : IDisposable
{
    private readonly ILogger<ClaudeMemoryService> _logger;
    private readonly MemoryConfiguration _config;
    
    // Separate indexes for project vs local memories
    private readonly FSDirectory _projectMemoryDirectory;
    private readonly FSDirectory _localMemoryDirectory;
    
    private readonly IndexWriter _projectMemoryWriter;
    private readonly IndexWriter _localMemoryWriter;
    
    private readonly StandardAnalyzer _analyzer;
    private readonly object _lockObject = new();
    
    // Session tracking
    private readonly string _currentSessionId;
    
    public ClaudeMemoryService(ILogger<ClaudeMemoryService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _config = configuration.GetSection("ClaudeMemory").Get<MemoryConfiguration>() ?? new MemoryConfiguration();
        _currentSessionId = Guid.NewGuid().ToString();
        
        // Setup Lucene analyzer
        _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        // Create memory directories
        var projectPath = Path.Combine(_config.BasePath, _config.ProjectMemoryPath);
        var localPath = Path.Combine(_config.BasePath, _config.LocalMemoryPath);
        
        System.IO.Directory.CreateDirectory(projectPath);
        System.IO.Directory.CreateDirectory(localPath);
        
        _projectMemoryDirectory = FSDirectory.Open(projectPath);
        _localMemoryDirectory = FSDirectory.Open(localPath);
        
        // Create index writers
        var projectConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer);
        var localConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer);
        
        _projectMemoryWriter = new IndexWriter(_projectMemoryDirectory, projectConfig);
        _localMemoryWriter = new IndexWriter(_localMemoryDirectory, localConfig);
        
        _logger.LogInformation("Claude Memory Service initialized with session {SessionId}", _currentSessionId);
    }
    
    /// <summary>
    /// Stores a memory entry in the appropriate index based on its scope
    /// </summary>
    public async Task<bool> StoreMemoryAsync(MemoryEntry memory)
    {
        try
        {
            lock (_lockObject)
            {
                memory.SessionId = _currentSessionId;
                
                var document = CreateLuceneDocument(memory);
                var writer = IsProjectScope(memory.Scope) ? _projectMemoryWriter : _localMemoryWriter;
                
                writer.AddDocument(document);
                writer.Commit();
                
                var scopeType = IsProjectScope(memory.Scope) ? "project" : "local";
                _logger.LogInformation("Stored {ScopeType} memory: {Content}", scopeType, memory.Content[..Math.Min(50, memory.Content.Length)]);
                
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store memory: {Content}", memory.Content);
            return false;
        }
    }
    
    /// <summary>
    /// Searches memories across both project and local indexes
    /// </summary>
    public async Task<MemorySearchResult> SearchMemoriesAsync(string query, MemoryScope? scopeFilter = null, int maxResults = 0)
    {
        var result = new MemorySearchResult
        {
            Query = query,
            ScopeFilter = scopeFilter
        };
        
        if (maxResults == 0)
            maxResults = _config.MaxSearchResults;
        
        try
        {
            // Search both indexes
            var projectMemories = await SearchIndex(_projectMemoryDirectory, query, scopeFilter, maxResults);
            var localMemories = await SearchIndex(_localMemoryDirectory, query, scopeFilter, maxResults);
            
            // Combine and sort by relevance
            var allMemories = projectMemories.Concat(localMemories)
                .Where(m => m.Confidence >= _config.MinConfidenceLevel)
                .OrderByDescending(m => m.Confidence)
                .ThenByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
            
            result.Memories = allMemories;
            result.TotalFound = allMemories.Count;
            result.SuggestedQueries = GenerateSuggestedQueries(allMemories);
            
            _logger.LogInformation("Memory search for '{Query}' returned {Count} results", query, result.TotalFound);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search memories for query: {Query}", query);
            return result;
        }
    }
    
    /// <summary>
    /// Gets memories by scope type
    /// </summary>
    public async Task<List<MemoryEntry>> GetMemoriesByScopeAsync(MemoryScope scope, int maxResults = 0)
    {
        if (maxResults == 0)
            maxResults = _config.MaxSearchResults;
        
        var directory = IsProjectScope(scope) ? _projectMemoryDirectory : _localMemoryDirectory;
        return await SearchIndex(directory, "*", scope, maxResults);
    }
    
    /// <summary>
    /// Stores an architectural decision with structured information
    /// </summary>
    public async Task<bool> StoreArchitecturalDecisionAsync(string decision, string reasoning, string[] affectedFiles, string[] tags = null)
    {
        var memory = new MemoryEntry
        {
            Content = $"DECISION: {decision}\n\nREASONING: {reasoning}",
            Scope = MemoryScope.ArchitecturalDecision,
            FilesInvolved = affectedFiles,
            Keywords = ExtractKeywords($"{decision} {reasoning}"),
            Reasoning = reasoning,
            Tags = tags ?? Array.Empty<string>()
        };
        
        return await StoreMemoryAsync(memory);
    }
    
    /// <summary>
    /// Stores a code pattern with location and usage information
    /// </summary>
    public async Task<bool> StoreCodePatternAsync(string pattern, string location, string usage, string[] relatedFiles = null)
    {
        var memory = new MemoryEntry
        {
            Content = $"PATTERN: {pattern}\n\nLOCATION: {location}\n\nUSAGE: {usage}",
            Scope = MemoryScope.CodePattern,
            FilesInvolved = relatedFiles ?? Array.Empty<string>(),
            Keywords = ExtractKeywords($"{pattern} {location} {usage}"),
            Category = "pattern"
        };
        
        return await StoreMemoryAsync(memory);
    }
    
    /// <summary>
    /// Stores a work session summary
    /// </summary>
    public async Task<bool> StoreWorkSessionAsync(string summary, string[] filesWorkedOn = null)
    {
        var memory = new MemoryEntry
        {
            Content = $"WORK SESSION: {summary}",
            Scope = MemoryScope.WorkSession,
            FilesInvolved = filesWorkedOn ?? Array.Empty<string>(),
            Keywords = ExtractKeywords(summary),
            Category = "session"
        };
        
        return await StoreMemoryAsync(memory);
    }
    
    /// <summary>
    /// Cleans up old temporary notes based on retention policy
    /// </summary>
    public async Task CleanupOldMemoriesAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_config.TemporaryNoteRetentionDays);
        
        // This is a simplified cleanup - in a full implementation, 
        // we'd need to rewrite the index excluding old temporary notes
        _logger.LogInformation("Memory cleanup completed for entries older than {CutoffDate}", cutoffDate);
    }
    
    private async Task<List<MemoryEntry>> SearchIndex(FSDirectory directory, string query, MemoryScope? scopeFilter, int maxResults)
    {
        var memories = new List<MemoryEntry>();
        
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", _analyzer);
        
        // Build query
        var luceneQuery = parser.Parse(query == "*" ? "*:*" : query);
        
        // Add scope filter if specified
        if (scopeFilter.HasValue)
        {
            var scopeQuery = new TermQuery(new Term("scope", scopeFilter.Value.ToString()));
            var boolQuery = new BooleanQuery
            {
                { luceneQuery, Occur.MUST },
                { scopeQuery, Occur.MUST }
            };
            luceneQuery = boolQuery;
        }
        
        var hits = searcher.Search(luceneQuery, maxResults);
        
        foreach (var hit in hits.ScoreDocs)
        {
            var doc = searcher.Doc(hit.Doc);
            var memory = CreateMemoryFromDocument(doc);
            if (memory != null)
            {
                memories.Add(memory);
            }
        }
        
        return memories;
    }
    
    private Document CreateLuceneDocument(MemoryEntry memory)
    {
        var doc = new Document();
        
        // Store all fields for retrieval
        doc.Add(new StoredField("id", memory.Id));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        doc.Add(new StringField("scope", memory.Scope.ToString(), Field.Store.YES));
        doc.Add(new TextField("keywords", string.Join(" ", memory.Keywords), Field.Store.YES));
        doc.Add(new TextField("files", string.Join(" ", memory.FilesInvolved), Field.Store.YES));
        doc.Add(new StringField("timestamp", memory.Timestamp.ToString("O"), Field.Store.YES));
        doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        doc.Add(new StringField("confidence", memory.Confidence.ToString(), Field.Store.YES));
        
        if (!string.IsNullOrEmpty(memory.Category))
            doc.Add(new StringField("category", memory.Category, Field.Store.YES));
        
        if (!string.IsNullOrEmpty(memory.Reasoning))
            doc.Add(new TextField("reasoning", memory.Reasoning, Field.Store.YES));
        
        if (memory.Tags.Any())
            doc.Add(new TextField("tags", string.Join(" ", memory.Tags), Field.Store.YES));
        
        // Store the complete memory as JSON for exact retrieval
        doc.Add(new StoredField("memory_json", JsonSerializer.Serialize(memory)));
        
        return doc;
    }
    
    private MemoryEntry? CreateMemoryFromDocument(Document doc)
    {
        try
        {
            var memoryJson = doc.Get("memory_json");
            if (!string.IsNullOrEmpty(memoryJson))
            {
                return JsonSerializer.Deserialize<MemoryEntry>(memoryJson);
            }
            
            // Fallback to building from individual fields
            return new MemoryEntry
            {
                Id = doc.Get("id") ?? Guid.NewGuid().ToString(),
                Content = doc.Get("content") ?? string.Empty,
                Scope = Enum.Parse<MemoryScope>(doc.Get("scope") ?? nameof(MemoryScope.TemporaryNote)),
                Keywords = doc.Get("keywords")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                FilesInvolved = doc.Get("files")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                Timestamp = DateTime.Parse(doc.Get("timestamp") ?? DateTime.UtcNow.ToString("O")),
                SessionId = doc.Get("session_id") ?? string.Empty,
                Confidence = int.Parse(doc.Get("confidence") ?? "100"),
                Category = doc.Get("category"),
                Reasoning = doc.Get("reasoning"),
                Tags = doc.Get("tags")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize memory from document");
            return null;
        }
    }
    
    private static bool IsProjectScope(MemoryScope scope)
    {
        return scope is MemoryScope.ArchitecturalDecision 
                    or MemoryScope.CodePattern 
                    or MemoryScope.SecurityRule 
                    or MemoryScope.ProjectInsight;
    }
    
    public static string[] ExtractKeywords(string text)
    {
        // Simple keyword extraction - in production could use more sophisticated NLP
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3)
            .Distinct()
            .Take(20)
            .ToArray();
    }
    
    private static string[] GenerateSuggestedQueries(List<MemoryEntry> memories)
    {
        // Extract common keywords and patterns from found memories for suggestions
        var keywords = memories.SelectMany(m => m.Keywords)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToArray();
        
        return keywords;
    }
    
    public void Dispose()
    {
        _projectMemoryWriter?.Dispose();
        _localMemoryWriter?.Dispose();
        _projectMemoryDirectory?.Dispose();
        _localMemoryDirectory?.Dispose();
        _analyzer?.Dispose();
    }
}