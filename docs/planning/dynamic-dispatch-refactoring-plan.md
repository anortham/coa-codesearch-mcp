# Dynamic Dispatch Refactoring Plan

## Current Situation Analysis

### What We've Learned

1. **Anonymous objects are already optimized**: Our performance tests showed that anonymous objects are:
   - Faster than JsonNode (97ms vs 156ms for 10k iterations)
   - Use less memory (6.7MB vs 19.8MB)
   - Compiler-optimized as strongly-typed classes

2. **The real performance issue**: Dynamic dispatch using `((dynamic)response).property` causes:
   - DLR (Dynamic Language Runtime) overhead
   - Runtime type resolution
   - No compile-time optimization

3. **Architecture insights**:
   - AIResponseBuilderService is designed to build consistent responses using anonymous objects
   - The service already has model classes defined at the bottom of the file
   - The goal is response consistency across all tools

### Current Dynamic Usage Locations

1. **FastTextSearchToolV2.cs** (Lines 230-234, 244-257)
   - Accessing response properties to store in resource provider
   - Creating enhanced response with resourceUri

2. **MemoryQualityAssessmentTool.cs** (5 instances)
   - Lines 341, 372, 378, 421, 427

3. **MemoryGraphNavigatorTool.cs** (7 instances)
   - Multiple helper methods using dynamic parameters

4. **FlexibleMemoryToolRegistrations.cs** (4 instances)
   - Helper methods for dynamic parameter access

## The Core Problem

We have a conflict between:
- **Flexibility**: Using anonymous objects for easy response building
- **Performance**: Avoiding dynamic dispatch overhead
- **Consistency**: Maintaining the same response format across all tools

## Proposed Solutions

### Option 1: Return Strongly-Typed Models (❌ Not Recommended)
- Create model classes for all responses
- Loses flexibility of anonymous objects
- Requires maintaining many model classes
- Goes against the current architecture design

### Option 2: Use JsonElement/JsonDocument (❌ Already Rejected)
- Performance tests show this is slower than anonymous objects
- Adds complexity without benefits

### Option 3: Avoid Dynamic Access Patterns (✅ Recommended)

#### Strategy 1: Pass Complete Objects
Instead of extracting properties with dynamic, pass the entire response object:

```csharp
// Bad: Dynamic property access
var resourceUri = _searchResultResourceProvider.StoreSearchResult(
    query, 
    new {
        query = ((dynamic)response).query,
        summary = ((dynamic)response).summary,
        // etc...
    }
);

// Good: Pass entire object
var resourceUri = _searchResultResourceProvider.StoreSearchResult(
    query, 
    response,  // The provider can handle the entire object
    metadata
);
```

#### Strategy 2: Use Builder Pattern for Enhanced Responses
When we need to add properties (like resourceUri), use a builder approach:

```csharp
// Instead of dynamic property copying, use a response enhancer
public static class ResponseEnhancer
{
    public static object AddResourceUri(object baseResponse, string resourceUri)
    {
        // Use JSON serialization once, not dynamic
        var json = JsonSerializer.Serialize(baseResponse);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        dict["resourceUri"] = resourceUri;
        return dict;
    }
}
```

#### Strategy 3: Use Interfaces for Common Properties
For tools that need to access specific properties, define minimal interfaces:

```csharp
public interface ISearchResponse
{
    object Query { get; }
    object Summary { get; }
}

// Anonymous objects can implement interfaces implicitly
```

## Implementation Plan

### Phase 1: FastTextSearchToolV2 Refactoring
1. **Remove dynamic casts** in lines 230-234
2. **Simplify resource storage** - pass entire response object
3. **Use ResponseEnhancer** for adding resourceUri
4. **Test** to ensure functionality is preserved

### Phase 2: MemoryQualityAssessmentTool Refactoring
1. **Identify patterns** in dynamic usage
2. **Create typed DTOs** for assessment results
3. **Replace dynamic with strongly-typed access**
4. **Test** quality assessment functionality

### Phase 3: MemoryGraphNavigatorTool Refactoring
1. **Review helper methods** using dynamic
2. **Create parameter models** for complex operations
3. **Replace dynamic with typed parameters**
4. **Test** graph navigation

### Phase 4: FlexibleMemoryToolRegistrations Refactoring
1. **Analyze GetBooleanProperty/GetIntegerProperty** methods
2. **Consider using generics** or reflection instead of dynamic
3. **Implement type-safe property access**
4. **Test** registration functionality

## Success Criteria

1. **No dynamic casts** in the codebase
2. **Maintain response consistency** - same JSON output
3. **Performance improvement** - measure with benchmarks
4. **All tests pass** - no functional regression

## Key Principles

1. **Prefer passing complete objects** over extracting properties
2. **Use JSON serialization sparingly** - only when necessary
3. **Keep anonymous objects** for flexibility
4. **Add types only where necessary** for performance

## Next Steps

1. Start with FastTextSearchToolV2 as the pilot
2. Measure performance before and after
3. Apply learnings to other tools
4. Document patterns for future development