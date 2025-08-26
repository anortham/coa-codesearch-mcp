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
5. **HTTP API testing also requires restart** - port 5020 serves cached old code until restart

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

## HTTP API Auto-Service

**NEW**: CodeSearch automatically starts an HTTP API on port 5020 when running in STDIO mode (default for Claude Code).

### Auto-Service Configuration
```csharp
builder.UseAutoService(config =>
{
    config.ServiceId = "codesearch-http";
    config.ExecutablePath = "dotnet";
    config.Arguments = new[] { dllPath, "--mode", "http" };
    config.Port = 5020;
    config.HealthEndpoint = "http://localhost:5020/health";
    config.AutoRestart = true;
    config.MaxRestartAttempts = 3;
    config.HealthCheckIntervalSeconds = 60;
});
```

### Available Endpoints
- Health: `GET /health`, `GET /api/health`
- Workspaces: `GET /api/workspace`, `POST /api/workspace/index`
- Search: `GET /api/search/symbol`, `GET /api/search/text`
- Documentation: `GET /swagger` (dev mode)

### Workspace Path Resolution Fix

**FIXED**: HTTP API now returns actual workspace paths instead of hashed directory names.

**Before:**
```json
{"path": "coa_codesearch_mcp_4785ab0febeec6c7"}  // ❌ Hashed name
```

**After:**
```json
{"path": "C:\\source\\COA CodeSearch MCP"}  // ✅ Real path
```

**Implementation:**
- `PathResolutionService.TryResolveWorkspacePath()` - Resolves hashed names to original paths
- `PathResolutionService.StoreWorkspaceMetadata()` - Stores metadata during indexing  
- `WorkspaceController.ListWorkspaces()` - Uses path resolution for API responses
- Workspace metadata stored in `workspace_metadata.json` per index directory
- Fallback path reconstruction for workspaces without metadata files

**Result:** API listing and search endpoints now use consistent workspace paths.

## Troubleshooting

- **Stuck locks**: Exit Claude Code, delete `~/.coa/codesearch/indexes/*/write.lock`
- **Changes not working**: **EXIT AND RESTART CLAUDE CODE** - code changes don't take effect until restart
- **Build errors**: Verify COA.Mcp.Framework references
- **Testing shows old behavior**: You're testing cached old code - restart Claude Code first!