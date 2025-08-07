# COA MCP Framework Migration Plan for CodeSearch

## Executive Summary

This document outlines a comprehensive plan to migrate the COA CodeSearch MCP Server from its current implementation to utilize the COA MCP Framework NuGet packages. This migration will bring significant improvements in AI response optimization, token management, and maintainability.

## Migration Goals

### Primary Objectives
1. **Adopt COA.Mcp.Framework** for standardized tool registration and base functionality
2. **Integrate COA.Mcp.Framework.TokenOptimization** for advanced AI response handling
3. **Improve token efficiency** by 50-70% through progressive reduction strategies
4. **Enhance AI agent experience** with structured insights and suggested actions
5. **Implement response caching** to improve performance
6. **Maintain backward compatibility** for existing tool interfaces

### Expected Benefits
- **Reduced token usage**: Progressive reduction and smart truncation
- **Better AI comprehension**: Structured responses with insights and actions
- **Improved performance**: Response caching and optimized serialization
- **Easier maintenance**: Standardized patterns and less custom code
- **Future-proof architecture**: Automatic framework updates

## Pre-Migration Assessment Checklist

### ✅ Environment Setup
- [ ] Verify access to internal COA NuGet feed
- [ ] Confirm .NET 9.0 compatibility of framework packages
- [ ] Create feature branch: `feature/coa-framework-migration`
- [ ] Backup current codebase and database
- [ ] Document current tool response formats for comparison

### ✅ Dependency Analysis
- [ ] List all current custom implementations that will be replaced:
  - [ ] Tool registration system
  - [ ] Response building patterns
  - [ ] Token estimation logic
  - [ ] Error handling patterns
- [ ] Identify potential conflicts with existing packages
- [ ] Review framework dependencies for compatibility

### ✅ Risk Assessment
- [ ] Identify critical tools that cannot have downtime
- [ ] List tools with custom response formats requiring adaptation
- [ ] Document integration points with Claude Code
- [ ] Plan for memory system compatibility

## Migration Architecture

### Current Architecture
```
COA.CodeSearch.McpServer
├── Tools/                    # Individual tool implementations
├── Services/                 # Business logic services
├── Models/                   # Data models
└── Program.cs               # Manual tool registration
```

### Target Architecture
```
COA.CodeSearch.McpServer
├── Tools/                    # Migrated to inherit from McpToolBase
├── Services/                 # Enhanced with framework services
├── Models/                   # Extended with AI response models
├── ResponseBuilders/         # New: AI-optimized response builders
├── TokenOptimization/        # New: Custom reduction strategies
└── Program.cs               # Framework-based registration
```

## Package Integration Plan

### Phase 1: Core Framework Integration

#### 1.1 Add NuGet Package References
```xml
<ItemGroup>
  <PackageReference Include="COA.Mcp.Framework" Version="1.0.0" />
  <PackageReference Include="COA.Mcp.Framework.TokenOptimization" Version="1.0.0" />
</ItemGroup>
```

#### 1.2 Update Program.cs
```csharp
// Before
services.AddSingleton<TextSearchTool>();
services.AddSingleton<FileSearchTool>();
// ... manual registration

// After
services.AddMcpFramework(options =>
{
    options.DiscoverTools = true;
    options.TokenOptimization = new TokenOptimizationOptions
    {
        DefaultTokenLimit = 10000,
        Level = TokenOptimizationLevel.Balanced,
        EnableAdaptiveLearning = true,
        EnableResourceStorage = true
    };
});
```

#### 1.3 Configure NuGet Feed
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="COA Internal" value="https://pkgs.dev.azure.com/coa/_packaging/coa/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

### Phase 2: Tool Migration Strategy

#### 2.1 Base Class Migration Pattern

**Before:**
```csharp
[McpServerToolType]
public class TextSearchTool
{
    [McpServerTool(Name = "text_search")]
    [Description("Search for text patterns in indexed files")]
    public async Task<object> ExecuteAsync(TextSearchParams parameters)
    {
        // Implementation
        return new { results = searchResults };
    }
}
```

