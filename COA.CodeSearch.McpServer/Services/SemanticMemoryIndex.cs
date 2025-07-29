using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for indexing memories semantically using embeddings
/// Provides concept-based search capabilities beyond keyword matching
/// </summary>
public class SemanticMemoryIndex
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorIndex _vectorIndex;
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<SemanticMemoryIndex> _logger;

    public SemanticMemoryIndex(
        IEmbeddingService embeddingService,
        IVectorIndex vectorIndex,
        FlexibleMemoryService memoryService,
        ILogger<SemanticMemoryIndex> logger)
    {
        _embeddingService = embeddingService;
        _vectorIndex = vectorIndex;
        _memoryService = memoryService;
        _logger = logger;
    }

    /// <summary>
    /// Index a memory for semantic search
    /// </summary>
    public async Task IndexMemoryAsync(FlexibleMemoryEntry memory, bool isBulkOperation = false)
    {
        try
        {
            // Create searchable content by combining relevant fields
            var searchableContent = CreateSearchableContent(memory);
            _logger.LogDebug("Created searchable content for memory {Id}: {Content}", 
                memory.Id, searchableContent.Substring(0, Math.Min(100, searchableContent.Length)));
            
            // Generate embedding
            var embedding = await _embeddingService.GetEmbeddingAsync(searchableContent);
            _logger.LogDebug("Generated embedding for memory {Id} with {Dimensions} dimensions", 
                memory.Id, embedding.Length);
            
            // Create metadata for filtering and context
            var metadata = new Dictionary<string, object>
            {
                ["type"] = memory.Type,
                ["created"] = memory.Created.Ticks,
                ["modified"] = memory.Modified.Ticks,
                ["isShared"] = memory.IsShared,
                ["accessCount"] = memory.AccessCount
            };

            // Add custom fields to metadata
            if (memory.Fields != null)
            {
                foreach (var field in memory.Fields)
                {
                    // Convert JsonElement to searchable value
                    var value = ExtractSearchableValue(field.Value);
                    if (value != null)
                    {
                        metadata[$"field_{field.Key}"] = value;
                    }
                }
            }

            // Store in vector index
            await _vectorIndex.AddAsync(memory.Id, embedding, metadata);
            
            // Use DEBUG level for bulk operations to reduce log noise, INFO for individual operations
            if (isBulkOperation)
            {
                _logger.LogDebug("Successfully stored memory {Id} in vector index with metadata keys: {Keys}", 
                    memory.Id, string.Join(", ", metadata.Keys));
            }
            else
            {
                _logger.LogInformation("Successfully stored memory {Id} in vector index with metadata keys: {Keys}", 
                    memory.Id, string.Join(", ", metadata.Keys));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index memory {Id} for semantic search", memory.Id);
        }
    }

    /// <summary>
    /// Perform semantic search to find conceptually similar memories
    /// </summary>
    public async Task<List<SemanticSearchResult>> SemanticSearchAsync(
        string query,
        int limit = 50,
        Dictionary<string, object>? filter = null,
        float threshold = 0.1f)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<SemanticSearchResult>();
            }

            // Generate query embedding
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);
            _logger.LogInformation("Generated query embedding for '{Query}' with {Dimensions} dimensions", 
                query, queryEmbedding.Length);
            
            // Get current index stats
            var stats = await _vectorIndex.GetStatsAsync();
            _logger.LogInformation("Vector index contains {Count} vectors", stats.TotalVectors);
            
            // Search for similar vectors
            var vectorMatches = await _vectorIndex.SearchAsync(
                queryEmbedding, 
                limit,
                filter,
                threshold);
            
            _logger.LogInformation("Vector search returned {Count} matches for query '{Query}'", 
                vectorMatches.Count, query);

            // Convert to semantic search results
            var results = vectorMatches.Select(match => new SemanticSearchResult
            {
                MemoryId = match.Id,
                Similarity = match.Score,
                Distance = match.Distance,
                Metadata = match.Metadata,
                SearchQuery = query
            }).ToList();

            _logger.LogDebug("Semantic search for '{Query}' found {Count} results", query, results.Count);
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform semantic search for query: {Query}", query);
            return new List<SemanticSearchResult>();
        }
    }

    /// <summary>
    /// Update semantic index for a memory
    /// </summary>
    public async Task UpdateMemoryIndexAsync(FlexibleMemoryEntry memory)
    {
        await IndexMemoryAsync(memory, isBulkOperation: false); // Vector index handles updates as AddOrUpdate
    }

    /// <summary>
    /// Remove memory from semantic index
    /// </summary>
    public async Task RemoveMemoryFromIndexAsync(string memoryId)
    {
        try
        {
            await _vectorIndex.DeleteAsync(memoryId);
            _logger.LogDebug("Removed memory {Id} from semantic index", memoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove memory {Id} from semantic index", memoryId);
        }
    }

    /// <summary>
    /// Find memories that are semantically similar to a given memory
    /// </summary>
    public async Task<List<SemanticSearchResult>> FindSimilarMemoriesAsync(
        string memoryId,
        int limit = 10,
        float threshold = 0.3f)
    {
        try
        {
            // Get the source memory
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = $"id:{memoryId}",
                MaxResults = 1
            };
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            var sourceMemory = searchResult.Memories.FirstOrDefault();
            if (sourceMemory == null)
            {
                _logger.LogWarning("Source memory {Id} not found for similarity search", memoryId);
                return new List<SemanticSearchResult>();
            }

            // Use the memory's content for similarity search
            var searchableContent = CreateSearchableContent(sourceMemory);
            var results = await SemanticSearchAsync(searchableContent, limit + 1, threshold: threshold);

            // Remove the source memory from results
            return results.Where(r => r.MemoryId != memoryId).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar memories for {Id}", memoryId);
            return new List<SemanticSearchResult>();
        }
    }

    /// <summary>
    /// Bulk index multiple memories for better performance
    /// </summary>
    public async Task BulkIndexMemoriesAsync(IEnumerable<FlexibleMemoryEntry> memories)
    {
        var memoryList = memories.ToList();
        _logger.LogInformation("Starting bulk indexing of {Count} memories", memoryList.Count);

        var tasks = memoryList.Select(memory => IndexMemoryAsync(memory, isBulkOperation: true));
        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed bulk indexing of {Count} memories", memoryList.Count);
    }

    /// <summary>
    /// Get statistics about the semantic index
    /// </summary>
    public async Task<VectorIndexStats> GetIndexStatsAsync()
    {
        return await _vectorIndex.GetStatsAsync();
    }

    /// <summary>
    /// Clear the semantic index
    /// </summary>
    public async Task ClearIndexAsync()
    {
        await _vectorIndex.ClearAsync();
        _logger.LogInformation("Cleared semantic memory index");
    }

    /// <summary>
    /// Create searchable content from memory entry
    /// Combines content, type, and relevant metadata for better embeddings
    /// </summary>
    private string CreateSearchableContent(FlexibleMemoryEntry memory)
    {
        var parts = new List<string>
        {
            memory.Content,
            $"Type: {memory.Type}"
        };

        // Add files involved as context
        if (memory.FilesInvolved?.Length > 0)
        {
            var fileContext = string.Join(", ", memory.FilesInvolved.Select(Path.GetFileName));
            parts.Add($"Files: {fileContext}");
        }

        // Add important custom fields
        if (memory.Fields != null)
        {
            var searchableFields = new[] { "priority", "status", "category", "importance", "urgency" };
            foreach (var fieldName in searchableFields)
            {
                if (memory.Fields.TryGetValue(fieldName, out var fieldValue))
                {
                    var value = ExtractSearchableValue(fieldValue);
                    if (value != null)
                    {
                        parts.Add($"{fieldName}: {value}");
                    }
                }
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Extract searchable string value from JsonElement
    /// </summary>
    private string? ExtractSearchableValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.ToString(),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => null
        };
    }
}

/// <summary>
/// Result from semantic search
/// </summary>
public class SemanticSearchResult
{
    /// <summary>
    /// Memory ID that matched
    /// </summary>
    public string MemoryId { get; set; } = string.Empty;
    
    /// <summary>
    /// Similarity score (0.0 to 1.0, higher is more similar)
    /// </summary>
    public float Similarity { get; set; }
    
    /// <summary>
    /// Distance metric (lower is more similar)
    /// </summary>
    public float Distance { get; set; }
    
    /// <summary>
    /// Metadata associated with the vector
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Original search query
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;
}