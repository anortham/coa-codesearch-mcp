using System.Collections.Generic;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for vector storage and similarity search
/// </summary>
public interface IVectorIndex
{
    /// <summary>
    /// Add a vector with metadata to the index
    /// </summary>
    /// <param name="id">Unique identifier for the vector</param>
    /// <param name="vector">Embedding vector</param>
    /// <param name="metadata">Optional metadata associated with the vector</param>
    Task AddAsync(string id, float[] vector, Dictionary<string, object>? metadata = null);
    
    /// <summary>
    /// Search for similar vectors
    /// </summary>
    /// <param name="queryVector">Query vector to find similarities for</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <param name="filter">Optional metadata filter criteria</param>
    /// <param name="threshold">Minimum similarity threshold (0.0 to 1.0)</param>
    /// <returns>List of matching vectors ordered by similarity</returns>
    Task<List<VectorMatch>> SearchAsync(
        float[] queryVector, 
        int limit, 
        Dictionary<string, object>? filter = null,
        float threshold = 0.0f);
    
    /// <summary>
    /// Update an existing vector and its metadata
    /// </summary>
    /// <param name="id">Vector identifier</param>
    /// <param name="vector">New embedding vector</param>
    /// <param name="metadata">New metadata</param>
    Task UpdateAsync(string id, float[] vector, Dictionary<string, object>? metadata = null);
    
    /// <summary>
    /// Delete a vector from the index
    /// </summary>
    /// <param name="id">Vector identifier to delete</param>
    Task DeleteAsync(string id);
    
    /// <summary>
    /// Check if a vector exists in the index
    /// </summary>
    /// <param name="id">Vector identifier to check</param>
    /// <returns>True if the vector exists</returns>
    Task<bool> ExistsAsync(string id);
    
    /// <summary>
    /// Get count of vectors in the index
    /// </summary>
    Task<long> GetCountAsync();
    
    /// <summary>
    /// Clear all vectors from the index
    /// </summary>
    Task ClearAsync();
    
    /// <summary>
    /// Get statistics about the index
    /// </summary>
    Task<VectorIndexStats> GetStatsAsync();
}

/// <summary>
/// Represents a vector match result from similarity search
/// </summary>
public class VectorMatch
{
    /// <summary>
    /// Unique identifier of the matched vector
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Similarity score (higher is better, typically 0.0 to 1.0)
    /// </summary>
    public float Score { get; set; }
    
    /// <summary>
    /// Distance metric (lower is better, depends on index type)
    /// </summary>
    public float Distance { get; set; }
    
    /// <summary>
    /// Metadata associated with the matched vector
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// The actual vector (optional, only if requested)
    /// </summary>
    public float[]? Vector { get; set; }
}

/// <summary>
/// Statistics about a vector index
/// </summary>
public class VectorIndexStats
{
    /// <summary>
    /// Total number of vectors in the index
    /// </summary>
    public long TotalVectors { get; set; }
    
    /// <summary>
    /// Dimensionality of vectors in the index
    /// </summary>
    public int Dimensions { get; set; }
    
    /// <summary>
    /// Index type or algorithm being used
    /// </summary>
    public string IndexType { get; set; } = string.Empty;
    
    /// <summary>
    /// Memory usage of the index in bytes
    /// </summary>
    public long MemoryUsageBytes { get; set; }
    
    /// <summary>
    /// Whether the index is trained/ready for search
    /// </summary>
    public bool IsTrained { get; set; }
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; }
}