**After:**
```csharp
[McpServerToolType]
public class TextSearchTool : McpToolBase<TextSearchParams, TextSearchResult>
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IResponseBuilder<TextSearchResult> _responseBuilder;
    
    public TextSearchTool(
        ILogger<TextSearchTool> logger,
        ITokenEstimator tokenEstimator,
        IResponseBuilder<TextSearchResult> responseBuilder) 
        : base(logger)
    {
        _tokenEstimator = tokenEstimator;
        _responseBuilder = responseBuilder;
    }
    
    protected override string ToolName => "text_search";
    protected override string ToolDescription => "Search for text patterns in indexed files";
    
    protected override async Task<TextSearchResult> ExecuteCoreAsync(
        TextSearchParams parameters, 
        ToolExecutionContext context)
    {
        // Core implementation
        var searchResults = await SearchAsync(parameters);
        
        // Build AI-optimized response
        return await _responseBuilder.BuildResponseAsync(
            searchResults, 
            context,
            new ResponseOptions
            {
                IncludeInsights = true,
                IncludeActions = true,
                TokenBudget = context.TokenBudget
            });
    }
    
    protected override Task<ToolValidationResult> ValidateParametersAsync(
        TextSearchParams parameters)
    {
        // Custom validation logic
        return base.ValidateParametersAsync(parameters);
    }
}
```

#### 2.2 Response Builder Implementation

```csharp
public class TextSearchResponseBuilder : BaseResponseBuilder<List<SearchResult>, TextSearchResult>
{
    private readonly IInsightGenerator _insightGenerator;
    private readonly INextActionProvider _actionProvider;
    
    protected override async Task<TextSearchResult> BuildCoreAsync(
        List<SearchResult> data,
        ResponseContext context)
    {
        // Apply token-aware reduction
        var tokenAware = await ApplyTokenManagementAsync(data, context);
        
        // Generate insights
        var insights = await _insightGenerator.GenerateInsightsAsync(
            tokenAware,
            new InsightContext
            {
                OperationName = "text_search",
                MinInsights = 2,
                MaxInsights = 5
            });
        
        // Generate actions
        var actions = await _actionProvider.GetNextActionsAsync(
            tokenAware,
            new ActionContext
            {
                OperationName = "text_search",
                RelatedInsights = insights
            });
        
        return new TextSearchResult
        {
            Format = "ai-optimized",
            Data = new TextSearchData
            {
                Results = tokenAware.Items,
                Count = tokenAware.Items.Count,
                TotalMatches = tokenAware.OriginalCount
            },
            Insights = insights.Select(i => i.Text).ToList(),
            Actions = actions,
            Meta = BuildMetadata(tokenAware, context)
        };
    }
}
```

## Tool-by-Tool Migration Guide

### Critical Path Tools (Migrate First)

#### 1. text_search
- **Priority**: HIGH - Most frequently used
- **Complexity**: MEDIUM - Has complex result sets
- **Token Impact**: HIGH - Can return large results
- **Migration Steps**:
  1. Create `TextSearchResponseBuilder`
  2. Implement custom reduction strategy for search results
  3. Add insights for search patterns and hotspots
  4. Add actions for refining search or exploring results
  5. Test with large result sets

#### 2. unified_memory
- **Priority**: HIGH - Complex natural language processing
- **Complexity**: HIGH - Multiple operation types
- **Token Impact**: HIGH - Processes varied data
- **Migration Steps**:
  1. Create operation-specific response builders
  2. Implement context-aware insight generation
  3. Add workflow-based action suggestions
  4. Ensure backward compatibility with existing commands
  5. Test all operation types thoroughly

#### 3. search_memories
- **Priority**: HIGH - Core memory functionality
- **Complexity**: MEDIUM - Structured data
- **Token Impact**: MEDIUM - Usually moderate result sizes
- **Migration Steps**:
  1. Create `MemorySearchResponseBuilder`
  2. Add temporal scoring integration
  3. Implement memory-specific insights
  4. Add navigation actions for memory graph
  5. Test with various memory types

### Standard Tools (Migrate Second)

#### 4. file_search, directory_search
- **Priority**: MEDIUM
- **Complexity**: LOW - Simple result structures
- **Token Impact**: MEDIUM
- **Migration Steps**:
  1. Use shared `FileSystemResponseBuilder`
  2. Add file type distribution insights
  3. Add actions for opening/analyzing files
  4. Test with large directory structures

