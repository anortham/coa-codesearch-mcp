using COA.CodeSearch.McpServer.Infrastructure;

namespace COA.CodeSearch.McpServer.Services.ResponseBuilders;

/// <summary>
/// Common interface for all AI-optimized response builders
/// </summary>
public interface IResponseBuilder
{
    /// <summary>
    /// The type of response this builder handles
    /// </summary>
    string ResponseType { get; }
}