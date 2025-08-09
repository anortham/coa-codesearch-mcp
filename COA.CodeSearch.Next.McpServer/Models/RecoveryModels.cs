using System.Text.Json.Serialization;

namespace COA.CodeSearch.Next.McpServer.Models;

/// <summary>
/// Error information for failed operations
/// </summary>
public class ErrorInfo
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("details")]
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// Recovery guidance for errors
/// </summary>
public class RecoveryInfo
{
    [JsonPropertyName("steps")]
    public List<string> Steps { get; set; } = new();

    [JsonPropertyName("suggestedActions")]
    public List<SuggestedAction> SuggestedActions { get; set; } = new();
}

/// <summary>
/// Suggested action for recovery
/// </summary>
public class SuggestedAction
{
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, object> Params { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}