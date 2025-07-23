using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

public interface IBatchOperationsTool
{
    Task<object> ExecuteAsync(
        JsonElement operations,
        string? defaultWorkspacePath = null,
        CancellationToken cancellationToken = default);
}