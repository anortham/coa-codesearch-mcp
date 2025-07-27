using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for components that can provide MCP resources.
/// Resource providers supply specific types of data as URI-addressable resources.
/// </summary>
public interface IResourceProvider
{
    /// <summary>
    /// Gets the URI scheme that this provider handles.
    /// </summary>
    /// <example>codesearch-workspace, codesearch-search, codesearch-memory</example>
    string Scheme { get; }

    /// <summary>
    /// Gets the human-readable name of this provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a description of what this provider offers.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a list of resources provided by this provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of available resources.</returns>
    Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the content of a specific resource by URI.
    /// </summary>
    /// <param name="uri">The URI of the resource to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resource content, or null if the URI is not handled by this provider.</returns>
    Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether this provider can handle the specified URI.
    /// </summary>
    /// <param name="uri">The URI to check.</param>
    /// <returns>True if this provider can handle the URI, false otherwise.</returns>
    bool CanHandle(string uri);
}