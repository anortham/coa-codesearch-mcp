using COA.CodeSearch.McpServer.Scoring;
using Lucene.Net.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Default implementation of the scoring service
/// </summary>
public class ScoringService : IScoringService
{
    private readonly ILogger<ScoringService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, IScoringFactor> _scoringFactors;
    private readonly HashSet<string> _defaultEnabledFactors;

    public ScoringService(ILogger<ScoringService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Initialize scoring factors
        _scoringFactors = new Dictionary<string, IScoringFactor>
        {
            { "ExactMatch", new ExactMatchBoostFactor() },
            { "FilenameRelevance", new FilenameRelevanceFactor() },
            { "PathRelevance", new PathRelevanceFactor() },
            { "InterfaceImplementation", new InterfaceImplementationFactor() },
            { "RecencyBoost", new RecencyBoostFactor() },
            { "FileTypeRelevance", new FileTypeRelevanceFactor() },
            { "TemporalScoring", new TemporalScoringFactor(null, logger) }
        };

        // Load configuration for factor weights
        LoadFactorWeights();

        // Default enabled factors (all enabled by default)
        _defaultEnabledFactors = new HashSet<string>(_scoringFactors.Keys);
    }

    public Query CreateScoredQuery(Query baseQuery, ScoringContext searchContext, HashSet<string>? enabledFactors = null)
    {
        // Use provided factors or defaults
        var factorsToUse = enabledFactors ?? _defaultEnabledFactors;
        
        // Get the enabled scoring factors
        var activeScoringFactors = _scoringFactors
            .Where(kvp => factorsToUse.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToArray();

        if (activeScoringFactors.Length == 0)
        {
            _logger.LogDebug("No scoring factors enabled, returning base query");
            return baseQuery;
        }

        _logger.LogDebug("Creating scored query with factors: {Factors}", 
            string.Join(", ", activeScoringFactors.Select(f => $"{f.Name}:{f.Weight:F2}")));

        // Wrap the base query with multi-factor scoring
        return new MultiFactorScoreQuery(baseQuery, searchContext, activeScoringFactors);
    }

    public Dictionary<string, IScoringFactor> GetAvailableFactors()
    {
        return new Dictionary<string, IScoringFactor>(_scoringFactors);
    }

    public void UpdateFactorWeight(string factorName, float weight)
    {
        if (_scoringFactors.TryGetValue(factorName, out var factor))
        {
            factor.Weight = Math.Max(0f, Math.Min(1f, weight)); // Clamp to 0-1
            _logger.LogInformation("Updated weight for {Factor} to {Weight:F2}", factorName, factor.Weight);
        }
        else
        {
            _logger.LogWarning("Scoring factor {Factor} not found", factorName);
        }
    }

    private void LoadFactorWeights()
    {
        var scoringSection = _configuration.GetSection("Scoring");
        if (!scoringSection.Exists())
        {
            _logger.LogDebug("No scoring configuration found, using default weights");
            return;
        }

        foreach (var factor in _scoringFactors)
        {
            var weightConfig = scoringSection[$"{factor.Key}:Weight"];
            if (!string.IsNullOrEmpty(weightConfig) && float.TryParse(weightConfig, out var weight))
            {
                factor.Value.Weight = Math.Max(0f, Math.Min(1f, weight));
                _logger.LogDebug("Loaded weight {Weight:F2} for factor {Factor}", weight, factor.Key);
            }
        }

        // Check for disabled factors
        var disabledFactors = scoringSection.GetSection("DisabledFactors").Get<string[]>();
        if (disabledFactors != null)
        {
            foreach (var disabled in disabledFactors)
            {
                _defaultEnabledFactors.Remove(disabled);
                _logger.LogDebug("Disabled scoring factor: {Factor}", disabled);
            }
        }
    }
}