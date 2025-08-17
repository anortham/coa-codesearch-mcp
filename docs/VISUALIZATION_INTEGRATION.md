# CodeSearch Visualization Integration Guide

## Overview

This guide covers integrating the new visualization protocol into CodeSearch MCP tools while maintaining backward compatibility with AI text responses. The key principle is **dual output**: tools provide both AI-optimized text AND structured visualization data.

## Migration Strategy

### Current State
- Tools use ResponseBuilder to create markdown strings
- Heavy string manipulation for formatting
- Tight coupling to display format

### Target State  
- Tools use IVSCodeBridge for visualization
- Return structured data with search results
- VS Code Bridge handles all rendering
- AI still receives optimized text responses

## Implementation Checklist

### Phase 1: Add VS Code Bridge Integration
- [ ] Reference COA.VSCodeBridge package
- [ ] Inject IVSCodeBridge into tools
- [ ] Use bridge for visualization output
- [ ] Maintain existing text response logic

### Phase 2: Update Search Tools
- [ ] TextSearchTool - Add search-results visualization
- [ ] FileSearchTool - Add file-list visualization  
- [ ] DirectorySearchTool - Add tree visualization
- [ ] SimilarFilesTool - Add similarity visualization

### Phase 3: Update Index Tools
- [ ] IndexWorkspaceTool - Add progress visualization
- [ ] BatchOperationsTool - Add multi-result visualization
- [ ] RecentFilesTool - Add timeline visualization

### Phase 4: Remove String Building
- [ ] Phase out StringBuilder usage
- [ ] Remove markdown generation helpers
- [ ] Clean up ResponseBuilder complexity
- [ ] Update tests for new approach

## Tool Implementation Pattern

### 1. Update Tool Class

```csharp
using COA.VSCodeBridge;

public class TextSearchTool : McpToolBase<TextSearchParameters, AIOptimizedResponse<SearchResult>>
{
    private readonly IVSCodeBridge _vscode;
    
    public TextSearchTool(IVSCodeBridge vscode, /* other dependencies */)
    {
        _vscode = vscode;
        // Initialize other dependencies...
    }
    
    protected override async Task<AIOptimizedResponse<SearchResult>> ExecuteInternalAsync(
        TextSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Perform search...
        var searchResult = await _luceneIndexService.SearchAsync(...);
        
        // Build AI-optimized response
        var response = _responseBuilder.Build(searchResult);
        
        // Send visualization to VS Code if requested
        if (parameters.ShowInVSCode ?? false)
        {
            await _vscode.ShowSearchResultsAsync(searchResult, parameters.VSCodeView);
        }
        
        return response;
    }
}
```

### 2. Use VS Code Bridge for Display

```csharp
public class TextSearchTool : McpToolBase<...>
{
    private readonly IVSCodeBridge _vscode;
    
    protected override async Task<AIOptimizedResponse<SearchResult>> ExecuteInternalAsync(
        TextSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Perform search...
        var searchResult = await _luceneIndexService.SearchAsync(...);
        
        // Store for visualization
        _lastSearchResult = searchResult;
        
        // Build AI-optimized text response (keep existing logic)
        var response = await _responseBuilder.BuildResponseAsync(searchResult, context);
        
        // Send to VS Code Bridge if requested
        if (parameters.ShowInVSCode ?? false)
        {
            await _vscode.ShowSearchResultsAsync(searchResult, parameters.VSCodeView);
        }
        
        return response;
    }
}
```

### 3. Dual Output Example

```csharp
// The tool provides BOTH outputs:

// 1. Text response for AI (existing)
var aiResponse = new AIOptimizedResponse<SearchResult>
{
    Success = true,
    Summary = $"Found {result.TotalHits} matches",
    Data = new
    {
        // Concise data for AI
        TopResults = result.Hits.Take(5).Select(h => new
        {
            File = Path.GetFileName(h.FilePath),
            Line = h.LineNumber,
            Match = h.Snippet
        })
    }
};

// 2. Send to VS Code Bridge for visualization
if (parameters.ShowInVSCode ?? false)
{
    await _vscode.ShowAsync(new
    {
        Type = "search-results",
        Query = parameters.Query,
        TotalHits = result.TotalHits,
        Results = result.Hits.Select(h => new
        {
            FilePath = h.FilePath,  // Full path for navigation
            LineNumber = h.LineNumber,
            Snippet = h.Snippet,
            Score = h.Score,
            ContextLines = h.ContextLines
        })
    });
}
```

## Specific Tool Migrations

### TextSearchTool

**Before:**
```csharp
// Complex markdown generation
var markdown = new StringBuilder();
markdown.AppendLine($"## Search Results");
foreach (var hit in results)
{
    markdown.AppendLine($"[{hit.FileName}]({hit.FilePath}:{hit.Line})");
    // ... more string manipulation
}
```

**After:**
```csharp
// Structured data sent to VS Code Bridge
await _vscode.ShowSearchResultsAsync(_searchResult, "list");
```

### FileSearchTool

**Visualization:** File list display

```csharp
// Send file search results to VS Code
if (parameters.ShowInVSCode ?? false)
{
    await _vscode.ShowFileListAsync(
        files: fileResults,
        view: parameters.VSCodeView ?? "list"
    );
}
```

### IndexWorkspaceTool

**Visualization:** Progress indicator

```csharp
// Report indexing progress to VS Code
await _vscode.ShowProgressAsync(
    title: "Indexing Workspace",
    current: currentProgress,
    total: totalFiles,
    message: currentFile
);
            ConsolidateTabs = true
        }
    };
}
```

### SimilarFilesTool

**Visualization Type:** `hierarchy`

