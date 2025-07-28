using System.Collections.Generic;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for generating text embeddings for semantic search
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Get embedding vector for a single text
    /// </summary>
    /// <param name="text">Text to embed</param>
    /// <returns>Embedding vector as float array</returns>
    Task<float[]> GetEmbeddingAsync(string text);
    
    /// <summary>
    /// Get embedding vectors for multiple texts (batched for efficiency)
    /// </summary>
    /// <param name="texts">List of texts to embed</param>
    /// <returns>List of embedding vectors</returns>
    Task<List<float[]>> GetEmbeddingsAsync(List<string> texts);
    
    /// <summary>
    /// Get the dimensionality of embeddings produced by this service
    /// </summary>
    int EmbeddingDimensions { get; }
    
    /// <summary>
    /// Get the model name/identifier used by this service
    /// </summary>
    string ModelName { get; }
    
    /// <summary>
    /// Check if the service is available and properly initialized
    /// </summary>
    Task<bool> IsAvailableAsync();
}