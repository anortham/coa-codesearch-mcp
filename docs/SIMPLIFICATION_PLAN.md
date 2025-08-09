# COA CodeSearch MCP - Simplification Plan

## Executive Summary

This document outlines the plan to simplify COA CodeSearch MCP by removing all memory/knowledge management features (moving them to ProjectKnowledge) and migrating to the COA MCP Framework. The result will be a focused, high-performance search server with 35-45% less code.

## Goals

1. **Remove all memory management** - 55+ files, ~25,000 lines of code
2. **Migrate to COA MCP Framework 1.4.2** - Modern framework with better features
3. **Focus on search excellence** - Text, file, directory, and code analysis
4. **Maintain AI optimizations** - Keep AI-friendly response formatting
5. **Simplify maintenance** - Reduce complexity by 35-45%

## Current vs Future Architecture

### Current Architecture (Complex)
```
COA CodeSearch MCP (319 files)
├── Search Components (85 files, 27%)
├── Memory Components (55 files, 17%)  ← REMOVE
├── Shared Infrastructure (179 files, 56%)
└── Complex interdependencies
```

### Future Architecture (Simplified)
```
COA CodeSearch MCP (264 files)
├── Search Tools (7 core + 3 advanced)
├── Lucene Services (5 services)
├── Response Builders (7 builders)
├── Clean Infrastructure
└── COA MCP Framework 1.4.2
```

## Components to Remove (55+ files)

### Services (22 files)
```
FlexibleMemoryService.cs
ClaudeMemoryService.cs
MemoryEventPublisher.cs
MemoryLifecycleService.cs
MemoryFacetingService.cs
MemoryValidationService.cs
MemoryQualityValidationService.cs
MemoryResourceProvider.cs
MemoryStorageOrchestrator.cs
MemoryPressureService.cs
CheckpointService.cs
JsonMemoryBackupService.cs
UnifiedMemoryService.cs
HybridMemorySearch.cs
SemanticMemoryIndex.cs
SemanticIndexingSubscriber.cs
EmbeddingService.cs
InMemoryVectorIndex.cs
ContextAwarenessService.cs
AIContextService.cs
TimestampIdGenerator.cs
CheckpointIdGenerator.cs
```

### Tools (15 files)
```
FlexibleMemoryTools.cs
FlexibleMemorySearchToolV2.cs
ClaudeMemoryTools.cs
ChecklistTools.cs
CheckpointTools.cs
UpdateMemoryTool.cs
LoadContextTool.cs
UnifiedMemoryTool.cs
MemoryGraphNavigatorTool.cs
MemoryLinkingTools.cs
MemoryQualityAssessmentTool.cs
SemanticSearchTool.cs
HybridSearchTool.cs
TimelineTool.cs
PatternDetectorTool.cs
```

### Models & Interfaces (12 files)
```
FlexibleMemoryModels.cs
MemoryModels.cs
MemoryTemplate.cs
UnifiedMemoryModels.cs
MemoryLimitsConfiguration.cs
AIWorkingContext.cs
IMemoryService.cs
IMemoryEventPublisher.cs
IMemoryLifecycleService.cs
IMemoryQualityValidator.cs
IVectorIndex.cs
IEmbeddingService.cs
```

### Quality Validators (3 files)
```
CompletenessValidator.cs
ConsistencyValidator.cs
RelevanceValidator.cs
```

### Tests (15+ files)
```
All FlexibleMemory*Tests.cs
CheckpointServiceTests.cs
MemoryLifecycleServiceTests.cs
Memory integration tests
```

## Components to Keep (Core Search)

### Core Search Tools
1. **FastTextSearchToolV2** - Text content search with CodeAnalyzer
2. **FastFileSearchToolV2** - File name search with fuzzy matching
3. **IndexWorkspaceTool** - Workspace indexing
4. **FastDirectorySearchTool** - Directory structure search
5. **FastRecentFilesTool** - Recently modified files
6. **FastFileSizeAnalysisTool** - File size analysis
7. **FastSimilarFilesTool** - Find similar files

### Advanced Search Tools
1. **BatchOperationsToolV2** - Multiple parallel searches
2. **StreamingTextSearchTool** - Large result streaming
3. **SearchAssistantTool** - AI-powered search orchestration

