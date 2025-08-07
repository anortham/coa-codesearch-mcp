# COA Framework Component Mapping Guide
## Detailed CodeSearch to Framework Migration Map

> **Version**: Framework v1.1.0  
> **Created**: 2025-08-06  
> **Purpose**: Exact component-by-component migration reference

## 🗺️ Complete Component Mapping

### 1. Tool Implementation Mapping

#### Current CodeSearch Pattern
```csharp
// Current: COA.CodeSearch.McpServer\Tools\TextSearchTool.cs
[McpServerToolType]
public class TextSearchTool
{
    private readonly ILuceneIndexService _indexService;
    private readonly ILogger<TextSearchTool> _logger;
    
    [McpServerTool(Name = "text_search")]
    [Description("Search for text patterns in indexed files")]
    public async Task<object> ExecuteAsync(TextSearchParams parameters)
    {
        // Manual validation
        if (string.IsNullOrEmpty(parameters.Query))
            throw new InvalidParametersException("Query is required");
            
        // Implementation
        var results = await _indexService.SearchAsync(parameters);
        
        // Manual response building
        return new { 
            format = "ai-optimized",
            data = results,
            insights = GenerateInsights(results),
            actions = GenerateActions(results)
        };
    }
}
```

#### Migrated Framework Pattern
```csharp
// Migrated: Using COA.Mcp.Framework
[McpServerToolType]
public class TextSearchTool : McpToolBase<TextSearchParams, TextSearchResult>
{
    private readonly ILuceneIndexService _indexService;
    private readonly TextSearchResponseBuilder _responseBuilder;
    
    public override string Name => "text_search";
    public override string Description => "Search for text patterns in indexed files";
    public override ToolCategory Category => ToolCategory.Search;
    
    protected override async Task<TextSearchResult> ExecuteInternalAsync(
        TextSearchParams parameters, 
        CancellationToken cancellationToken)
    {
        // Validation handled by framework via attributes
        
        // Same implementation
        var results = await _indexService.SearchAsync(parameters);
        
        // Framework response building
        return await _responseBuilder.BuildResponseAsync(results, 
            new ResponseContext 
            { 
                ResponseMode = parameters.ResponseMode,
                TokenLimit = 10000 
            });
    }
}
```

### 2. Parameter Validation Mapping

#### Current CodeSearch
```csharp
public class TextSearchParams
{
    public string? Query { get; set; }
    public string? SearchType { get; set; }
    public int? MaxResults { get; set; }
    
    // Manual validation in tool
    public void Validate()
    {
        if (string.IsNullOrEmpty(Query))
            throw new InvalidParametersException("Query is required");
        if (MaxResults < 1 || MaxResults > 500)
            throw new InvalidParametersException("MaxResults must be 1-500");
    }
}
```

#### Framework Pattern
```csharp
public class TextSearchParams
{
    [Required]
    [Description("Text to search for")]
    public string Query { get; set; } = string.Empty;
    
    [Description("Search algorithm type")]
    public string SearchType { get; set; } = "standard";
    
    [Range(1, 500)]
    [Description("Maximum number of results")]
    public int MaxResults { get; set; } = 50;
}
```

### 3. Response Building Mapping

#### Current CodeSearch Services
| CodeSearch Component | Framework Replacement | Location |
|---------------------|----------------------|----------|
| Manual JSON building | `AIOptimizedResponse` class | `COA.Mcp.Framework.TokenOptimization.Models` |
| Custom token counting | `TokenEstimator` static methods | `COA.Mcp.Framework.TokenOptimization` |
| Progressive disclosure | `ProgressiveReductionEngine` | `COA.Mcp.Framework.TokenOptimization.Reduction` |
| Custom truncation | `StandardReductionStrategy` | `COA.Mcp.Framework.TokenOptimization.Reduction` |
| Insight generation | `InsightGenerator` + templates | `COA.Mcp.Framework.TokenOptimization.Intelligence` |
| Action suggestions | `NextActionProvider` + templates | `COA.Mcp.Framework.TokenOptimization.Actions` |

### 4. Service Registration Mapping

