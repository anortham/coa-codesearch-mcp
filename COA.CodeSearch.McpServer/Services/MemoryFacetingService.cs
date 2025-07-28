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
    /// Add facet fields to a document during indexing
    /// </summary>
    public Document AddFacetFields(Document document, FlexibleMemoryEntry memory)
    {
        try
        {
            // TODO: Native Lucene faceting temporarily disabled until taxonomy infrastructure is complete
            // For now, just return the original document to avoid breaking memory storage
            // The faceting will be handled by manual facet calculation as before
            _logger.LogDebug("Native faceting temporarily disabled for memory {Id} - using manual faceting", memory.Id);
            return document;

            // The code below will be enabled once taxonomy directory creation is properly implemented
            /*
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
            
            // Add status facet
            var status = memory.GetField<string>("status");
            if (!string.IsNullOrEmpty(status))
            {
                document.Add(new FacetField(STATUS_FACET, status));
            }
            
            // Add priority facet
            var priority = memory.GetField<string>("priority");
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
            
            // Build the document with facet configuration
            // This is required for Lucene.NET faceting to work properly
            var facetDocument = _facetsConfig.Build(document);
            
            _logger.LogDebug("Added facet fields to document for memory {Id}", memory.Id);
            return facetDocument;
            */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding facet fields to document for memory {Id} - faceting will be skipped", memory.Id);
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
                var newReader = DirectoryTaxonomyReader.OpenIfChanged(existingReader);
                if (newReader != null)
                {
                    existingReader.Dispose();
                    _taxonomyReaders[workspacePath] = newReader;
                    return newReader;
                }
                return existingReader;
            }
            
            // Create new reader
            var directory = FSDirectory.Open(taxonomyPath);
            if (!_taxonomyDirectories.ContainsKey(workspacePath))
            {
                _taxonomyDirectories[workspacePath] = directory;
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
            var taxonomyReader = await GetTaxonomyReaderAsync(workspacePath);
            var facetsCollector = new FacetsCollector();
            
            // Perform the search with facet collection
            var results = FacetsCollector.Search(searcher, query, maxResults, facetsCollector);
            
            // Get facet results
            var facets = new FastTaxonomyFacetCounts(taxonomyReader, _facetsConfig, facetsCollector);
            
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
            
            return facetResults.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing faceted search for workspace {WorkspacePath}", workspacePath);
            return Array.Empty<FacetResult>();
        }
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
    /// Get the taxonomy directory path for a workspace
    /// </summary>
    private string GetTaxonomyPath(string workspacePath)
    {
        // Create taxonomy directory alongside the main index
        // Use the PathResolutionService to properly resolve the index path
        var indexBasePath = _pathResolution.GetIndexPath(workspacePath);
        return Path.Combine(indexBasePath, "taxonomy");
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