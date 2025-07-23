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

/// <summary>
/// Defines standard JSON-RPC 2.0 error codes and MCP-specific error codes.
/// This replaces magic numbers with named constants for better code readability and maintenance.
/// </summary>
public static class JsonRpcErrorCodes
{
    #region JSON-RPC 2.0 Standard Error Codes

    /// <summary>
    /// Invalid JSON was received by the server.
    /// An error occurred on the server while parsing the JSON text.
    /// </summary>
    public const int ParseError = -32700;

    /// <summary>
    /// The JSON sent is not a valid Request object.
    /// </summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// The method does not exist / is not available.
    /// </summary>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid method parameter(s).
    /// </summary>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal JSON-RPC error.
    /// </summary>
    public const int InternalError = -32603;

    #endregion

    #region Server Error Range (-32000 to -32099)

    /// <summary>
    /// Generic server error. Reserved for implementation-defined server-errors.
    /// </summary>
    public const int ServerError = -32000;

    /// <summary>
    /// The requested resource could not be found.
    /// </summary>
    public const int ResourceNotFound = -32001;

    /// <summary>
    /// Access to the requested resource was denied.
    /// </summary>
    public const int ResourceAccessDenied = -32002;

    /// <summary>
    /// The requested operation timed out.
    /// </summary>
    public const int OperationTimeout = -32003;

    /// <summary>
    /// The server is temporarily unavailable.
    /// </summary>
    public const int ServiceUnavailable = -32004;

    /// <summary>
    /// The operation was cancelled by the client or server.
    /// </summary>
    public const int OperationCancelled = -32005;

    #endregion

    #region MCP-Specific Error Codes (-32100 to -32199)

    /// <summary>
    /// A tool with the requested name was not found.
    /// </summary>
    public const int ToolNotFound = -32100;

    /// <summary>
    /// The tool failed to execute due to invalid arguments or runtime error.
    /// </summary>
    public const int ToolExecutionError = -32101;

    /// <summary>
    /// The requested workspace could not be accessed or does not exist.
    /// </summary>
    public const int WorkspaceNotFound = -32102;

    /// <summary>
    /// The workspace index is not available or corrupted.
    /// </summary>
    public const int IndexNotAvailable = -32103;

    /// <summary>
    /// The requested memory operation failed.
    /// </summary>
    public const int MemoryOperationFailed = -32104;

    #endregion

    /// <summary>
    /// Determines if the given error code is in the server error range (-32000 to -32099).
    /// </summary>
    /// <param name="errorCode">The error code to check.</param>
    /// <returns>True if the error code is a server error, false otherwise.</returns>
    public static bool IsServerError(int errorCode)
    {
        return errorCode >= -32099 && errorCode <= -32000;
    }

    /// <summary>
    /// Determines if the given error code is an MCP-specific error (-32100 to -32199).
    /// </summary>
    /// <param name="errorCode">The error code to check.</param>
    /// <returns>True if the error code is MCP-specific, false otherwise.</returns>
    public static bool IsMcpError(int errorCode)
    {
        return errorCode >= -32199 && errorCode <= -32100;
    }

    /// <summary>
    /// Gets a human-readable description for standard JSON-RPC error codes.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>A description of the error, or null if the code is not recognized.</returns>
    public static string? GetStandardErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            ParseError => "Parse error - Invalid JSON was received",
            InvalidRequest => "Invalid Request - The JSON sent is not a valid Request object",
            MethodNotFound => "Method not found - The method does not exist or is not available",
            InvalidParams => "Invalid params - Invalid method parameter(s)",
            InternalError => "Internal error - Internal JSON-RPC error",
            ResourceNotFound => "Resource not found - The requested resource could not be found",
            ResourceAccessDenied => "Access denied - Access to the requested resource was denied",
            OperationTimeout => "Operation timeout - The requested operation timed out",
            ServiceUnavailable => "Service unavailable - The server is temporarily unavailable",
            OperationCancelled => "Operation cancelled - The operation was cancelled",
            ToolNotFound => "Tool not found - A tool with the requested name was not found",
            ToolExecutionError => "Tool execution error - The tool failed to execute",
            WorkspaceNotFound => "Workspace not found - The requested workspace could not be accessed",
            IndexNotAvailable => "Index not available - The workspace index is not available or corrupted",
            MemoryOperationFailed => "Memory operation failed - The requested memory operation failed",
            _ => null
        };
    }
}