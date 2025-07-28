using Lucene.Net.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Analyzes search result scores to determine optimal result counts for token efficiency
/// </summary>
public class ResultConfidenceService : IResultConfidenceService
{
    private readonly ILogger<ResultConfidenceService> _logger;
    private readonly IConfiguration _configuration;
    
    // Configuration keys
    private const string ConfigPrefix = "ResultConfidence:";
    private const string HighConfidenceThresholdKey = ConfigPrefix + "HighConfidenceThreshold";
    private const string MediumConfidenceThresholdKey = ConfigPrefix + "MediumConfidenceThreshold";
    private const string ScoreGapThresholdKey = ConfigPrefix + "ScoreGapThreshold";
    
    // Default thresholds
    private readonly float _highConfidenceThreshold;
    private readonly float _mediumConfidenceThreshold;
    private readonly float _scoreGapThreshold;
    
    public ResultConfidenceService(ILogger<ResultConfidenceService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Load thresholds from configuration with defaults
        _highConfidenceThreshold = configuration.GetValue<float>(HighConfidenceThresholdKey, 0.8f);
        _mediumConfidenceThreshold = configuration.GetValue<float>(MediumConfidenceThresholdKey, 0.5f);
        _scoreGapThreshold = configuration.GetValue<float>(ScoreGapThresholdKey, 0.3f);
    }
    
    public ConfidenceAnalysis AnalyzeResults(TopDocs topDocs, int defaultMax, bool hasContext)
    {
        if (topDocs.ScoreDocs.Length == 0)
        {
            return new ConfidenceAnalysis
            {
                RecommendedCount = 0,
                ConfidenceLevel = "none",
                TopScore = 0,
                ScoreGap = 0
            };
        }
        
        var topScore = topDocs.ScoreDocs[0].Score;
        var scoreGap = 0f;
        
        if (topDocs.ScoreDocs.Length > 1)
        {
            scoreGap = topScore - topDocs.ScoreDocs[1].Score;
        }
        
        // Determine confidence level and recommended count
        int recommendedCount;
        string confidenceLevel;
        string? insight = null;
        
        if (topScore > _highConfidenceThreshold && scoreGap > _scoreGapThreshold)
        {
            // Very confident - top result is significantly better
            confidenceLevel = "high";
            recommendedCount = hasContext ? 2 : 3;
            insight = "Top result has high confidence";
            _logger.LogDebug("High confidence: topScore={TopScore:F2}, gap={Gap:F2}", topScore, scoreGap);
        }
        else if (topScore > _mediumConfidenceThreshold)
        {
            // Medium confidence - show a moderate number
            confidenceLevel = "medium";
            recommendedCount = hasContext ? 3 : 5;
            
            // Check if scores are very close (indicating multiple good matches)
            if (scoreGap < 0.1f && topDocs.ScoreDocs.Length > 3)
            {
                var thirdScore = topDocs.ScoreDocs[2].Score;
                if (topScore - thirdScore < 0.15f)
                {
                    recommendedCount = hasContext ? 4 : 6;
                    insight = "Multiple results with similar high scores";
                }
            }
        }
        else
        {
            // Low confidence - show more results or suggest refinement
            confidenceLevel = "low";
            recommendedCount = hasContext ? 5 : 8;
            insight = "Low confidence scores - consider refining search";
            _logger.LogDebug("Low confidence: topScore={TopScore:F2}", topScore);
        }
        
        // Apply context-based adjustments
        if (hasContext)
        {
            // Context takes more tokens, so be more conservative
            recommendedCount = Math.Min(recommendedCount, 5);
        }
        else
        {
            // Without context, we can afford more results
            recommendedCount = Math.Min(recommendedCount, 10);
        }
        
        // Never exceed the requested maximum or available results
        recommendedCount = Math.Min(recommendedCount, Math.Min(defaultMax, topDocs.ScoreDocs.Length));
        
        return new ConfidenceAnalysis
        {
            RecommendedCount = recommendedCount,
            ConfidenceLevel = confidenceLevel,
            TopScore = topScore,
            ScoreGap = scoreGap,
            Insight = insight
        };
    }
}