#### 5. batch_operations
- **Priority**: MEDIUM
- **Complexity**: HIGH - Multiple operations
- **Token Impact**: HIGH - Aggregated results
- **Migration Steps**:
  1. Create `BatchOperationResponseBuilder`
  2. Implement operation summary generation
  3. Add failure recovery actions
  4. Test with various batch sizes

### Utility Tools (Migrate Last)

#### 6. workflow_discovery, system_health_check
- **Priority**: LOW
- **Complexity**: LOW
- **Token Impact**: LOW
- **Migration Steps**:
  1. Simple migration to base class
  2. Minimal response building needed
  3. Focus on structured data output

## Custom Implementation Requirements

### 1. CodeAnalyzer Integration
```csharp
public class CodeSearchReductionStrategy : IReductionStrategy
{
    public string Name => "code-search";
    
    public ReductionResult<T> Reduce<T>(
        IList<T> items,
        Func<T, int> itemEstimator,
        int tokenLimit,
        ReductionContext context)
    {
        // Preserve code pattern matches
        // Prioritize exact matches over partial
        // Group by file for context preservation
    }
}
```

### 2. Memory-Aware Caching
```csharp
public class MemoryAwareCacheKeyGenerator : ICacheKeyGenerator
{
    public string GenerateKey(string tool, object parameters, string userId)
    {
        // Include workspace context
        // Include memory version/checkpoint
        // Include user-specific context
    }
}
```

### 3. Insight Templates for CodeSearch
```csharp
public class CodeSearchInsightTemplates : IInsightTemplateProvider
{
    public async Task<List<IInsightTemplate>> GetTemplatesAsync(
        Type dataType,
        InsightContext context)
    {
        return new List<IInsightTemplate>
        {
            new PatternDistributionInsight(),
            new HotspotIdentificationInsight(),
            new CodeSmellDetectionInsight(),
            new RefactoringOpportunityInsight()
        };
    }
}
```

## Testing Strategy

### Unit Test Migration
```csharp
[TestFixture]
public class TextSearchToolTests : ToolTestBase<TextSearchTool>
{
    [Test]
    public async Task Search_WithTokenLimit_ReturnsOptimizedResponse()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new TextSearchParams { Query = "test" };
        var context = CreateContext(tokenBudget: 1000);
        
        // Act
        var result = await tool.ExecuteAsync(parameters, context);
        
        // Assert
        result.Should().BeOfType<TextSearchResult>();
        result.Meta.TokenInfo.Estimated.Should().BeLessOrEqualTo(1000);
        result.Insights.Should().NotBeEmpty();
        result.Actions.Should().NotBeEmpty();
    }
}
```

### Integration Test Scenarios
1. **Token Limit Compliance**: Verify all tools respect token budgets
2. **Insight Quality**: Ensure insights are relevant and actionable
3. **Action Accuracy**: Validate suggested actions make sense
4. **Cache Effectiveness**: Measure cache hit rates
5. **Performance Impact**: Compare before/after response times

### Load Testing
```csharp
[Test]
public async Task LoadTest_ConcurrentSearches_MaintainsPerformance()
{
    // Test 100 concurrent searches
    // Measure: response time, token usage, cache performance
    // Assert: <100ms p99, <10KB average response
}
```

## Rollback Plan

### Preparation
1. **Feature Toggle**: Implement framework usage behind feature flag
2. **Parallel Implementation**: Keep old implementation during migration
3. **Database Backup**: Before any memory system changes
4. **Version Tag**: Tag pre-migration version

### Rollback Steps
1. **Immediate**: Toggle feature flag to disable framework
2. **Quick**: Revert to tagged version
3. **Full**: Restore from backup and redeploy

### Rollback Triggers
- Performance degradation >20%
- Critical tool failures
- Memory system incompatibility
- Token usage increase instead of decrease

## Migration Timeline

### Week 1: Preparation
- [ ] Day 1-2: Environment setup and package configuration
- [ ] Day 3-4: Create base response builders and strategies
- [ ] Day 5: Migration tooling and test framework setup

### Week 2: Critical Tools
- [ ] Day 1-2: Migrate text_search with full testing
- [ ] Day 3-4: Migrate unified_memory with all operations
- [ ] Day 5: Migrate search_memories and memory tools

