# Phase 2: Token Optimization Plan

## Problem Statement
Memory storage operations are consuming ~28,600 tokens for simple operations that should use <1,000 tokens. This represents a 28x inefficiency.

## Root Cause Analysis

### Investigation Results
1. **Memory tools return minimal data** - Simple JSON responses (~200-500 characters)
2. **No unnecessary searches during storage** - StoreMemoryAsync is clean
3. **Tool registration adds JSON serialization** - Minor overhead, not 28k tokens worth

### Suspected Causes
1. **MCP Protocol Overhead** - The protocol itself might be verbose
2. **Claude Code Integration** - Additional wrapping/logging at the integration layer
3. **Response Duplication** - Responses might be duplicated in the protocol
4. **Hidden Debugging Data** - Protocol might include stack traces or verbose metadata

## Phase 2 Implementation Plan

### 1. Token Tracking and Measurement (Day 1)
- [ ] Add token counting to all memory operations
- [ ] Create TokenUsageService to track and report usage
- [ ] Add response size logging to ToolRegistrationHelper
- [ ] Implement token usage reporting in tool responses

### 2. Response Optimization (Day 1-2)
- [ ] Remove JSON pretty-printing from CreateSuccessResult
- [ ] Implement compact response format option
- [ ] Add response compression for large results
- [ ] Create minimal response mode for simple confirmations

### 3. Protocol Analysis (Day 2)
- [ ] Add MCP protocol logging to capture raw messages
- [ ] Analyze protocol overhead vs actual payload
- [ ] Identify any response duplication
- [ ] Check for hidden metadata in responses

### 4. Tool-Specific Optimizations (Day 2-3)
- [ ] Optimize RememberSession to return minimal confirmation
- [ ] Reduce RecallContext response size with pagination
- [ ] Implement streaming for large responses
- [ ] Add response size limits with overflow handling

### 5. Integration Improvements (Day 3)
- [ ] Work with Claude Code team to understand integration overhead
- [ ] Implement protocol-level compression if supported
- [ ] Add response caching to avoid redundant operations
- [ ] Create token budget system for operations

## Success Metrics
- Memory storage operations use <1,000 tokens
- Complex queries use <5,000 tokens
- 95% reduction in token usage
- No loss of functionality

## Implementation Priority
1. **Critical**: Token tracking (can't optimize what we can't measure)
2. **High**: Response optimization (quick wins)
3. **Medium**: Protocol analysis (understand the problem)
4. **Medium**: Tool-specific optimizations
5. **Low**: Integration improvements (requires external coordination)

## Code Changes Required

### 1. TokenUsageService.cs
```csharp
public interface ITokenUsageService
{
    int EstimateTokens(string text);
    void TrackUsage(string operation, int tokens);
    TokenUsageReport GetReport();
}
```

### 2. Modify ToolRegistrationHelper.cs
```csharp
public static CallToolResult CreateSuccessResult(object result, bool compact = true)
{
    var options = new JsonSerializerOptions 
    { 
        WriteIndented = !compact, // No pretty printing by default
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    var json = JsonSerializer.Serialize(result, options);
    
    // Log token usage
    var estimatedTokens = EstimateTokens(json);
    _logger.LogDebug("Tool response: {Tokens} tokens", estimatedTokens);
    
    return new CallToolResult
    {
        Content = new List<ToolContent>
        {
            new ToolContent
            {
                Type = "text",
                Text = json
            }
        }
    };
}
```

### 3. Minimal Confirmation Response
```csharp
public class MinimalResponse
{
    public bool Success { get; set; }
    public string? Id { get; set; } // Only if needed
    // No message unless error
}
```

## Testing Strategy
1. Create token usage benchmarks for all operations
2. Compare before/after token counts
3. Ensure functionality remains intact
4. Test with large datasets to verify optimizations

## Rollback Plan
- All changes should be behind feature flags
- Ability to switch between compact/verbose modes
- Maintain backward compatibility

## Next Steps
1. Get approval for Phase 2 plan
2. Set up token tracking infrastructure
3. Begin implementation with quick wins
4. Measure and iterate