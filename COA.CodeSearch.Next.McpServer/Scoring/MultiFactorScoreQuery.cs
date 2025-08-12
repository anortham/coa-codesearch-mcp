using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text;

namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// A custom query that wraps another query and applies multiple scoring factors
/// to produce a more accurate relevance score for AI-focused search results.
/// </summary>
public class MultiFactorScoreQuery : Query
{
    private readonly Query _baseQuery;
    private readonly List<IScoringFactor> _scoringFactors;
    private readonly ScoringContext _searchContext;
    private readonly ILogger? _logger;

    public MultiFactorScoreQuery(Query baseQuery, ScoringContext searchContext, ILogger? logger, params IScoringFactor[] scoringFactors)
    {
        _baseQuery = baseQuery ?? throw new ArgumentNullException(nameof(baseQuery));
        _searchContext = searchContext ?? throw new ArgumentNullException(nameof(searchContext));
        _scoringFactors = scoringFactors?.ToList() ?? new List<IScoringFactor>();
        _logger = logger;
    }

    public void AddScoringFactor(IScoringFactor factor)
    {
        _scoringFactors.Add(factor);
    }

    public override Weight CreateWeight(IndexSearcher searcher)
    {
        try
        {
            // Always try to rewrite the query first - this handles all query types that need rewriting
            var rewrittenQuery = _baseQuery.Rewrite(searcher.IndexReader);
            
            // If the query changed after rewriting, use the rewritten version
            if (!ReferenceEquals(rewrittenQuery, _baseQuery))
            {
                var baseWeight = rewrittenQuery.CreateWeight(searcher);
                return new MultiFactorWeight(this, baseWeight, searcher, _logger);
            }
            
            // Otherwise, use the original query
            var weight = _baseQuery.CreateWeight(searcher);
            return new MultiFactorWeight(this, weight, searcher, _logger);
        }
        catch (NotSupportedException ex) when (ex.Message.Contains("does not implement createWeight"))
        {
            // If CreateWeight fails, try to rewrite more aggressively
            var rewrittenQuery = _baseQuery.Rewrite(searcher.IndexReader);
            var baseWeight = rewrittenQuery.CreateWeight(searcher);
            return new MultiFactorWeight(this, baseWeight, searcher, _logger);
        }
    }

    public override string ToString(string field)
    {
        var sb = new StringBuilder();
        sb.Append("MultiFactorScore(");
        sb.Append(_baseQuery.ToString(field));
        sb.Append(", factors=[");
        sb.Append(string.Join(", ", _scoringFactors.Select(f => $"{f.Name}:{f.Weight:F2}")));
        sb.Append("])");
        return sb.ToString();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not MultiFactorScoreQuery other) return false;
        return _baseQuery.Equals(other._baseQuery) && 
               _scoringFactors.SequenceEqual(other._scoringFactors);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_baseQuery, _scoringFactors.Count);
    }

    private class MultiFactorWeight : Weight
    {
        private readonly MultiFactorScoreQuery _query;
        private readonly Weight _baseWeight;
        private readonly IndexSearcher _searcher;
        private readonly ILogger? _logger;

        public MultiFactorWeight(MultiFactorScoreQuery query, Weight baseWeight, IndexSearcher searcher, ILogger? logger)
        {
            _query = query;
            _baseWeight = baseWeight;
            _searcher = searcher;
            _logger = logger;
        }

        public override Query Query => _query;

        public override float GetValueForNormalization()
        {
            return _baseWeight.GetValueForNormalization();
        }

        public override void Normalize(float norm, float boost)
        {
            _baseWeight.Normalize(norm, boost);
        }

        public override Scorer? GetScorer(AtomicReaderContext context, IBits acceptDocs)
        {
            var baseScorer = _baseWeight.GetScorer(context, acceptDocs);
            if (baseScorer == null) return null;

            return new MultiFactorScorer(this, baseScorer, context.Reader, _query._scoringFactors, _query._searchContext, _logger);
        }

        public override Explanation Explain(AtomicReaderContext context, int doc)
        {
            var baseExplanation = _baseWeight.Explain(context, doc);
            if (!baseExplanation.IsMatch) return baseExplanation;

            var result = new ComplexExplanation(true, baseExplanation.Value, "multi-factor score");

            result.AddDetail(baseExplanation);

            var factorScore = 0f;
            var totalWeight = 0f;

            foreach (var factor in _query._scoringFactors)
            {
                var score = factor.CalculateScore(context.Reader, doc, _query._searchContext);
                factorScore += score * factor.Weight;
                totalWeight += factor.Weight;

                result.AddDetail(new Explanation(score * factor.Weight, $"{factor.Name} (weight={factor.Weight:F2})"));
            }

            if (totalWeight > 0)
            {
                factorScore /= totalWeight; // Normalize to 0-1 range
            }

            // Combine base score with factor scores
            // For codebase searches, give more weight to factor scores to prioritize structure over pure text matching
            // Use weighted average: 40% base score, 60% factor-adjusted score
            var finalScore = (baseExplanation.Value * 0.4f) + (factorScore * baseExplanation.Value * 0.6f);
            result.Value = finalScore;

            return result;
        }
    }

    private class MultiFactorScorer : Scorer
    {
        private readonly Scorer _baseScorer;
        private readonly IndexReader _reader;
        private readonly List<IScoringFactor> _scoringFactors;
        private readonly ScoringContext _searchContext;
        private readonly ILogger? _logger;

        public MultiFactorScorer(Weight weight, Scorer baseScorer, IndexReader reader, 
            List<IScoringFactor> scoringFactors, ScoringContext searchContext, ILogger? logger) 
            : base(weight)
        {
            _baseScorer = baseScorer;
            _reader = reader;
            _scoringFactors = scoringFactors;
            _searchContext = searchContext;
            _logger = logger;
        }

        public override int DocID => _baseScorer.DocID;

        public override int Freq => _baseScorer.Freq;

        public override int NextDoc()
        {
            return _baseScorer.NextDoc();
        }

        public override int Advance(int target)
        {
            return _baseScorer.Advance(target);
        }

        public override float GetScore()
        {
            var baseScore = _baseScorer.GetScore();
            
            // Calculate factor scores
            var factorScore = 0f;
            var totalWeight = 0f;
            var factorDetails = new List<string>();
            
            foreach (var factor in _scoringFactors)
            {
                var score = factor.CalculateScore(_reader, DocID, _searchContext);
                var weightedScore = score * factor.Weight;
                factorScore += weightedScore;
                totalWeight += factor.Weight;
                
                factorDetails.Add($"{factor.Name}={score:F3}*{factor.Weight:F2}={weightedScore:F3}");
            }
            
            if (totalWeight > 0)
            {
                factorScore /= totalWeight; // Normalize
            }
            
            // Combine base score with factor scores
            // For codebase searches, give significant weight to factor scores
            // Use weighted average: 60% base score, 40% factor-adjusted score
            var finalScore = (baseScore * 0.6f) + (factorScore * 0.4f);
            
            // Log detailed scoring for high-scoring documents (for debugging)
            if (finalScore > 0.5f && _logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Doc {DocId}: Base={BaseScore:F3}, Factors=[{Factors}], Final={FinalScore:F3}",
                    DocID, baseScore, string.Join(", ", factorDetails), finalScore);
            }
            
            return finalScore;
        }

        public override long GetCost()
        {
            return _baseScorer.GetCost();
        }
    }
}