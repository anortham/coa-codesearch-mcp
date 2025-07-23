using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for sending notifications to the MCP client
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a JSON-RPC notification to the client
    /// </summary>
    /// <param name="notification">The notification to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a progress notification to the client
    /// </summary>
    /// <param name="progressToken">Unique token identifying the operation</param>
    /// <param name="progress">Current progress value</param>
    /// <param name="total">Total value (optional)</param>
    /// <param name="message">Progress message (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendProgressAsync(string progressToken, int progress, int? total = null, string? message = null, CancellationToken cancellationToken = default);
}