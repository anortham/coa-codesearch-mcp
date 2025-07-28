using COA.CodeSearch.McpServer.Models;
using Lucene.Net.Index;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// Scoring factor that applies temporal decay to boost recent memories over older ones
/// </summary>
public class TemporalScoringFactor : IScoringFactor
{
    private readonly TemporalDecayFunction _decayFunction;
    private readonly ILogger? _logger;
    private readonly long _nowTicks;

    public string Name => "TemporalScoring";
    public float Weight { get; set; } = 1.0f;

    /// <summary>
    /// Create a temporal scoring factor with specified decay function
    /// </summary>
    /// <param name="decayFunction">The decay function to apply (null for default)</param>
    /// <param name="logger">Optional logger for debugging</param>
    public TemporalScoringFactor(TemporalDecayFunction? decayFunction = null, ILogger? logger = null)
    {
        _decayFunction = decayFunction ?? TemporalDecayFunction.Default;
        _logger = logger;
        _nowTicks = DateTime.UtcNow.Ticks;
        
        _logger?.LogDebug("Created TemporalScoringFactor with {DecayFunction}", _decayFunction.GetDescription());
    }

    /// <summary>
    /// Create a temporal scoring factor with a specific reference time
    /// </summary>
    /// <param name="referenceTime">The "current" time for age calculations</param>
    /// <param name="decayFunction">The decay function to apply</param>
    /// <param name="logger">Optional logger</param>
    public TemporalScoringFactor(DateTime referenceTime, TemporalDecayFunction? decayFunction = null, ILogger? logger = null)
    {
        _decayFunction = decayFunction ?? TemporalDecayFunction.Default;
        _logger = logger;
        _nowTicks = referenceTime.Ticks;
    }

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            
            // Get temporal data from document
            var createdStr = doc.Get("created");
            var modifiedStr = doc.Get("modified");
            var accessCountStr = doc.Get("access_count");

            if (string.IsNullOrEmpty(createdStr) || !long.TryParse(createdStr, out var createdTicks))
            {
                // No creation date available - return neutral score
                return 0.5f;
            }

            // Use modified date if available, otherwise use created date
            var relevantTicks = createdTicks;
            if (!string.IsNullOrEmpty(modifiedStr) && long.TryParse(modifiedStr, out var modifiedTicks))
            {
                relevantTicks = Math.Max(createdTicks, modifiedTicks);
            }

            // Calculate age in days
            var ageInDays = (_nowTicks - relevantTicks) / (double)TimeSpan.TicksPerDay;

            // Apply temporal decay
            var temporalScore = _decayFunction.Calculate(ageInDays);

            // Add access frequency boost (logarithmic scaling to prevent overwhelming the temporal component)
            var accessBoost = 1.0f;
            if (!string.IsNullOrEmpty(accessCountStr) && int.TryParse(accessCountStr, out var accessCount) && accessCount > 0)
            {
                // Small boost for frequently accessed memories (max 20% boost)
                accessBoost = 1.0f + (float)(Math.Log10(accessCount + 1) * 0.05);
            }

            var finalScore = temporalScore * accessBoost;

            // Log detailed scoring for debugging (only for significant boosts to avoid spam)
            if (finalScore > 0.8f && _logger != null)
            {
                _logger.LogDebug("Temporal scoring - Doc: {DocId}, Age: {Age:F1}d, Temporal: {Temporal:F3}, Access: {AccessBoost:F3}, Final: {Final:F3}",
                    docId, ageInDays, temporalScore, accessBoost, finalScore);
            }

            // Ensure score is in valid range [0.1, 1.0]
            return Math.Max(0.1f, Math.Min(1.0f, finalScore));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error in temporal scoring for doc {DocId}, returning neutral score", docId);
            return 0.5f; // Neutral score on error
        }
    }

    /// <summary>
    /// Create a temporal scoring factor with the specified mode
    /// </summary>
    public static TemporalScoringFactor Create(TemporalScoringMode mode, ILogger? logger = null)
    {
        var decayFunction = mode switch
        {
            TemporalScoringMode.Aggressive => TemporalDecayFunction.Aggressive,
            TemporalScoringMode.Gentle => TemporalDecayFunction.Gentle,
            TemporalScoringMode.Default => TemporalDecayFunction.Default,
            _ => TemporalDecayFunction.Default
        };

        return new TemporalScoringFactor(decayFunction, logger);
    }

    /// <summary>
    /// Get the decay function being used
    /// </summary>
    public TemporalDecayFunction DecayFunction => _decayFunction;

    /// <summary>
    /// Get the reference time for age calculations
    /// </summary>
    public DateTime ReferenceTime => new DateTime(_nowTicks);
}