### Lucene Services
1. **LuceneIndexService** - Core indexing and search
2. **FileIndexingService** - File content extraction
3. **BatchIndexingService** - Batch operations
4. **RegexSearchService** - Regex pattern matching
5. **CodeAnalyzer** - Programming language tokenization

## Migration to COA MCP Framework

### Step 1: Create New Project Structure
```
COA.CodeSearch.McpServer/
├── Program.cs                    # Updated for Framework
├── COA.CodeSearch.McpServer.csproj
├── Services/
│   ├── Search/
│   │   ├── LuceneIndexService.cs
│   │   ├── FileIndexingService.cs
│   │   ├── CodeAnalyzer.cs
│   │   └── RegexSearchService.cs
│   └── Infrastructure/
│       ├── PathResolutionService.cs
│       ├── QueryCacheService.cs
│       └── ErrorHandlingService.cs
├── Tools/
│   ├── TextSearchTool.cs
│   ├── FileSearchTool.cs
│   ├── DirectorySearchTool.cs
│   ├── IndexWorkspaceTool.cs
│   ├── RecentFilesTool.cs
│   ├── SimilarFilesTool.cs
│   └── BatchOperationsTool.cs
├── ResponseBuilders/
│   ├── TextSearchResponseBuilder.cs
│   ├── FileSearchResponseBuilder.cs
│   └── AIResponseBuilderService.cs
└── appsettings.json
```

### Step 2: Update Project File
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="COA.Mcp.Framework" Version="1.4.2" />
    <PackageReference Include="Lucene.Net" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.QueryParser" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.Highlighter" Version="4.8.0-beta00017" />
  </ItemGroup>
</Project>
```

### Step 3: Update Program.cs
```csharp
using COA.Mcp.Framework.Server;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;

var builder = McpServer.CreateBuilder()
    .WithServerInfo("CodeSearch", "2.0.0");

// Use STDIO transport (no HTTP needed for pure search)
builder.UseStdioTransport();

// Register services
builder.Services.AddSingleton<LuceneIndexService>();
builder.Services.AddSingleton<FileIndexingService>();
builder.Services.AddSingleton<CodeAnalyzer>();
builder.Services.AddSingleton<PathResolutionService>();
builder.Services.AddSingleton<QueryCacheService>();

// Register tools using Framework attributes
builder.RegisterToolType<TextSearchTool>();
builder.RegisterToolType<FileSearchTool>();
builder.RegisterToolType<DirectorySearchTool>();
builder.RegisterToolType<IndexWorkspaceTool>();
builder.RegisterToolType<RecentFilesTool>();
builder.RegisterToolType<SimilarFilesTool>();
builder.RegisterToolType<BatchOperationsTool>();

await builder.RunAsync();
```

### Step 4: Convert Tools to Framework Pattern
```csharp
using COA.Mcp.Framework.Attributes;

[McpServerToolType]
public class TextSearchTool
{
    private readonly LuceneIndexService _luceneService;
    
    public TextSearchTool(LuceneIndexService luceneService)
    {
        _luceneService = luceneService;
    }
    
    [McpServerTool(Name = "text_search")]
    [Description("Search file contents for text patterns")]
    public async Task<TextSearchResult> ExecuteAsync(TextSearchParams parameters)
    {
        // Simplified implementation
        var results = await _luceneService.SearchTextAsync(
            parameters.Query,
            parameters.WorkspacePath,
            parameters.MaxResults ?? 50
        );
        
        return new TextSearchResult
        {
            Results = results,
            Count = results.Count,
            Query = parameters.Query
        };
    }
}

public class TextSearchParams
{
    [Description("Search query supporting wildcards and regex")]
    public string Query { get; set; }
    
    [Description("Workspace path to search")]
    public string WorkspacePath { get; set; }
    
