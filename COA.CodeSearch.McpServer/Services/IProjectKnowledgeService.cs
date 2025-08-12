using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for integrating with ProjectKnowledge MCP via HTTP API
/// </summary>
public interface IProjectKnowledgeService
{
    /// <summary>
    /// Store knowledge in ProjectKnowledge via HTTP API
    /// </summary>
    /// <param name="content">Knowledge content</param>
    /// <param name="type">Knowledge type (TechnicalDebt, ProjectInsight, WorkNote)</param>
    /// <param name="metadata">Additional metadata</param>
    /// <param name="tags">Tags for categorization</param>
    /// <param name="priority">Priority level (low, normal, high, critical)</param>
    /// <returns>Knowledge ID if successful, null if failed</returns>
    Task<string?> StoreKnowledgeAsync(
        string content, 
        string type = "TechnicalDebt",
        Dictionary<string, object>? metadata = null,
        string[]? tags = null,
        string? priority = null);

    /// <summary>
    /// Check if ProjectKnowledge API is available
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Get knowledge entries related to search results
    /// </summary>
    Task<IEnumerable<KnowledgeReference>?> SearchKnowledgeAsync(string query);
}

/// <summary>
/// Reference to a knowledge entry in ProjectKnowledge
/// </summary>
public class KnowledgeReference
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string[]? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
}