#### Current Program.cs
```csharp
// Manual registration
services.AddSingleton<TextSearchTool>();
services.AddSingleton<FileSearchTool>();
services.AddSingleton<DirectorySearchTool>();
// ... 30+ manual registrations

var server = new MpcServer();
foreach (var tool in services.GetServices<object>())
{
    if (tool.GetType().GetCustomAttribute<McpServerToolTypeAttribute>() != null)
    {
        server.RegisterTool(tool);
    }
}
```

#### Framework Program.cs
```csharp
// Automatic registration with framework
services.AddMcpFramework(options =>
{
    options.DiscoverTools = true;  // Auto-discovers all [McpServerToolType]
    options.TokenOptimization = new TokenOptimizationOptions
    {
        DefaultTokenLimit = 10000,
        Level = TokenOptimizationLevel.Balanced
    };
});

// Server creation simplified
var server = services.GetRequiredService<IMcpServer>();
```

### 5. Error Handling Mapping

#### Current Error Pattern
```csharp
try
{
    // Tool execution
}
catch (Exception ex)
{
    return new
    {
        success = false,
        error = ex.Message,
        code = "SEARCH_FAILED"
    };
}
```

#### Framework Error Pattern
```csharp
// Automatic error handling in McpToolBase
// Returns structured ErrorInfo with recovery steps
return new TextSearchResult
{
    Success = false,
    Error = new ErrorInfo
    {
        Code = "SEARCH_FAILED",
        Message = "Search operation failed",
        Details = ex.Message,
        Recovery = new RecoveryInfo
        {
            Steps = new[]
            {
                "Verify index exists",
                "Check query syntax",
                "Try simpler search terms"
            },
            Actions = new[]
            {
                new SuggestedAction("index_workspace", "Rebuild index"),
                new SuggestedAction("text_search", "Retry with simpler query")
            }
        }
    }
};
```

### 6. Custom CodeSearch Components

#### Components Requiring Custom Implementation

| Component | Framework Base | Custom Extension Needed |
|-----------|---------------|------------------------|
| `CodeAnalyzer` | N/A | Keep as-is, integrate with reduction |
| `LuceneIndexService` | N/A | Keep as-is, no change |
| `FlexibleMemoryService` | N/A | Keep as-is, no change |
| `PathResolutionService` | N/A | Keep as-is, no change |
| `QueryCacheService` | `ResponseCacheService` | Extend with query-specific logic |

#### Custom Response Builders to Create

```csharp
// 1. TextSearchResponseBuilder
public class TextSearchResponseBuilder : BaseResponseBuilder<List<SearchResult>>
{
    // Custom logic for code search results
}

// 2. MemorySearchResponseBuilder  
public class MemorySearchResponseBuilder : BaseResponseBuilder<List<Memory>>
{
    // Custom logic for memory results with temporal scoring
}

// 3. BatchOperationResponseBuilder
public class BatchOperationResponseBuilder : BaseResponseBuilder<BatchResults>
{
    // Custom logic for aggregated batch results
}

// 4. FileSystemResponseBuilder
public class FileSystemResponseBuilder : BaseResponseBuilder<List<FileInfo>>
{
    // Shared by file_search, directory_search, recent_files
}
```

### 7. Tool-by-Tool Migration Map

| Tool | Current Class | Framework Base | Response Builder | Priority |
|------|--------------|----------------|------------------|----------|
| `text_search` | TextSearchTool | McpToolBase<TextSearchParams, TextSearchResult> | TextSearchResponseBuilder | HIGH |
| `file_search` | FileSearchTool | McpToolBase<FileSearchParams, FileSearchResult> | FileSystemResponseBuilder | HIGH |
| `directory_search` | DirectorySearchTool | McpToolBase<DirectoryParams, DirectoryResult> | FileSystemResponseBuilder | HIGH |
| `recent_files` | RecentFilesTool | McpToolBase<RecentParams, RecentResult> | FileSystemResponseBuilder | MEDIUM |
| `search_memories` | SearchMemoriesTool | McpToolBase<MemoryParams, MemoryResult> | MemorySearchResponseBuilder | HIGH |
| `unified_memory` | UnifiedMemoryTool | McpToolBase<UnifiedParams, UnifiedResult> | Custom per operation | HIGH |
| `batch_operations` | BatchOperationsTool | McpToolBase<BatchParams, BatchResult> | BatchOperationResponseBuilder | MEDIUM |
| `semantic_search` | SemanticSearchTool | McpToolBase<SemanticParams, SemanticResult> | Custom with embeddings | LOW |
| `hybrid_search` | HybridSearchTool | McpToolBase<HybridParams, HybridResult> | Custom merge strategy | LOW |
| `store_memory` | StoreMemoryTool | McpToolBase<StoreParams, StoreResult> | Simple result builder | MEDIUM |
| `workflow_discovery` | WorkflowTool | McpToolBase<EmptyParameters, WorkflowResult> | Direct response | LOW |
| `system_health_check` | HealthCheckTool | McpToolBase<EmptyParameters, HealthResult> | Direct response | LOW |

