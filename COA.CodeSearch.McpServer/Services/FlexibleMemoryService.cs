using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries.Mlt;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Enhanced memory service with flexible schema and advanced search capabilities
/// </summary>
public class FlexibleMemoryService
{
    private readonly ILogger<FlexibleMemoryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _indexService;
    private readonly string _projectMemoryWorkspace;
    private readonly string _localMemoryWorkspace;
    
    // Lucene components
    private readonly StandardAnalyzer _analyzer;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    
    public FlexibleMemoryService(
        ILogger<FlexibleMemoryService> logger, 
        IConfiguration configuration,
        ILuceneIndexService indexService)
    {
        _logger = logger;
        _configuration = configuration;
        _indexService = indexService;
        
        _analyzer = new StandardAnalyzer(LUCENE_VERSION);
        
        // Initialize workspace paths
        var basePath = _configuration["MemoryConfiguration:BasePath"] ?? ".codesearch";
        _projectMemoryWorkspace = Path.GetFullPath(Path.Combine(basePath, "project-memory"));
        _localMemoryWorkspace = Path.GetFullPath(Path.Combine(basePath, "local-memory"));
    }
    
    /// <summary>
    /// Store a flexible memory entry
    /// </summary>
    public async Task<bool> StoreMemoryAsync(FlexibleMemoryEntry memory)
    {
        try
        {
            var workspacePath = memory.IsShared ? _projectMemoryWorkspace : _localMemoryWorkspace;
            
            // Get writer from index service
            var writer = await _indexService.GetIndexWriterAsync(workspacePath);
            
            // Remove existing document if updating
            writer.DeleteDocuments(new Term("id", memory.Id));
            
            // Add the new document
            var doc = CreateDocument(memory);
            writer.AddDocument(doc);
            
            // Commit through index service
            await _indexService.CommitAsync(workspacePath);
            
            _logger.LogInformation("Stored flexible memory: {Type} - {Id}", memory.Type, memory.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store flexible memory");
            return false;
        }
    }
    
    /// <summary>
    /// Search for memories with advanced filtering
    /// </summary>
    public async Task<FlexibleMemorySearchResult> SearchMemoriesAsync(FlexibleMemorySearchRequest request)
    {
        var result = new FlexibleMemorySearchResult();
        var allMemories = new List<FlexibleMemoryEntry>();
        
        try
        {
            // Search both project and local memories
            var projectResults = await SearchIndexAsync(_projectMemoryWorkspace, request, true);
            var localResults = await SearchIndexAsync(_localMemoryWorkspace, request, false);
            
            allMemories.AddRange(projectResults);
            allMemories.AddRange(localResults);
            
            // Apply additional filtering
            var filtered = ApplyFilters(allMemories, request);
            
            // Apply sorting
            var sorted = ApplySorting(filtered, request);
            
            // Calculate facets
            result.FacetCounts = CalculateFacets(sorted);
            
            // Apply pagination
            result.TotalFound = sorted.Count;
            result.Memories = sorted.Take(request.MaxResults).ToList();
            
            // Generate insights if we have results
            if (result.Memories.Any())
            {
                result.Insights = GenerateInsights(result.Memories);
            }
            
            // Update access counts for returned memories
            foreach (var memory in result.Memories)
            {
                memory.AccessCount++;
                memory.LastAccessed = DateTime.UtcNow;
                await UpdateMemoryAsync(memory);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching flexible memories");
            result.Memories = new List<FlexibleMemoryEntry>();
            return result;
        }
    }
    
    /// <summary>
    /// Update an existing memory
    /// </summary>
    public async Task<bool> UpdateMemoryAsync(MemoryUpdateRequest request)
    {
        try
        {
            // First, find the existing memory
            var existing = await GetMemoryByIdAsync(request.Id);
            if (existing == null)
            {
                _logger.LogWarning("Memory not found for update: {Id}", request.Id);
                return false;
            }
            
            // Apply updates
            if (!string.IsNullOrEmpty(request.Content))
            {
                existing.Content = request.Content;
            }
            
            existing.Modified = DateTime.UtcNow;
            
            // Update fields
            foreach (var (key, value) in request.FieldUpdates)
            {
                if (value == null)
                {
                    existing.Fields.Remove(key);
                }
                else
                {
                    existing.Fields[key] = value.Value;
                }
            }
            
            // Update files
            if (request.AddFiles != null)
            {
                existing.FilesInvolved = existing.FilesInvolved.Union(request.AddFiles).ToArray();
            }
            
            if (request.RemoveFiles != null)
            {
                existing.FilesInvolved = existing.FilesInvolved.Except(request.RemoveFiles).ToArray();
            }
            
            // Store the updated memory
            return await StoreMemoryAsync(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating memory");
            return false;
        }
    }
    
    /// <summary>
    /// Get a memory by ID
    /// </summary>
    public async Task<FlexibleMemoryEntry?> GetMemoryByIdAsync(string id)
    {
        // Use direct term query for exact ID match instead of QueryParser
        // This avoids issues with QueryParser not finding StringField values
        try
        {
            // Search both project and local memories
            var projectMemory = await SearchMemoryByIdInIndexAsync(_projectMemoryWorkspace, id, true);
            if (projectMemory != null)
                return projectMemory;
                
            var localMemory = await SearchMemoryByIdInIndexAsync(_localMemoryWorkspace, id, false);
            return localMemory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memory by ID: {Id}", id);
            return null;
        }
    }
    
    /// <summary>
    /// Find memories similar to a given memory
    /// </summary>
    public async Task<List<FlexibleMemoryEntry>> FindSimilarMemoriesAsync(string memoryId, int maxResults = 10)
    {
        var sourceMemory = await GetMemoryByIdAsync(memoryId);
        if (sourceMemory == null)
        {
            return new List<FlexibleMemoryEntry>();
        }
        
        // Use Lucene's MoreLikeThis functionality
        var similarMemories = new List<FlexibleMemoryEntry>();
        
        try
        {
            // Search both indexes
            similarMemories.AddRange(await FindSimilarInIndexAsync(_projectMemoryWorkspace, sourceMemory, maxResults));
            similarMemories.AddRange(await FindSimilarInIndexAsync(_localMemoryWorkspace, sourceMemory, maxResults));
            
            // Remove the source memory itself and duplicates
            return similarMemories
                .Where(m => m.Id != memoryId)
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar memories");
            return new List<FlexibleMemoryEntry>();
        }
    }
    
    /// <summary>
    /// Archive old memories
    /// </summary>
    public async Task<int> ArchiveMemoriesAsync(string type, TimeSpan olderThan)
    {
        var cutoffDate = DateTime.UtcNow - olderThan;
        _logger.LogInformation("Archiving memories of type {Type} older than {Cutoff} (older than {Days} days)", 
            type, cutoffDate, olderThan.TotalDays);
            
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { type },
            DateRange = new DateRangeFilter { From = DateTime.MinValue, To = cutoffDate },
            MaxResults = int.MaxValue
        };
        
        // Search without updating access counts to avoid conflicts
        var memories = new List<FlexibleMemoryEntry>();
        memories.AddRange(await SearchIndexAsync(_projectMemoryWorkspace, searchRequest, true));
        memories.AddRange(await SearchIndexAsync(_localMemoryWorkspace, searchRequest, false));
        
        _logger.LogInformation("Found {Count} memories to archive of type {Type}", memories.Count, type);
        
        var archived = 0;
        
        foreach (var memory in memories)
        {
            memory.SetField("archived", true);
            memory.SetField("archivedDate", DateTime.UtcNow);
            
            try
            {
                if (await StoreMemoryAsync(memory))
                {
                    archived++;
                }
                else
                {
                    _logger.LogWarning("Failed to store archived memory: {Id}", memory.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving memory: {Id}", memory.Id);
            }
        }
        
        _logger.LogInformation("Archived {Count} memories of type {Type} older than {Cutoff}", 
            archived, type, cutoffDate);
        
        return archived;
    }
    
    // Private helper methods
    
    /// <summary>
    /// Search for a memory by ID in a specific index using direct TermQuery
    /// </summary>
    private async Task<FlexibleMemoryEntry?> SearchMemoryByIdInIndexAsync(string workspacePath, string id, bool isShared)
    {
        try
        {
            // Get searcher from index service
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                _logger.LogDebug("No index searcher available for workspace {WorkspacePath}", workspacePath);
                return null;
            }
            
            // Create direct term query for exact ID match
            var termQuery = new TermQuery(new Term("id", id));
            
            // Execute search
            var topDocs = searcher.Search(termQuery, 1);
            
            if (topDocs.ScoreDocs.Length > 0)
            {
                var doc = searcher.Doc(topDocs.ScoreDocs[0].Doc);
                var memory = DocumentToMemory(doc);
                memory.IsShared = isShared;
                return memory;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for memory by ID in index at {Path}", workspacePath);
            return null;
        }
    }
    
    private Document CreateDocument(FlexibleMemoryEntry memory)
    {
        var doc = new Document();
        
        // Core fields
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        
        // Date fields need both Int64Field (for storage) and NumericDocValuesField (for range queries)
        doc.Add(new Int64Field("created", memory.Created.Ticks, Field.Store.YES));
        doc.Add(new NumericDocValuesField("created", memory.Created.Ticks));
        
        doc.Add(new Int64Field("modified", memory.Modified.Ticks, Field.Store.YES));
        doc.Add(new NumericDocValuesField("modified", memory.Modified.Ticks));
        
        doc.Add(new Int64Field("timestamp_ticks", memory.TimestampTicks, Field.Store.YES));
        doc.Add(new NumericDocValuesField("timestamp_ticks", memory.TimestampTicks));
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.YES));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        if (memory.LastAccessed.HasValue)
        {
            doc.Add(new Int64Field("last_accessed", memory.LastAccessed.Value.Ticks, Field.Store.YES));
            doc.Add(new NumericDocValuesField("last_accessed", memory.LastAccessed.Value.Ticks));
        }
        
        // Files
        foreach (var file in memory.FilesInvolved)
        {
            doc.Add(new StringField("file", file, Field.Store.YES));
        }
        
        // Extended fields as JSON
        if (memory.Fields.Any())
        {
            var fieldsJson = JsonSerializer.Serialize(memory.Fields);
            doc.Add(new StoredField("extended_fields", fieldsJson));
            
            // Index specific fields for searching
            IndexExtendedFields(doc, memory.Fields);
        }
        
        // Create searchable content
        var searchableContent = BuildSearchableContent(memory);
        doc.Add(new TextField("_all", searchableContent, Field.Store.NO));
        
        return doc;
    }
    
    private void IndexExtendedFields(Document doc, Dictionary<string, JsonElement> fields)
    {
        foreach (var (key, value) in fields)
        {
            var fieldName = $"field_{key}";
            
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var strValue = value.GetString() ?? "";
                    // Use TextField for long strings, StringField for short ones
                    if (strValue.Length > 100)
                    {
                        doc.Add(new TextField(fieldName, strValue, Field.Store.NO));
                    }
                    else
                    {
                        doc.Add(new StringField(fieldName, strValue, Field.Store.NO));
                    }
                    break;
                    
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out var intVal))
                    {
                        doc.Add(new Int32Field(fieldName, intVal, Field.Store.NO));
                        doc.Add(new NumericDocValuesField(fieldName, intVal));
                    }
                    else if (value.TryGetInt64(out var longVal))
                    {
                        doc.Add(new Int64Field(fieldName, longVal, Field.Store.NO));
                        doc.Add(new NumericDocValuesField(fieldName, longVal));
                    }
                    else if (value.TryGetDouble(out var doubleVal))
                    {
                        doc.Add(new DoubleField(fieldName, doubleVal, Field.Store.NO));
                        doc.Add(new DoubleDocValuesField(fieldName, doubleVal));
                    }
                    break;
                    
                case JsonValueKind.True:
                case JsonValueKind.False:
                    doc.Add(new StringField(fieldName, value.GetBoolean().ToString(), Field.Store.NO));
                    break;
                    
                case JsonValueKind.Array:
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
    
    private string BuildSearchableContent(FlexibleMemoryEntry memory)
    {
        var parts = new List<string>
        {
            memory.Content,
            memory.Type,
            string.Join(" ", memory.FilesInvolved)
        };
        
        // Add string values from extended fields
        foreach (var (key, value) in memory.Fields)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                parts.Add(value.GetString() ?? "");
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(element.GetString() ?? "");
                    }
                }
            }
        }
        
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
    
    private async Task<List<FlexibleMemoryEntry>> SearchIndexAsync(string workspacePath, FlexibleMemorySearchRequest request, bool isShared)
    {
        var memories = new List<FlexibleMemoryEntry>();
        
        try
        {
            // Get searcher from index service
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                _logger.LogDebug("No index searcher available for workspace {WorkspacePath}", workspacePath);
                return memories;
            }
            
            // Build the query
            var query = BuildQuery(request);
            
            // Execute search
            var topDocs = searcher.Search(query, request.MaxResults * 2); // Get extra for filtering
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var memory = DocumentToMemory(doc);
                memory.IsShared = isShared;
                memories.Add(memory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching index at {Path}", workspacePath);
        }
        
        return memories;
    }
    
    private Query BuildQuery(FlexibleMemorySearchRequest request)
    {
        var booleanQuery = new BooleanQuery();
        
        // Main search query
        if (!string.IsNullOrWhiteSpace(request.Query) && request.Query != "*")
        {
            var parser = new QueryParser(LUCENE_VERSION, "_all", _analyzer);
            try
            {
                var textQuery = parser.Parse(request.Query);
                booleanQuery.Add(textQuery, Occur.MUST);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing query: {Query}", request.Query);
                // Fall back to simple term query
                booleanQuery.Add(new TermQuery(new Term("_all", request.Query)), Occur.MUST);
            }
        }
        
        // Type filter
        if (request.Types != null && request.Types.Any())
        {
            var typeQuery = new BooleanQuery();
            foreach (var type in request.Types)
            {
                typeQuery.Add(new TermQuery(new Term("type", type)), Occur.SHOULD);
            }
            booleanQuery.Add(typeQuery, Occur.MUST);
        }
        
        // Date range filter
        if (request.DateRange != null)
        {
            request.DateRange.ParseRelativeTime();
            
            var fromTicks = request.DateRange.From?.Ticks ?? 0L;
            var toTicks = request.DateRange.To?.Ticks ?? DateTime.MaxValue.Ticks;
            
            var dateQuery = NumericRangeQuery.NewInt64Range("created", fromTicks, toTicks, true, true);
            booleanQuery.Add(dateQuery, Occur.MUST);
        }
        
        // Facet filters
        if (request.Facets != null)
        {
            foreach (var (field, value) in request.Facets)
            {
                var fieldName = $"field_{field}";
                booleanQuery.Add(new TermQuery(new Term(fieldName, value)), Occur.MUST);
            }
        }
        
        return booleanQuery.Clauses.Count > 0 ? booleanQuery : new MatchAllDocsQuery();
    }
    
    private FlexibleMemoryEntry DocumentToMemory(Document doc)
    {
        var memory = new FlexibleMemoryEntry
        {
            Id = doc.Get("id"),
            Type = doc.Get("type"),
            Content = doc.Get("content"),
            Created = new DateTime(long.Parse(doc.Get("created"))),
            Modified = new DateTime(long.Parse(doc.Get("modified"))),
            IsShared = bool.Parse(doc.Get("is_shared") ?? "true"),
            AccessCount = int.Parse(doc.Get("access_count") ?? "0"),
            SessionId = doc.Get("session_id") ?? ""
        };
        
        // Parse last accessed
        var lastAccessedStr = doc.Get("last_accessed");
        if (!string.IsNullOrEmpty(lastAccessedStr))
        {
            memory.LastAccessed = new DateTime(long.Parse(lastAccessedStr));
        }
        
        // Parse files
        var files = doc.GetValues("file");
        if (files != null)
        {
            memory.FilesInvolved = files;
        }
        
        // Parse extended fields
        var fieldsJson = doc.Get("extended_fields");
        if (!string.IsNullOrEmpty(fieldsJson))
        {
            try
            {
                memory.Fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fieldsJson) 
                    ?? new Dictionary<string, JsonElement>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing extended fields for memory {Id}", memory.Id);
            }
        }
        
        return memory;
    }
    
    private List<FlexibleMemoryEntry> ApplyFilters(List<FlexibleMemoryEntry> memories, FlexibleMemorySearchRequest request)
    {
        var filtered = memories.AsEnumerable();
        
        // Filter by related IDs
        if (request.RelatedToIds != null && request.RelatedToIds.Any())
        {
            filtered = filtered.Where(m =>
            {
                var related = m.GetField<string[]>(MemoryFields.RelatedTo);
                return related != null && related.Intersect(request.RelatedToIds).Any();
            });
        }
        
        // Filter out archived unless requested
        if (!request.IncludeArchived)
        {
            filtered = filtered.Where(m => m.GetField<bool>("archived") != true);
        }
        
        return filtered.ToList();
    }
    
    private List<FlexibleMemoryEntry> ApplySorting(List<FlexibleMemoryEntry> memories, FlexibleMemorySearchRequest request)
    {
        var query = memories.AsQueryable();
        
        // Apply boosting for scoring
        if (request.BoostRecent || request.BoostFrequent)
        {
            memories = memories.Select(m =>
            {
                var score = 1.0;
                
                if (request.BoostRecent)
                {
                    var daysSinceCreated = (DateTime.UtcNow - m.Created).TotalDays;
                    score *= Math.Max(0.1, 1.0 - (daysSinceCreated / 365.0)); // Decay over a year
                }
                
                if (request.BoostFrequent)
                {
                    score *= Math.Log(Math.Max(1, m.AccessCount)) + 1;
                }
                
                // Store score in a temporary field for sorting
                m.SetField("_score", score);
                return m;
            }).ToList();
        }
        
        // Apply sorting
        if (!string.IsNullOrEmpty(request.OrderBy))
        {
            switch (request.OrderBy.ToLower())
            {
                case "created":
                    query = request.OrderDescending ? 
                        query.OrderByDescending(m => m.Created) : 
                        query.OrderBy(m => m.Created);
                    break;
                    
                case "modified":
                    query = request.OrderDescending ? 
                        query.OrderByDescending(m => m.Modified) : 
                        query.OrderBy(m => m.Modified);
                    break;
                    
                case "type":
                    query = request.OrderDescending ? 
                        query.OrderByDescending(m => m.Type) : 
                        query.OrderBy(m => m.Type);
                    break;
                    
                case "score":
                    query = query.OrderByDescending(m => m.GetField<double>("_score"));
                    break;
                    
                default:
                    // Try to sort by extended field
                    var fieldName = request.OrderBy;
                    var memoriesList = query.ToList();
                    
                    if (request.OrderDescending)
                    {
                        memoriesList = memoriesList.OrderByDescending(m => 
                        {
                            var fieldValue = m.GetField<object>(fieldName);
                            return fieldValue?.ToString() ?? "";
                        }).ToList();
                    }
                    else
                    {
                        memoriesList = memoriesList.OrderBy(m => 
                        {
                            var fieldValue = m.GetField<object>(fieldName);
                            return fieldValue?.ToString() ?? "";
                        }).ToList();
                    }
                    
                    return memoriesList;
            }
        }
        else if (request.BoostRecent || request.BoostFrequent)
        {
            // Default to score-based sorting if boosting is enabled
            query = query.OrderByDescending(m => m.GetField<double>("_score"));
        }
        else
        {
            // Default to newest first
            query = query.OrderByDescending(m => m.Created);
        }
        
        return query.ToList();
    }
    
    private Dictionary<string, Dictionary<string, int>> CalculateFacets(List<FlexibleMemoryEntry> memories)
    {
        var facets = new Dictionary<string, Dictionary<string, int>>();
        
        // Calculate type facets
        facets["type"] = memories
            .GroupBy(m => m.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Calculate common field facets
        var commonFields = new[] { "status", "priority", "category" };
        
        foreach (var field in commonFields)
        {
            var fieldFacets = new Dictionary<string, int>();
            
            foreach (var memory in memories)
            {
                var value = memory.GetField<string>(field);
                if (!string.IsNullOrEmpty(value))
                {
                    fieldFacets.TryGetValue(value, out var count);
                    fieldFacets[value] = count + 1;
                }
            }
            
            if (fieldFacets.Any())
            {
                facets[field] = fieldFacets;
            }
        }
        
        return facets;
    }
    
    private MemorySearchInsights GenerateInsights(List<FlexibleMemoryEntry> memories)
    {
        var insights = new MemorySearchInsights();
        
        // Generate summary
        var typeGroups = memories.GroupBy(m => m.Type).OrderByDescending(g => g.Count()).ToList();
        if (typeGroups.Any())
        {
            insights.Summary = $"Found {memories.Count} memories across {typeGroups.Count} types. " +
                $"Most common: {typeGroups.First().Key} ({typeGroups.First().Count()} items).";
        }
        
        // Identify patterns
        var patterns = new List<string>();
        
        // Pattern: Many pending items
        var pendingCount = memories.Count(m => m.GetField<string>("status") == MemoryStatus.Pending);
        if (pendingCount > memories.Count / 2)
        {
            patterns.Add($"{pendingCount} items are still pending - consider reviewing these");
        }
        
        // Pattern: Old unresolved items
        var oldPending = memories.Where(m => 
            m.GetField<string>("status") == MemoryStatus.Pending && 
            (DateTime.UtcNow - m.Created).TotalDays > 30
        ).ToList();
        
        if (oldPending.Any())
        {
            patterns.Add($"{oldPending.Count} pending items are over 30 days old");
        }
        
        insights.Patterns = patterns.ToArray();
        
        // Generate recommended actions
        var actions = new List<string>();
        
        if (pendingCount > 5)
        {
            actions.Add("Review and prioritize pending items");
        }
        
        if (memories.Any(m => m.GetField<string>("priority") == MemoryPriority.Critical))
        {
            actions.Add("Address critical priority items first");
        }
        
        insights.RecommendedActions = actions.ToArray();
        
        return insights;
    }
    
    private async Task<List<FlexibleMemoryEntry>> FindSimilarInIndexAsync(string workspacePath, FlexibleMemoryEntry sourceMemory, int maxResults)
    {
        var similar = new List<FlexibleMemoryEntry>();
        
        try
        {
            // Get searcher from index service
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                _logger.LogDebug("No index searcher available for workspace {WorkspacePath}", workspacePath);
                return similar;
            }
            
            // Use Lucene's MoreLikeThis for better similarity matching
            var mlt = new MoreLikeThis(searcher.IndexReader)
            {
                Analyzer = _analyzer,
                MinTermFreq = 1,    // Minimum times a term must appear in source doc
                MinDocFreq = 1,     // Minimum docs a term must appear in
                MinWordLen = 3,     // Minimum word length
                MaxWordLen = 30,    // Maximum word length
                MaxQueryTerms = 25  // Maximum number of query terms
            };
            
            // Set fields to analyze for similarity
            mlt.FieldNames = new[] { "content", "type" };
            
            // Create a StringReader from the source memory content
            using var reader = new StringReader(sourceMemory.Content);
            var query = mlt.Like(reader, "content");
            
            if (query != null)
            {
                var topDocs = searcher.Search(query, maxResults + 1);
                
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var memory = DocumentToMemory(doc);
                    similar.Add(memory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar memories in {Path}", workspacePath);
        }
        
        return similar;
    }
    
    private async Task<bool> UpdateMemoryAsync(FlexibleMemoryEntry memory)
    {
        // Simple update - just store it again
        return await StoreMemoryAsync(memory);
    }
}