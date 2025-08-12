using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// Interface for custom scoring factors that can be combined to create multi-factor scoring
/// </summary>
public interface IScoringFactor
{
    /// <summary>
    /// Name of this scoring factor for debugging and configuration
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Weight of this factor in the overall score (0.0 to 1.0)
    /// </summary>
    float Weight { get; set; }
    
    /// <summary>
    /// Calculate the score contribution for this factor
    /// </summary>
    /// <param name="reader">The index reader</param>
    /// <param name="docId">The document ID being scored</param>
    /// <param name="searchContext">Context information about the search</param>
    /// <returns>Score contribution (0.0 to 1.0)</returns>
    float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext);
}

/// <summary>
/// Context information passed to scoring factors
/// </summary>
public class ScoringContext
{
    /// <summary>
    /// The original search query text
    /// </summary>
    public string QueryText { get; set; } = "";
    
    /// <summary>
    /// The type of search being performed
    /// </summary>
    public string SearchType { get; set; } = "standard";
    
    /// <summary>
    /// The workspace path being searched
    /// </summary>
    public string WorkspacePath { get; set; } = "";
    
    /// <summary>
    /// Additional context data that scoring factors might need
    /// </summary>
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}