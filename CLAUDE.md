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

## 🔍 Essential Usage Patterns

### Always Start With
```bash
# REQUIRED FIRST - Initialize search index
mcp__codesearch__index_workspace --workspacePath "path/to/project"
```

### Common Searches
```bash
# Find code patterns
mcp__codesearch__text_search --query "class UserService"

# Navigate to definitions  
mcp__codesearch__goto_definition --symbol "UserService"

# Find all usages before refactoring
mcp__codesearch__find_references --symbol "UpdateUser" 

# Search files by name
mcp__codesearch__file_search --pattern "*Controller.cs"
```

## 🔤 Enhanced Features (2025-09-05)

### CamelCase Tokenization with Generic Type Support
- **Before**: "McpToolBase" search returned 0 hits
- **After**: Returns all `McpToolBase<TParams, TResult>` occurrences
- **Implementation**: Enhanced `SplitCamelCase` in `CodeAnalyzer.cs:519-587`

**Examples:**
```bash
search: "McpToolBase" → finds "McpToolBase<TParams, TResult>"
search: "Tool" → finds "CodeSearchToolBase", "McpToolBase", etc.
search: "Repository" → finds "IRepository<T>", "UserRepository"
```

### ResourceStorageProvider & MCP Resources
- **Problem**: `mcp-resource://memory-compressed/...` URIs timing out
- **Solution**: Implemented `ResourceStorageProvider.cs`
- **Result**: Large search results now accessible without timeouts

## 🏠 Hybrid Local Indexing Architecture

### Storage Model
- **Local Workspace Indexes**: `.coa/codesearch/indexes/{workspace-name_hash}/` within workspace
- **Global Logs**: `~/.coa/codesearch/logs/` for centralized logging
- **Multi-Workspace**: Each workspace gets isolated index
- **Cross-Platform**: SimpleFSLockFactory ensures compatibility

## ⚠️ Common Pitfalls

1. **Assuming method names:** Always verify actual method signatures with goto_definition
2. **Field access:** Use `hit.Fields["name"]` not `hit.Document.Get()`
3. **Missing index:** Run `index_workspace` first, always
4. **Type extraction:** Check `type_info` field structure in results
5. **Testing changes:** Must restart Claude Code after building

## 📋 Scriban Template Context Reference

**CRITICAL**: When working with instruction templates, NEVER guess what's available.

### Template Context Location
`COA.Mcp.Framework/src/COA.Mcp.Framework/Services/InstructionTemplateProcessor.cs:131-206`

### Available Variables
- `available_tools` - String array of tool names  
- `available_markers` - String array of capability markers
- `tool_priorities` - Dictionary<string, int> of tool priorities
- `workflow_suggestions` - Array of workflow suggestions
- `server_info` - Server information object
- `builtin_tools` - String array of built-in Claude tools
- `tool_comparisons` - Dictionary of tool comparisons
- `enforcement_level` - String: "strongly_urge", "recommend", "suggest"

### Available Helper Functions
- `has_tool(tools, tool)` - Check if tool exists in array
- `has_marker(markers, marker)` - Check if marker exists  
- `has_builtin(builtins, tool)` - Check if builtin exists

### Available Scriban Built-in Functions
With v2.1.5+, standard Scriban functions are now available:
- `array.size` - Get array length
- `object.default` - Provide default value for null/undefined objects
- `string.*` - All standard string functions (upcase, downcase, etc.)

### Template Pattern Examples
```scriban
// ✅ CORRECT - Use native Scriban functions
{{ tool_priorities[tool] | object.default 50 }}

// ✅ CORRECT - Use native array.size
{{ available_tools.size }}

// ✅ CORRECT - Use native string functions
{{ enforcement_level | string.upcase }}

// ✅ CORRECT - Check if variable exists (still works)
{{ if tool_priorities[tool] }}{{ tool_priorities[tool] }}{{ else }}50{{ end }}

// ❌ WRONG - Old custom functions (deprecated)
{{ array_length available_tools }}
{{ string_join available_tools ", " }}
```

## 🛠️ Code Patterns

### Path Resolution (Use Service)
```csharp
// ✅ CORRECT
_pathResolver.GetIndexPath(workspacePath)
_pathResolver.DirectoryExists(path)

// ❌ WRONG
Path.Combine("~/.coa", "indexes")
Directory.Exists(path)
```

### Lucene Operations (Use Service)
```csharp
// ✅ CORRECT
await _indexService.IndexDocumentAsync(...)
await _indexService.SearchAsync(...)

// ❌ WRONG
using (var writer = IndexWriter.Create(...))
```

### Response Building
```csharp
// ✅ CORRECT
Data = new AIResponseData<T>
return new AIOptimizedResponse<T>

// ❌ WRONG
Data = new AIOptimizedData<T>
```

## 🧪 Testing & Debugging

### Unit Tests
```bash
# All tests (should be 206+)
dotnet test

# Specific tool tests
dotnet test --filter "SymbolSearchToolTests"
```

### Health Checks
```bash
# Check if indexed
mcp__codesearch__recent_files --workspacePath "."

# Force rebuild if issues
mcp__codesearch__index_workspace --workspacePath "." --forceRebuild true
```

## 📚 Related Projects

- **COA MCP Framework**: Core framework for MCP tools
- **Goldfish MCP**: Session and memory management  
- **Tree-sitter bindings**: `C:\source\tree-sitter-dotnet-bindings`

---
*Last updated: 2025-09-05 - Enhanced CamelCase tokenization with generic type support, ResourceStorageProvider for MCP resource URIs, hybrid local indexing model*