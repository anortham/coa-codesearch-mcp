using Lucene.Net.Index;

namespace COA.CodeSearch.Next.McpServer.Scoring;

/// <summary>
/// Boosts recently modified files, as they are often more relevant for active development.
/// Uses a decay function to gradually reduce the boost for older files.
/// </summary>
public class RecencyBoostFactor : IScoringFactor
{
    private readonly TimeSpan _halfLife;
    private readonly DateTime _referenceTime;

    public string Name => "RecencyBoost";
    public float Weight { get; set; } = 0.3f;

    /// <summary>
    /// Creates a recency boost factor
    /// </summary>
    /// <param name="halfLife">The time period after which the boost is halved (default: 7 days)</param>
    public RecencyBoostFactor(TimeSpan? halfLife = null)
    {
        _halfLife = halfLife ?? TimeSpan.FromDays(7);
        _referenceTime = DateTime.UtcNow;
    }

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            var lastModifiedStr = doc.Get("lastModified");
            
            if (string.IsNullOrEmpty(lastModifiedStr) || !long.TryParse(lastModifiedStr, out var ticks))
                return 0.5f; // Neutral score if no date

            var lastModified = new DateTime(ticks, DateTimeKind.Utc);
            var age = _referenceTime - lastModified;
            
            // Files modified in the future get neutral score (clock skew handling)
            if (age < TimeSpan.Zero)
                return 0.5f;

            // Use exponential decay function
            // Score = e^(-λt) where λ = ln(2)/halfLife
            var lambda = Math.Log(2) / _halfLife.TotalDays;
            var score = (float)Math.Exp(-lambda * age.TotalDays);

            // Apply different decay rates based on file type
            var extension = doc.Get("extension")?.ToLowerInvariant() ?? "";
            score = AdjustScoreByFileType(score, extension, age);

            // Ensure score is in valid range
            return Math.Max(0.1f, Math.Min(1.0f, score));
        }
        catch (Exception)
        {
            return 0.5f; // Neutral score on error
        }
    }

    private float AdjustScoreByFileType(float baseScore, string extension, TimeSpan age)
    {
        // Configuration files and documentation might be relevant even if old
        var stableFileTypes = new HashSet<string> { ".md", ".json", ".xml", ".config", ".yaml", ".yml" };
        if (stableFileTypes.Contains(extension))
        {
            // Use slower decay for stable file types
            return baseScore + (1 - baseScore) * 0.3f;
        }

        // Test files might be less relevant if very old
        var testExtensions = new HashSet<string> { ".test.cs", ".tests.cs", ".spec.cs" };
        if (testExtensions.Any(ext => extension.EndsWith(ext)))
        {
            // Use faster decay for test files
            return baseScore * 0.8f;
        }

        // Build artifacts should have very fast decay
        var buildArtifacts = new HashSet<string> { ".dll", ".exe", ".pdb", ".obj" };
        if (buildArtifacts.Contains(extension))
        {
            return baseScore * 0.5f;
        }

        // Special boost for very recent files (modified in last 24 hours)
        if (age < TimeSpan.FromHours(24))
        {
            return Math.Min(1.0f, baseScore * 1.2f);
        }

        return baseScore;
    }
}