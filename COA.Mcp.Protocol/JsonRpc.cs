using System.Text.Json.Serialization;

namespace COA.Mcp.Protocol;

/// <summary>
/// Base class for all JSON-RPC messages conforming to JSON-RPC 2.0 specification.
/// </summary>
public abstract class JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the JSON-RPC protocol version. Always "2.0" for JSON-RPC 2.0.
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
}

/// <summary>
/// Represents a JSON-RPC 2.0 request message.
/// </summary>
/// <remarks>
/// A request object must have an id to correlate with the response.
/// </remarks>
public class JsonRpcRequest : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the request identifier. Used to correlate requests with responses.
    /// </summary>
    /// <value>Can be a string, number, or null.</value>
    [JsonPropertyName("id")]
    public object Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the parameters for the method call.
    /// </summary>
    /// <value>Can be an object, array, or null if no parameters are needed.</value>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; set; }
}

/// <summary>
/// Represents a JSON-RPC 2.0 response message.
/// </summary>
/// <remarks>
/// A response must contain either a result or an error, but not both.
/// </remarks>
public class JsonRpcResponse : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the identifier matching the request this response is for.
    /// </summary>
    [JsonPropertyName("id")]
    public object Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the result of the method call.
    /// </summary>
    /// <value>Present only if the request succeeded. Mutually exclusive with Error.</value>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    /// <summary>
    /// Gets or sets the error information if the request failed.
    /// </summary>
    /// <value>Present only if the request failed. Mutually exclusive with Result.</value>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

/// <summary>
/// Represents an error in a JSON-RPC 2.0 response.
/// </summary>
public class JsonRpcError
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    /// <remarks>
    /// Standard error codes:
    /// -32700: Parse error
    /// -32600: Invalid Request
    /// -32601: Method not found
    /// -32602: Invalid params
    /// -32603: Internal error
    /// -32000 to -32099: Server error
    /// </remarks>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Gets or sets a short description of the error.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets additional information about the error.
    /// </summary>
    /// <value>May contain detailed error information, stack traces, or context.</value>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }
}

/// <summary>
/// Represents a JSON-RPC 2.0 notification message.
/// </summary>
/// <remarks>
/// A notification is a request without an id. The server must not reply to a notification.
/// </remarks>
public class JsonRpcNotification : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the parameters for the method call.
    /// </summary>
    /// <value>Can be an object, array, or null if no parameters are needed.</value>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; set; }
}