### 8. Configuration Mapping

#### Current appsettings.json
```json
{
  "Logging": { },
  "LuceneIndex": {
    "IndexPath": ".codesearch/index"
  },
  "Memory": {
    "StoragePath": ".codesearch/memory"
  }
}
```

#### Framework appsettings.json
```json
{
  "Logging": { },
  "LuceneIndex": {
    "IndexPath": ".codesearch/index"
  },
  "Memory": {
    "StoragePath": ".codesearch/memory"
  },
  "MpcFramework": {
    "TokenOptimization": {
      "DefaultLimit": 10000,
      "Level": "Balanced",
      "EnableCache": true,
      "CacheExpiration": "00:15:00"
    },
    "ResponseBuilding": {
      "IncludeInsights": true,
      "IncludeActions": true,
      "MinInsights": 2,
      "MaxInsights": 5
    }
  }
}
```

### 9. Testing Infrastructure Mapping

#### Current Test Pattern
```csharp
[TestFixture]
public class TextSearchToolTests
{
    private TextSearchTool _tool;
    
    [SetUp]
    public void Setup()
    {
        _tool = new TextSearchTool(mockIndex, mockLogger);
    }
    
    [Test]
    public async Task TestSearch()
    {
        var result = await _tool.ExecuteAsync(new { query = "test" });
        Assert.NotNull(result);
    }
}
```

#### Framework Test Pattern
```csharp
[TestFixture]
public class TextSearchToolTests : ToolTestBase<TextSearchTool>
{
    [Test]
    public async Task Search_WithTokenLimit_ReturnsOptimizedResponse()
    {
        // Framework provides test helpers
        var tool = CreateTool();
        var context = CreateContext(tokenBudget: 1000);
        
        var result = await tool.ExecuteAsync(
            new TextSearchParams { Query = "test" },
            context);
        
        // Fluent assertions from framework
        result.Should().BeOfType<TextSearchResult>();
        result.Should().HaveSucceeded();
        result.Meta.TokenInfo.Estimated.Should().BeLessThan(1000);
    }
}
```

### 10. Migration Automation Commands

```bash
# Step 1: Analyze what can be automated
mcp-migrate analyze --project "COA.CodeSearch.McpServer.csproj" --output migration-report.json

# Step 2: Generate migration plan
mcp-migrate plan --report migration-report.json --output migration-plan.md

# Step 3: Apply automated changes
mcp-migrate apply --plan migration-plan.md --backup ./backup

# Step 4: Generate custom builders
mcp-migrate scaffold response-builder --tool text_search --output TextSearchResponseBuilder.cs

# Step 5: Verify migration
mcp-migrate verify --project "COA.CodeSearch.McpServer.csproj"
```

## 📝 Manual Migration Checklist

For each tool:

- [ ] Change inheritance to `McpToolBase<TParams, TResult>`
- [ ] Remove `[McpServerTool]` attribute (keep `[McpServerToolType]`)
- [ ] Change `ExecuteAsync` to `ExecuteInternalAsync`
- [ ] Add parameter validation attributes
- [ ] Create or assign response builder
- [ ] Update return type to strongly-typed result
- [ ] Remove manual validation code
- [ ] Update tests to use framework helpers
- [ ] Test with token limits
- [ ] Verify insights and actions generate

## 🎯 Key Takeaways

1. **Most code remains unchanged** - Business logic stays the same
2. **Framework handles boilerplate** - Validation, error handling, token management
3. **Response builders are reusable** - Share across similar tools
4. **Testing is simplified** - Framework provides comprehensive test helpers
5. **Migration tools help** - Automated analysis and conversion available

---

*This mapping guide provides exact component correspondence between CodeSearch MCP and COA MCP Framework v1.1.0*