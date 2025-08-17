# CodeSearch MCP Server

## 🎨 VS Code Bridge Integration

**NEW**: Tools use `IVSCodeBridge` for rich VS Code displays:
- Return structured data with search results
- VS Code Bridge handles all rendering and visualization
- Line numbers and code snippets are automatically displayed
- See `docs/VISUALIZATION_INTEGRATION.md` for implementation details

## Critical Development Warnings

### ⚠️ NEVER Run MCP Server Locally
```bash
❌ dotnet run --project COA.CodeSearch.McpServer -- stdio
```
Creates orphaned processes that lock Lucene indexes. User must restart Claude Code after code changes.

### 🔄 CODE CHANGES REQUIRE RESTART
**IMPORTANT**: After making ANY code changes:
1. Exit Claude Code completely 
2. Build the project: `dotnet build -c Release`
3. Restart Claude Code to load new version
4. **Testing before restart shows OLD CODE - not your changes!**

### ⚠️ Path Resolution
```csharp
❌ Path.Combine("~/.coa", "indexes")  
✅ _pathResolver.GetIndexPath(workspacePath)
```

### ⚠️ Service Interfaces
Verify methods exist - don't assume:
```csharp
✅ ILuceneIndexService.SearchAsync()
❌ ILuceneIndexService.GetIndexWriterAsync() // doesn't exist
```

## Testing & Building

```bash
# Build and test
dotnet build -c Debug && dotnet test

# Lint (if configured)
dotnet format --verify-no-changes
```

## Architecture

- Built on COA MCP Framework 1.7.0
- Uses `McpToolBase<TParams, TResult>` for all tools
- Lucene.NET 4.8.0-beta00017 for indexing
- Memory management handled by ProjectKnowledge MCP

## Troubleshooting

- **Stuck locks**: Exit Claude Code, delete `~/.coa/codesearch/indexes/*/write.lock`
- **Changes not working**: **EXIT AND RESTART CLAUDE CODE** - code changes don't take effect until restart
- **Build errors**: Verify COA.Mcp.Framework references
- **Testing shows old behavior**: You're testing cached old code - restart Claude Code first!