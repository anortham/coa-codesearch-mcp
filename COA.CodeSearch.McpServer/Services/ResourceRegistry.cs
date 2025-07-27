using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Central registry for managing MCP resource providers and routing resource requests.
/// Coordinates between different resource providers to offer a unified resource API.
/// </summary>
public class ResourceRegistry : IResourceRegistry
{
    private readonly ILogger<ResourceRegistry> _logger;
    private readonly List<IResourceProvider> _providers = new();

    public ResourceRegistry(ILogger<ResourceRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var allResources = new List<Resource>();

        foreach (var provider in _providers)
        {
            try
            {
                var resources = await provider.ListResourcesAsync(cancellationToken);
                allResources.AddRange(resources);
                _logger.LogDebug("Provider {ProviderName} contributed {Count} resources", 
                    provider.Name, resources.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing resources from provider {ProviderName}", provider.Name);
                // Continue with other providers
            }
        }

        _logger.LogInformation("Listed {TotalCount} resources from {ProviderCount} providers", 
            allResources.Count, _providers.Count);

        return allResources;
    }

    /// <inheritdoc />
    public async Task<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI cannot be null or empty", nameof(uri));
        }

        foreach (var provider in _providers)
        {
            try
            {
                if (provider.CanHandle(uri))
                {
                    var result = await provider.ReadResourceAsync(uri, cancellationToken);
                    if (result != null)
                    {
                        _logger.LogDebug("Provider {ProviderName} successfully read resource {Uri}", 
                            provider.Name, uri);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading resource {Uri} from provider {ProviderName}", 
                    uri, provider.Name);
                // Continue with other providers
            }
        }

        _logger.LogWarning("No provider could handle resource URI: {Uri}", uri);
        throw new InvalidOperationException($"No provider found for resource URI: {uri}");
    }

    /// <inheritdoc />
    public void RegisterProvider(IResourceProvider provider)
    {
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        // Check for duplicate schemes
        var existingProvider = _providers.FirstOrDefault(p => p.Scheme == provider.Scheme);
        if (existingProvider != null)
        {
            _logger.LogWarning("Replacing existing provider for scheme '{Scheme}'. " +
                             "Old: {OldProvider}, New: {NewProvider}", 
                             provider.Scheme, existingProvider.Name, provider.Name);
            _providers.Remove(existingProvider);
        }

        _providers.Add(provider);
        _logger.LogInformation("Registered resource provider: {ProviderName} for scheme '{Scheme}'", 
            provider.Name, provider.Scheme);
    }
}