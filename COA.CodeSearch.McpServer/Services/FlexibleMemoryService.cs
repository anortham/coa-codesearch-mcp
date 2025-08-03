using System.Text.Json;
using COA.CodeSearch.McpServer.Events;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Scoring;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Index;
using Lucene.Net.Queries.Mlt;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.QueryParsers.Simple;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
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
    private readonly MemoryFacetingService _facetingService;
    private readonly IScoringService _scoringService;
    private readonly IMemoryEventPublisher _eventPublisher;
    private readonly string _projectMemoryWorkspace;
    private readonly string _localMemoryWorkspace;
    
    // Lucene components
    // Note: We get the analyzer from LuceneIndexService to ensure consistency between indexing and querying
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
        IMemoryValidationService validation,
        MemoryFacetingService facetingService,
        IScoringService scoringService,
        IMemoryEventPublisher eventPublisher)
    {
        _logger = logger;
        _configuration = configuration;
        _indexService = indexService;
        _pathResolution = pathResolution;
        _errorHandling = errorHandling;
        _validation = validation;
        _facetingService = facetingService;  
        _scoringService = scoringService;
        _eventPublisher = eventPublisher;
        
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
            
            // Calculate facets using native Lucene faceting on both project and local indices
            result.FacetCounts = await CalculateCombinedFacetsAsync(request);
            
            // Generate intelligent facet suggestions based on the current search context
            var combinedFacetResults = await GetCombinedFacetResultsAsync(request);
            if (combinedFacetResults != null && combinedFacetResults.Any())
            {
                result.FacetSuggestions = _facetingService.GenerateFacetSuggestions(
                    combinedFacetResults,
                    request.Query,
                    request.Facets);
            }
            
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
    /// Delete a memory by ID
    /// </summary>
    public async Task<bool> DeleteMemoryAsync(string memoryId)
    {
        var context = new ErrorContext("DeleteMemory", additionalData: new Dictionary<string, object>
        {
            ["MemoryId"] = memoryId
        });

        try
        {
            return await _errorHandling.ExecuteWithErrorHandlingAsync(async () =>
            {
                // Validate memory ID
                if (string.IsNullOrEmpty(memoryId))
                {
                    _logger.LogWarning("DeleteMemoryAsync called with empty memory ID");
                    return false;
                }

                // Check if memory exists first
                var existing = await GetMemoryByIdAsync(memoryId);
                if (existing == null)
                {
                    _logger.LogWarning("Memory not found for deletion: {Id}", memoryId);
                    return false;
                }

                // Determine which workspace to delete from
                var workspacePath = existing.IsShared ? _projectMemoryWorkspace : _localMemoryWorkspace;

                // Get the index writer
                var writer = await _indexService.GetIndexWriterAsync(workspacePath);

                // Delete the document by ID term
                var idTerm = new Term("id", memoryId);
                writer.DeleteDocuments(idTerm);

                // Commit the changes
                await _indexService.CommitAsync(workspacePath);

                // Publish deletion event
                await _eventPublisher.PublishMemoryStorageEventAsync(new MemoryStorageEvent
                {
                    Memory = existing,
                    Action = MemoryStorageAction.Deleted,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("Successfully deleted memory: {Id} of type {Type}", memoryId, existing.Type);
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
    
    /// <summary>
    /// Create document with native Lucene faceting support (async version)
    /// </summary>
    private async Task<Document> CreateDocumentAsync(FlexibleMemoryEntry memory, string workspacePath)
    {
        var doc = new Document();
        
        // Core fields - DOCVALUES OPTIMIZATION APPLIED
        // ID: Keep stored for retrieval, but no DocValues needed (not sorted/faceted)
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        
        // Type: Add DocValues for efficient sorting and faceting (3-5x performance improvement)
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new SortedDocValuesField("type", new BytesRef(memory.Type)));
        
        // Content: Keep stored for display, searchable via _all field
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        // Removed content.raw - AI agents don't need it, they can use proper Lucene syntax
        
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
        
        // Shared status: Add DocValues for efficient filtering (shared vs local memories)
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new SortedDocValuesField("is_shared", new BytesRef(memory.IsShared.ToString())));
        
        // Access count: Optimize storage + add DocValues for sorting by popularity
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.NO));
        doc.Add(new NumericDocValuesField("access_count", memory.AccessCount));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        if (memory.LastAccessed.HasValue)
        {
            doc.Add(new Int64Field("last_accessed", memory.LastAccessed.Value.Ticks, dateFieldType));
            doc.Add(new NumericDocValuesField("last_accessed", memory.LastAccessed.Value.Ticks));
        }
        
        // Files: Optimize for multi-value faceting while preserving stored access
        foreach (var file in memory.FilesInvolved)
        {
            doc.Add(new StringField("file", file, Field.Store.YES));
        }
        
        // Add SortedSetDocValues for efficient file-based faceting if there are files
        if (memory.FilesInvolved.Any())
        {
            foreach (var file in memory.FilesInvolved)
            {
                doc.Add(new SortedSetDocValuesField("files_facet", new BytesRef(file)));
            }
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
        doc.Add(new TextField("_all", searchableContent, Field.Store.YES)); // Temporarily store for debugging
        
        // Add native Lucene facet fields with proper taxonomy writer integration
        doc = await _facetingService.AddFacetFieldsAsync(doc, memory, workspacePath);
        
        return doc;
    }

    /// <summary>
    /// Create document (synchronous version for backward compatibility)
    /// </summary>
    private Document CreateDocument(FlexibleMemoryEntry memory)
    {
        var doc = new Document();
        
        // Core fields - DOCVALUES OPTIMIZATION APPLIED
        // ID: Keep stored for retrieval, but no DocValues needed (not sorted/faceted)
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        
        // Type: Add DocValues for efficient sorting and faceting (3-5x performance improvement)
        doc.Add(new StringField("type", memory.Type, Field.Store.YES));
        doc.Add(new SortedDocValuesField("type", new BytesRef(memory.Type)));
        
        // Content: Keep stored for display, searchable via _all field
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        // Removed content.raw - AI agents don't need it, they can use proper Lucene syntax
        
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
        
        // Shared status: Add DocValues for efficient filtering (shared vs local memories)
        doc.Add(new StringField("is_shared", memory.IsShared.ToString(), Field.Store.YES));
        doc.Add(new SortedDocValuesField("is_shared", new BytesRef(memory.IsShared.ToString())));
        
        // Access count: Optimize storage + add DocValues for sorting by popularity
        doc.Add(new Int32Field("access_count", memory.AccessCount, Field.Store.NO));
        doc.Add(new NumericDocValuesField("access_count", memory.AccessCount));
        
        if (!string.IsNullOrEmpty(memory.SessionId))
        {
            doc.Add(new StringField("session_id", memory.SessionId, Field.Store.YES));
        }
        
        if (memory.LastAccessed.HasValue)
        {
            doc.Add(new Int64Field("last_accessed", memory.LastAccessed.Value.Ticks, dateFieldType));
            doc.Add(new NumericDocValuesField("last_accessed", memory.LastAccessed.Value.Ticks));
        }
        
        // Files: Optimize for multi-value faceting while preserving stored access
        foreach (var file in memory.FilesInvolved)
        {
            doc.Add(new StringField("file", file, Field.Store.YES));
        }
        
        // Add SortedSetDocValues for efficient file-based faceting if there are files
        if (memory.FilesInvolved.Any())
        {
            foreach (var file in memory.FilesInvolved)
            {
                doc.Add(new SortedSetDocValuesField("files_facet", new BytesRef(file)));
            }
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
        doc.Add(new TextField("_all", searchableContent, Field.Store.YES)); // Temporarily store for debugging
        
        // Add native Lucene facet fields for efficient faceting (synchronous version - limited support)
        doc = _facetingService.AddFacetFields(doc, memory);
        
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
            memory.Id, // Include ID in searchable content
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
        
        var result = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return result;
    }
    
    private async Task<List<FlexibleMemoryEntry>> SearchIndexAsync(string workspacePath, FlexibleMemorySearchRequest request, bool isShared)
    {
        var memories = new List<FlexibleMemoryEntry>();
        
        _logger.LogInformation("SearchIndexAsync: workspacePath={WorkspacePath}, query={Query}, isShared={IsShared}", 
            workspacePath, request.Query, isShared);
        
        try
        {
            
            // Get searcher from index service
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                _logger.LogWarning("No index searcher available for workspace {WorkspacePath}", workspacePath);
                return memories;
            }
            
            _logger.LogInformation("Got searcher for workspace, index has {DocCount} documents", 
                searcher.IndexReader.NumDocs);
            
            // Build the query
            
            var baseQuery = await BuildQueryAsync(request);
            _logger.LogInformation("Built base query: {Query}", baseQuery.ToString());
            
            
            // Apply scoring based on temporal scoring mode
            var finalQuery = baseQuery;
            
            if (request.TemporalScoring != TemporalScoringMode.None)
            {
                // Configure temporal scoring factor based on mode
                var scoringFactors = new HashSet<string> { "TemporalScoring" };
                var searchContext = new ScoringContext
                {
                    QueryText = request.Query,
                    SearchType = "memory_search", 
                    WorkspacePath = workspacePath,
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["TemporalScoringMode"] = request.TemporalScoring
                    }
                };
                
                // Update temporal scoring factor based on mode
                var availableFactors = _scoringService.GetAvailableFactors();
                if (availableFactors.TryGetValue("TemporalScoring", out var temporalFactor) && temporalFactor is TemporalScoringFactor tsFactor)
                {
                    // Create a new temporal factor with the appropriate decay function
                    var decayFunction = request.TemporalScoring switch
                    {
                        TemporalScoringMode.Aggressive => TemporalDecayFunction.Aggressive,
                        TemporalScoringMode.Gentle => TemporalDecayFunction.Gentle,
                        _ => TemporalDecayFunction.Default
                    };
                    
                    // Replace the temporal factor with one configured for this request
                    var customTemporalFactor = new TemporalScoringFactor(decayFunction, _logger);
                    searchContext.AdditionalData["CustomTemporalFactor"] = customTemporalFactor;
                }
                
                finalQuery = _scoringService.CreateScoredQuery(baseQuery, searchContext, scoringFactors);
                _logger.LogInformation("Applied temporal scoring ({Mode}) to query: {Query}", 
                    request.TemporalScoring, finalQuery.ToString());
            }
            
            
            // Execute search  
            
            var topDocs = searcher.Search(finalQuery, request.MaxResults * 2); // Get extra for filtering
            _logger.LogInformation("Search returned {HitCount} hits", topDocs.TotalHits);
            
            // Setup highlighting if enabled
            Highlighter? highlighter = null;
            if (request.EnableHighlighting && !string.IsNullOrWhiteSpace(request.Query) && request.Query != "*")
            {
                // Use base query for highlighting to avoid scoring interference
                highlighter = CreateHighlighter(baseQuery, request);
            }
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var memory = DocumentToMemory(doc);
                memory.IsShared = isShared;
                
                // Add highlighting if enabled
                if (highlighter != null)
                {
                    await AddHighlightsToMemoryAsync(memory, doc, highlighter, request);
                }
                
                memories.Add(memory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching index at {Path}", workspacePath);
        }
        
        return memories;
    }
    
    /// <summary>
    /// Create a Lucene highlighter with HTML formatter
    /// </summary>
    private Highlighter CreateHighlighter(Query query, FlexibleMemorySearchRequest request)
    {
        // Create a formatter that wraps matches in HTML tags
        var formatter = new SimpleHTMLFormatter("<mark>", "</mark>");
        
        // Create scorer for the query
        var scorer = new QueryScorer(query);
        
        // Create highlighter with formatter and scorer
        var highlighter = new Highlighter(formatter, scorer);
        
        // Configure fragment size (optimized for tokens - roughly 25 words)
        var fragmenter = new SimpleFragmenter(request.FragmentSize);
        highlighter.TextFragmenter = fragmenter;
        
        return highlighter;
    }
    
    /// <summary>
    /// Add highlighted fragments to memory entry
    /// </summary>
    private async Task AddHighlightsToMemoryAsync(
        FlexibleMemoryEntry memory, 
        Document doc, 
        Highlighter highlighter, 
        FlexibleMemorySearchRequest request)
    {
        try
        {
            // Get analyzer for highlighting
            var analyzer = await _indexService.GetAnalyzerAsync(_projectMemoryWorkspace);
            
            // Highlight content field (most important)
            var content = doc.Get("content") ?? "";
            if (!string.IsNullOrEmpty(content))
            {
                var contentFragments = highlighter.GetBestFragments(
                    analyzer, "content", content, request.MaxFragments);
                
                if (contentFragments.Length > 0)
                {
                    memory.Highlights["content"] = contentFragments;
                }
            }
            
            // Highlight type field if it has matches
            var type = doc.Get("type") ?? "";
            if (!string.IsNullOrEmpty(type))
            {
                var typeFragments = highlighter.GetBestFragments(
                    analyzer, "type", type, 1); // Only one fragment for type
                
                if (typeFragments.Length > 0)
                {
                    memory.Highlights["type"] = typeFragments;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating highlights for memory {Id}", memory.Id);
            // Don't fail the search if highlighting fails
        }
    }
    
    /// <summary>
    /// Build text query using MultiFieldQueryParser with field boosting
    /// Enhanced version that maintains compatibility while adding improvements
    /// </summary>
    private async Task<Query> BuildTextQueryAsync(string queryText)
    {
        try
        {
            // Always use the configured analyzer for consistency
            // This ensures synonyms work correctly for both indexing and searching
            var analyzer = await _indexService.GetAnalyzerAsync(_projectMemoryWorkspace);
            
            // Try the MultiFieldQueryParser approach
            var query = await TryBuildMultiFieldQueryAsync(queryText, analyzer);
            if (query != null)
            {
                _logger.LogInformation("MultiFieldQueryParser: '{Query}' -> '{ParsedQuery}'", 
                    queryText, query.ToString());
                return query;
            }
            
            // Fallback to single-field approach for compatibility
            _logger.LogInformation("Falling back to single-field QueryParser for: {Query}", queryText);
            var parser = new QueryParser(LUCENE_VERSION, "_all", analyzer);
            return parser.Parse(queryText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "All query parsing failed for: {Query}", queryText);
            // Final fallback to simple term query
            return new TermQuery(new Term("_all", queryText));
        }
    }
    
    /// <summary>
    /// Try to build query using MultiFieldQueryParser with enhancements
    /// </summary>
    private Task<Query?> TryBuildMultiFieldQueryAsync(string queryText, Analyzer analyzer)
    {
        try
        {
            // Configure multi-field search with boosts
            var fields = new[] { "content", "type", "_all" };
            var boosts = new Dictionary<string, float>
            {
                ["content"] = 2.0f,  // Content is most important
                ["type"] = 1.5f,     // Type is moderately important
                ["_all"] = 1.0f      // Fallback field
            };
            
            // Create MultiFieldQueryParser with the same analyzer used for indexing
            var parser = new MultiFieldQueryParser(LUCENE_VERSION, fields, analyzer, boosts);
            
            // Configure for AI agents who can construct proper queries
            parser.DefaultOperator = QueryParserBase.AND_OPERATOR; // AND is more precise
            parser.AllowLeadingWildcard = true;
            // Remove artificial "natural language" settings - AI agents don't need them
            
            // Parse and return
            var query = parser.Parse(queryText);
            return Task.FromResult<Query?>(query);
        }
        catch
        {
            // Return null to indicate fallback should be used
            return Task.FromResult<Query?>(null);
        }
    }
    
    
    private async Task<Query> BuildQueryAsync(FlexibleMemorySearchRequest request)
    {
        var booleanQuery = new BooleanQuery();
        
        // Main search query
        if (!string.IsNullOrWhiteSpace(request.Query) && request.Query != "*")
        {
            var textQuery = await BuildTextQueryAsync(request.Query);
            booleanQuery.Add(textQuery, Occur.MUST);
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
                // Determine field name based on whether it's a native facet field or extended field
                string fieldName;
                var nativeFacetFields = new[] { "type", "is_shared", "files" }; // Only fields indexed natively
                
                if (nativeFacetFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                {
                    // Native facet fields are indexed directly
                    fieldName = field.ToLowerInvariant();
                }
                else
                {
                    // Extended fields use the field_ prefix
                    fieldName = $"field_{field}";
                }
                
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
    
    private async Task<Dictionary<string, Dictionary<string, int>>> CalculateFacetsAsync(string workspacePath, IndexSearcher searcher, Query query)
    {
        try
        {
            // Use native Lucene faceting for much better performance
            var facetResults = await _facetingService.SearchFacetsAsync(workspacePath, searcher, query, 10);
            return _facetingService.ConvertFacetResults(facetResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating facets using native Lucene faceting, falling back to manual calculation");
            
            // Fallback to manual calculation if native faceting fails
            return new Dictionary<string, Dictionary<string, int>>();
        }
    }

    private async Task<Dictionary<string, Dictionary<string, int>>> CalculateCombinedFacetsAsync(FlexibleMemorySearchRequest request)
    {
        var combinedFacets = new Dictionary<string, Dictionary<string, int>>();
        
        try
        {
            // Calculate facets from project memory index
            var projectFacets = await CalculateFacetsFromIndexAsync(_projectMemoryWorkspace, request);
            
            // Calculate facets from local memory index  
            var localFacets = await CalculateFacetsFromIndexAsync(_localMemoryWorkspace, request);
            
            // Merge the facet results
            MergeFacetCounts(combinedFacets, projectFacets);
            MergeFacetCounts(combinedFacets, localFacets);
            
            return combinedFacets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating combined facets, returning empty facets");
            return new Dictionary<string, Dictionary<string, int>>();
        }
    }

    private async Task<Dictionary<string, Dictionary<string, int>>> CalculateFacetsFromIndexAsync(string workspacePath, FlexibleMemorySearchRequest request)
    {
        try
        {
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                return new Dictionary<string, Dictionary<string, int>>();
            }

            var query = await BuildQueryAsync(request);
            var facetResults = await _facetingService.SearchFacetsAsync(workspacePath, searcher, query, 10);
            
            // Convert FacetResult[] to Dictionary format
            var facets = new Dictionary<string, Dictionary<string, int>>();
            if (facetResults != null)
            {
                foreach (var facetResult in facetResults)
                {
                    var facetName = facetResult.Dim;
                    if (!facets.ContainsKey(facetName))
                    {
                        facets[facetName] = new Dictionary<string, int>();
                    }
                    
                    foreach (var labelValue in facetResult.LabelValues)
                    {
                        var label = labelValue.Label;
                        var count = (int)labelValue.Value;
                        facets[facetName][label] = count;
                    }
                }
            }
            
            return facets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating facets from index {WorkspacePath}", workspacePath);
            return new Dictionary<string, Dictionary<string, int>>();
        }
    }

    private void MergeFacetCounts(Dictionary<string, Dictionary<string, int>> target, Dictionary<string, Dictionary<string, int>> source)
    {
        foreach (var (facetName, facetValues) in source)
        {
            if (!target.ContainsKey(facetName))
            {
                target[facetName] = new Dictionary<string, int>();
            }

            foreach (var (value, count) in facetValues)
            {
                target[facetName][value] = target[facetName].GetValueOrDefault(value, 0) + count;
            }
        }
    }

    /// <summary>
    /// Get combined facet results from both project and local memory indices for suggestions
    /// </summary>
    private async Task<FacetResult[]?> GetCombinedFacetResultsAsync(FlexibleMemorySearchRequest request)
    {
        try
        {
            var allFacetResults = new List<FacetResult>();
            
            // Get facet results from project memory index
            var projectFacetResults = await GetFacetResultsFromIndexAsync(_projectMemoryWorkspace, request);
            if (projectFacetResults != null)
            {
                allFacetResults.AddRange(projectFacetResults);
            }
            
            // Get facet results from local memory index
            var localFacetResults = await GetFacetResultsFromIndexAsync(_localMemoryWorkspace, request);
            if (localFacetResults != null)
            {
                allFacetResults.AddRange(localFacetResults);
            }
            
            return allFacetResults.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting combined facet results for suggestions");
            return null;
        }
    }

    /// <summary>
    /// Get facet results from a specific index for suggestions
    /// </summary>
    private async Task<FacetResult[]?> GetFacetResultsFromIndexAsync(string workspacePath, FlexibleMemorySearchRequest request)
    {
        try
        {
            var searcher = await _indexService.GetIndexSearcherAsync(workspacePath);
            if (searcher == null)
            {
                return null;
            }

            var query = await BuildQueryAsync(request);
            return await _facetingService.SearchFacetsAsync(workspacePath, searcher, query, 10);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting facet results from index {WorkspacePath}", workspacePath);
            return null;
        }
    }
    
    // Keep the old method as a fallback (marked as obsolete)
    [Obsolete("Use CalculateFacetsAsync with native Lucene faceting instead")]
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
            // Get analyzer for similarity comparison
            var analyzer = await _indexService.GetAnalyzerAsync(workspacePath);
            var mlt = new MoreLikeThis(searcher.IndexReader)
            {
                Analyzer = analyzer,
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
                        
                        // Invalidate facet cache for both workspaces since memories were updated
                        _facetingService.InvalidateFacetCache(_projectMemoryWorkspace);
                        _facetingService.InvalidateFacetCache(_localMemoryWorkspace);
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
        _logger.LogInformation("StoreMemoryAsync: id={Id}, type={Type}, content={Content}, isShared={IsShared}", 
            memory.Id, memory.Type, memory.Content, memory.IsShared);
            
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
                    var errorMessage = string.Join("; ", validationResult.Errors);
                    _logger.LogWarning("Memory validation failed for {MemoryId}: {Errors}", 
                        memory.Id, errorMessage);
                    throw new COA.Mcp.Protocol.InvalidParametersException(errorMessage);
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
                
                // Create document with faceting support
                var document = await CreateDocumentAsync(memory, workspace);
                
                // Check if memory already exists to determine operation type
                var existingQuery = new TermQuery(new Term("id", memory.Id));
                var searcher = await _indexService.GetIndexSearcherAsync(workspace);
                var existingHits = searcher.Search(existingQuery, 1);
                
                if (existingHits.TotalHits > 0)
                {
                    // Update existing memory
                    indexWriter.UpdateDocument(new Term("id", memory.Id), document);
                    _logger.LogDebug("Updated memory {MemoryId} of type {Type}", memory.Id, memory.Type);
                    
                    // Invalidate facet cache since memory was updated
                    _facetingService.InvalidateFacetCache(workspace);
                }
                else
                {
                    // Add new memory
                    indexWriter.AddDocument(document);
                    
                    // Invalidate facet cache since new memory was added
                    _facetingService.InvalidateFacetCache(workspace);
                    _logger.LogDebug("Added new memory {MemoryId} of type {Type}", memory.Id, memory.Type);
                }
                
                // Commit changes
                indexWriter.Commit();
                
                // Also commit through the service to ensure test implementations refresh their searchers
                await _indexService.CommitAsync(workspace);
                
                // Publish memory storage event for semantic indexing and other subscribers
                try
                {
                    var eventData = new MemoryStorageEvent
                    {
                        Memory = memory,
                        Action = existingHits.TotalHits > 0 ? MemoryStorageAction.Updated : MemoryStorageAction.Created
                    };
                    await _eventPublisher.PublishMemoryStorageEventAsync(eventData);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish memory storage event for {MemoryId} (non-blocking)", memory.Id);
                }
                
                return true;
            }, context, ErrorSeverity.Recoverable);
        }
        catch (COA.Mcp.Protocol.InvalidParametersException)
        {
            // Let validation exceptions propagate to tool layer
            throw;
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
        // Note: _analyzer removed - LuceneIndexService manages analyzer lifecycle
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