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