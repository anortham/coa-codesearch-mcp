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
public class FlexibleMemoryService : IMemoryService, IDisposable
{
    private readonly ILogger<FlexibleMemoryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _indexService;
    private readonly IPathResolutionService _pathResolution;
    private readonly IErrorHandlingService _errorHandling;
    private readonly IMemoryValidationService _validation;
    private readonly string _projectMemoryWorkspace;
    private readonly string _localMemoryWorkspace;
    
    // Lucene components
    private readonly StandardAnalyzer _analyzer;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    
    // Safety constants
    private const int MaxAllowedResults = 10000; // Maximum results to prevent OOM
    
    // Concurrency control
    private readonly SemaphoreSlim _batchUpdateSemaphore = new(1, 1);
    
    public FlexibleMemoryService(
        ILogger<FlexibleMemoryService> logger, 
        IConfiguration configuration,
        ILuceneIndexService indexService,
        IPathResolutionService pathResolution,
        IErrorHandlingService errorHandling,
        IMemoryValidationService validation)
    {
        _logger = logger;
        _configuration = configuration;
        _indexService = indexService;
        _pathResolution = pathResolution;
        _errorHandling = errorHandling;
        _validation = validation;
        
        _analyzer = new StandardAnalyzer(LUCENE_VERSION);
        
        // Initialize workspace paths from PathResolutionService
        _projectMemoryWorkspace = _pathResolution.GetProjectMemoryPath();
        _localMemoryWorkspace = _pathResolution.GetLocalMemoryPath();
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
            // Validate search request
            if (!ValidateSearchRequest(request, out var validationErrors))
            {
                _logger.LogWarning("Invalid search request: {Errors}", string.Join("; ", validationErrors));
                result.Memories = new List<FlexibleMemoryEntry>();
                return result;
            }
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
            
            // Apply pagination with bounds checking
            result.TotalFound = sorted.Count;
            var safeMaxResults = Math.Min(request.MaxResults, MaxAllowedResults);
            if (request.MaxResults > MaxAllowedResults)
            {
                _logger.LogWarning("MaxResults {Requested} exceeds limit {Max}, capping to {Max}", 
                    request.MaxResults, MaxAllowedResults, MaxAllowedResults);
            }
            result.Memories = sorted.Take(safeMaxResults).ToList();
            
            // Generate insights if we have results
            if (result.Memories.Any())
            {
                result.Insights = GenerateInsights(result.Memories);
            }
            
            // Update access counts for returned memories using batch operation to prevent race conditions
            if (result.Memories.Any())
            {
                await UpdateAccessCountsBatchAsync(result.Memories.Select(m => m.Id));
                
                // Also update the in-memory objects so the returned result reflects the new access counts
                var now = DateTime.UtcNow;
                foreach (var memory in result.Memories)
                {
                    memory.AccessCount++;
                    memory.LastAccessed = now;
                }
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
        var context = new ErrorContext("UpdateMemory", additionalData: new Dictionary<string, object>
        {
            ["MemoryId"] = request.Id
        });

        try
        {
            return await _errorHandling.ExecuteWithErrorHandlingAsync(async () =>
            {
                // Validate update request
                var validationResult = _validation.ValidateUpdateRequest(request);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Memory update validation failed for {MemoryId}: {Errors}", 
                        request.Id, string.Join("; ", validationResult.Errors));
                    return false;
                }

                // Log validation warnings
                if (validationResult.Warnings.Any())
                {
                    _logger.LogWarning("Memory update validation warnings for {MemoryId}: {Warnings}", 
                        request.Id, string.Join("; ", validationResult.Warnings));
                }

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
            }, context, ErrorSeverity.Recoverable);
        }
        catch (Exception ex)
        {
            _errorHandling.LogError(ex, context, ErrorSeverity.Recoverable);
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
    
    /// <summary>
    /// Clean up expired working memories
    /// </summary>
    public async Task<int> CleanupExpiredMemoriesAsync()
    {
        _logger.LogInformation("Starting cleanup of expired working memories");
        
        // Search for all working memories
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "*",
            Types = new[] { MemoryTypes.WorkingMemory },
            MaxResults = 1000,
            IncludeArchived = false
        };
        
        var result = await SearchMemoriesAsync(searchRequest);
        var expiredCount = 0;
        
        foreach (var memory in result.Memories)
        {
            var expiresAt = memory.GetField<DateTime?>(MemoryFields.ExpiresAt);
            if (expiresAt.HasValue && DateTime.UtcNow > expiresAt.Value)
            {
                try
                {
                    // Delete the expired memory
                    var workspacePath = memory.IsShared ? _projectMemoryWorkspace : _localMemoryWorkspace;
                    var writer = await _indexService.GetIndexWriterAsync(workspacePath);
                    writer.DeleteDocuments(new Term("id", memory.Id));
                    await _indexService.CommitAsync(workspacePath);
                    
                    expiredCount++;
                    _logger.LogDebug("Deleted expired working memory: {Id}, expired at {ExpiresAt}", memory.Id, expiresAt.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete expired memory: {Id}", memory.Id);
                }
            }
        }
        
        _logger.LogInformation("Cleaned up {Count} expired working memories", expiredCount);
        return expiredCount;
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
        
        // Date fields with custom field type for proper numeric range query support
        var dateFieldType = new FieldType 
        { 
            IsIndexed = true,
            IsStored = true,
            NumericType = NumericType.INT64,
            NumericPrecisionStep = 8 // Required for NumericRangeQuery to work properly
        };
        
        doc.Add(new Int64Field("created", memory.Created.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("created", memory.Created.Ticks));
        
        doc.Add(new Int64Field("modified", memory.Modified.Ticks, dateFieldType));
        doc.Add(new NumericDocValuesField("modified", memory.Modified.Ticks));
        
        doc.Add(new Int64Field("timestamp_ticks", memory.TimestampTicks, dateFieldType));
        doc.Add(new NumericDocValuesField("timestamp_ticks", memory.TimestampTicks));
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.YES));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        if (memory.LastAccessed.HasValue)
        {
            doc.Add(new Int64Field("last_accessed", memory.LastAccessed.Value.Ticks, dateFieldType));
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
            // Check if this is a natural language query that needs smart recall
            if (IsNaturalLanguageQuery(request.Query))
            {
                var enhancedQuery = BuildSmartRecallQuery(request.Query);
                booleanQuery.Add(enhancedQuery, Occur.MUST);
            }
            else
            {
                // Standard query parsing
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
            
            var dateQuery = NumericRangeQuery.NewInt64Range("created", 8, fromTicks, toTicks, true, true);
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
            Id = doc.Get("id") ?? Guid.NewGuid().ToString(),
            Type = doc.Get("type") ?? "Unknown",
            Content = doc.Get("content") ?? "",
            Created = new DateTime(long.Parse(doc.Get("created") ?? DateTime.UtcNow.Ticks.ToString())),
            Modified = new DateTime(long.Parse(doc.Get("modified") ?? DateTime.UtcNow.Ticks.ToString())),
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
        
        // Filter out expired working memories
        filtered = filtered.Where(m =>
        {
            // Check if this is a working memory with expiration
            var expiresAt = m.GetField<DateTime?>(MemoryFields.ExpiresAt);
            if (expiresAt.HasValue && DateTime.UtcNow > expiresAt.Value)
            {
                _logger.LogDebug("Filtering out expired working memory: {Id}, expired at {ExpiresAt}", m.Id, expiresAt.Value);
                return false;
            }
            return true;
        });
        
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
            .Where(m => !string.IsNullOrEmpty(m.Type))
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
    
    // Smart Recall methods
    
    /// <summary>
    /// Determines if a query is natural language that would benefit from smart recall
    /// </summary>
    private bool IsNaturalLanguageQuery(string query)
    {
        // Natural language indicators
        var naturalLanguagePatterns = new[]
        {
            "that", "about", "where", "when", "how", "what", "which", "why",
            "find", "show", "get", "need", "remember", "recall", "was", "were",
            "discussed", "mentioned", "talked", "related", "regarding", "concerning"
        };
        
        var lowerQuery = query.ToLowerInvariant();
        
        // Check for natural language patterns
        var hasNaturalLanguage = naturalLanguagePatterns.Any(pattern => 
            lowerQuery.Contains($" {pattern} ") || 
            lowerQuery.StartsWith($"{pattern} ") || 
            lowerQuery.EndsWith($" {pattern}"));
        
        // Also consider queries with multiple words and no special operators
        var hasMultipleWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3;
        var hasNoOperators = !query.Contains(":") && !query.Contains("AND") && !query.Contains("OR") 
                            && !query.Contains("*") && !query.Contains("~");
        
        return hasNaturalLanguage || (hasMultipleWords && hasNoOperators);
    }
    
    /// <summary>
    /// Builds an enhanced query for smart recall with semantic understanding
    /// </summary>
    private Query BuildSmartRecallQuery(string naturalLanguageQuery)
    {
        var booleanQuery = new BooleanQuery();
        
        // Extract key concepts and expand them
        var concepts = ExtractKeyConcepts(naturalLanguageQuery);
        var expandedTerms = ExpandWithSynonyms(concepts);
        
        // Create a more flexible query
        foreach (var term in expandedTerms)
        {
            // Use SHOULD for flexibility - matches don't need all terms
            var termQuery = new TermQuery(new Term("_all", term.ToLowerInvariant()));
            booleanQuery.Add(termQuery, Occur.SHOULD);
        }
        
        // Also include the original query as a phrase for exact matches
        if (naturalLanguageQuery.Length > 3)
        {
            try
            {
                var phraseQuery = new PhraseQuery();
                var terms = naturalLanguageQuery.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms.Where(t => t.Length > 2)) // Skip very short words
                {
                    phraseQuery.Add(new Term("_all", term));
                }
                if (phraseQuery.GetTerms().Length > 0)
                {
                    booleanQuery.Add(phraseQuery, Occur.SHOULD);
                    booleanQuery.MinimumNumberShouldMatch = Math.Max(1, expandedTerms.Count / 3); // Require at least 1/3 of terms
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not create phrase query for: {Query}", naturalLanguageQuery);
            }
        }
        
        return booleanQuery;
    }
    
    /// <summary>
    /// Extracts key concepts from natural language query
    /// </summary>
    private List<string> ExtractKeyConcepts(string query)
    {
        var concepts = new List<string>();
        
        // Remove common stop words
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "been",
            "that", "this", "these", "those", "i", "we", "you", "me", "us"
        };
        
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !stopWords.Contains(w) && w.Length > 2)
            .ToList();
        
        concepts.AddRange(words);
        
        // Extract potential code-related terms (camelCase, PascalCase, snake_case)
        var codeTerms = ExtractCodeTerms(query);
        concepts.AddRange(codeTerms);
        
        return concepts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    
    /// <summary>
    /// Expands concepts with synonyms and related terms
    /// </summary>
    private List<string> ExpandWithSynonyms(List<string> concepts)
    {
        var expanded = new List<string>(concepts);
        
        // Common development synonyms
        var synonymMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["bug"] = new[] { "defect", "issue", "error", "problem", "fault" },
            ["fix"] = new[] { "repair", "resolve", "patch", "correct", "solution" },
            ["test"] = new[] { "testing", "tests", "unit", "integration", "spec" },
            ["auth"] = new[] { "authentication", "authorization", "security", "login" },
            ["db"] = new[] { "database", "data", "storage", "repository" },
            ["api"] = new[] { "endpoint", "service", "interface", "rest" },
            ["config"] = new[] { "configuration", "settings", "setup", "options" },
            ["perf"] = new[] { "performance", "speed", "optimization", "efficiency" },
            ["refactor"] = new[] { "restructure", "reorganize", "cleanup", "improve" },
            ["user"] = new[] { "customer", "client", "account", "profile" },
            ["error"] = new[] { "exception", "fault", "failure", "crash" },
            ["cache"] = new[] { "caching", "cached", "memory", "storage" },
            ["async"] = new[] { "asynchronous", "await", "task", "concurrent" }
        };
        
        foreach (var concept in concepts.ToList())
        {
            // Check if concept has synonyms
            if (synonymMap.TryGetValue(concept, out var synonyms))
            {
                expanded.AddRange(synonyms);
            }
            
            // Check if concept is a synonym of something else
            foreach (var (key, values) in synonymMap)
            {
                if (values.Contains(concept, StringComparer.OrdinalIgnoreCase))
                {
                    expanded.Add(key);
                }
            }
        }
        
        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    
    /// <summary>
    /// Extracts code-related terms from the query
    /// </summary>
    private List<string> ExtractCodeTerms(string query)
    {
        var codeTerms = new List<string>();
        
        // Match camelCase, PascalCase, snake_case, CONSTANT_CASE
        var codePatterns = new[]
        {
            @"\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b", // PascalCase
            @"\b[a-z]+(?:[A-Z][a-z]+)+\b",      // camelCase
            @"\b[a-z]+(?:_[a-z]+)+\b",          // snake_case
            @"\b[A-Z]+(?:_[A-Z]+)+\b"           // CONSTANT_CASE
        };
        
        foreach (var pattern in codePatterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(query, pattern);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                codeTerms.Add(match.Value);
                
                // Also split camelCase/PascalCase into parts
                var parts = System.Text.RegularExpressions.Regex.Split(match.Value, @"(?<!^)(?=[A-Z])");
                codeTerms.AddRange(parts.Where(p => p.Length > 2));
            }
        }
        
        return codeTerms;
    }
    
    /// <summary>
    /// Update access counts for multiple memories in a batch to prevent race conditions
    /// </summary>
    private async Task UpdateAccessCountsBatchAsync(IEnumerable<string> memoryIds)
    {
        var context = new ErrorContext("BatchUpdateAccessCounts");
        
        try
        {
            await _errorHandling.ExecuteWithErrorHandlingAsync(async () =>
            {
                var workspace = _projectMemoryWorkspace;
                var now = DateTime.UtcNow;
                var memoryIdsList = memoryIds.ToList();
                
                if (!memoryIdsList.Any())
                    return;

                // Use shared semaphore to prevent concurrent batch updates
                await _batchUpdateSemaphore.WaitAsync();
                
                try
                {
                    var indexWriter = await _indexService.GetIndexWriterAsync(workspace);
                    
                    // Process updates in batches to avoid large transactions
                    const int batchSize = 10;
                    for (int i = 0; i < memoryIdsList.Count; i += batchSize)
                    {
                        var batch = memoryIdsList.Skip(i).Take(batchSize);
                        
                        // Prepare all updates for this batch first
                        var updates = new List<(Term term, Document doc)>();
                        
                        foreach (var memoryId in batch)
                        {
                            // Create update term for the specific memory
                            var term = new Term("id", memoryId);
                            
                            // Retrieve current document with fresh searcher
                            var searcher = await _indexService.GetIndexSearcherAsync(workspace);
                            var query = new TermQuery(term);
                            var hits = searcher.Search(query, 1);
                            
                            if (hits.TotalHits > 0)
                            {
                                var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
                                var memory = DocumentToMemory(doc);
                                
                                // Increment access count and update timestamp
                                memory.AccessCount++;
                                memory.LastAccessed = now;
                                
                                // TODO: Add version field for optimistic concurrency control
                                
                                // Create updated document
                                var updatedDoc = CreateDocument(memory);
                                updates.Add((term, updatedDoc));
                            }
                        }
                        
                        // Apply all updates in this batch atomically
                        foreach (var (term, doc) in updates)
                        {
                            indexWriter.UpdateDocument(term, doc);
                        }
                    }
                    
                    // Commit all changes
                    indexWriter.Commit();
                }
                finally
                {
                    _batchUpdateSemaphore.Release();
                }
            }, context, ErrorSeverity.Recoverable);
        }
        catch (Exception ex)
        {
            _errorHandling.LogError(ex, context, ErrorSeverity.Recoverable);
            // Don't rethrow - access count updates are not critical to user functionality
        }
    }
    
    /// <summary>
    /// Store a memory with validation and error handling
    /// </summary>
    public async Task<bool> StoreMemoryAsync(FlexibleMemoryEntry memory)
    {
        var context = new ErrorContext("StoreMemory", additionalData: new Dictionary<string, object>
        {
            ["MemoryId"] = memory.Id,
            ["MemoryType"] = memory.Type
        });

        try
        {
            return await _errorHandling.ExecuteWithErrorHandlingAsync(async () =>
            {
                // Validate input
                var validationResult = _validation.ValidateMemory(memory);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Memory validation failed for {MemoryId}: {Errors}", 
                        memory.Id, string.Join("; ", validationResult.Errors));
                    return false;
                }

                // Log validation warnings
                if (validationResult.Warnings.Any())
                {
                    _logger.LogWarning("Memory validation warnings for {MemoryId}: {Warnings}", 
                        memory.Id, string.Join("; ", validationResult.Warnings));
                }

                // Determine workspace based on memory sharing
                var workspace = memory.IsShared ? _projectMemoryWorkspace : _localMemoryWorkspace;
                
                // Ensure memory has required fields
                if (string.IsNullOrEmpty(memory.Id))
                {
                    memory.Id = Guid.NewGuid().ToString();
                }
                
                if (memory.Created == default)
                {
                    memory.Created = DateTime.UtcNow;
                }
                
                memory.Modified = DateTime.UtcNow;

                // Get index writer
                var indexWriter = await _indexService.GetIndexWriterAsync(workspace);
                
                // Create document
                var document = CreateDocument(memory);
                
                // Check if memory already exists to determine operation type
                var existingQuery = new TermQuery(new Term("id", memory.Id));
                var searcher = await _indexService.GetIndexSearcherAsync(workspace);
                var existingHits = searcher.Search(existingQuery, 1);
                
                if (existingHits.TotalHits > 0)
                {
                    // Update existing memory
                    indexWriter.UpdateDocument(new Term("id", memory.Id), document);
                    _logger.LogDebug("Updated memory {MemoryId} of type {Type}", memory.Id, memory.Type);
                }
                else
                {
                    // Add new memory
                    indexWriter.AddDocument(document);
                    _logger.LogDebug("Added new memory {MemoryId} of type {Type}", memory.Id, memory.Type);
                }
                
                // Commit changes
                indexWriter.Commit();
                
                return true;
            }, context, ErrorSeverity.Recoverable);
        }
        catch (Exception ex)
        {
            _errorHandling.LogError(ex, context, ErrorSeverity.Recoverable);
            return false;
        }
    }
    
    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        _batchUpdateSemaphore?.Dispose();
        _analyzer?.Dispose();
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// Validate search request parameters
    /// </summary>
    private bool ValidateSearchRequest(FlexibleMemorySearchRequest request, out List<string> errors)
    {
        errors = new List<string>();
        
        // Validate query length
        if (!string.IsNullOrEmpty(request.Query) && request.Query.Length > 1000)
        {
            errors.Add("Query string exceeds maximum length of 1000 characters");
        }
        
        // Validate MaxResults
        if (request.MaxResults <= 0)
        {
            errors.Add("MaxResults must be greater than 0");
        }
        
        // Validate date range
        if (request.DateRange != null)
        {
            request.DateRange.ParseRelativeTime();
            if (request.DateRange.From.HasValue && request.DateRange.To.HasValue && 
                request.DateRange.From.Value > request.DateRange.To.Value)
            {
                errors.Add("Date range From date must be before To date");
            }
        }
        
        // Validate OrderBy field
        var allowedOrderByFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "created", "modified", "type", "score", "accesscount", "lastaccessed"
        };
        
        if (!string.IsNullOrEmpty(request.OrderBy) && !allowedOrderByFields.Contains(request.OrderBy))
        {
            // Check if it's a valid extended field name (alphanumeric with underscores)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.OrderBy, @"^[a-zA-Z0-9_]+$"))
            {
                errors.Add($"Invalid OrderBy field: {request.OrderBy}");
            }
        }
        
        // Validate memory types - includes all types from MemoryTypes class
        var allowedMemoryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Existing types (for backwards compatibility)
            "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight",
            "WorkSession", "ConversationSummary", "PersonalContext", "TemporaryNote",
            
            // New flexible types
            "TechnicalDebt", "DeferredTask", "Question", "Assumption", "Experiment",
            "Learning", "Blocker", "Idea", "CodeReview", "BugReport", "GitCommit",
            "PerformanceIssue", "Refactoring", "Documentation", "Dependency",
            "Configuration", "WorkingMemory", "Checklist", "ChecklistItem",
            
            // Additional types that were in use
            "PerformanceOptimization", "BugFix", "FeatureIdea", "SecurityConcern",
            "DocumentationTodo", "RefactoringNote", "TestingNote", "DeploymentNote",
            "ConfigurationNote", "DependencyNote", "TeamNote", "PersonalNote",
            "CustomType", "LocalInsight"
        };
        
        if (request.Types != null)
        {
            foreach (var type in request.Types)
            {
                if (!allowedMemoryTypes.Contains(type))
                {
                    errors.Add($"Invalid memory type: {type}");
                }
            }
        }
        
        // Validate facet field names
        if (request.Facets != null)
        {
            foreach (var facetKey in request.Facets.Keys)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(facetKey, @"^[a-zA-Z0-9_]+$"))
                {
                    errors.Add($"Invalid facet field name: {facetKey}");
                }
            }
        }
        
        return errors.Count == 0;
    }
}