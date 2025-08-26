# CodeSearch MCP Server - AI Development Context

## 🚨 Critical Development Constraints

### Code Changes Require Complete Restart
After ANY code changes:
1. Exit Claude Code completely
2. `dotnet build -c Release`
3. Restart Claude Code
4. **Testing before restart shows OLD CODE - not your changes!**

### Never Run Server Locally
```bash
❌ dotnet run --project COA.CodeSearch.McpServer -- stdio
```
Creates orphaned processes that lock Lucene indexes.

## 🔧 Essential Code Patterns

### Path Resolution - Always Use Service
```csharp
❌ Path.Combine("~/.coa", "indexes")
❌ Path.Combine(Environment.GetFolderPath(...), ".coa")
✅ _pathResolver.GetIndexPath(workspacePath)
✅ _pathResolver.GetBasePath()
```

### Service Interface Verification
Verify methods exist before using:
```csharp
✅ ILuceneIndexService.SearchAsync()
✅ ILuceneIndexService.IndexDocumentAsync()
❌ ILuceneIndexService.GetIndexWriterAsync() // doesn't exist
❌ ILuceneIndexService.HasAsync() // doesn't exist
```

## 🏗️ Architecture Essentials

**Framework:** COA MCP Framework 1.7.0
**Search Engine:** Lucene.NET 4.8.0-beta00017
**Tool Base:** `McpToolBase<TParams, TResult>`
**Memory Management:** ProjectKnowledge MCP integration

### Key Services (Constructor Injection)
- `ILuceneIndexService` - Core search operations
- `IPathResolutionService` - All path operations
- `IBatchIndexingService` - Bulk operations
- `IMemoryPressureService` - Resource management

### Tool Names (Use Constants)
```csharp
// From ToolNames.cs
ToolNames.IndexWorkspace = "index_workspace"
ToolNames.TextSearch = "text_search"
ToolNames.FileSearch = "file_search"
ToolNames.BatchOperations = "batch_operations"
```

## 🚀 HTTP API Integration

**Auto-starts on port 5020** when in STDIO mode.

**Key Endpoints:**
- `/api/workspace` - List indexed workspaces
- `/api/workspace/index` - Index workspace
- `/api/search/text` - Text search
- `/api/search/symbol` - Symbol search

**Path Behavior:** Returns actual workspace paths, not hashed directory names.

## ⚠️ Common Anti-Patterns

```csharp
❌ using (var writer = IndexWriter.Create(...))  // Manual Lucene
✅ await _indexService.IndexDocumentAsync(...)   // Use service

❌ Directory.Exists(path)                        // Direct I/O
✅ _pathResolver.DirectoryExists(path)           // Safe wrapper

❌ var results = await SearchFiles();            // Assume async naming
✅ var results = await SearchAsync();            // Verify actual method names
```