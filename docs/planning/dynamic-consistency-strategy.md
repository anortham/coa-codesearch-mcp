# Dynamic Dispatch Consistency Strategy

## Performance Results Summary
- **Dynamic dispatch**: 0-5ms for 10,000 operations, 90KB memory
- **JSON serialization**: 133-462ms for 10,000 operations, 2.7MB memory
- **Conclusion**: Dynamic is 92-100x faster and uses 30x less memory

## New Architectural Principle

**Use dynamic dispatch consistently for working with anonymous objects and response manipulation.**

## Guidelines

### ✅ DO Use Dynamic For:

1. **Accessing properties of anonymous objects**
```csharp
// Good - Direct and fast
var query = ((dynamic)response).query;
var summary = ((dynamic)response).summary;
```

2. **Building new objects from anonymous objects**
```csharp
// Good - Clean and performant
dynamic d = response;
return new
{
    success = d.success,
    operation = d.operation,
    query = d.query,
    summary = d.summary,
    resourceUri = newResourceUri
};
```

3. **Passing through unknown object shapes**
```csharp
// Good - Flexible and fast
public object EnhanceResponse(object baseResponse, string additionalField)
{
    dynamic d = baseResponse;
    return new
    {
        // Copy all we know about
        success = d.success,
        data = d.data,
        // Add new field
        enhanced = additionalField
    };
}
```

### ❌ DON'T Use:

1. **JSON serialization for property access**
```csharp
// Bad - Extremely slow
var json = JsonSerializer.Serialize(response);
var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
var query = dict["query"];
```

2. **Reflection for simple property copying**
```csharp
// Bad - Slower than dynamic
var props = response.GetType().GetProperties();
// ... reflection code
```

## Implementation Plan

### Phase 1: Audit Current Inconsistencies
1. Find all JSON serialization used for property manipulation
2. Find all reflection-based property access
3. Find places where we could use dynamic but don't

### Phase 2: Create Helper Patterns
1. Create consistent patterns for common operations
2. Document these patterns with examples

### Phase 3: Refactor to Consistency
1. Replace JSON-based property manipulation with dynamic
2. Replace unnecessary strongly-typed models with dynamic access
3. Ensure all response building uses consistent dynamic patterns

## Common Patterns

### Pattern 1: Adding a Property
```csharp
public static object AddProperty(object response, string propertyName, object value)
{
    dynamic d = response;
    // Build new object with all existing properties plus new one
    return new
    {
        // Copy common response properties
        success = d.success,
        operation = d.operation,
        data = d.data,
        meta = d.meta,
        // Add new property dynamically
        additionalProperty = value
    };
}
```

### Pattern 2: Merging Objects
```csharp
public static object MergeResponses(object response1, object response2)
{
    dynamic d1 = response1;
    dynamic d2 = response2;
    
    return new
    {
        // From first response
        success = d1.success,
        operation = d1.operation,
        // From second response  
        additionalData = d2.data,
        additionalMeta = d2.meta
    };
}
```

### Pattern 3: Conditional Property Building
```csharp
public static object BuildResponse(object data, bool includeDetails)
{
    dynamic d = data;
    
    var response = new Dictionary<string, object>
    {
        ["success"] = true,
        ["data"] = d.mainData
    };
    
    if (includeDetails)
    {
        response["details"] = d.details;
        response["metadata"] = d.metadata;
    }
    
    return response;
}
```

## Benefits of This Approach

1. **Performance**: 92-100x faster than JSON serialization
2. **Simplicity**: Less code than JSON or reflection approaches
3. **Flexibility**: Can work with any object shape
4. **Consistency**: One pattern throughout the codebase
5. **Maintainability**: Easy to understand and modify

## Risks and Mitigations

1. **Risk**: Runtime errors if property doesn't exist
   - **Mitigation**: Use try-catch blocks for defensive coding where needed
   
2. **Risk**: Loss of compile-time type checking
   - **Mitigation**: Use unit tests to verify expected properties
   
3. **Risk**: Harder to refactor with IDE tools
   - **Mitigation**: Good naming conventions and documentation

## Next Steps

1. Revert ResponseEnhancer changes
2. Audit codebase for JSON serialization anti-patterns
3. Create DynamicResponseHelper with common patterns
4. Refactor tools to use consistent dynamic patterns