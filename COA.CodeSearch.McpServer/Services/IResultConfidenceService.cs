using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service that analyzes search result scores to determine confidence levels
/// and optimal result counts for token efficiency
/// </summary>
public interface IResultConfidenceService
{
    /// <summary>
    /// Analyzes score distribution and returns optimal number of results to include
    /// </summary>
    /// <param name="topDocs">The search results from Lucene</param>
    /// <param name="defaultMax">Default maximum results if no optimization applied</param>
    /// <param name="hasContext">Whether results include context lines (affects token usage)</param>
    /// <returns>Optimal number of results and confidence level</returns>
    ConfidenceAnalysis AnalyzeResults(TopDocs topDocs, int defaultMax, bool hasContext);
}

/// <summary>
/// Result of confidence analysis
/// </summary>
public class ConfidenceAnalysis
{
    /// <summary>
    /// Recommended number of results to include
    /// </summary>
    public int RecommendedCount { get; set; }
    
    /// <summary>
    /// Confidence level: high, medium, low
    /// </summary>
    public string ConfidenceLevel { get; set; } = "medium";
    
    /// <summary>
    /// Optional insight about the score distribution
    /// </summary>
    public string? Insight { get; set; }
    
    /// <summary>
    /// The score gap between top results
    /// </summary>
    public float ScoreGap { get; set; }
    
    /// <summary>
    /// Top score value
    /// </summary>
    public float TopScore { get; set; }
}