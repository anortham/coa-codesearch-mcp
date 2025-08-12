using Lucene.Net.Index;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger? _logger;

    public string Name => "PathRelevance";
    public float Weight { get; set; } = 0.7f; // Increased weight for codebase-aware path scoring

    public PathRelevanceFactor(ILogger? logger = null)
    {
        _logger = logger;
        
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
            
            // Trace logging for path parsing (only in very verbose mode)
            if (_logger != null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("PathRelevance: Path parsing - RelativePath: '{RelativePath}', PathParts: [{PathParts}]", 
                    relativePath, string.Join(", ", pathParts.Select(p => $"'{p}'")));
            }
            
            // Check for deprioritized paths
            if (pathParts.Any(part => _deprioritizedPaths.Contains(part)))
            {
                return 0.1f; // Very low score for deprioritized paths
            }

            // Codebase-aware scoring: Start with production code assumption
            var baseScore = 1.0f;
            var isTestRelated = false;
            var filename = pathParts.LastOrDefault() ?? "";

            // First check: Is this a test file by filename?
            if (IsTestFile(filename))
            {
                isTestRelated = true;
            }

            // Second check: Is this in test-related directories?
            var hasTestDirectory = pathParts.Any(part => 
                part.Equals("test", StringComparison.OrdinalIgnoreCase) || 
                part.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("spec", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("specs", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".tests", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".spec", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".specs", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("spec", StringComparison.OrdinalIgnoreCase));

            // Debug logging for test directory detection
            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                var testParts = pathParts.Where(part => 
                    part.Equals("test", StringComparison.OrdinalIgnoreCase) || 
                    part.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("spec", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("specs", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".tests", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".spec", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".specs", StringComparison.OrdinalIgnoreCase) ||
                    part.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                    part.Contains("spec", StringComparison.OrdinalIgnoreCase)).ToList();
                
                _logger.LogTrace("PathRelevance: Test detection - HasTestDirectory: {HasTestDirectory}, MatchingParts: [{MatchingParts}]", 
                    hasTestDirectory, string.Join(", ", testParts.Select(p => $"'{p}'")));
            }

            if (hasTestDirectory)
            {
                isTestRelated = true;
            }

            // Apply strong test penalty for codebase searches (not searching for "test")
            if (isTestRelated && !searchContext.QueryText.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                // Much stronger penalty for test files - they should rank significantly lower
                baseScore *= 0.15f; // Reduced from 0.5f to 0.15f for stronger de-prioritization
            }

            // Calculate directory path score with multiplicative approach for test paths
            var pathScore = 1.0f;

            foreach (var part in pathParts.Take(pathParts.Length - 1)) // Exclude filename
            {
                if (_directoryWeights.TryGetValue(part, out var weight))
                {
                    if (weight < 0.5f) // Test directories get multiplicative penalty
                    {
                        pathScore *= weight;
                    }
                    else
                    {
                        pathScore = Math.Max(pathScore, weight); // Production directories get boost
                    }
                }
                else if (_preferredPaths.Contains(part))
                {
                    pathScore = Math.Max(pathScore, 0.9f); // Strong boost for preferred paths
                }
            }

            // Special boost for main production code patterns
            if (HasProductionCodePatterns(relativePath, filename))
            {
                pathScore *= 1.2f; // 20% boost for clearly production code
            }

            // Combine base score (test penalty) with path score
            var finalScore = baseScore * pathScore;

            // Boost for shorter paths in production code, penalty for deep test paths
            var depthFactor = isTestRelated 
                ? Math.Max(0.5f, 1.0f - (pathParts.Length - 2) * 0.1f) // Stronger depth penalty for tests
                : Math.Max(0.8f, 1.0f - (pathParts.Length - 2) * 0.05f); // Gentler penalty for production
            
            finalScore *= depthFactor;

            var result = Math.Min(1.0f, Math.Max(0.05f, finalScore));

            // Debug logging for troubleshooting
            if (_logger != null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("PathRelevance: File {FilePath}, IsTest: {IsTest}, HasTestDir: {HasTestDir}, TestPenalty: {TestPenalty:F3}, PathScore: {PathScore:F3}, DepthFactor: {DepthFactor:F3}, FinalScore: {FinalScore:F3}", 
                    relativePath, isTestRelated, hasTestDirectory, baseScore, pathScore, depthFactor, result);
            }

            return result;
        }
        catch (Exception)
        {
            return 0.5f; // Neutral score on error
        }
    }

    private bool HasProductionCodePatterns(string relativePath, string filename)
    {
        var lowerPath = relativePath.ToLowerInvariant();
        var lowerFile = filename.ToLowerInvariant();
        
        // Look for patterns that indicate production/implementation code
        return lowerPath.Contains("\\services\\") ||
               lowerPath.Contains("\\controllers\\") ||
               lowerPath.Contains("\\models\\") ||
               lowerPath.Contains("\\core\\") ||
               lowerPath.Contains("\\domain\\") ||
               lowerPath.Contains("\\infrastructure\\") ||
               (lowerFile.EndsWith("service.cs") && !lowerFile.Contains("mock") && !lowerFile.Contains("test")) ||
               (lowerFile.EndsWith("controller.cs") && !lowerFile.Contains("mock") && !lowerFile.Contains("test")) ||
               (lowerFile.EndsWith("repository.cs") && !lowerFile.Contains("mock") && !lowerFile.Contains("test"));
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