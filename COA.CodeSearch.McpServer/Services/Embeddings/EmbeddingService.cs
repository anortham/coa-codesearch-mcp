using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services.Embeddings;

/// <summary>
/// Service for generating text embeddings using ONNX Runtime and bge-small-en-v1.5 model.
/// Produces 384-dimensional vectors for semantic similarity search.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embedding vector for text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings for multiple texts in batch
    /// </summary>
    Task<List<float[]>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if embedding service is available
    /// </summary>
    bool IsAvailable();
}

public class EmbeddingService : IEmbeddingService, IDisposable
{
    private readonly ILogger<EmbeddingService> _logger;
    private readonly InferenceSession? _session;
    private readonly BertTokenizer? _tokenizer;
    private readonly int _maxLength = 512;
    private bool _disposed;

    public EmbeddingService(ILogger<EmbeddingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            var modelPath = FindModelPath();
            var tokenizerPath = FindTokenizerPath();

            if (modelPath != null && tokenizerPath != null)
            {
                _session = new InferenceSession(modelPath);
                _tokenizer = BertTokenizer.FromFile(tokenizerPath);

                _logger.LogInformation("✅ Embedding service initialized with bge-small-en-v1.5 (384 dimensions)");
            }
            else
            {
                _logger.LogWarning("⚠️ Embedding model or tokenizer not found - semantic search disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize embedding service");
        }
    }

    public bool IsAvailable() => _session != null && _tokenizer != null;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
            throw new InvalidOperationException("Embedding service not available");

        // Tokenize
        var tokens = _tokenizer!.Tokenize(text, _maxLength);

        // Create input tensors
        var inputIds = new DenseTensor<long>(new[] { 1, tokens.InputIds.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, tokens.AttentionMask.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.InputIds.Length }); // All zeros for single sentence

        for (int i = 0; i < tokens.InputIds.Length; i++)
        {
            inputIds[0, i] = tokens.InputIds[i];
            attentionMask[0, i] = tokens.AttentionMask[i];
            tokenTypeIds[0, i] = 0; // All zeros for single-sentence embedding
        }

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        using var results = await Task.Run(() => _session!.Run(inputs), cancellationToken);

        // Extract embedding from model output (last_hidden_state)
        var output = results.First().AsEnumerable<float>().ToArray();

        // Mean pooling: average the token embeddings
        var embedding = MeanPooling(output, tokens.AttentionMask);

        // Normalize to unit length
        return Normalize(embedding);
    }

    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var embeddings = new List<float[]>();

        foreach (var text in texts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            embeddings.Add(embedding);
        }

        return embeddings;
    }

    private static float[] MeanPooling(float[] embeddings, long[] attentionMask)
    {
        // Assuming 384 dimensions for bge-small
        const int dim = 384;
        var result = new float[dim];
        var tokenCount = attentionMask.Sum();

        for (int i = 0; i < dim; i++)
        {
            float sum = 0;
            for (int j = 0; j < attentionMask.Length; j++)
            {
                if (attentionMask[j] == 1)
                {
                    sum += embeddings[j * dim + i];
                }
            }
            result[i] = sum / tokenCount;
        }

        return result;
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= (float)norm;
            }
        }
        return vector;
    }

    private string? FindModelPath()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Models",
            "bge-small-en-v1.5",
            "model.onnx");

        return File.Exists(path) ? path : null;
    }

    private string? FindTokenizerPath()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Models",
            "bge-small-en-v1.5",
            "tokenizer.json");

        return File.Exists(path) ? path : null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Simple BERT tokenizer for bge-small model
/// </summary>
public class BertTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsTokenId = 101;  // [CLS]
    private readonly int _sepTokenId = 102;  // [SEP]
    private readonly int _padTokenId = 0;    // [PAD]

    private BertTokenizer(Dictionary<string, int> vocab)
    {
        _vocab = vocab;
    }

    public static BertTokenizer FromFile(string tokenizerPath)
    {
        var json = File.ReadAllText(tokenizerPath);
        var doc = JsonDocument.Parse(json);

        var vocab = new Dictionary<string, int>();
        foreach (var item in doc.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject())
        {
            vocab[item.Name] = item.Value.GetInt32();
        }

        return new BertTokenizer(vocab);
    }

    public (long[] InputIds, long[] AttentionMask) Tokenize(string text, int maxLength)
    {
        // Simple whitespace + lowercase tokenization
        var tokens = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(maxLength - 2)  // Reserve space for [CLS] and [SEP]
            .ToList();

        // Build input_ids: [CLS] + tokens + [SEP] + [PAD]...
        var inputIds = new List<long> { _clsTokenId };

        foreach (var token in tokens)
        {
            if (_vocab.TryGetValue(token, out var tokenId))
            {
                inputIds.Add(tokenId);
            }
            else if (_vocab.TryGetValue("[UNK]", out var unkId))
            {
                inputIds.Add(unkId);
            }
        }

        inputIds.Add(_sepTokenId);

        // Pad to maxLength
        while (inputIds.Count < maxLength)
        {
            inputIds.Add(_padTokenId);
        }

        // Create attention mask (1 for real tokens, 0 for padding)
        var attentionMask = inputIds.Select(id => id != _padTokenId ? 1L : 0L).ToArray();

        return (inputIds.ToArray(), attentionMask);
    }
}
