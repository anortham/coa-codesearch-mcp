using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Simple in-memory vector index for semantic search
/// Uses cosine similarity for vector matching
/// </summary>
public class InMemoryVectorIndex : IVectorIndex
{
    private readonly ILogger<InMemoryVectorIndex> _logger;
    private readonly ConcurrentDictionary<string, VectorEntry> _vectors;
    private readonly object _statsLock = new();
    private VectorIndexStats _stats;

    public InMemoryVectorIndex(ILogger<InMemoryVectorIndex> logger)
    {
        _logger = logger;
        _vectors = new ConcurrentDictionary<string, VectorEntry>();
        _stats = new VectorIndexStats
        {
            IndexType = "InMemory-Cosine",
            IsTrained = true,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task AddAsync(string id, float[] vector, Dictionary<string, object>? metadata = null)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Vector ID cannot be null or empty", nameof(id));
        
        if (vector == null || vector.Length == 0)
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));

        var entry = new VectorEntry
        {
            Id = id,
            Vector = vector,
            Metadata = metadata ?? new Dictionary<string, object>(),
            AddedAt = DateTime.UtcNow
        };

        _vectors.AddOrUpdate(id, entry, (key, oldValue) => entry);

        await UpdateStatsAsync();
        
        _logger.LogDebug("Added vector for ID: {Id} with dimensions: {Dimensions}", id, vector.Length);
    }

    public async Task<List<VectorMatch>> SearchAsync(
        float[] queryVector, 
        int limit, 
        Dictionary<string, object>? filter = null,
        float threshold = 0.0f)
    {
        if (queryVector == null || queryVector.Length == 0)
            throw new ArgumentException("Query vector cannot be null or empty", nameof(queryVector));

        var results = new List<VectorMatch>();

        await Task.Run(() =>
        {
            var similarities = new List<(string Id, float Similarity, VectorEntry Entry)>();

            foreach (var kvp in _vectors)
            {
                var entry = kvp.Value;
                
                // Apply metadata filter if provided
                if (filter != null && !MatchesFilter(entry.Metadata, filter))
                {
                    continue;
                }

                // Calculate cosine similarity
                var similarity = CalculateCosineSimilarity(queryVector, entry.Vector);
                
                // Apply threshold filter
                if (similarity >= threshold)
                {
                    similarities.Add((entry.Id, similarity, entry));
                }
            }

            // Sort by similarity (descending) and take top results
            var topSimilarities = similarities
                .OrderByDescending(s => s.Similarity)
                .Take(limit);

            foreach (var (id, similarity, entry) in topSimilarities)
            {
                results.Add(new VectorMatch
                {
                    Id = id,
                    Score = similarity,
                    Distance = 1.0f - similarity, // Convert similarity to distance
                    Metadata = new Dictionary<string, object>(entry.Metadata)
                });
            }
        });

        _logger.LogDebug("Vector search found {Count} results for query with {Dimensions} dimensions", 
            results.Count, queryVector.Length);

        return results;
    }

    public async Task UpdateAsync(string id, float[] vector, Dictionary<string, object>? metadata = null)
    {
        await AddAsync(id, vector, metadata); // AddOrUpdate handles both cases
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        _vectors.TryRemove(id, out _);
        await UpdateStatsAsync();
        
        _logger.LogDebug("Deleted vector for ID: {Id}", id);
    }

    public async Task<bool> ExistsAsync(string id)
    {
        return await Task.FromResult(_vectors.ContainsKey(id));
    }

    public async Task<long> GetCountAsync()
    {
        return await Task.FromResult(_vectors.Count);
    }

    public async Task ClearAsync()
    {
        _vectors.Clear();
        await UpdateStatsAsync();
        
        _logger.LogInformation("Cleared all vectors from index");
    }

    public async Task<VectorIndexStats> GetStatsAsync()
    {
        await UpdateStatsAsync();
        return _stats;
    }

    /// <summary>
    /// Calculate cosine similarity between two vectors
    /// Returns a value between -1 and 1, where 1 means identical vectors
    /// </summary>
    private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            _logger.LogWarning("Vector dimensions mismatch: {DimA} vs {DimB}", vectorA.Length, vectorB.Length);
            return 0f;
        }

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0f || normB == 0f)
        {
            return 0f; // Avoid division by zero
        }

        return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Check if metadata matches the provided filter criteria
    /// </summary>
    private bool MatchesFilter(Dictionary<string, object> metadata, Dictionary<string, object> filter)
    {
        foreach (var kvp in filter)
        {
            if (!metadata.TryGetValue(kvp.Key, out var value))
            {
                return false; // Required field not present
            }

            // Simple equality check - could be extended for more complex filters
            if (!value.Equals(kvp.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Update index statistics
    /// </summary>
    private async Task UpdateStatsAsync()
    {
        await Task.Run(() =>
        {
            lock (_statsLock)
            {
                _stats.TotalVectors = _vectors.Count;
                _stats.LastUpdated = DateTime.UtcNow;

                // Calculate dimensions from first vector
                if (_vectors.Any())
                {
                    var firstVector = _vectors.Values.First();
                    _stats.Dimensions = firstVector.Vector.Length;
                }

                // Estimate memory usage (rough calculation)
                var avgVectorSize = _stats.Dimensions * sizeof(float); // Vector data
                var avgMetadataSize = 100; // Estimated metadata size
                var avgEntrySize = avgVectorSize + avgMetadataSize + 64; // Including overhead
                
                _stats.MemoryUsageBytes = _stats.TotalVectors * avgEntrySize;
            }
        });
    }
}

/// <summary>
/// Internal representation of a vector entry
/// </summary>
internal class VectorEntry
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime AddedAt { get; set; }
}