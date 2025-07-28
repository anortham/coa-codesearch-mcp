# Analyzer Architecture Decision Record

## Status
**IMPLEMENTED** - January 2025

## Context

The COA CodeSearch MCP server contains two distinct search systems with fundamentally different requirements:

1. **Code Search Tools** - Used to find precise matches in source code
2. **Memory Search Tools** - Used to find conceptual matches in knowledge artifacts

Initially, both systems used different analyzers inconsistently, leading to:
- Search result mismatches between indexing and querying
- Failed memory search tests 
- Confusion about which analyzer to use where

## Problem

**The Root Issue**: Using the wrong analyzer for the wrong search type breaks functionality:

- **Code Search** needs **precise matching**: `class UserService` should find exact class names, not conceptually related terms
- **Memory Search** needs **conceptual matching**: "auth" should find memories about "authentication", "authorization", "security", etc.

**Critical Bug**: The Lucene expert initially changed ALL searches to use `MemoryAnalyzer`, breaking code search precision and causing analyzer instance mismatches between indexing and querying operations.

**Additional Issue**: The expert also implemented HTML highlighting for search results, but consultation with the AI-UX expert revealed this adds 10-15% token overhead with minimal benefit for AI agents. Highlighting was deliberately disabled by default but kept available in case future structured highlighting needs arise.

## Decision

**Implement Path-Based Analyzer Selection in `LuceneIndexService`**:

### Architecture
```
LuceneIndexService
├── StandardAnalyzer    (for code workspace paths)
└── MemoryAnalyzer      (for .codesearch/memory/ paths)
```

### Implementation
- **Single source of analyzer instances** in `LuceneIndexService`
- **Path-based selection** using `GetAnalyzerForWorkspace()`
- **Consistent indexing/querying** - same analyzer used for both operations
- **FlexibleMemoryService** gets analyzer from `LuceneIndexService` via `GetAnalyzerAsync()`

### Analyzer Responsibilities

| System | Analyzer | Features | Use Case |
|--------|----------|----------|----------|
| Code Search | `StandardAnalyzer` | Precise, literal matching | Finding exact functions, classes, variables |
| Memory Search | `MemoryAnalyzer` | Synonyms, stemming, fuzzy | Finding conceptual knowledge artifacts |

## Implementation Details

### LuceneIndexService Changes
```csharp
private readonly StandardAnalyzer _standardAnalyzer;
private readonly MemoryAnalyzer _memoryAnalyzer;

private Analyzer GetAnalyzerForWorkspace(string pathToCheck)
{
    var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
    var localMemoryPath = _pathResolution.GetLocalMemoryPath();
    
    if (pathToCheck.Equals(projectMemoryPath, StringComparison.OrdinalIgnoreCase) ||
        pathToCheck.Equals(localMemoryPath, StringComparison.OrdinalIgnoreCase) ||
        _pathResolution.IsProtectedPath(pathToCheck))
    {
        return _memoryAnalyzer;  // Memory paths use conceptual search
    }
    
    return _standardAnalyzer;    // Code paths use precise search
}
```

### FlexibleMemoryService Changes
```csharp
// REMOVED: private readonly MemoryAnalyzer _analyzer;

// NEW: Get analyzer from LuceneIndexService
var analyzer = await _indexService.GetAnalyzerAsync(_projectMemoryWorkspace);
```

## Consequences

### Positive
- ✅ **Correct analyzer for each search type**
- ✅ **Consistent indexing/querying** - no more analyzer instance mismatches
- ✅ **Memory search tests now pass** 
- ✅ **Code search precision maintained**
- ✅ **Single source of truth** for analyzer instances

### Negative
- ❌ **Slight complexity** in path-based selection logic
- ❌ **Async calls** needed to get analyzer instances

### Neutral
- 🔶 **Expert guidance required** - future Lucene experts must understand dual system

## Lessons Learned

### For Future Expert Engagement
1. **Always brief experts on dual system architecture**
2. **Require cross-system impact analysis** for any analyzer changes  
3. **Test both code search AND memory search** after Lucene modifications
4. **Document architectural boundaries clearly**

### Key Insight
**Specialized systems need specialized analyzers**. One-size-fits-all approaches break when systems have fundamentally different requirements.

## Monitoring

To ensure this decision remains effective:
- **Memory search tests** must pass: `BasicStoreAndSearch_Works`, `StoreMultipleAndSearchAll_Works`
- **Code search precision** must be maintained for exact matches
- **No analyzer instance mismatches** between indexing and querying
- **Performance impact** should be minimal (< 5ms overhead for analyzer selection)

## References

- [PHASE_2_TASKS.md](PHASE_2_TASKS.md) - Task 2.1 implementation
- [LuceneIndexService.cs](../COA.CodeSearch.McpServer/Services/LuceneIndexService.cs) - Implementation
- [FlexibleMemoryService.cs](../COA.CodeSearch.McpServer/Services/FlexibleMemoryService.cs) - Usage
- [LUCENE_EXPERT_BRIEF.md](LUCENE_EXPERT_BRIEF.md) - Expert guidance document