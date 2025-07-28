using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// Adjusts scores based on the file path, preferring certain directories over others.
/// For example, preferring source files over test files, or main code over examples.
/// </summary>
public class PathRelevanceFactor : IScoringFactor
{
    private readonly Dictionary<string, float> _directoryWeights;
    private readonly HashSet<string> _preferredPaths;
    private readonly HashSet<string> _deprioritizedPaths;

    public string Name => "PathRelevance";
    public float Weight { get; set; } = 0.5f;

    public PathRelevanceFactor()
    {
        // Default directory weights
        _directoryWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            { "src", 1.0f },
            { "source", 1.0f },
            { "lib", 0.9f },
            { "core", 0.9f },
            { "main", 0.9f },
            { "app", 0.8f },
            { "services", 0.8f },
            { "models", 0.8f },
            { "controllers", 0.8f },
            { "views", 0.7f },
            { "utilities", 0.7f },
            { "helpers", 0.7f },
            { "test", 0.4f },
            { "tests", 0.4f },
            { "spec", 0.4f },
            { "specs", 0.4f },
            { "examples", 0.3f },
            { "samples", 0.3f },
            { "demo", 0.3f },
            { "docs", 0.2f },
            { "documentation", 0.2f },
            { "node_modules", 0.1f },
            { "packages", 0.1f },
            { "bin", 0.1f },
            { "obj", 0.1f },
            { "debug", 0.1f },
            { "release", 0.1f },
            { "temp", 0.1f },
            { "tmp", 0.1f },
            { "cache", 0.1f },
            { "backup", 0.1f }
        };

        _preferredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Services", "Models", "Controllers", "Core", "Domain", "Infrastructure",
            "Application", "Business", "Logic", "Handlers", "Managers"
        };

        _deprioritizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", ".idea", "node_modules", "packages",
            "bin", "obj", "dist", "build", "out", "target"
        };
    }

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            var relativePath = doc.Get("relativePath") ?? "";
            
            if (string.IsNullOrEmpty(relativePath))
                return 0.5f; // Neutral score if no path

            // Split path into components
            var pathParts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Check for deprioritized paths
            if (pathParts.Any(part => _deprioritizedPaths.Contains(part)))
            {
                return 0.1f; // Very low score for deprioritized paths
            }

            var totalScore = 0f;
            var componentCount = 0;

            // Calculate weighted average of path components
            foreach (var part in pathParts.Take(pathParts.Length - 1)) // Exclude filename
            {
                if (_directoryWeights.TryGetValue(part, out var weight))
                {
                    totalScore += weight;
                    componentCount++;
                }
                else if (_preferredPaths.Contains(part))
                {
                    totalScore += 0.8f;
                    componentCount++;
                }
                else
                {
                    // Neutral weight for unknown directories
                    totalScore += 0.5f;
                    componentCount++;
                }
            }

            // Special handling for test files
            var filename = pathParts.LastOrDefault() ?? "";
            if (IsTestFile(filename))
            {
                // Reduce score for test files unless searching for tests
                if (!searchContext.QueryText.Contains("test", StringComparison.OrdinalIgnoreCase))
                {
                    totalScore *= 0.5f;
                }
            }

            // Calculate average score
            var averageScore = componentCount > 0 ? totalScore / componentCount : 0.5f;

            // Boost for shorter paths (less nesting usually means more important)
            var depthPenalty = Math.Max(0.7f, 1.0f - (pathParts.Length - 2) * 0.05f);
            
            return Math.Min(1.0f, averageScore * depthPenalty);
        }
        catch (Exception)
        {
            return 0.5f; // Neutral score on error
        }
    }

    private bool IsTestFile(string filename)
    {
        var lower = filename.ToLowerInvariant();
        return lower.Contains("test") || 
               lower.Contains("spec") ||
               lower.EndsWith(".test.cs") ||
               lower.EndsWith(".tests.cs") ||
               lower.EndsWith("tests.cs") ||
               lower.EndsWith("test.cs");
    }
}