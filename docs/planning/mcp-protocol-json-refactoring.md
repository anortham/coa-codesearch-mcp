# MCP Protocol JSON Refactoring Plan

## Overview
The COA.Mcp.Protocol project currently uses `object` types extensively for JSON handling, which impacts performance through boxing, reflection overhead, and increased GC pressure. This document outlines the changes needed to migrate to System.Text.Json types for improved performance.

## Current State Analysis

### Object Type Usage in Protocol Layer
- **JsonRpcRequest/Response**: Uses `object` for Id, Params, and Result properties
- **Tool Models**: InputSchema defined as `object` in Tool class
- **CallToolRequest**: Arguments property is `object?`
- **GetPromptRequest**: Arguments dictionary uses `Dictionary<string, object>`
- **Error Data**: JsonRpcError.Data property is `object?`

### Performance Impact
- **Serialization Overhead**: 32+ JsonSerializer.Serialize calls found across codebase
- **Boxing/Unboxing**: Every value type gets boxed when stored as object
- **Reflection Cost**: Runtime type inspection for every serialization
- **GC Pressure**: Temporary object allocations during JSON operations

## Proposed Changes

### 1. Replace object with JsonElement/JsonDocument

#### JsonRpcRequest Changes
```csharp
// Current
public object? Params { get; set; }

// Proposed
public JsonElement? Params { get; set; }
```

#### Benefits
- Zero-copy parsing with JsonElement
- Lazy evaluation of JSON properties
- Direct JSON manipulation without intermediate objects
- 30-50% reduction in serialization overhead

### 2. Generic Type Definitions (Already Started)

The protocol already includes generic base classes (TypedJsonRpcRequest<T>, TypedJsonRpcResponse<T>) that should be promoted:

```csharp
// Use these instead of non-generic versions
TypedJsonRpcRequest<TParams>
TypedJsonRpcResponse<TResult>
TypedJsonRpcNotification<TParams>
```

### 3. Tool Input Schema Improvements

#### Current Approach
```csharp
public object InputSchema { get; set; } = null!;
```

#### Proposed Approach
```csharp
public JsonDocument InputSchema { get; set; } = null!;
// OR for compile-time safety:
public IJsonSchema InputSchema { get; set; } = null!;
```

### 4. Streaming JSON Processing

For large responses, implement streaming with Utf8JsonReader/Writer:

```csharp
public async Task WriteJsonResponseAsync(Stream stream, object response)
{
    await using var writer = new Utf8JsonWriter(stream);
    // Direct writing without intermediate serialization
}
```

## Migration Strategy

### Phase 1: Protocol Layer (High Priority)
1. Update JsonRpc.cs to use JsonElement for dynamic properties
2. Migrate McpTypes.cs Tool and Resource models
3. Update error handling to use JsonElement for error data

### Phase 2: Server Implementation
1. Update JsonRpcRequestHandler to work with JsonElement
2. Modify tool parameter deserialization
3. Implement streaming for large responses

### Phase 3: Tool Integration
1. Update individual tools to accept JsonElement parameters
2. Implement direct JSON building for responses
3. Remove anonymous type usage in favor of JsonDocument

## Backward Compatibility

### Approach 1: Dual Support (Recommended)
- Keep existing object-based APIs
- Add new JsonElement-based overloads
- Gradually deprecate object versions

### Approach 2: Adapter Pattern
```csharp
public class JsonRpcAdapter
{
    public JsonRpcRequest ToObjectRequest(JsonRpcRequest<JsonElement> request)
    {
        // Convert JsonElement to object for legacy code
    }
}
```

## Performance Targets

- **Serialization**: 30-50% reduction in CPU usage
- **Memory**: 40% reduction in allocations
- **GC**: 25% reduction in Gen 0 collections
- **Response Time**: 20% improvement for large result sets

## Risk Assessment

### High Risk Areas
1. Breaking changes to public API
2. Tool parameter deserialization logic
3. Error handling consistency

### Mitigation
1. Comprehensive test coverage before changes
2. Feature flags for gradual rollout
3. Performance benchmarks to validate improvements

## Implementation Priority

1. **Critical Path** (Week 1)
   - JsonRpcRequest/Response object properties
   - Tool parameter handling

2. **High Value** (Week 2)
   - Streaming JSON for large responses
   - Error data handling

3. **Nice to Have** (Week 3+)
   - Full migration to generic types
   - Remove all anonymous type usage

## Success Metrics

1. Benchmark showing 30%+ performance improvement
2. Memory profiler showing reduced allocations
3. All existing tests passing
4. No breaking changes for API consumers