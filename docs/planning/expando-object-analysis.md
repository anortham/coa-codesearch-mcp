# ExpandoObject Performance Analysis

## Test Results Summary

### Object Construction Performance (10,000 iterations)
1. **Dynamic reconstruction with anonymous objects**: 0ms (baseline)
2. **ExpandoObject (manual copying)**: 6ms (6x slower)
3. **ExpandoObject (via JSON)**: 124ms (124x slower)
4. **Dictionary<string,object>**: 1ms (marginally slower)

### Memory Usage (1,000 iterations)
1. **Dynamic with anonymous objects**: 82,000 bytes
2. **ExpandoObject**: 426,400 bytes (5.2x more memory)

### Property Access Performance (100,000 iterations)
1. **Anonymous object via dynamic**: 1ms
2. **ExpandoObject**: 4ms (4x slower)
3. **Dictionary**: 1ms (same as dynamic)

## Analysis

### ExpandoObject Characteristics

**Pros:**
- True dynamic object - can add/remove properties at runtime
- Implements `IDictionary<string, object>` for enumeration
- Works well with dynamic languages and scripting scenarios
- Can bind to data grids and other UI elements

**Cons:**
- **5.2x more memory** than anonymous objects
- **6x slower** for object construction
- **4x slower** for property access
- More complex than anonymous objects

### Why ExpandoObject is Slower

1. **Internal Implementation**: ExpandoObject maintains an internal dictionary and implements `IDynamicMetaObjectProvider`
2. **Runtime Overhead**: Each property access goes through DLR binding
3. **Memory Layout**: Not as cache-friendly as compiler-generated anonymous types
4. **No Compile-Time Optimization**: Can't be optimized like anonymous objects

### Dictionary<string,object> Performance

Surprisingly competitive:
- Only marginally slower than dynamic (1ms vs 0ms)
- Same property access speed as dynamic
- More explicit about being a key-value store
- Better for scenarios where keys are dynamic strings

## Recommendations

### ✅ Best Choice: Dynamic with Anonymous Objects

```csharp
// BEST: Fast, memory-efficient, clean syntax
dynamic d = response;
return new
{
    success = d.success,
    operation = d.operation,
    data = d.data,
    newProperty = "value"
};
```

### 🤔 Consider Dictionary<string,object> When:

```csharp
// When property names are dynamic or computed
var result = new Dictionary<string, object>();
foreach (var prop in propertiesToCopy)
{
    result[prop.Name] = prop.Value;
}
```

### ❌ Avoid ExpandoObject For:
- High-performance scenarios
- Memory-constrained environments
- Simple property copying/enhancement

### ✅ Use ExpandoObject Only When:
- True runtime property addition/removal is needed
- Interfacing with dynamic languages
- Building objects with completely unknown shape at compile time

## Consistency Strategy Update

Based on these findings, our consistency strategy remains:

1. **Primary Pattern**: Use dynamic with anonymous objects
2. **Secondary Pattern**: Use Dictionary<string,object> when keys are computed
3. **Avoid**: ExpandoObject unless truly needed for runtime flexibility
4. **Never**: JSON serialization for property manipulation

## Example Patterns

### Pattern 1: Response Enhancement (Use Dynamic)
```csharp
public static object EnhanceResponse(object response, string resourceUri)
{
    dynamic d = response;
    return new
    {
        // All original properties
        success = d.success,
        operation = d.operation,
        data = d.data,
        // New property
        resourceUri = resourceUri
    };
}
```

### Pattern 2: Dynamic Property Names (Use Dictionary)
```csharp
public static Dictionary<string, object> BuildDynamicResponse(
    object data, 
    Dictionary<string, object> additionalProps)
{
    dynamic d = data;
    var result = new Dictionary<string, object>
    {
        ["success"] = true,
        ["data"] = d.mainData
    };
    
    // Add dynamic properties
    foreach (var kvp in additionalProps)
    {
        result[kvp.Key] = kvp.Value;
    }
    
    return result;
}
```

### Pattern 3: Avoid This (ExpandoObject)
```csharp
// DON'T DO THIS - 5x memory, 6x slower
dynamic expando = new ExpandoObject();
expando.success = source.success;
expando.data = source.data;
return expando;
```

## Conclusion

ExpandoObject doesn't help with consistency - it actually makes things worse for our use case. Stick with dynamic + anonymous objects for the best performance and memory efficiency.