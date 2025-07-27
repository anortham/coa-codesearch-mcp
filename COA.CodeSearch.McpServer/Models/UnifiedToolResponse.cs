using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Unified response format for all MCP tools to ensure consistency
/// </summary>
public class UnifiedToolResponse<T>
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Response mode: 'summary' or 'full'
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "summary";

    /// <summary>
    /// Format of the response: 'structured', 'markdown', or 'mixed'
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "structured";

    /// <summary>
    /// The actual data returned by the tool
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    /// <summary>
    /// Optional markdown representation for display
    /// </summary>
    [JsonPropertyName("display")]
    public string? Display { get; set; }

    /// <summary>
    /// Metadata about the response
    /// </summary>
    [JsonPropertyName("metadata")]
    public UnifiedResponseMetadata? Metadata { get; set; }

    /// <summary>
    /// Error information if Success is false
    /// </summary>
    [JsonPropertyName("error")]
    public ErrorInfo? Error { get; set; }

    /// <summary>
    /// Recovery guidance for errors
    /// </summary>
    [JsonPropertyName("recovery")]
    public RecoveryInfo? Recovery { get; set; }

    /// <summary>
    /// Creates a successful response
    /// </summary>
    public static UnifiedToolResponse<T> CreateSuccess(T data, UnifiedResponseMetadata? metadata = null, string mode = "summary", string format = "structured", string? display = null)
    {
        return new UnifiedToolResponse<T>
        {
            Success = true,
            Mode = mode,
            Format = format,
            Data = data,
            Display = display,
            Metadata = metadata,
            Error = null,
            Recovery = null
        };
    }

    /// <summary>
    /// Creates a successful response with both structured data and markdown display
    /// </summary>
    public static UnifiedToolResponse<T> CreateMixed(T data, string display, UnifiedResponseMetadata? metadata = null, string mode = "summary")
    {
        return new UnifiedToolResponse<T>
        {
            Success = true,
            Mode = mode,
            Format = "mixed",
            Data = data,
            Display = display,
            Metadata = metadata,
            Error = null,
            Recovery = null
        };
    }

    /// <summary>
    /// Creates a markdown-only response
    /// </summary>
    public static UnifiedToolResponse<string> CreateMarkdown(string markdown, UnifiedResponseMetadata? metadata = null, string mode = "summary")
    {
        return new UnifiedToolResponse<string>
        {
            Success = true,
            Mode = mode,
            Format = "markdown",
            Data = markdown,
            Display = markdown,
            Metadata = metadata,
            Error = null,
            Recovery = null
        };
    }

    /// <summary>
    /// Creates an error response with recovery guidance
    /// </summary>
    public static UnifiedToolResponse<T> CreateError(string errorCode, string message, RecoveryInfo? recovery = null)
    {
        return new UnifiedToolResponse<T>
        {
            Success = false,
            Mode = "error",
            Data = default,
            Metadata = null,
            Error = new ErrorInfo { Code = errorCode, Message = message },
            Recovery = recovery
        };
    }
}

/// <summary>
/// Metadata about the response
/// </summary>
public class UnifiedResponseMetadata
{
    [JsonPropertyName("totalResults")]
    public int? TotalResults { get; set; }

    [JsonPropertyName("returnedResults")]
    public int? ReturnedResults { get; set; }

    [JsonPropertyName("estimatedTokens")]
    public int? EstimatedTokens { get; set; }

    [JsonPropertyName("autoModeSwitched")]
    public bool AutoModeSwitched { get; set; }

    [JsonPropertyName("detailRequestToken")]
    public string? DetailRequestToken { get; set; }

    [JsonPropertyName("searchTimeMs")]
    public double? SearchTimeMs { get; set; }

    [JsonPropertyName("processingTimeMs")]
    public double? ProcessingTimeMs { get; set; }
}

/// <summary>
/// Error information
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

/// <summary>
/// Common error codes
/// </summary>
public static class ErrorCodes
{
    public const string INDEX_NOT_FOUND = "INDEX_NOT_FOUND";
    public const string DIRECTORY_NOT_FOUND = "DIRECTORY_NOT_FOUND";
    public const string FILE_NOT_FOUND = "FILE_NOT_FOUND";
    public const string VALIDATION_ERROR = "VALIDATION_ERROR";
    public const string PERMISSION_DENIED = "PERMISSION_DENIED";
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
    public const string TIMEOUT = "TIMEOUT";
    public const string CIRCUIT_BREAKER_OPEN = "CIRCUIT_BREAKER_OPEN";
}

/// <summary>
/// Response format types
/// </summary>
public static class ResponseFormats
{
    public const string Structured = "structured";
    public const string Markdown = "markdown";
    public const string Mixed = "mixed";
}