# MCP Protocol Implementation Comparison

## Overview

This document compares the MCP (Model Context Protocol) implementations between two projects:
1. **COA CodeSearch MCP** (Current project) - Roslyn-based code search
2. **COA Directus MCP** - Directus CMS integration

## Key Findings

### 1. Protocol Architecture

#### COA Directus MCP (Separated Protocol)
- **Separate Protocol Project**: `COA.Directus.Mcp.Protocol`
  - Contains all JSON-RPC and MCP type definitions
  - Can be packaged and reused as a NuGet package
  - Clean separation of concerns
  - Well-documented with XML comments

- **Structure**:
  ```
  COA.Directus.Mcp.Protocol/
  ├── JsonRpc.cs      # JSON-RPC base types
  └── McpTypes.cs     # MCP-specific types
  ```

#### COA CodeSearch MCP (Inline Protocol)
- **Inline Implementation**: Protocol types defined directly in Program.cs
  - JsonRpcRequest, JsonRpcResponse, JsonRpcError at bottom of file
  - No separate protocol assembly
  - Minimal implementation focused on immediate needs

### 2. JSON-RPC Implementation

#### COA Directus MCP
```csharp
// Base class hierarchy
public abstract class JsonRpcMessage
public class JsonRpcRequest : JsonRpcMessage
public class JsonRpcResponse : JsonRpcMessage
public class JsonRpcNotification : JsonRpcMessage
public class JsonRpcError

// Rich type system with proper inheritance
```

#### COA CodeSearch MCP
```csharp
// Simple flat types
public class JsonRpcRequest
public class JsonRpcResponse
public class JsonRpcError

// No base class, no notification support
```

### 3. MCP Types

#### COA Directus MCP
- Comprehensive type definitions:
  - `ServerCapabilities`, `ClientCapabilities`
  - `InitializeRequest`, `InitializeResult`
  - `Tool`, `Resource`, `Prompt`
  - `CallToolRequest`, `CallToolResult`
  - Detailed capability negotiation

#### COA CodeSearch MCP
- Minimal type support:
  - No dedicated MCP types
  - Inline anonymous objects for responses
  - Hard-coded capability responses

### 4. Request Handling

#### COA Directus MCP
```csharp
// Structured handler with proper error handling
private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request)
{
    try
    {
        object? result = request.Method switch
        {
            "initialize" => await HandleInitializeAsync(request),
            "tools/call" => await HandleCallToolAsync(request),
            // ... more handlers
        };
        // Structured error handling for different exception types
    }
}
```

#### COA CodeSearch MCP
```csharp
// Single static method with all logic inline
static async Task<JsonRpcResponse> HandleRequest(
    JsonRpcRequest request,
    // ... many tool parameters
)
{
    // Large switch statement with inline logic
    // Less structured error handling
}
```

### 5. Server Architecture

#### COA Directus MCP
- **Hosted Service Pattern**: Uses `IHostedService`
- **DI Container**: Full dependency injection
- **Logging**: Structured logging to stderr
- **Configuration**: IConfiguration support
- **Clean separation**: McpServer class handles protocol

#### COA CodeSearch MCP
- **Direct Execution**: No hosted service
- **Manual DI**: Services created manually
- **Minimal Logging**: NullLogger by default
- **Basic Configuration**: Limited config support
- **Inline Implementation**: All in Program.cs

### 6. Tool Registration

#### COA Directus MCP
- **Registry Pattern**: `ToolRegistry` class
- **Dynamic Registration**: Tools can be added/removed
- **Metadata Rich**: Full tool descriptions and schemas

#### COA CodeSearch MCP
- **Static List**: Hard-coded tool list in `GetToolsList()`
- **Manual Wiring**: Each tool manually connected in switch
- **Fixed Set**: Tools cannot be dynamically registered

## Recommendations

### 1. Reuse Directus Protocol Package

The COA.Directus.Mcp.Protocol package could be extracted and reused:

**Benefits:**
- Consistent protocol implementation across projects
- Well-tested and documented types
- Reduces code duplication
- Easier to maintain protocol compliance

**Implementation Steps:**
1. Extract Protocol package to separate repository or NuGet
2. Reference in CodeSearch project
3. Refactor to use proper types instead of anonymous objects
4. Implement proper request/response handling

### 2. Adopt Directus Architecture Patterns

Consider adopting these patterns from Directus:
- Hosted service for cleaner lifecycle management
- Registry pattern for tools
- Structured error handling with custom exceptions
- Proper logging to stderr

### 3. Migration Path

To migrate CodeSearch to use the shared protocol:

```csharp
// 1. Add package reference
<PackageReference Include="COA.Directus.Mcp.Protocol" Version="1.0.0" />

// 2. Replace inline types
// Old:
public class JsonRpcRequest { ... }

// New:
using COA.Directus.Mcp.Protocol;

// 3. Update response handling
// Old:
Result = new { protocolVersion = "2024-11-05", ... }

// New:
Result = new InitializeResult
{
    ProtocolVersion = "2024-11-05",
    ServerInfo = new Implementation { ... },
    Capabilities = new ServerCapabilities { ... }
}

// 4. Implement proper tool result format
// Old:
var mcpResult = new { content = new[] { new { type = "text", text = ... } } };

// New:
var mcpResult = new CallToolResult
{
    Content = new List<ToolContent>
    {
        new() { Type = "text", Text = JsonSerializer.Serialize(result) }
    }
};
```

## Summary

The Directus project demonstrates a more mature and maintainable approach to MCP implementation:
- Clean separation of protocol types
- Better error handling and logging
- More extensible architecture
- Reusable components

The CodeSearch project would benefit significantly from adopting the Directus protocol package and architectural patterns, leading to:
- Better maintainability
- Easier protocol compliance
- Reduced code duplication
- More robust error handling