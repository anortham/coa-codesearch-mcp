using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing and providing access to MCP resources.
/// Resources are URI-addressable data that clients can read,
/// such as indexed files, search results, and memory documents.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// Gets a list of all available resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of available resources.</returns>
    Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the content of a specific resource by URI.
    /// </summary>
    /// <param name="uri">The URI of the resource to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resource content.</returns>
    Task<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a resource provider that can supply resources of a specific type.
    /// </summary>
    /// <param name="provider">The resource provider to register.</param>
    void RegisterProvider(IResourceProvider provider);
}