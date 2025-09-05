using COA.Mcp.Protocol;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Providers;

/// <summary>
/// Resource provider that serves resources from the ResourceStorageService.
/// Handles URIs with scheme "mcp-resource" and host "memory-compressed".
/// </summary>
public class ResourceStorageProvider : IResourceProvider
{
    private readonly IResourceStorageService _storageService;
    private readonly ILogger<ResourceStorageProvider> _logger;

    public ResourceStorageProvider(IResourceStorageService storageService, ILogger<ResourceStorageProvider> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Name => "ResourceStorage";

    /// <inheritdoc />
    public string Scheme => "mcp-resource";

    /// <inheritdoc />
    public string Description => "Provides access to stored search results and large response data via memory-compressed URIs";

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        try
        {
            var parsedUri = new Uri(uri);
            var canHandle = parsedUri.Scheme == Scheme && 
                           parsedUri.Host == "memory-compressed";
            
            _logger.LogDebug("CanHandle({Uri}) = {CanHandle} (scheme: {Scheme}, host: {Host})", 
                uri, canHandle, parsedUri.Scheme, parsedUri.Host);
                
            return canHandle;
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid URI format: {Uri}", uri);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // ResourceStorageService doesn't have a list method, so we return empty list
            // This is acceptable since stored resources are typically accessed directly by URI
            _logger.LogDebug("ListResourcesAsync called - returning empty list (resources accessed by direct URI)");
            return Task.FromResult(new List<Resource>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing resources from ResourceStorageService");
            return Task.FromResult(new List<Resource>());
        }
    }

    /// <inheritdoc />
    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
        {
            _logger.LogWarning("Cannot handle URI: {Uri}", uri);
            return null;
        }

        try
        {
            _logger.LogDebug("Attempting to read resource: {Uri}", uri);

            // Convert string URI to ResourceUri and retrieve the stored data
            var resourceUri = new ResourceUri(uri);
            var storedData = await _storageService.RetrieveAsync<string>(resourceUri);
            
            if (storedData == null)
            {
                _logger.LogWarning("No data found for URI: {Uri}", uri);
                return new ReadResourceResult
                {
                    Contents = new List<ResourceContent>
                    {
                        new ResourceContent
                        {
                            Uri = uri,
                            Text = "Resource not found or expired",
                            MimeType = "text/plain"
                        }
                    }
                };
            }

            _logger.LogDebug("Successfully retrieved resource data for: {Uri} (length: {Length})", 
                uri, storedData.Length);

            return new ReadResourceResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = uri,
                        Text = storedData,
                        MimeType = "application/json"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading resource from ResourceStorageService: {Uri}", uri);
            
            return new ReadResourceResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = uri,
                        Text = $"Error reading resource: {ex.Message}",
                        MimeType = "text/plain"
                    }
                }
            };
        }
    }
}