### Week 3: Remaining Tools
- [ ] Day 1-2: Migrate file system tools
- [ ] Day 3: Migrate batch_operations
- [ ] Day 4: Migrate utility tools
- [ ] Day 5: Integration testing

### Week 4: Optimization
- [ ] Day 1-2: Performance tuning
- [ ] Day 3: Load testing
- [ ] Day 4: Documentation updates
- [ ] Day 5: Deployment preparation

## Post-Migration Validation

### Functional Validation
- [ ] All tools respond within token limits
- [ ] Insights are generated for all major operations
- [ ] Actions are contextually appropriate
- [ ] Cache is functioning correctly
- [ ] No regression in core functionality

### Performance Validation
- [ ] Response times improved or maintained
- [ ] Token usage reduced by target percentage
- [ ] Memory usage within acceptable limits
- [ ] CPU usage not significantly increased

### AI Experience Validation
- [ ] Claude can parse all responses
- [ ] Insights provide value
- [ ] Actions are executable
- [ ] Truncation is clearly indicated
- [ ] Resource URIs work when provided

## Success Metrics

### Quantitative
- **Token Reduction**: 50-70% for large responses
- **Cache Hit Rate**: >60% for repeated queries
- **Response Time**: <100ms for cached, <500ms for computed
- **Error Rate**: <0.1% increase from baseline

### Qualitative
- **Developer Feedback**: Easier to maintain and extend
- **AI Agent Feedback**: Better comprehension and actions
- **Code Quality**: Reduced custom code by 40%
- **Documentation**: All tools have response examples

## Configuration Management

### appsettings.json Updates
```json
{
  "MpcFramework": {
    "TokenOptimization": {
      "DefaultLimit": 10000,
      "Level": "Balanced",
      "EnableCache": true,
      "CacheExpiration": "00:15:00",
      "AdaptiveLearning": {
        "Enabled": true,
        "MinSamples": 10,
        "AdjustmentThreshold": 0.2
      }
    },
    "ResponseBuilding": {
      "IncludeInsights": true,
      "IncludeActions": true,
      "MinInsights": 2,
      "MaxInsights": 5,
      "MaxActions": 5
    }
  }
}
```

### Environment-Specific Settings
- **Development**: Aggressive token limits for testing
- **Staging**: Balanced settings with full logging
- **Production**: Optimized settings with monitoring

## Monitoring and Observability

### Key Metrics to Track
1. **Token Usage**: Per tool, per operation
2. **Cache Performance**: Hit rate, eviction rate
3. **Response Times**: P50, P95, P99
4. **Error Rates**: By tool and error type
5. **Insight/Action Generation**: Success rate, relevance

### Logging Enhancements
```csharp
logger.LogInformation("Tool {Tool} completed in {Duration}ms with {Tokens} tokens",
    toolName, duration, estimatedTokens);

logger.LogDebug("Reduction applied: {Original} -> {Reduced} items, Strategy: {Strategy}",
    originalCount, reducedCount, strategyName);
```

## Risk Mitigation

### Technical Risks
1. **Package Version Conflicts**: Use exact versions, test thoroughly
2. **Breaking Changes**: Implement adapter patterns where needed
3. **Performance Degradation**: Extensive load testing before deployment
4. **Memory Leaks**: Monitor memory usage during testing

### Operational Risks
1. **User Disruption**: Staged rollout with monitoring
2. **Training Needs**: Document all changes for users
3. **Integration Issues**: Test with Claude Code extensively

## Documentation Updates

### Required Documentation
1. **Migration Guide**: For other MCP servers
2. **Response Format Guide**: New AI-optimized formats
3. **Tool Documentation**: Updated with new features
4. **Configuration Guide**: All new settings explained
5. **Troubleshooting Guide**: Common issues and solutions

## Next Steps

1. **Approval**: Review and approve migration plan
2. **Team Assignment**: Assign developers to tool groups
3. **Kick-off**: Schedule migration start
4. **Daily Standups**: Track progress and issues
5. **Go/No-Go Decision**: Before production deployment

---

This migration plan provides a structured approach to adopting the COA MCP Framework while minimizing risk and maximizing benefits. The token optimization and AI response improvements will significantly enhance the CodeSearch MCP server's effectiveness for AI agents.