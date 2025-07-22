# MCP Protocol Implementation Plan for CodeSearch

This document outlines which MCP protocol improvements would benefit the CodeSearch project and their implementation priority.

## High Priority - Immediate Benefits

### 1. Progress Tracking (Estimated: 2-3 hours)
**Why**: Long-running operations need feedback
**Use Cases**:
- Workspace indexing (can take 30+ seconds)
- Batch operations (multiple searches/renames)
- Memory summarization
- Large file analysis

**Implementation**:
```csharp
public class ProgressNotification : JsonRpcNotification
{
    [JsonPropertyName("progressToken")]
    public string ProgressToken { get; set; } = null!;
    
    [JsonPropertyName("progress")]
    public int Progress { get; set; }
    
    [JsonPropertyName("total")]
    public int? Total { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
```

### 2. Type Safety Improvements (Estimated: 3-4 hours)
**Why**: Current `object` types make tool development error-prone
**Changes Needed**:
- Generic request/response base classes
- Strongly typed tool parameters
- Better JSON serialization helpers

**Example**:
```csharp
public abstract class TypedToolRequest<TParams>
{
    public string Name { get; set; } = null!;
    public TParams Arguments { get; set; } = default!;
}
```

### 3. Error Code Constants (Estimated: 1 hour)
**Why**: Magic numbers (-32602, etc.) are unclear
**Implementation**:
```csharp
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    
    // MCP-specific
    public const int ResourceNotFound = -32001;
    public const int ResourceAccessDenied = -32002;
}
```

## Medium Priority - Future Enhancements

### 4. Resource Operations (Estimated: 4-5 hours)
**Why**: Expose read-only data without tool invocations
**Potential Resources**:
- `codesearch://stats` - Index statistics
- `codesearch://project-structure` - Solution hierarchy
- `codesearch://memory-dashboard` - Memory system overview
- `codesearch://indexed-files/{workspace}` - File list

**Benefits**:
- Clients can browse available data
- More efficient than tool calls for static data
- Can support subscriptions for live updates

### 5. Logging Protocol (Estimated: 2-3 hours)
**Why**: Better debugging and monitoring
**Features**:
- Real-time log streaming to clients
- Configurable log levels per session
- Structured log data

## Low Priority - Nice to Have

### 6. Lifecycle Management
- Ping/pong for connection health
- Graceful shutdown notifications
- Session management

### 7. Prompt Operations
- Not currently needed (we use tools exclusively)
- Could be useful for guided workflows in future

## Implementation Strategy

### Phase 1 (Week 1)
1. Add progress tracking
2. Implement error code constants
3. Create generic base classes for type safety

### Phase 2 (Week 2)
4. Add resource infrastructure
5. Implement 2-3 key resources
6. Add resource subscription support

### Phase 3 (Future)
7. Logging protocol
8. Additional resources as needed
9. Lifecycle improvements

## Breaking Changes

Most improvements can be added without breaking existing functionality:
- Progress notifications are optional
- Resources are additive
- Type improvements can use adapter pattern

Only breaking change would be replacing `object` with generics, which we can phase in gradually.

## Testing Requirements

Each new feature needs:
1. Unit tests for serialization/deserialization
2. Integration tests with actual MCP communication
3. Example usage in CodeSearch tools
4. Documentation updates

## Conclusion

The highest ROI improvements for CodeSearch are:
1. **Progress tracking** - Essential for UX during long operations
2. **Type safety** - Reduces bugs and improves developer experience
3. **Resources** - Enables new client interaction patterns

These can be implemented incrementally without disrupting current functionality.