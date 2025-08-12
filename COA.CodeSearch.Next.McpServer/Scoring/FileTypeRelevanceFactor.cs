using Lucene.Net.Index;

namespace COA.CodeSearch.Next.McpServer.Scoring;

/// <summary>
/// Adjusts scores based on file type relevance for different search contexts.
/// For example, when searching for code, .cs files should score higher than .md files.
/// </summary>
public class FileTypeRelevanceFactor : IScoringFactor
{
    private readonly Dictionary<string, float> _extensionWeights;
    private readonly Dictionary<string, HashSet<string>> _contextualWeights;

    public string Name => "FileTypeRelevance";
    public float Weight { get; set; } = 0.4f;

    public FileTypeRelevanceFactor()
    {
        // Default extension weights for code search
        _extensionWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            // Primary source code files
            { ".cs", 1.0f },
            { ".ts", 1.0f },
            { ".js", 0.95f },
            { ".tsx", 0.95f },
            { ".jsx", 0.95f },
            { ".py", 1.0f },
            { ".java", 1.0f },
            { ".cpp", 1.0f },
            { ".c", 0.95f },
            { ".go", 1.0f },
            { ".rs", 1.0f },
            { ".kt", 1.0f },
            { ".swift", 1.0f },
            
            // Web files
            { ".html", 0.7f },
            { ".css", 0.7f },
            { ".scss", 0.7f },
            { ".vue", 0.85f },
            { ".razor", 0.9f },
            { ".cshtml", 0.85f },
            
            // Configuration and data
            { ".json", 0.6f },
            { ".xml", 0.6f },
            { ".yaml", 0.6f },
            { ".yml", 0.6f },
            { ".config", 0.6f },
            { ".ini", 0.5f },
            { ".env", 0.5f },
            
            // Project files
            { ".csproj", 0.7f },
            { ".sln", 0.6f },
            { ".proj", 0.6f },
            { ".props", 0.6f },
            { ".targets", 0.6f },
            
            // Documentation
            { ".md", 0.4f },
            { ".txt", 0.3f },
            { ".rst", 0.4f },
            { ".adoc", 0.4f },
            
            // Scripts
            { ".sh", 0.7f },
            { ".ps1", 0.7f },
            { ".bat", 0.6f },
            { ".cmd", 0.6f },
            
            // Database
            { ".sql", 0.8f },
            
            // Build artifacts (low relevance)
            { ".dll", 0.1f },
            { ".exe", 0.1f },
            { ".pdb", 0.1f },
            { ".obj", 0.1f },
            { ".cache", 0.1f }
        };

        // Context-specific weights
        _contextualWeights = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "config", new HashSet<string> { ".json", ".xml", ".yaml", ".yml", ".config", ".ini", ".env" } },
            { "configuration", new HashSet<string> { ".json", ".xml", ".yaml", ".yml", ".config", ".ini", ".env" } },
            { "settings", new HashSet<string> { ".json", ".xml", ".yaml", ".yml", ".config", ".ini", ".env" } },
            { "style", new HashSet<string> { ".css", ".scss", ".sass", ".less" } },
            { "styles", new HashSet<string> { ".css", ".scss", ".sass", ".less" } },
            { "css", new HashSet<string> { ".css", ".scss", ".sass", ".less" } },
            { "view", new HashSet<string> { ".html", ".cshtml", ".razor", ".vue", ".jsx", ".tsx" } },
            { "views", new HashSet<string> { ".html", ".cshtml", ".razor", ".vue", ".jsx", ".tsx" } },
            { "ui", new HashSet<string> { ".html", ".cshtml", ".razor", ".vue", ".jsx", ".tsx", ".xaml" } },
            { "test", new HashSet<string> { ".cs", ".ts", ".js", ".py", ".java" } }, // Test files still need code extensions
            { "tests", new HashSet<string> { ".cs", ".ts", ".js", ".py", ".java" } },
            { "spec", new HashSet<string> { ".cs", ".ts", ".js", ".py", ".java" } },
            { "sql", new HashSet<string> { ".sql" } },
            { "database", new HashSet<string> { ".sql" } },
            { "query", new HashSet<string> { ".sql" } },
            { "script", new HashSet<string> { ".sh", ".ps1", ".bat", ".cmd", ".py", ".js" } },
            { "scripts", new HashSet<string> { ".sh", ".ps1", ".bat", ".cmd", ".py", ".js" } },
            { "doc", new HashSet<string> { ".md", ".txt", ".rst", ".adoc" } },
            { "docs", new HashSet<string> { ".md", ".txt", ".rst", ".adoc" } },
            { "documentation", new HashSet<string> { ".md", ".txt", ".rst", ".adoc" } },
            { "readme", new HashSet<string> { ".md", ".txt", ".rst" } }
        };
    }

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            var extension = doc.Get("extension")?.ToLowerInvariant() ?? "";
            
            if (string.IsNullOrEmpty(extension))
                return 0.5f; // Neutral score for files without extension

            // Check if the search context suggests specific file types
            var contextBoost = GetContextualBoost(extension, searchContext.QueryText);
            if (contextBoost > 0)
            {
                return contextBoost;
            }

            // Use default weights
            if (_extensionWeights.TryGetValue(extension, out var weight))
            {
                return weight;
            }

            // Check for composite extensions (e.g., .test.cs)
            var parts = extension.Split('.');
            if (parts.Length > 2)
            {
                var primaryExt = "." + parts[parts.Length - 1];
                if (_extensionWeights.TryGetValue(primaryExt, out weight))
                {
                    // Slightly reduce weight for composite extensions
                    return weight * 0.9f;
                }
            }

            // Unknown file type gets neutral score
            return 0.5f;
        }
        catch (Exception)
        {
            return 0.5f; // Neutral score on error
        }
    }

    private float GetContextualBoost(string extension, string queryText)
    {
        var queryLower = queryText.ToLowerInvariant();
        
        foreach (var (context, extensions) in _contextualWeights)
        {
            if (queryLower.Contains(context))
            {
                if (extensions.Contains(extension))
                {
                    // Boost relevant file types for this context
                    return 0.9f;
                }
                else if (_extensionWeights.TryGetValue(extension, out var baseWeight))
                {
                    // Reduce weight for non-relevant file types in this context
                    return baseWeight * 0.5f;
                }
            }
        }

        return 0f; // No contextual adjustment
    }
}