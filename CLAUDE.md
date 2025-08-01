# COA CodeSearch MCP Server - Claude AI Assistant Guide

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

- **MCP Tools (`mcp__codesearch__*`)**: Execute on INSTALLED server, not your edits
- **Testing Changes**: Must build → user reinstalls → new Claude session
- **Example**: Editing `JsonMemoryBackupService.cs` won't affect `backup_memories` until restart
- **⚠️ REMINDER**: If you edit tool code, IMMEDIATELY tell user to restart before testing

### 4. **Path Resolution**

- **ALWAYS** use `IPathResolutionService` for ALL path computation
- **NEVER** use `Path.Combine()` for building .codesearch paths manually
- PathResolutionService computes paths; services create directories when needed
- See [docs/PATH_RESOLUTION_CRITICAL.md](docs/PATH_RESOLUTION_CRITICAL.md)

### 5. **Editing Code**

- **NEVER** make assumptions about what properties or methods are available on a type, go look it up and see.
- **ALWAYS** make use of the codesearch tools, that's what they are for and the best way to test and improve on them is to dogfood them.

### 6. **Commit Changes**

- **ALWAYS** use git and commit code after code changes after you've checked that the project builds and the tests pass
- **NEVER** check in broken builds or failing tests! BUILD -> TEST -> COMMIT IN THAT ORDER

## 📋 Quick Reference

### Essential Commands

```bash
# Development workflow
dotnet build -c Debug          # Build during development
dotnet test                    # Run tests (always build first!)
dotnet list package --outdated # Check for updates

# Memory system startup
mcp__codesearch__recall_context "what I'm working on"  # ALWAYS start with this

# Search workflow
mcp__codesearch__index_workspace --workspacePath "C:/project"  # Required first
mcp__codesearch__text_search --query "TODO"                    # Then search (NEW: standardized 'query' parameter)

# NEW: Workflow discovery
mcp__codesearch__workflow_discovery --goal "search code"       # Learn tool dependencies
```

### Tool Categories

| Purpose             | Text Search Tools                     | Memory Tools                                         | Utility Tools                           |
| ------------------- | ------------------------------------- | ---------------------------------------------------- | --------------------------------------- |
| Find Files          | `text_search`, `file_search`          | -                                                    | -                                       |
| Analyze Files       | `recent_files`, `file_size_analysis`  | -                                                    | -                                       |
| Discover Code       | `similar_files`, `directory_search`   | -                                                    | -                                       |
| Index & Search      | `index_workspace`, `batch_operations` | -                                                    | -                                       |
| Store Knowledge     | -                                     | `store_memory`                                       | -                                       |
| Find Knowledge      | -                                     | `search_memories`, `recall_context`                  | `workflow_discovery`                    |
| **Smart Search**    | -                                     | `unified_memory`, `semantic_search`, `hybrid_search` | -                                       |
| **AI Intelligence** | -                                     | `memory_quality_assessment`, `load_context`          | -                                       |
| Explore Memory      | -                                     | `memory_graph_navigator`                             | -                                       |
| Manage Data         | -                                     | `backup_memories`, `restore_memories`                | `index_health_check`, `log_diagnostics` |

### Memory System Essentials

```bash
# Store discoveries
mcp__codesearch__store_memory --type "TechnicalDebt" --content "Issue description"

# Search memories
mcp__codesearch__search_memories --query "authentication" --types ["ArchitecturalDecision"]

# Backup/restore
mcp__codesearch__backup_memories    # Creates JSON backup
mcp__codesearch__restore_memories   # Restores from JSON
```

### 🆕 Phase 3: Advanced Memory Intelligence

```bash
# Natural language memory operations - use for intuitive commands
mcp__codesearch__unified_memory --command "remember that UserService has performance issues"
mcp__codesearch__unified_memory --command "find all technical debt related to authentication"
mcp__codesearch__unified_memory --command "create checklist for database migration"

# Semantic search - find by concepts and meaning
mcp__codesearch__semantic_search --query "security vulnerabilities in user login systems"

# Hybrid search - best of both text and semantic
mcp__codesearch__hybrid_search --query "authentication patterns"

# Memory quality assessment and improvement
mcp__codesearch__memory_quality_assessment --memoryId "memory_123"

# Auto-load relevant context for current work
mcp__codesearch__load_context --workingDirectory "C:/YourProject/Services"

# Advanced temporal scoring for recency-weighted results
mcp__codesearch__search_memories --query "recent decisions" --boostRecent true
```

[Full memory system guide →](docs/MEMORY_SYSTEM.md)

## 🏗️ Project Overview

High-performance MCP server in .NET 9.0 providing text search and intelligent memory management. Features:

