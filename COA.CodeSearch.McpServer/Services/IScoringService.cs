using COA.CodeSearch.McpServer.Scoring;
using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for creating multi-factor scoring queries to improve search relevance
/// </summary>
public interface IScoringService
{
    /// <summary>
    /// Wraps a base query with multi-factor scoring
    /// </summary>
    /// <param name="baseQuery">The original Lucene query</param>
    /// <param name="searchContext">Context information about the search</param>
    /// <param name="enabledFactors">Which scoring factors to enable (null = use defaults)</param>
    /// <returns>A query with multi-factor scoring applied</returns>
    Query CreateScoredQuery(Query baseQuery, ScoringContext searchContext, HashSet<string>? enabledFactors = null);
    
    /// <summary>
    /// Gets the available scoring factors
    /// </summary>
    Dictionary<string, IScoringFactor> GetAvailableFactors();
    
    /// <summary>
    /// Updates the weight for a specific scoring factor
    /// </summary>
    void UpdateFactorWeight(string factorName, float weight);
}