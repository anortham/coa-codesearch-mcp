using System.Text.Json.Serialization;

namespace COA.Mcp.Protocol;

/// <summary>
/// Defines the capabilities supported by an MCP server.
/// </summary>
public class ServerCapabilities
{
    /// <summary>
    /// Gets or sets the tools capability marker.
    /// </summary>
    /// <value>An empty object {} indicates tool support is available.</value>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Tools { get; set; }

    /// <summary>
    /// Gets or sets the resource capabilities supported by the server.
    /// </summary>
    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResourceCapabilities? Resources { get; set; }

    /// <summary>
    /// Gets or sets the prompts capability marker.
    /// </summary>
    /// <value>An empty object {} indicates prompt support is available.</value>
    [JsonPropertyName("prompts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Prompts { get; set; }
}

/// <summary>
/// Defines specific capabilities for resource handling.
/// </summary>
public class ResourceCapabilities
{
    /// <summary>
    /// Gets or sets whether the server supports resource subscriptions.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    /// <summary>
    /// Gets or sets whether the server can notify when the resource list changes.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

/// <summary>
/// Request to initialize the MCP connection.
/// </summary>
/// <remarks>
/// This is the first request sent by the client after establishing a connection.
/// </remarks>
public class InitializeRequest
{
    /// <summary>
    /// Gets or sets the MCP protocol version the client supports.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Gets or sets the capabilities supported by the client.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets information about the client implementation.
    /// </summary>
    [JsonPropertyName("clientInfo")]
    public Implementation ClientInfo { get; set; } = null!;
}

/// <summary>
/// Response to an initialization request.
/// </summary>
/// <remarks>
/// Contains the server's capabilities and version information.
/// </remarks>
public class InitializeResult
{
    /// <summary>
    /// Gets or sets the MCP protocol version the server supports.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>
    /// Gets or sets the capabilities supported by the server.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets information about the server implementation.
    /// </summary>
    [JsonPropertyName("serverInfo")]
    public Implementation ServerInfo { get; set; } = null!;
}

/// <summary>
/// Defines the capabilities supported by an MCP client.
/// </summary>
public class ClientCapabilities
{
    /// <summary>
    /// Gets or sets the root directory capabilities.
    /// </summary>
    [JsonPropertyName("roots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RootCapabilities? Roots { get; set; }

    /// <summary>
    /// Gets or sets the sampling capability marker.
    /// </summary>
    /// <value>An empty object {} indicates sampling support is available.</value>
    [JsonPropertyName("sampling")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Sampling { get; set; }
}

/// <summary>
/// Defines capabilities for root directory handling.
/// </summary>
public class RootCapabilities
{
    /// <summary>
    /// Gets or sets whether the client can notify when the root list changes.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

/// <summary>
/// Contains information about a client or server implementation.
/// </summary>
public class Implementation
{
    /// <summary>
    /// Gets or sets the name of the implementation.
    /// </summary>
    /// <example>COA Directus MCP Server</example>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the version of the implementation.
    /// </summary>
    /// <example>1.0.0</example>
    [JsonPropertyName("version")]
    public string Version { get; set; } = null!;
}

/// <summary>
/// Represents a tool that can be invoked by the client.
/// </summary>
public class Tool
{
    /// <summary>
    /// Gets or sets the unique name of the tool.
    /// </summary>
    /// <example>directus_list_items</example>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets a human-readable description of what the tool does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema defining the tool's input parameters.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = null!;
}

/// <summary>
/// Represents a resource that can be read by the client.
/// </summary>
public class Resource
{
    /// <summary>
    /// Gets or sets the URI identifying the resource.
    /// </summary>
    /// <example>directus://collections</example>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = null!;

    /// <summary>
    /// Gets or sets the human-readable name of the resource.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets a description of the resource.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the resource content.
    /// </summary>
    /// <example>application/json</example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

/// <summary>
/// Represents an interactive prompt that can guide the user through complex operations.
/// </summary>
public class Prompt
{
    /// <summary>
    /// Gets or sets the unique name of the prompt.
    /// </summary>
    /// <example>setup_directus_connection</example>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets a description of what the prompt helps accomplish.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the arguments that can be provided to customize the prompt.
    /// </summary>
    [JsonPropertyName("arguments")]
    public List<PromptArgument>? Arguments { get; set; }
}

/// <summary>
/// Represents an argument that can be passed to a prompt.
/// </summary>
public class PromptArgument
{
    /// <summary>
    /// Gets or sets the name of the argument.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets a description of the argument's purpose.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets whether this argument must be provided.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// Request to invoke a specific tool.
/// </summary>
public class CallToolRequest
{
    /// <summary>
    /// Gets or sets the name of the tool to invoke.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the arguments to pass to the tool.
    /// </summary>
    /// <value>Must match the tool's input schema.</value>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Arguments { get; set; }
}

/// <summary>
/// Result returned from a tool invocation.
/// </summary>
public class CallToolResult
{
    /// <summary>
    /// Gets or sets the content returned by the tool.
    /// </summary>
    [JsonPropertyName("content")]
    public List<ToolContent> Content { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the tool encountered an error.
    /// </summary>
    /// <value>If true, the content contains error information.</value>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

/// <summary>
/// Represents a piece of content returned by a tool.
/// </summary>
public class ToolContent
{
    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    /// <value>Currently only "text" is supported.</value>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = null!;
}

/// <summary>
/// Result of a tools/list request.
/// </summary>
public class ListToolsResult
{
    /// <summary>
    /// Gets or sets the list of available tools.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<Tool> Tools { get; set; } = new();
}

/// <summary>
/// Result of a resources/list request.
/// </summary>
public class ListResourcesResult
{
    /// <summary>
    /// Gets or sets the list of available resources.
    /// </summary>
    [JsonPropertyName("resources")]
    public List<Resource> Resources { get; set; } = new();
}

/// <summary>
/// Result of a prompts/list request.
/// </summary>
public class ListPromptsResult
{
    /// <summary>
    /// Gets or sets the list of available prompts.
    /// </summary>
    [JsonPropertyName("prompts")]
    public List<Prompt> Prompts { get; set; } = new();
}

/// <summary>
/// Request to read a specific resource by URI.
/// </summary>
public class ReadResourceRequest
{
    /// <summary>
    /// Gets or sets the URI of the resource to read.
    /// </summary>
    /// <example>codesearch://workspace/indexed-files</example>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = null!;
}

/// <summary>
/// Result of a resources/read request containing the resource content.
/// </summary>
public class ReadResourceResult
{
    /// <summary>
    /// Gets or sets the list of content items for this resource.
    /// </summary>
    [JsonPropertyName("contents")]
    public List<ResourceContent> Contents { get; set; } = new();
}

/// <summary>
/// Represents a piece of content within a resource.
/// </summary>
public class ResourceContent
{
    /// <summary>
    /// Gets or sets the URI identifying this content piece.
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = null!;

    /// <summary>
    /// Gets or sets the MIME type of the content.
    /// </summary>
    /// <example>text/plain, application/json, text/markdown</example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the base64-encoded binary content.
    /// </summary>
    [JsonPropertyName("blob")]
    public string? Blob { get; set; }
}

/// <summary>
/// Request to get a specific prompt by name with optional arguments.
/// </summary>
public class GetPromptRequest
{
    /// <summary>
    /// Gets or sets the name of the prompt to retrieve.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the arguments to customize the prompt.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Result of a prompts/get request containing the rendered prompt content.
/// </summary>
public class GetPromptResult
{
    /// <summary>
    /// Gets or sets the description of what this prompt accomplishes.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of messages that make up this prompt.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<PromptMessage> Messages { get; set; } = new();
}

/// <summary>
/// Represents a message within a prompt template.
/// </summary>
public class PromptMessage
{
    /// <summary>
    /// Gets or sets the role of the message sender.
    /// </summary>
    /// <example>user, assistant, system</example>
    [JsonPropertyName("role")]
    public string Role { get; set; } = null!;

    /// <summary>
    /// Gets or sets the content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public PromptContent Content { get; set; } = null!;
}

/// <summary>
/// Represents the content of a prompt message.
/// </summary>
public class PromptContent
{
    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    /// <value>Currently only "text" is supported.</value>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = null!;
}

/// <summary>
/// Notification sent to inform clients about the progress of long-running operations.
/// This extends JsonRpcNotification and provides structured progress information for
/// operations like workspace indexing, batch operations, and large file analysis.
/// </summary>
public class ProgressNotification : JsonRpcNotification
{
    /// <summary>
    /// Gets or sets the progress token that identifies this specific operation.
    /// This token is typically provided when starting the operation and should be
    /// used consistently across all progress updates for that operation.
    /// </summary>
    [JsonPropertyName("progressToken")]
    public string ProgressToken { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current progress value.
    /// This represents the number of completed items/steps in the operation.
    /// </summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>
    /// Gets or sets the total number of items/steps for this operation.
    /// When null, indicates that the total is unknown (indeterminate progress).
    /// </summary>
    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Total { get; set; }

    /// <summary>
    /// Gets or sets an optional descriptive message about the current progress step.
    /// </summary>
    /// <example>
    /// "Indexing file: UserService.cs"
    /// "Processing batch operation 5 of 12"
    /// "Analyzing project structure..."
    /// </example>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    /// <summary>
    /// Initializes a new instance of the ProgressNotification class.
    /// Sets the method to "notifications/progress" as per MCP protocol.
    /// </summary>
    public ProgressNotification()
    {
        Method = "notifications/progress";
    }

    /// <summary>
    /// Initializes a new instance of the ProgressNotification class with the specified parameters.
    /// </summary>
    /// <param name="progressToken">The progress token that identifies this operation.</param>
    /// <param name="progress">The current progress value.</param>
    /// <param name="total">The total number of items/steps (optional).</param>
    /// <param name="message">An optional descriptive message.</param>
    public ProgressNotification(string progressToken, int progress, int? total = null, string? message = null)
        : this()
    {
        ProgressToken = progressToken;
        Progress = progress;
        Total = total;
        Message = message;
    }
}

#region Type Safety Improvements - Generic Base Classes

/// <summary>
/// Generic base class for strongly-typed JSON-RPC requests.
/// This eliminates the need for object parameters and provides compile-time type safety.
/// </summary>
/// <typeparam name="TParams">The type of the request parameters.</typeparam>
public class TypedJsonRpcRequest<TParams> : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the request identifier. Used to correlate requests with responses.
    /// </summary>
    [JsonPropertyName("id")]
    public object Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the strongly-typed parameters for the method call.
    /// </summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TParams? Params { get; set; }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcRequest class.
    /// </summary>
    public TypedJsonRpcRequest() { }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcRequest class with the specified parameters.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The strongly-typed parameters.</param>
    public TypedJsonRpcRequest(object id, string method, TParams? parameters = default)
    {
        Id = id;
        Method = method;
        Params = parameters;
    }
}

/// <summary>
/// Generic base class for strongly-typed JSON-RPC responses.
/// This provides compile-time type safety for response data.
/// </summary>
/// <typeparam name="TResult">The type of the response result.</typeparam>
public class TypedJsonRpcResponse<TResult> : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the request identifier that this response correlates to.
    /// </summary>
    [JsonPropertyName("id")]
    public object Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the strongly-typed result of the method call.
    /// This property is mutually exclusive with Error.
    /// </summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TResult? Result { get; set; }

    /// <summary>
    /// Gets or sets the error information if the method call failed.
    /// This property is mutually exclusive with Result.
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }

    /// <summary>
    /// Gets a value indicating whether this response represents an error.
    /// </summary>
    [JsonIgnore]
    public bool IsError => Error != null;

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcResponse class.
    /// </summary>
    public TypedJsonRpcResponse() { }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcResponse class with a successful result.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="result">The result data.</param>
    public TypedJsonRpcResponse(object id, TResult result)
    {
        Id = id;
        Result = result;
    }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcResponse class with an error.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="error">The error information.</param>
    public TypedJsonRpcResponse(object id, JsonRpcError error)
    {
        Id = id;
        Error = error;
    }
}

/// <summary>
/// Generic base class for strongly-typed JSON-RPC notifications.
/// Notifications do not expect a response from the receiver.
/// </summary>
/// <typeparam name="TParams">The type of the notification parameters.</typeparam>
public class TypedJsonRpcNotification<TParams> : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the strongly-typed parameters for the method call.
    /// </summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TParams? Params { get; set; }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcNotification class.
    /// </summary>
    public TypedJsonRpcNotification() { }

    /// <summary>
    /// Initializes a new instance of the TypedJsonRpcNotification class with the specified parameters.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The strongly-typed parameters.</param>
    public TypedJsonRpcNotification(string method, TParams? parameters = default)
    {
        Method = method;
        Params = parameters;
    }
}

/// <summary>
/// Generic base class for MCP tool requests with strongly-typed parameters.
/// This provides a foundation for building type-safe tool implementations.
/// </summary>
/// <typeparam name="TParams">The type of the tool parameters.</typeparam>
public abstract class TypedToolRequest<TParams> : TypedJsonRpcRequest<TParams>
{
    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    [JsonIgnore]
    public abstract string ToolName { get; }

    /// <summary>
    /// Initializes a new instance of the TypedToolRequest class.
    /// </summary>
    protected TypedToolRequest()
    {
        Method = "tools/call";
    }

    /// <summary>
    /// Initializes a new instance of the TypedToolRequest class with the specified parameters.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="parameters">The strongly-typed tool parameters.</param>
    protected TypedToolRequest(object id, TParams? parameters = default)
        : base(id, "tools/call", parameters)
    {
    }
}

/// <summary>
/// Generic base class for MCP tool responses with strongly-typed results.
/// This provides compile-time type safety for tool result data.
/// </summary>
/// <typeparam name="TResult">The type of the tool result.</typeparam>
public class TypedToolResponse<TResult> : TypedJsonRpcResponse<CallToolResult>
{
    /// <summary>
    /// Gets or sets the strongly-typed tool result data.
    /// This is a convenience property that wraps the Result.Content.
    /// </summary>
    [JsonIgnore]
    public TResult? ToolResult
    {
        get
        {
            if (Result?.Content?.Count > 0 && Result.Content[0].Type == "application/json")
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<TResult>(Result.Content[0].Text);
                }
                catch
                {
                    return default;
                }
            }
            return default;
        }
        set
        {
            if (value != null)
            {
                Result = new CallToolResult
                {
                    Content = new List<ToolContent>
                    {
                        new ToolContent
                        {
                            Type = "application/json",
                            Text = System.Text.Json.JsonSerializer.Serialize(value)
                        }
                    }
                };
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the TypedToolResponse class.
    /// </summary>
    public TypedToolResponse() { }

    /// <summary>
    /// Initializes a new instance of the TypedToolResponse class with a successful result.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="toolResult">The strongly-typed tool result.</param>
    public TypedToolResponse(object id, TResult toolResult)
        : base(id, new CallToolResult())
    {
        ToolResult = toolResult;
    }

    /// <summary>
    /// Initializes a new instance of the TypedToolResponse class with an error.
    /// </summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="error">The error information.</param>
    public TypedToolResponse(object id, JsonRpcError error)
        : base(id, error)
    {
    }
}

#endregion