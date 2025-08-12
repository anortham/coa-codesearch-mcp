# COA CodeSearch MCP Server - Claude AI Assistant Guide

## 🚨 CRITICAL: This is CodeSearch (v2.0)

This is the **next-generation** CodeSearch built on COA MCP Framework 1.5.4. It's a complete rewrite with centralized architecture and clean separation from memory management (now handled by ProjectKnowledge).

## 🚨 CRITICAL WARNINGS - READ FIRST

### 1. **NEVER Run MCP Server Locally**

```bash
# DO NOT RUN THESE:
❌ dotnet run --project COA.CodeSearch.McpServer -- stdio
❌ dotnet run -- stdio --test-mode
```

**Why**: Creates orphaned processes that lock Lucene indexes, breaking all search operations.

### 2. **Build Configuration**

- **During Development**: Use `dotnet build -c Debug` (Release DLL is locked by running session)
- **Before Tests**: ALWAYS build first: `dotnet build -c Debug && dotnet test`
- **For User**: They build Release mode after exiting Claude Code

### 3. **🚨 STOP! CODE vs RUNTIME VERSION 🚨**

```
⚠️  BEFORE TESTING ANY MCP TOOL CHANGES:
    1. User must restart Claude Code
    2. Changes don't work until restart
    3. NO EXCEPTIONS TO THIS RULE
```

- **MCP Tools (`mcp__codesearch-next__*`)**: Execute on INSTALLED server, not your edits
- **Testing Changes**: Must build → user reinstalls → new Claude session
- **⚠️ REMINDER**: If you edit tool code, IMMEDIATELY tell user to restart before testing

### 4. **Path Resolution**

- **ALWAYS** use `IPathResolutionService` for ALL path computation
- **NEVER** use `Path.Combine()` for building .coa paths manually
- PathResolutionService computes paths; services create directories when needed

### 5. **Editing Code**

- **NEVER** make assumptions about what properties or methods are available on a type, go look it up and see
- **ALWAYS** verify interface methods before using them
- **ALWAYS** check the COA MCP Framework interfaces and base classes

### 6. **Commit Changes**

- **ALWAYS** use git and commit code after code changes after you've checked that the project builds and the tests pass
- **NEVER** check in broken builds or failing tests! BUILD -> TEST -> COMMIT IN THAT ORDER

## 🏗️ Project Status

### ✅ Working Components
- **Core Services**: PathResolution, LuceneIndex, QueryCache, CircuitBreaker, MemoryPressure
- **Working Tools**: HelloWorldTool, SystemInfoTool, IndexWorkspaceTool
- **Build Status**: Project builds successfully

### 🚧 In Progress
- **Search Tools**: Need implementation using correct ILuceneIndexService interface
- **File Services**: FileIndexingService, BatchIndexingService need refactoring
- **Background Services**: FileWatcherService depends on FileIndexingService

## 📋 Development Guidelines

### 1. **Framework Usage**

This project uses COA MCP Framework 1.5.4. All tools must:
- Inherit from `McpToolBase<TParams, TResult>`
- Use `ToolResultBase` for results
- Use `ToolCategory` enum from `COA.Mcp.Framework`
- Follow the framework patterns (see HelloWorldTool for reference)

### 2. **Service Interfaces**

**ALWAYS verify interface methods before using them:**

```csharp
// ILuceneIndexService provides these methods:
- InitializeIndexAsync(workspacePath)
- IndexDocumentAsync(workspacePath, document)
- IndexDocumentsAsync(workspacePath, documents)
- DeleteDocumentAsync(workspacePath, filePath)
- SearchAsync(workspacePath, query, maxResults)
- GetDocumentCountAsync(workspacePath)
- ClearIndexAsync(workspacePath)
- CommitAsync(workspacePath)
- IndexExistsAsync(workspacePath)
- GetHealthAsync(workspacePath)
- GetStatisticsAsync(workspacePath)

// It does NOT provide:
- GetIndexWriterAsync() ❌
- Direct IndexWriter access ❌
```

### 3. **Tool Implementation Pattern**

