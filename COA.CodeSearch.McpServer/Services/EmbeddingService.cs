using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Simple embedding service using basic text vectorization for semantic search
/// This is a lightweight implementation that can be replaced with more sophisticated models
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly ILogger<EmbeddingService> _logger;
    private readonly EmbeddingOptions _options;

    // Simple word frequency-based embeddings (TF-IDF style)
    private readonly Dictionary<string, int> _vocabulary = new();
    private readonly object _vocabLock = new();
    
    public EmbeddingService(
        ILogger<EmbeddingService> logger,
        IOptions<EmbeddingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public int EmbeddingDimensions => _options.Dimensions;
    public string ModelName => "SimpleTextVectorizer";

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[EmbeddingDimensions];
        }

        return await Task.Run(() => CreateSimpleEmbedding(text));
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(List<string> texts)
    {
        var tasks = texts.Select(GetEmbeddingAsync);
        var embeddings = await Task.WhenAll(tasks);
        return embeddings.ToList();
    }

    public async Task<bool> IsAvailableAsync()
    {
        return await Task.FromResult(true);
    }

    /// <summary>
    /// Create a simple embedding using word frequency and semantic features
    /// This provides basic semantic understanding for concept-based search
    /// </summary>
    private float[] CreateSimpleEmbedding(string text)
    {
        var embedding = new float[EmbeddingDimensions];
        
        // Normalize text
        var normalizedText = NormalizeText(text);
        var words = tokenize(normalizedText);
        
        if (words.Count == 0)
        {
            return embedding;
        }

        // Update vocabulary (thread-safe)
        lock (_vocabLock)
        {
            foreach (var word in words.Distinct())
            {
                if (!_vocabulary.ContainsKey(word))
                {
                    _vocabulary[word] = _vocabulary.Count;
                }
            }
        }

        // Create word frequency vector
        var wordCounts = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var totalWords = words.Count;

        // Feature extraction based on different aspects
        var features = new Dictionary<string, float>();
        
        // 1. Technical terms (classes, methods, files, etc.)
        features["technical"] = CountMatches(words, new[] { "class", "method", "function", "file", "api", "service", "controller", "model" });
        
        // 2. Problem indicators
        features["problem"] = CountMatches(words, new[] { "bug", "error", "issue", "problem", "fix", "broken", "fail", "exception" });
        
        // 3. Architecture terms
        features["architecture"] = CountMatches(words, new[] { "architecture", "design", "pattern", "structure", "component", "module", "interface" });
        
        // 4. Security terms
        features["security"] = CountMatches(words, new[] { "security", "auth", "authentication", "authorization", "token", "permission", "access" });
        
        // 5. Performance terms
        features["performance"] = CountMatches(words, new[] { "performance", "slow", "fast", "optimize", "cache", "memory", "cpu", "latency" });
        
        // 6. Quality terms
        features["quality"] = CountMatches(words, new[] { "test", "testing", "quality", "refactor", "clean", "maintainable", "readable" });
        
        // 7. Data terms
        features["data"] = CountMatches(words, new[] { "data", "database", "query", "sql", "storage", "persistence", "entity" });
        
        // 8. UI terms
        features["ui"] = CountMatches(words, new[] { "ui", "user", "interface", "frontend", "view", "component", "render", "display" });

        // Fill embedding with semantic features (first 32 dimensions)
        int featureIndex = 0;
        foreach (var feature in features)
        {
            if (featureIndex < Math.Min(32, EmbeddingDimensions))
            {
                embedding[featureIndex] = feature.Value / totalWords; // Normalize by document length
                featureIndex++;
            }
        }

        // Add word frequency features (remaining dimensions)
        var topWords = wordCounts
            .OrderByDescending(kv => kv.Value)
            .Take(EmbeddingDimensions - featureIndex)
            .ToList();

        for (int i = 0; i < topWords.Count && featureIndex + i < EmbeddingDimensions; i++)
        {
            var word = topWords[i];
            embedding[featureIndex + i] = (float)word.Value / totalWords;
        }

        // Normalize the embedding vector
        var norm = Math.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= (float)norm;
            }
        }

        return embedding;
    }

    private string NormalizeText(string text)
    {
        return text.ToLowerInvariant()
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
    }

    private List<string> tokenize(string text)
    {
        // Simple tokenization - split on whitespace and punctuation
        var words = new List<string>();
        var currentWord = new StringBuilder();
        
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                currentWord.Append(c);
            }
            else
            {
                if (currentWord.Length > 0)
                {
                    var word = currentWord.ToString();
                    if (word.Length >= 2 && !IsStopWord(word)) // Filter short words and stop words
                    {
                        words.Add(word);
                    }
                    currentWord.Clear();
                }
            }
        }
        
        // Add final word
        if (currentWord.Length > 0)
        {
            var word = currentWord.ToString();
            if (word.Length >= 2 && !IsStopWord(word))
            {
                words.Add(word);
            }
        }
        
        return words;
    }

    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
            "is", "are", "was", "were", "be", "been", "have", "has", "had", "will", "would",
            "this", "that", "these", "those", "it", "its", "they", "them", "their", "we", "us", "our"
        };
        
        return stopWords.Contains(word);
    }

    private float CountMatches(List<string> words, string[] terms)
    {
        return words.Count(w => terms.Contains(w));
    }
}

/// <summary>
/// Configuration options for the embedding service
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Dimensionality of the embedding vectors
    /// </summary>
    public int Dimensions { get; set; } = 128;
    
    /// <summary>
    /// Whether to use caching for embeddings
    /// </summary>
    public bool EnableCaching { get; set; } = true;
    
    /// <summary>
    /// Maximum cache size for embeddings
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;
}