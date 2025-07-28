using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
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

    public MultiFactorScoreQuery(Query baseQuery, ScoringContext searchContext, params IScoringFactor[] scoringFactors)
    {
        _baseQuery = baseQuery ?? throw new ArgumentNullException(nameof(baseQuery));
        _searchContext = searchContext ?? throw new ArgumentNullException(nameof(searchContext));
        _scoringFactors = scoringFactors?.ToList() ?? new List<IScoringFactor>();
    }

    public override Weight CreateWeight(IndexSearcher searcher)
    {
        var baseWeight = _baseQuery.CreateWeight(searcher);
        return new MultiFactorWeight(this, baseWeight, searcher);
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

        public MultiFactorWeight(MultiFactorScoreQuery query, Weight baseWeight, IndexSearcher searcher)
        {
            _query = query;
            _baseWeight = baseWeight;
            _searcher = searcher;
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

            return new MultiFactorScorer(this, baseScorer, context.Reader, _query._scoringFactors, _query._searchContext);
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
            // Use weighted average: 60% base score, 40% factor scores
            var finalScore = (baseExplanation.Value * 0.6f) + (factorScore * baseExplanation.Value * 0.4f);
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

        public MultiFactorScorer(Weight weight, Scorer baseScorer, IndexReader reader, 
            List<IScoringFactor> scoringFactors, ScoringContext searchContext) 
            : base(weight)
        {
            _baseScorer = baseScorer;
            _reader = reader;
            _scoringFactors = scoringFactors;
            _searchContext = searchContext;
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
            
            foreach (var factor in _scoringFactors)
            {
                var score = factor.CalculateScore(_reader, DocID, _searchContext);
                factorScore += score * factor.Weight;
                totalWeight += factor.Weight;
            }
            
            if (totalWeight > 0)
            {
                factorScore /= totalWeight; // Normalize to 0-1 range
            }
            
            // Combine base score with factor scores
            // Use weighted average: 60% base score, 40% factor scores
            return (baseScore * 0.6f) + (factorScore * baseScore * 0.4f);
        }

        public override long GetCost()
        {
            return _baseScorer.GetCost();
        }
    }
}