```csharp
public class MyTool : McpToolBase<MyParameters, MyResult>
{
    private readonly ILogger<MyTool> _logger;
    
    public MyTool(ILogger<MyTool> logger) : base(logger)
    {
        _logger = logger;
    }
    
    public override string Name => "my_tool";
    public override string Description => "Description here";
    public override ToolCategory Category => ToolCategory.Query;
    
    protected override async Task<MyResult> ExecuteInternalAsync(
        MyParameters parameters,
        CancellationToken cancellationToken)
    {
        // Use ValidateRequired, ValidateRange, etc. from base class
        var param = ValidateRequired(parameters.SomeParam, nameof(parameters.SomeParam));
        
        // Return result with Success/Error properly set
        return new MyResult
        {
            Success = true,
            // Set other properties
        };
    }
}
```

## 🔍 Architecture Overview

### Centralized Storage
- All indexes in `~/.coa/codesearch/indexes/`
- Workspace isolation via hash-based directories
- Single service instance serves all workspaces

### Service Architecture
```
Core Services:
├── IPathResolutionService     # Path management
├── ILuceneIndexService         # Search operations
├── IQueryCacheService          # Result caching
├── ICircuitBreakerService     # Fault tolerance
├── IMemoryPressureService     # Resource monitoring
├── IIndexingMetricsService    # Performance metrics
├── IFieldSelectorService      # Field optimization
└── IErrorRecoveryService      # Error handling

Tools (via COA MCP Framework):
├── IndexWorkspaceTool         # Index management
├── TextSearchTool             # Full-text search (TODO)
├── FileSearchTool             # File name search (TODO)
├── DirectorySearchTool        # Directory search (TODO)
├── RecentFilesTool            # Recent files (TODO)
└── SimilarFilesTool           # Similar files (TODO)
```

## 🚀 Quick Reference

### Essential Commands

```bash
# Development workflow
dotnet build -c Debug          # Build during development
dotnet test                    # Run tests (always build first!)

# Tool naming convention (when they're implemented)
mcp__codesearch-next__index_workspace    # Index a workspace
mcp__codesearch-next__text_search        # Search text
mcp__codesearch-next__file_search        # Search files
```

## 📚 Documentation

- [Vision & Architecture](COA.CodeSearch.McpServer/docs/CODESEARCH_NEXT_VISION.md)
- [Implementation Checklist](COA.CodeSearch.McpServer/docs/IMPLEMENTATION_CHECKLIST.md)
- [Framework Migration Guide](docs/FRAMEWORK_MIGRATION_GUIDE.md)
- [Integration with ProjectKnowledge](docs/INTEGRATION_WITH_PROJECTKNOWLEDGE.md)

## ⚠️ Common Pitfalls

1. **Don't assume interface methods** - Always check what's actually available
2. **Don't access IndexWriter directly** - Use ILuceneIndexService methods
3. **Don't run locally** - MCP servers are managed by Claude Code
4. **Don't mix concerns** - Search is here, memory is in ProjectKnowledge
5. **Don't use Release mode during dev** - DLL gets locked by running session

## 🔗 Related Projects

- **COA MCP Framework**: The foundation this is built on (`C:\source\COA MCP Framework`)
- **ProjectKnowledge MCP**: Handles memory and knowledge management
- **Legacy CodeSearch**: Being replaced by this project

## 💡 Implementation Tips

- Check HelloWorldTool and SystemInfoTool for working examples
- Use ILuceneIndexService.IndexDocumentsAsync() for batch indexing
- All tools auto-register via framework's DiscoverTools()
- Progressive disclosure happens automatically at 5k tokens
- Centralized indexes mean no more lock conflicts between sessions

## 🐛 Troubleshooting

### Build Errors
- **"Type or namespace not found"**: Check COA.Mcp.Framework references
- **"Method does not exist"**: Verify against actual interface, not assumptions
- **"Access denied"**: Use Debug mode, Release DLL may be locked

### Common Issues
- **Stuck write.lock files**: Exit Claude Code, delete `.coa/codesearch/indexes/*/write.lock`
- **Changes not working**: User must restart Claude Code after rebuild
- **Missing tools**: Check if tool is registered in Program.cs

## 🎯 Current Focus

The immediate priority is to:
1. Fix FileIndexingService to use ILuceneIndexService properly
2. Implement remaining search tools with correct interfaces
3. Get FileWatcherService working again
4. Test with Claude Code

Remember: This is a clean slate rebuild on COA MCP Framework. Don't carry over assumptions from the old CodeSearch - verify everything!