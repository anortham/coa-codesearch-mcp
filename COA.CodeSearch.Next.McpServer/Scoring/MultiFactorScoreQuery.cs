using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.Scoring;

/// <summary>
/// Custom query that applies multiple scoring factors to search results.
/// This allows for sophisticated relevance tuning beyond Lucene's default scoring.
/// </summary>
public class MultiFactorScoreQuery : CustomScoreQuery
{
    private readonly List<IScoringFactor> _scoringFactors;
    private readonly ScoringContext _scoringContext;
    private readonly ILogger? _logger;

    public MultiFactorScoreQuery(Query subQuery, ScoringContext context, ILogger? logger = null) 
        : base(subQuery)
    {
        _scoringContext = context;
        _logger = logger;
        _scoringFactors = new List<IScoringFactor>();
    }

    public void AddScoringFactor(IScoringFactor factor)
    {
        _scoringFactors.Add(factor);
    }

    protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
    {
        return new MultiFactorScoreProvider(context, _scoringFactors, _scoringContext, _logger);
    }

    private class MultiFactorScoreProvider : CustomScoreProvider
    {
        private readonly List<IScoringFactor> _factors;
        private readonly ScoringContext _context;
        private readonly ILogger? _logger;

        public MultiFactorScoreProvider(
            AtomicReaderContext context, 
            List<IScoringFactor> factors, 
            ScoringContext scoringContext,
            ILogger? logger) 
            : base(context)
        {
            _factors = factors;
            _context = scoringContext;
            _logger = logger;
        }

        public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
        {
            if (_factors.Count == 0)
                return subQueryScore;

            var factorScore = 0f;
            var totalWeight = 0f;

            foreach (var factor in _factors)
            {
                try
                {
                    var score = factor.CalculateScore(m_context.Reader, doc, _context);
                    factorScore += score * factor.Weight;
                    totalWeight += factor.Weight;

                    if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Factor {FactorName}: Score={Score:F3}, Weight={Weight:F3}, Contribution={Contribution:F3}",
                            factor.Name, score, factor.Weight, score * factor.Weight);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error calculating score for factor {FactorName}", factor.Name);
                }
            }

            // Normalize by total weight
            if (totalWeight > 0)
            {
                factorScore /= totalWeight;
            }

            // Combine with original Lucene score
            // Use weighted average: 60% Lucene score, 40% custom factors
            var finalScore = (subQueryScore * 0.6f) + (factorScore * 0.4f);

            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Final score calculation: Lucene={LuceneScore:F3}, Factors={FactorScore:F3}, Final={FinalScore:F3}",
                    subQueryScore, factorScore, finalScore);
            }

            return finalScore;
        }
    }
}