- Lucene-powered millisecond text search
- Intelligent memory system for architectural knowledge
- **🆕 Phase 3 Complete**: Advanced Memory Intelligence with natural language commands, semantic search, and temporal scoring
- File discovery and analysis tools
- Project-wide content indexing
- **NEW**: AI-optimized tool consistency with standardized parameters
- **NEW**: Workflow discovery for proactive tool guidance
- **NEW**: Enhanced error handling with actionable recovery

## 🤖 AI UX Optimizations (Latest)

This project implements comprehensive AI agent experience optimizations:

### Parameter Standardization

- **All search tools** now use `query` as the primary parameter
- **Backward compatible** with legacy parameters (`searchQuery`, `nameQuery`, `directoryQuery`)
- **Consistent interface** across `text_search`, `file_search`, `directory_search`

### Response Format Consistency

- **Unified envelope** with `format` field indicating response type
- **Mixed format support** for both structured data and markdown display
- **Predictable parsing** for AI agents

### Workflow Discovery

- **New tool**: `workflow_discovery` provides proactive guidance
- **Tool chains**: Understand prerequisites and dependencies
- **Use case mapping**: Find the right tools for your goals

### Enhanced Error States

- **Actionable guidance** instead of generic error messages
- **Suggested next steps** when tools encounter empty states
- **Recovery workflows** with specific commands to try

See [docs/AI_UX_REVIEW.md](docs/AI_UX_REVIEW.md) for complete analysis.

## 🔧 Development Guidelines

### Adding a New Tool

1. **Create tool class** in `Tools/` folder:

```csharp
public class MyTool
{
    private readonly ILogger<MyTool> _logger;

    public async Task<MyResult> ExecuteAsync(string param1)
    {
        // Implementation
    }
}
```

2. **Register in DI** (`Program.cs`):

```csharp
services.AddSingleton<MyTool>();
```

3. **Add registration** in `AllToolRegistrations.cs`:

```csharp
private static void RegisterMyTool(ToolRegistry registry, MyTool tool)
{
    registry.RegisterTool<MyToolParams>(
        name: "my_tool",
        description: "What it does",
        inputSchema: new { /* schema */ },
        handler: async (p, ct) => await tool.ExecuteAsync(p.Param1)
    );
}
```

### Key Services

| Service                   | Purpose                                 |
| ------------------------- | --------------------------------------- |
| `LuceneIndexService`      | Fast text indexing and search           |
| `FlexibleMemoryService`   | Memory storage with custom fields       |
| `JsonMemoryBackupService` | JSON-based memory backup/restore        |
| `PathResolutionService`   | Path computation and resolution service |
| `FileIndexingService`     | File content extraction and indexing    |
| `QueryCacheService`       | Query result caching for performance    |

### Progressive Disclosure (V2 Tools)

Tools auto-switch to summary mode at 5,000 tokens:

- Provides insights, hotspots, and next actions
- Supports detail requests for drilling down
- Saves tokens while providing better analysis

Example:

```json
{
  "mode": "summary",
  "autoModeSwitch": true,
  "data": {
    /* smart summary */
  },
  "insights": ["Key finding"],
  "actions": [{ "cmd": "drill down command" }]
}
```

## 🐛 Troubleshooting

### Common Issues

**Stuck write.lock files**

- Symptom: All searches fail
- Fix: Exit Claude Code, manually delete `.codesearch/index/*/write.lock`

**Index corruption or locked files**

- Symptom: "Index is locked" or search operations failing
- Fix: Use `index_health_check` tool or exit Claude and delete stuck lock files

**Build errors in Release mode**

- Symptom: "Access denied" errors
- Fix: Use Debug mode during development

**Tests failing**

- Symptom: Various test failures
- Fix: Always build before testing

### Debug Logging

```bash
mcp__codesearch__log_diagnostics --action status
# View current log status and manage log files
```

## 📚 Additional Documentation

- [Memory System Guide](docs/MEMORY_SYSTEM.md) - Detailed memory tools documentation
- [Path Resolution Critical](docs/PATH_RESOLUTION_CRITICAL.md) - Path handling requirements
- [Tool Reference](docs/TOOLS.md) - Complete tool documentation
- [Architecture Decisions](docs/ARCHITECTURE.md) - Design decisions and patterns

## 🚀 Performance Targets

- Startup: < 500ms (simplified architecture)
- Text search: < 10ms indexed
- File search: < 50ms
- Memory operations: < 100ms
- Memory usage: < 200MB typical

## 🔗 Integration Notes

- This project uses Claude Code (NOT Claude Desktop)
- MCP servers configured differently than Desktop
- Server runs in STDIO mode
- User manages installation/restart
