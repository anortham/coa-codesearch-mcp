# CodeSearch MCP Server - AI Development Context

## 🎯 Quick Start for AI Assistants

This server provides powerful code search and navigation capabilities via Lucene.NET indexing and Tree-sitter type extraction. Features hybrid local indexing model with multi-workspace support and cross-platform compatibility.

### Core Tools Available (12 total)
```csharp
// Search & Navigation
ToolNames.TextSearch = "text_search"           // Full-text code search
ToolNames.SymbolSearch = "symbol_search"       // Find classes, methods, interfaces
ToolNames.GoToDefinition = "goto_definition"   // Jump to symbol definitions
ToolNames.FindReferences = "find_references"   // Find all usages

// File Operations
ToolNames.FileSearch = "file_search"           // Find files by pattern
ToolNames.DirectorySearch = "directory_search" // Find directories
ToolNames.RecentFiles = "recent_files"         // Recent modifications
ToolNames.SimilarFiles = "similar_files"       // Find similar code

// Advanced Operations
ToolNames.LineSearch = "line_search"           // Line-by-line search (replaces grep)
ToolNames.SearchAndReplace = "search_and_replace" // Bulk find & replace
ToolNames.BatchOperations = "batch_operations" // Multiple operations at once
ToolNames.IndexWorkspace = "index_workspace"   // Build/update search index
```

## 🚨 Critical: Code Changes Require Full Restart

**After ANY code changes:**
1. Exit Claude Code completely
2. Run: `dotnet build -c Release`
3. Restart Claude Code
4. **⚠️ Testing before restart shows OLD CODE - not your changes!**

**Never run locally:** `dotnet run -- stdio` creates orphaned processes that lock indexes.

## 🔍 Architecture Overview

