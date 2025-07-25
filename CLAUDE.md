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

### 3. **Code vs Runtime Version**
- **MCP Tools (`mcp__codesearch__*`)**: Execute on INSTALLED server, not your edits
- **Testing Changes**: Must build → user reinstalls → new Claude session
- **Example**: Editing `JsonMemoryBackupService.cs` won't affect `backup_memories` until restart

### 4. **TypeScript Requirements**
- Requires npm installed for TypeScript support
- Without npm: TypeScript tools will fail (installer can't extract .tgz files)
- Install Node.js from https://nodejs.org/ to fix

### 5. **Path Resolution**
- **ALWAYS** use `IPathResolutionService` for ALL file/directory operations
- **NEVER** use `Path.Combine()` or `Directory.CreateDirectory()` directly
- See [docs/PATH_RESOLUTION_CRITICAL.md](docs/PATH_RESOLUTION_CRITICAL.md)

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
mcp__codesearch__text_search --query "TODO"                    # Then search
```

### Tool Categories

| Purpose | C# Tools | TypeScript Tools | All Languages |
|---------|----------|------------------|---------------|
| Find Code | `search_symbols` | `search_typescript` | `text_search`, `file_search` |
| Navigate | `go_to_definition`, `find_references` | `typescript_go_to_definition`, `typescript_find_references` | - |
| Analyze | `get_implementations`, `get_call_hierarchy`, `dependency_analysis` | - | `batch_operations` |
| Modify | `rename_symbol` | `typescript_rename_symbol` | - |
| Memory | - | - | `store_memory`, `recall_context`, `backup_memories` |

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

[Full memory system guide →](docs/MEMORY_SYSTEM.md)

## 🏗️ Project Overview

High-performance MCP server in .NET 9.0 providing LSP-like code navigation. Features:
- Roslyn-based C# analysis
- TypeScript support via tsserver
- Lucene-powered millisecond search
- Intelligent memory system for architectural knowledge

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

| Service | Purpose |
|---------|---------|
| `CodeAnalysisService` | Manages Roslyn workspaces and C# analysis |
| `TypeScriptAnalysisService` | TypeScript analysis via tsserver |
| `LuceneIndexService` | Fast text indexing and search |
| `JsonMemoryBackupService` | JSON-based memory backup/restore |
| `FlexibleMemoryService` | Memory storage with custom fields |
| `PathResolutionService` | SINGLE source of truth for all paths |

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
  "data": { /* smart summary */ },
  "insights": ["Key finding"],
  "actions": [{"cmd": "drill down command"}]
}
```

## 🐛 Troubleshooting

### Common Issues

**Stuck write.lock files**
- Symptom: All searches fail
- Fix: Exit Claude Code, manually delete `.codesearch/index/*/write.lock`

**TypeScript tools failing**
- Symptom: "TypeScript installation failed"
- Fix: Install npm (Node.js)

**Build errors in Release mode**
- Symptom: "Access denied" errors
- Fix: Use Debug mode during development

**Tests failing**
- Symptom: Various test failures
- Fix: Always build before testing

### Debug Logging
```bash
mcp__codesearch__set_logging --action start --level debug
# Logs written to: %LOCALAPPDATA%\COA.CodeSearch\.codesearch\logs\
```

## 📚 Additional Documentation

- [Memory System Guide](docs/MEMORY_SYSTEM.md) - Detailed memory tools documentation
- [TypeScript Support](docs/TYPESCRIPT.md) - TypeScript configuration and tools
- [Path Resolution Critical](docs/PATH_RESOLUTION_CRITICAL.md) - Path handling requirements
- [Tool Reference](docs/TOOLS.md) - Complete tool documentation
- [Architecture Decisions](docs/ARCHITECTURE.md) - Design decisions and patterns

## 🚀 Performance Targets

- Startup: < 100ms (with AOT)
- GoToDefinition: < 50ms cached
- FindReferences: < 200ms average
- Text search: < 10ms indexed
- Memory usage: < 500MB typical

## 🔗 Integration Notes

- This project uses Claude Code (NOT Claude Desktop)
- MCP servers configured differently than Desktop
- Server runs in STDIO mode
- User manages installation/restart