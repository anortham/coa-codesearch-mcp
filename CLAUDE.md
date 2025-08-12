# CodeSearch MCP Server

## Critical Development Warnings

### ⚠️ NEVER Run MCP Server Locally
```bash
❌ dotnet run --project COA.CodeSearch.McpServer -- stdio
```
Creates orphaned processes that lock Lucene indexes. User must restart Claude Code after code changes.

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
- **Changes not working**: Restart Claude Code after rebuild
- **Build errors**: Verify COA.Mcp.Framework references