```csharp
public VisualizationDescriptor GetVisualizationDescriptor()
{
    return new VisualizationDescriptor
    {
        Type = StandardVisualizationTypes.Hierarchy,
        Data = new
        {
            root = new
            {
                name = _sourceFile,
                type = "source",
                children = _similarFiles.Select(f => new
                {
                    name = f.FilePath,
                    type = "similar",
                    score = f.SimilarityScore,
                    children = Array.Empty<object>()
                })
            }
        }
    };
}
```

## Testing Approach

### 1. Unit Tests

```csharp
[Test]
public void TextSearchTool_ProvidesVisualization()
{
    // Arrange
    var tool = new TextSearchTool(...);
    var parameters = new TextSearchParameters 
    { 
        Query = "test",
        ShowInVSCode = true 
    };
    
    // Act
    var result = await tool.ExecuteAsync(parameters);
    var visualization = tool.GetVisualizationDescriptor();
    
    // Assert
    Assert.NotNull(visualization);
    Assert.AreEqual(StandardVisualizationTypes.SearchResults, visualization.Type);
    Assert.NotNull(visualization.Data);
}
```

### 2. Integration Tests

```csharp
[Test]
public async Task DualOutput_BothResponsesValid()
{
    // Execute tool
    var textResponse = await tool.ExecuteAsync(params);
    var vizResponse = tool.GetVisualizationDescriptor();
    
    // Verify text response for AI
    Assert.IsTrue(textResponse.Success);
    Assert.IsNotEmpty(textResponse.Summary);
    
    // Verify visualization response
    Assert.NotNull(vizResponse);
    Assert.AreEqual("search-results", vizResponse.Type);
}
```

### 3. Backward Compatibility Tests

```csharp
[Test]
public async Task NoVisualization_WhenNotRequested()
{
    var parameters = new TextSearchParameters 
    { 
        ShowInVSCode = false  // Not requested
    };
    
    var result = await tool.ExecuteAsync(parameters);
    var viz = tool.GetVisualizationDescriptor();
    
    // Should work without visualization
    Assert.IsTrue(result.Success);
    Assert.IsNull(viz);  // No visualization when not needed
}
```

## Performance Considerations

### 1. Lazy Visualization

Only create visualization when needed:

```csharp
public VisualizationDescriptor? GetVisualizationDescriptor()
{
    // Don't create visualization if not requested
    if (!_visualizationRequested)
        return null;
        
    // Don't create for empty results
    if (_lastResult?.Hits?.Any() != true)
        return null;
        
    // Create visualization
    return new VisualizationDescriptor { ... };
}
```

### 2. Data Size Limits

Limit visualization data size:

```csharp
Data = new
{
    results = _lastResult.Hits
        .Take(100)  // Limit to 100 for visualization
        .Select(h => new { ... })
}
```

### 3. Caching

Cache visualization descriptors:

```csharp
private VisualizationDescriptor? _cachedVisualization;
private string? _cachedVisualizationKey;

public VisualizationDescriptor? GetVisualizationDescriptor()
{
    var key = $"{_lastQuery}_{_lastResult?.GetHashCode()}";
    if (key == _cachedVisualizationKey)
        return _cachedVisualization;
        
    _cachedVisualization = CreateVisualization();
    _cachedVisualizationKey = key;
    return _cachedVisualization;
}
```

## Migration Timeline

### Week 1
- Add visualization package reference
- Update TextSearchTool with dual output
- Test with VS Code Bridge

### Week 2  
- Migrate remaining search tools
- Update index and batch tools
- Remove deprecated markdown helpers

### Week 3
- Full testing and validation
- Performance optimization
- Documentation updates

## Common Patterns

### Pattern 1: Optional Visualization

```csharp
public VisualizationDescriptor? GetVisualizationDescriptor()
{
    // Only when we have data
    if (_lastResult == null) return null;
    
    // Only when successful
    if (!_lastResult.Success) return null;
    
    // Only when requested
    if (!_parameters?.ShowInVSCode ?? true) return null;
    
    return new VisualizationDescriptor { ... };
}
```

### Pattern 2: Progressive Enhancement

```csharp
// Start with basic type
var descriptor = new VisualizationDescriptor
{
    Type = StandardVisualizationTypes.JsonTree,
    Data = _lastResult
};

// Enhance if possible
if (CanUseRichVisualization())
{
    descriptor.Type = StandardVisualizationTypes.SearchResults;
    descriptor.Hint = new VisualizationHint { ... };
}

return descriptor;
```

### Pattern 3: Fallback Chain

```csharp
public VisualizationDescriptor GetVisualizationDescriptor()
{
    try
    {
        // Try rich visualization
        return CreateRichVisualization();
    }
    catch
    {
        // Fall back to simple
        return new VisualizationDescriptor
        {
            Type = StandardVisualizationTypes.JsonTree,
            Data = _lastResult,
            Hint = new VisualizationHint 
            { 
                FallbackFormat = "json" 
            }
        };
    }
}
```

## Troubleshooting

### Issue: Visualization not appearing
- Check `ShowInVSCode` parameter
- Verify VS Code Bridge is connected
- Check browser console in webview

### Issue: Performance degradation
- Limit data size in visualization
- Use lazy creation pattern
- Enable caching

### Issue: Type mismatch errors
- Verify data structure matches type
- Check version compatibility
- Use fallback format

## Benefits After Migration

1. **No more string building frustration**
2. **Faster development iteration**
3. **Cleaner, more maintainable code**
4. **Rich, interactive visualizations**
5. **Better separation of concerns**
6. **Easier to test**
7. **Framework handles complexity**

## Next Steps

1. Start with TextSearchTool as proof of concept
2. Test with VS Code Bridge
3. Iterate on visualization format
4. Roll out to other tools
5. Remove legacy markdown code
6. Document patterns for new tools