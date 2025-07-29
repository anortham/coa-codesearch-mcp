using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.ResponseBuilders;

/// <summary>
/// Factory for creating and managing response builders
/// </summary>
public class ResponseBuilderFactory : IResponseBuilderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ResponseBuilderFactory> _logger;
    private readonly Dictionary<string, Type> _builderTypes;

    public ResponseBuilderFactory(IServiceProvider serviceProvider, ILogger<ResponseBuilderFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Register all response builder types
        _builderTypes = new Dictionary<string, Type>
        {
            ["text_search"] = typeof(TextSearchResponseBuilder),
            ["file_search"] = typeof(FileSearchResponseBuilder),
            ["memory_search"] = typeof(MemorySearchResponseBuilder),
            ["directory_search"] = typeof(DirectorySearchResponseBuilder),
            ["similar_files"] = typeof(SimilarFilesResponseBuilder),
            ["recent_files"] = typeof(RecentFilesResponseBuilder),
            ["file_size_analysis"] = typeof(FileSizeAnalysisResponseBuilder),
            ["file_size_distribution"] = typeof(FileSizeDistributionResponseBuilder),
            ["batch_operations"] = typeof(BatchOperationsResponseBuilder)
        };
    }

    /// <summary>
    /// Get a response builder for the specified response type
    /// </summary>
    public T GetBuilder<T>(string responseType) where T : class, IResponseBuilder
    {
        if (!_builderTypes.TryGetValue(responseType, out var builderType))
        {
            throw new InvalidOperationException($"No response builder registered for type: {responseType}");
        }

        if (!typeof(T).IsAssignableFrom(builderType))
        {
            throw new InvalidOperationException($"Builder type {builderType.Name} is not assignable to {typeof(T).Name}");
        }

        try
        {
            var builder = _serviceProvider.GetService(builderType) as T;
            if (builder == null)
            {
                throw new InvalidOperationException($"Failed to resolve builder of type {builderType.Name}");
            }

            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create response builder for type {ResponseType}", responseType);
            throw;
        }
    }

    /// <summary>
    /// Get a response builder by type
    /// </summary>
    public IResponseBuilder GetBuilder(string responseType)
    {
        return GetBuilder<IResponseBuilder>(responseType);
    }

    /// <summary>
    /// Check if a builder is registered for the given response type
    /// </summary>
    public bool HasBuilder(string responseType)
    {
        return _builderTypes.ContainsKey(responseType);
    }

    /// <summary>
    /// Get all registered response types
    /// </summary>
    public IEnumerable<string> GetRegisteredTypes()
    {
        return _builderTypes.Keys;
    }
}

/// <summary>
/// Interface for the response builder factory
/// </summary>
public interface IResponseBuilderFactory
{
    /// <summary>
    /// Get a response builder for the specified response type
    /// </summary>
    T GetBuilder<T>(string responseType) where T : class, IResponseBuilder;

    /// <summary>
    /// Get a response builder by type
    /// </summary>
    IResponseBuilder GetBuilder(string responseType);

    /// <summary>
    /// Check if a builder is registered for the given response type
    /// </summary>
    bool HasBuilder(string responseType);

    /// <summary>
    /// Get all registered response types
    /// </summary>
    IEnumerable<string> GetRegisteredTypes();
}