### Technology Stack
- **Framework:** COA MCP Framework 2.0.1
- **Search Engine:** Lucene.NET 4.8.0-beta00017
- **Type Extraction:** Tree-sitter (C#, TypeScript, Python support)
- **Response Optimization:** Token-aware response building
- **Tool Base Class:** `McpToolBase<TParams, TResult>`

### Core Services (via Dependency Injection)
```csharp
ILuceneIndexService      // Search operations
IPathResolutionService   // Path handling
IBatchIndexingService    // Bulk indexing
IMemoryPressureService   // Memory management
IFileIndexingService     // File processing
IResponseCacheService    // Response caching
IResourceStorageService  // Large response storage
```

## 📝 Type Extraction & Navigation

### How Type Information Works
1. **Extraction:** Tree-sitter parses files during indexing
2. **Storage:** JSON in `type_info` field, searchable names in `type_names`
3. **Access:** Navigation tools deserialize and search this data
4. **Deserialization:** Uses `StoredTypeInfo.DeserializationOptions`

### Accessing Type Data
```csharp
// ✅ Correct field access
var typeInfo = hit.Fields["type_info"];
var typeNames = hit.Fields["type_names"];

// ❌ Wrong - Don't use Document.Get()
var wrong = hit.Document?.Get("type_info");
```

### JSON Structure
```json
{
  "Types": [{
    "Name": "ClassName",
    "Kind": "class",
    "Signature": "public class ClassName",
    "Line": 10,
    "Column": 1,
    "Modifiers": ["public"],
    "BaseType": "BaseClass",
    "Interfaces": ["IInterface1"]
  }],
  "Methods": [{
    "Name": "MethodName",
    "Signature": "public void MethodName()",
    "ReturnType": "void",
    "Line": 15,
    "ContainingType": "ClassName",
    "Parameters": [],
    "Modifiers": ["public"]
  }],
  "Language": "c-sharp"
}
```

## 🛠️ Common Code Patterns

### Path Resolution
```csharp
// ❌ WRONG - Direct path manipulation
Path.Combine("~/.coa", "indexes")
Path.Combine(Environment.GetFolderPath(...), ".coa")
Directory.Exists(path)

// ✅ CORRECT - Use service (Hybrid Local Model)
_pathResolver.GetIndexPath(workspacePath)        // Returns {workspace}/.coa/codesearch/indexes/{workspace-name_hash}/
_pathResolver.GetBasePath()                       // Returns ~/.coa/codesearch (global logs, config)
_pathResolver.DirectoryExists(path)
```

### Lucene Operations
```csharp
// ❌ WRONG - Manual IndexWriter
using (var writer = IndexWriter.Create(...))

// ✅ CORRECT - Use service
await _indexService.IndexDocumentAsync(...)
await _indexService.SearchAsync(...)
```

### Response Building
```csharp
// ❌ WRONG type names
Data = new AIOptimizedData<T>

// ✅ CORRECT framework types
Data = new AIResponseData<T>
return new AIOptimizedResponse<T>
```

### Query Building
```csharp
// ❌ WRONG Lucene.NET syntax
new BooleanQuery.Builder()

// ✅ CORRECT instantiation
new BooleanQuery()
```

## 🏠 Hybrid Local Indexing Architecture

### Storage Model
- **Local Workspace Indexes**: `.coa/codesearch/indexes/{workspace-name_hash}/` within primary workspace
- **Global Components**: `~/.coa/codesearch/logs/` for centralized logging
- **Multi-Workspace Support**: Each workspace gets isolated index, searchable from single CodeSearch session
- **Cross-Platform Compatibility**: SimpleFSLockFactory ensures consistent behavior across macOS, Windows, Linux

### Key Benefits
1. **Fast Access**: Indexes co-located with source code for optimal I/O performance
2. **Perfect Isolation**: Each workspace maintains its own search index with zero cross-contamination
3. **Lock Reliability**: SimpleFSLockFactory eliminates cross-platform lock issues
4. **Multi-Project Support**: Index multiple workspaces simultaneously without conflicts
5. **Version Control Friendly**: `.coa/` directories can be gitignored safely

### Architecture Changes
- **Removed**: WorkspaceRegistryService (no more orphaned workspace tracking)
- **Enhanced**: PathResolutionService with hybrid local/global path resolution
- **Fixed**: Lock management with explicit SimpleFSLockFactory configuration
- **Improved**: Multi-workspace session support with isolated indexes

## 🔧 Token Optimization

### Response Builder Pattern
All tools use response builders that:
1. Reduce results to fit token limits
2. Store full results in ResourceStorage
3. Return resource URIs for large responses
4. Clean unnecessary fields from SearchHit objects

**Important:** When modifying `SearchHit`, update `SearchResponseBuilder.CleanupHits()`

## ⚠️ Common Pitfalls

1. **Assuming method names:** Always verify actual method signatures
2. **Field access:** Use `hit.Fields["name"]` not `hit.Document.Get()`
3. **Async naming:** Not all async methods end with "Async"
4. **Type casing:** JSON uses PascalCase (Types, Methods, Language)
5. **Testing changes:** Must restart Claude Code after building

## 📊 Testing

### Unit Test Structure
- Base class: `CodeSearchToolTestBase<TTool>`
- Mock services provided via DI
- Test files in: `COA.CodeSearch.McpServer.Tests/Tools/`

### Running Tests
```bash
# All tests
dotnet test

# Specific tool tests
dotnet test --filter "SymbolSearchToolTests"

# Navigation tools only
dotnet test --filter "SymbolSearchToolTests|GoToDefinitionToolTests|FindReferencesToolTests"
```

## 🔍 Debugging Tips

1. **Check logs:** Look for `[WRN]` and `[ERR]` in output
2. **Index issues:** Run `index_workspace` with `forceRebuild: true`
3. **Type extraction:** Verify `type_info` field in search results
4. **Memory issues:** Monitor with `IMemoryPressureService`
5. **Performance:** Use batch operations for multiple searches

## 📚 Related Projects

- **Tree-sitter bindings:** `C:\source\tree-sitter-dotnet-bindings`
- **COA MCP Framework:** Core framework for MCP tools
- **Goldfish MCP:** Session and memory management
- **CodeNav MCP:** Advanced C#/TypeScript navigation (being consolidated)

---
*Last updated: 2025-09-04 - Hybrid local indexing model implemented with multi-workspace support*