    [Description("Maximum results to return")]
    public int? MaxResults { get; set; }
}
```

## Integration with ProjectKnowledge

### When Users Find Issues During Search

```csharp
[McpServerTool(Name = "search_and_document")]
[Description("Search code and optionally document findings")]
public async Task<SearchAndDocumentResult> ExecuteAsync(SearchAndDocumentParams parameters)
{
    // Perform search
    var searchResults = await _luceneService.SearchTextAsync(parameters.Query);
    
    // If user wants to document findings
    if (parameters.DocumentFindings && searchResults.Any())
    {
        // Call ProjectKnowledge via HTTP
        var knowledgeClient = new HttpClient();
        await knowledgeClient.PostAsJsonAsync(
            "http://localhost:5100/api/knowledge/store",
            new
            {
                type = "TechnicalDebt",
                content = $"Found {searchResults.Count} instances of '{parameters.Query}'",
                metadata = new
                {
                    query = parameters.Query,
                    fileCount = searchResults.Select(r => r.FilePath).Distinct().Count(),
                    source = "codesearch"
                }
            }
        );
    }
    
    return new SearchAndDocumentResult
    {
        SearchResults = searchResults,
        Documented = parameters.DocumentFindings
    };
}
```

## Implementation Timeline

### Week 1: Preparation
- [ ] Complete ProjectKnowledge implementation
- [ ] Export all memories from current system
- [ ] Create backup of current CodeSearch

### Week 2: Simplification
- [ ] Remove all memory-related files
- [ ] Clean shared components
- [ ] Update configuration files
- [ ] Fix compilation errors

### Week 3: Framework Migration
- [ ] Convert to COA MCP Framework structure
- [ ] Update all tools to use Framework attributes
- [ ] Update DI registration
- [ ] Test all search functionality

### Week 4: Testing & Documentation
- [ ] Run all search tests
- [ ] Performance testing
- [ ] Update documentation
- [ ] Update CLAUDE.md

## Configuration Changes

### Remove from appsettings.json
```json
// REMOVE all these sections:
"MemorySystem": { ... },
"SemanticSearch": { ... },
"Checkpoints": { ... },
"MemoryLimits": { ... }
```

### Keep in appsettings.json
```json
{
  "CodeSearch": {
    "Index": {
      "Path": ".codesearch/index",
      "MaxFieldLength": 100000,
      "RamBufferSizeMB": 256
    },
    "Search": {
      "MaxResults": 100,
      "CacheEnabled": true,
      "CacheDurationMinutes": 10
    },
    "CodeAnalyzer": {
      "Enabled": true,
      "PreservePatterns": true
    }
  }
}
```

## Testing Strategy

### Core Search Tests
1. Text search with CodeAnalyzer
2. File name search with fuzzy matching
3. Directory search
4. Recent files
5. Similar files detection
6. Batch operations
7. Large file handling

### Performance Benchmarks
- Index 10,000 files < 30 seconds
- Search response < 10ms
- Memory usage < 150MB
- Startup time < 500ms

## Benefits of Simplification

### Quantitative Benefits
- **35-45% code reduction** - Easier to maintain
- **50% faster startup** - No memory system initialization
- **60% less memory usage** - No in-memory indexes
- **Simpler dependencies** - Only Lucene.NET required

### Qualitative Benefits
- **Single responsibility** - Only does search
- **Clearer architecture** - No complex interdependencies
- **Easier onboarding** - New developers understand faster
- **Better testability** - Isolated components
- **Framework benefits** - Modern MCP Framework features

## Risk Mitigation

### Risk 1: Breaking Existing Workflows
**Mitigation**: 
- Provide migration guide for users
- Keep tool names consistent
- Maintain backward compatibility where possible

### Risk 2: Missing Functionality
**Mitigation**:
- Users can run both ProjectKnowledge and CodeSearch
- Document which tool to use for what
- Provide clear error messages

### Risk 3: Performance Regression
**Mitigation**:
- Benchmark before and after
- Keep existing optimizations
- Profile and optimize as needed

## Success Criteria

1. ✅ All memory code removed (55+ files)
2. ✅ Successfully migrated to COA MCP Framework
3. ✅ All search tests passing
4. ✅ Performance equal or better
5. ✅ Clean architecture with single responsibility
6. ✅ Documentation updated
7. ✅ Integration with ProjectKnowledge documented

## Next Steps

1. **Review this plan** with stakeholders
2. **Complete ProjectKnowledge** implementation
3. **Begin CodeSearch simplification** in a branch
4. **Test thoroughly** before merging
5. **Update all documentation**
6. **Communicate changes** to team

## Conclusion

Simplifying CodeSearch by removing memory management and migrating to the COA MCP Framework will result in a cleaner, faster, more maintainable codebase. The 35-45% code reduction will make the project easier to understand and extend while maintaining all core search functionality.