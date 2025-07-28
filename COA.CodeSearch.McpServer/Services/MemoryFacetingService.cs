using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Facet.Taxonomy;
using Lucene.Net.Facet.Taxonomy.Directory;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing native Lucene faceting in the memory system
/// Provides efficient facet calculation and drill-down functionality
/// </summary>
public class MemoryFacetingService : IDisposable
{
    private readonly ILogger<MemoryFacetingService> _logger;
    private readonly IPathResolutionService _pathResolution;
    private readonly FacetsConfig _facetsConfig;
    
    // Taxonomy directories for each workspace
    private readonly Dictionary<string, DirectoryTaxonomyWriter> _taxonomyWriters = new();
    private readonly Dictionary<string, DirectoryTaxonomyReader> _taxonomyReaders = new();
    private readonly Dictionary<string, FSDirectory> _taxonomyDirectories = new();
    
    // Facet caching for performance
    private readonly Dictionary<string, (FacetResult[] Results, DateTime CacheTime)> _facetCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5); // Cache facets for 5 minutes
    
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    
    // Facet field names
    public const string TYPE_FACET = "type";
    public const string STATUS_FACET = "status";
    public const string PRIORITY_FACET = "priority";
    public const string CATEGORY_FACET = "category";
    public const string SHARED_FACET = "is_shared";
    public const string FILES_FACET = "files";
    
    public MemoryFacetingService(
        ILogger<MemoryFacetingService> logger,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _pathResolution = pathResolution;
        _facetsConfig = CreateFacetsConfig();
    }
    
    /// <summary>
    /// Create and configure the facets configuration
    /// </summary>
    private FacetsConfig CreateFacetsConfig()
    {
        var config = new FacetsConfig();
        
        // Configure facet fields
        // Type facet - hierarchical (e.g., "TechnicalDebt/Database/Performance")
        config.SetHierarchical(TYPE_FACET, true);
        config.SetMultiValued(TYPE_FACET, false);  // Each memory has one type
        
        // Status facet - flat
        config.SetHierarchical(STATUS_FACET, false);
        config.SetMultiValued(STATUS_FACET, false);
        
        // Priority facet - flat
        config.SetHierarchical(PRIORITY_FACET, false);
        config.SetMultiValued(PRIORITY_FACET, false);
        
        // Category facet - hierarchical (e.g., "Backend/Database", "Frontend/UI")
        config.SetHierarchical(CATEGORY_FACET, true);
        config.SetMultiValued(CATEGORY_FACET, false);
        
        // Shared status facet - flat
        config.SetHierarchical(SHARED_FACET, false);
        config.SetMultiValued(SHARED_FACET, false);
        
        // Files facet - multi-valued (one memory can relate to multiple files)
        config.SetHierarchical(FILES_FACET, false);
        config.SetMultiValued(FILES_FACET, true);
        
        _logger.LogInformation("FacetsConfig created with {FieldCount} configured fields", 6);
        return config;
    }
    
    /// <summary>
    /// Add facet fields to a document and build it for indexing
    /// </summary>
    public async Task<Document> AddFacetFieldsAsync(Document document, FlexibleMemoryEntry memory, string workspacePath)
    {
        try
        {
            // Skip faceting if no useful facet data is available
            if (string.IsNullOrEmpty(memory.Type) && 
                memory.GetField<string>("status") == null &&
                memory.GetField<string>("priority") == null &&
                memory.GetField<string>("category") == null &&
                !memory.FilesInvolved.Any())
            {
                _logger.LogDebug("Skipping facet fields for memory {Id} - no facet data available", memory.Id);
                return document;
            }

            // Add type facet
            if (!string.IsNullOrEmpty(memory.Type))
            {
                document.Add(new FacetField(TYPE_FACET, memory.Type));
            }
            
            // Add status facet (check multiple field names due to reserved field restrictions)
            var status = memory.GetField<string>("status") ?? memory.GetField<string>("state");
            if (!string.IsNullOrEmpty(status))
            {
                document.Add(new FacetField(STATUS_FACET, status));
            }
            
            // Add priority facet (check multiple field names due to reserved field restrictions)
            var priority = memory.GetField<string>("priority") ?? memory.GetField<string>("importance");
            if (!string.IsNullOrEmpty(priority))
            {
                document.Add(new FacetField(PRIORITY_FACET, priority));
            }
            
            // Add category facet
            var category = memory.GetField<string>("category");
            if (!string.IsNullOrEmpty(category))
            {
                document.Add(new FacetField(CATEGORY_FACET, category));
            }
            
            // Add shared status facet
            document.Add(new FacetField(SHARED_FACET, memory.IsShared.ToString()));
            
            // Add files facet (multi-valued)
            foreach (var file in memory.FilesInvolved)
            {
                if (!string.IsNullOrEmpty(file))
                {
                    document.Add(new FacetField(FILES_FACET, file));
                }
            }
            
            // Get or create taxonomy writer for this workspace
            var taxonomyWriter = await GetTaxonomyWriterAsync(workspacePath);
            
            // Build the document with facet configuration
            // This associates the document with the taxonomy
            var facetDocument = _facetsConfig.Build(taxonomyWriter, document);
            
            // Commit taxonomy changes to make facets available for search
            await Task.Run(() => taxonomyWriter.Commit());
            
            _logger.LogDebug("Added facet fields and committed taxonomy for memory {Id}", memory.Id);
            return facetDocument;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding facet fields to document for memory {Id} - faceting will be skipped", memory.Id);
            return document; // Return original document if faceting fails
        }
    }
    
    /// <summary>
    /// Add facet fields to a document during indexing (synchronous version for backward compatibility)
    /// NOTE: This version cannot add raw FacetField objects as they require taxonomy writer processing
    /// Use AddFacetFieldsAsync for full faceting support with taxonomy management
    /// </summary>
    public Document AddFacetFields(Document document, FlexibleMemoryEntry memory)
    {
        try
        {
            // Skip adding raw FacetField objects as they require taxonomy writer processing
            // The synchronous version cannot access the workspace path needed for taxonomy setup
            // For now, faceting is handled by the manual CalculateFacets method
            
            _logger.LogDebug("Skipping native facet fields for memory {Id} in synchronous mode - use AddFacetFieldsAsync for full faceting", memory.Id);
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AddFacetFields for memory {Id}", memory.Id);
            return document; // Return original document if faceting fails
        }
    }
    
    /// <summary>
    /// Get or create taxonomy writer for a workspace
    /// </summary>
    public async Task<DirectoryTaxonomyWriter> GetTaxonomyWriterAsync(string workspacePath)
    {
        if (_taxonomyWriters.TryGetValue(workspacePath, out var existingWriter))
        {
            return existingWriter;
        }
        
        try
        {
            var taxonomyPath = GetTaxonomyPath(workspacePath);
            var directory = FSDirectory.Open(taxonomyPath);
            _taxonomyDirectories[workspacePath] = directory;
            
            var writer = new DirectoryTaxonomyWriter(directory);
            _taxonomyWriters[workspacePath] = writer;
            
            _logger.LogInformation("Created taxonomy writer for workspace {WorkspacePath} at {TaxonomyPath}", 
                workspacePath, taxonomyPath);
            
            return writer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create taxonomy writer for workspace {WorkspacePath}", workspacePath);
            throw;
        }
    }
    
    /// <summary>
    /// Get or create taxonomy reader for a workspace
    /// </summary>
    public async Task<DirectoryTaxonomyReader> GetTaxonomyReaderAsync(string workspacePath)
    {
        try
        {
            var taxonomyPath = GetTaxonomyPath(workspacePath);
            
            // Check if we need to refresh the reader
            if (_taxonomyReaders.TryGetValue(workspacePath, out var existingReader))
            {
                try
                {
                    var newReader = DirectoryTaxonomyReader.OpenIfChanged(existingReader);
                    if (newReader != null)
                    {
                        existingReader.Dispose();
                        _taxonomyReaders[workspacePath] = newReader;
                        return newReader;
                    }
                    return existingReader;
                }
                catch (IndexNotFoundException)
                {
                    // Taxonomy was deleted, need to recreate
                    _logger.LogDebug("Taxonomy index missing for workspace {WorkspacePath}, will recreate", workspacePath);
                    existingReader.Dispose();
                    _taxonomyReaders.Remove(workspacePath);
                    // Fall through to create new reader
                }
            }
            
            // Create new reader
            var directory = FSDirectory.Open(taxonomyPath);
            if (!_taxonomyDirectories.ContainsKey(workspacePath))
            {
                _taxonomyDirectories[workspacePath] = directory;
            }
            
            // Check if taxonomy index exists, if not create it
            if (!DirectoryReader.IndexExists(directory))
            {
                _logger.LogDebug("Creating new taxonomy index for workspace {WorkspacePath}", workspacePath);
                // Create empty taxonomy by creating and immediately closing a writer
                using (var tempWriter = new DirectoryTaxonomyWriter(directory))
                {
                    tempWriter.Commit();
                }
            }
            
            var reader = new DirectoryTaxonomyReader(directory);
            _taxonomyReaders[workspacePath] = reader;
            
            _logger.LogDebug("Created taxonomy reader for workspace {WorkspacePath}", workspacePath);
            return reader;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create taxonomy reader for workspace {WorkspacePath}", workspacePath);
            throw;
        }
    }
    
    /// <summary>
    /// Perform faceted search on memories
    /// </summary>
    public async Task<FacetResult[]> SearchFacetsAsync(
        string workspacePath, 
        IndexSearcher searcher, 
        Query query, 
        int maxResults = 10)
    {
        try
        {
            // Generate cache key from workspace, query, and maxResults
            var cacheKey = $"{workspacePath}:{query.ToString()}:{maxResults}";
            
            // Check cache first
            if (_facetCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.CacheTime < _cacheExpiry)
                {
                    _logger.LogDebug("Returning cached facet results for workspace {WorkspacePath}", workspacePath);
                    return cached.Results;
                }
                else
                {
                    // Remove expired cache entry
                    _facetCache.Remove(cacheKey);
                }
            }
            var taxonomyReader = await GetTaxonomyReaderAsync(workspacePath);
            var facetsCollector = new FacetsCollector();
            
            // Perform the search with facet collection
            var results = FacetsCollector.Search(searcher, query, maxResults, facetsCollector);
            
            // Get facet results - handle case where taxonomy is empty or mismatched
            FastTaxonomyFacetCounts facets;
            try
            {
                facets = new FastTaxonomyFacetCounts(taxonomyReader, _facetsConfig, facetsCollector);
            }
            catch (IndexOutOfRangeException ex)
            {
                _logger.LogWarning("Taxonomy appears empty or corrupted for workspace {WorkspacePath}, returning empty facets: {Error}", 
                    workspacePath, ex.Message);
                return new FacetResult[0];
            }
            
            var facetResults = new List<FacetResult>();
            
            // Get results for each configured facet field
            var facetFields = new[] { TYPE_FACET, STATUS_FACET, PRIORITY_FACET, CATEGORY_FACET, SHARED_FACET, FILES_FACET };
            
            foreach (var field in facetFields)
            {
                try
                {
                    var facetResult = facets.GetTopChildren(10, field);
                    if (facetResult != null && facetResult.ChildCount > 0)
                    {
                        facetResults.Add(facetResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "No facet results for field {Field}", field);
                }
            }
            
            _logger.LogDebug("Retrieved {Count} facet results for workspace {WorkspacePath}", 
                facetResults.Count, workspacePath);
            
            var facetArray = facetResults.ToArray();
            
            // Cache the results
            _facetCache[cacheKey] = (facetArray, DateTime.UtcNow);
            
            return facetArray;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing faceted search for workspace {WorkspacePath}", workspacePath);
            return Array.Empty<FacetResult>();
        }
    }
    
    /// <summary>
    /// Invalidate facet cache for a workspace (call when memories are updated)
    /// </summary>
    public void InvalidateFacetCache(string workspacePath)
    {
        var keysToRemove = _facetCache.Keys.Where(k => k.StartsWith($"{workspacePath}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _facetCache.Remove(key);
        }
        _logger.LogDebug("Invalidated {Count} cached facet entries for workspace {WorkspacePath}", 
            keysToRemove.Count, workspacePath);
    }
    
    /// <summary>
    /// Convert native Lucene facet results to our Dictionary format for backward compatibility
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> ConvertFacetResults(FacetResult[] facetResults)
    {
        var result = new Dictionary<string, Dictionary<string, int>>();
        
        foreach (var facetResult in facetResults)
        {
            var fieldName = facetResult.Dim;
            var fieldCounts = new Dictionary<string, int>();
            
            foreach (var labelValue in facetResult.LabelValues)
            {
                fieldCounts[labelValue.Label] = (int)labelValue.Value;
            }
            
            if (fieldCounts.Any())
            {
                result[fieldName] = fieldCounts;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Commit changes to taxonomy writers
    /// </summary>
    public async Task CommitAsync(string workspacePath)
    {
        if (_taxonomyWriters.TryGetValue(workspacePath, out var writer))
        {
            try
            {
                writer.Commit();
                _logger.LogDebug("Committed taxonomy writer for workspace {WorkspacePath}", workspacePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit taxonomy writer for workspace {WorkspacePath}", workspacePath);
            }
        }
    }
    
    /// <summary>
    /// Get the taxonomy directory path for a workspace and ensure it exists
    /// </summary>
    private string GetTaxonomyPath(string workspacePath)
    {
        // Create taxonomy directory alongside the main index
        // Use the PathResolutionService to properly resolve the index path
        var indexBasePath = _pathResolution.GetIndexPath(workspacePath);
        var taxonomyPath = Path.Combine(indexBasePath, "taxonomy");
        
        // Ensure the taxonomy directory exists
        if (!System.IO.Directory.Exists(taxonomyPath))
        {
            System.IO.Directory.CreateDirectory(taxonomyPath);
            _logger.LogDebug("Created taxonomy directory: {TaxonomyPath}", taxonomyPath);
        }
        
        return taxonomyPath;
    }
    
    /// <summary>
    /// Get the facets configuration
    /// </summary>
    public FacetsConfig GetFacetsConfig()
    {
        return _facetsConfig;
    }
    
    public void Dispose()
    {
        try
        {
            // Dispose taxonomy writers
            foreach (var writer in _taxonomyWriters.Values)
            {
                try
                {
                    writer.Commit();
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing taxonomy writer");
                }
            }
            _taxonomyWriters.Clear();
            
            // Dispose taxonomy readers
            foreach (var reader in _taxonomyReaders.Values)
            {
                try
                {
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing taxonomy reader");
                }
            }
            _taxonomyReaders.Clear();
            
            // Dispose directories
            foreach (var directory in _taxonomyDirectories.Values)
            {
                try
                {
                    directory.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing taxonomy directory");
                }
            }
            _taxonomyDirectories.Clear();
            
            _logger.LogInformation("MemoryFacetingService disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during MemoryFacetingService disposal");
        }
    }
}