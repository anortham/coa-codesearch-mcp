# CodeSearch MCP Server - Developer Guide

## ⚠️ NON-NEGOTIABLE REQUIREMENTS

**SEMANTIC SEARCH IS A CORE FEATURE - NEVER SUGGEST REMOVING IT**
- 3-tier search architecture: SQLite → Lucene → Semantic (vec0/HNSW)
- Semantic search is the tentpole feature - focus on making it FAST, not removing it
- If performance is an issue, optimize the implementation, don't remove functionality

## 🎯 Quick Reference

Lucene.NET-powered code search with Tree-sitter type extraction. Local workspace indexing with cross-platform support.

**Version**: 4b32d04 | **Status**: Production Ready | **Performance**: 117 files/sec | **Framework**: TreeSitter.DotNet

### Core Tools (17 available)

```bash
# Essential Search
text_search         # Full-text code search with CamelCase tokenization
symbol_search       # Find classes, methods, interfaces by name
goto_definition     # Jump to exact symbol definitions
find_references     # Find ALL usages (critical before refactoring)

# File Discovery
file_search         # Pattern-based file finding (supports **/*.ext)
recent_files        # Recent modifications (great for context)
similar_files       # Find similar code patterns
directory_search    # Find directories by pattern

# Advanced Search
line_search         # Line-by-line search (replaces grep/rg)
search_and_replace  # Bulk find & replace with preview
batch_operations    # Multiple searches in parallel

# Code Editing
insert_at_line      # Insert code at specific line numbers
replace_lines       # Replace line ranges with new content
delete_lines        # Delete line ranges precisely

# Semantic Analysis
get_symbols_overview # Extract all symbols from files (classes, methods, etc.)
find_patterns       # Detect code patterns and quality issues

# System
index_workspace     # Build/update search index (REQUIRED FIRST)
```

## 🚨 Development Workflow

**Code Changes:**

1. Exit Claude Code completely
2. `dotnet build -c Debug` (Debug mode recommended)
3. Restart Claude Code
4. ⚠️ **Testing before restart shows OLD CODE**

**Never run:** `dotnet run -- stdio` (creates orphaned processes)

## 🔍 Usage Essentials

**Always start with:**

```bash
mcp__codesearch__index_workspace --workspacePath "."
```

**Common patterns:**

```bash
# Search code
mcp__codesearch__text_search --query "class UserService"

# Verify types before coding
mcp__codesearch__goto_definition --symbol "UserService"

# Check impact before refactoring
mcp__codesearch__find_references --symbol "UpdateUser"

# File patterns (simple and recursive)
mcp__codesearch__file_search --pattern "*Controller.cs"
mcp__codesearch__file_search --pattern "**/*.csproj"
```

## 🏗️ Architecture

**Index Storage**: `.coa/codesearch/indexes/{workspace-hash}/` (local per workspace)
- `lucene/` - Lucene.NET full-text search index
- `db/` - SQLite canonical symbol database (workspace.db)
- `vectors/` - HNSW semantic search index (julie-semantic)

**Logs**: `.coa/codesearch/logs/` (workspace-specific logging)

**Token Optimization**: Active via `BaseResponseBuilder<T>` with 40% safety budget
**Test Framework**: NUnit (528+ tests, zero warnings)
**Framework**: Local project references for active development

### 3-Tier Search Architecture

**Tier 1: SQLite Exact Lookups** (~1ms)
- Symbol definitions (classes, methods, interfaces)
- Identifier usages (calls, references) via LSP-quality extraction
- Use: `goto_definition`, `find_references`, exact symbol queries

**Tier 2: Lucene Fuzzy Search** (~20ms)
- Full-text code search with CamelCase tokenization
- Smart scoring: type definitions boosted 10x, test files de-prioritized
- Use: `text_search`, `symbol_search`, fuzzy matching

**Tier 3: Semantic Search** (~47ms) ✅ **PRODUCTION READY**
- Vector similarity via sqlite-vec (vec0) + HNSW index
- 384-dimensional embeddings (bge-small-en-v1.5 model)
- Cross-language semantic code discovery
- Use: Finding conceptually similar code, semantic refactoring

### Semantic Search Pipeline

**Bulk Indexing** (~40.6s total):
1. julie-semantic generates embeddings with ONNX (~40s)
2. Writes f32 vectors as BLOBs to SQL (~0.05s)
3. C# copies BLOBs to vec0 for fast KNN search (~0.6s)

**Incremental Updates** (~0.6s per file):
1. julie-semantic update --write-db regenerates embeddings (~0.4s)
2. C# copies updated BLOBs to vec0 (~0.2s)
3. Semantic search stays current automatically

**Query Time**:
- Search queries converted to vectors via C# ONNX (instant, <10ms)
- vec0 KNN search finds top matches (~47ms for 5074 symbols)
- Results enriched with symbol metadata from SQLite

**Key Components**:
- `julie-semantic` (Rust): ONNX inference, HNSW indexing, BLOB storage
- `SqliteVecExtensionService.cs`: Loads vec0 extension for vector operations
- `SQLiteSymbolService.BulkGenerateEmbeddingsAsync`: Copies BLOBs to vec0
- `SQLiteSymbolService.SearchSymbolsSemanticAsync`: Executes KNN queries

## 🚀 Recent Improvements (v2.1.8+)

**SmartQueryPreprocessor Integration**

- ✅ Comprehensive test suite: 35 unit tests with full coverage
- ✅ Intelligent query routing: Auto-detects Symbol/Pattern/Standard modes
- ✅ Wildcard validation: Shared utility prevents invalid Lucene queries
- ✅ Multi-field optimization: Routes queries to optimal indexed fields

**CodeAnalyzer Consistency Audit**

- ✅ Fixed 3 critical inconsistencies in SmartSnippetService, GoToDefinitionTool, SymbolSearchTool
- ✅ Unified dependency injection: All tools use single configured CodeAnalyzer instance
- ✅ Consistent tokenization: `preserveCase: false, splitCamelCase: true` across system

**Framework Integration**

- ✅ Local project references: Active development with live framework changes
- ✅ All 456 tests passing: Full compatibility with framework improvements
- ✅ Zero regressions: Maintains production stability during development

## ⚠️ Common Pitfalls

1. **Missing index**: Always run `index_workspace` first
2. **Field access**: Use `hit.Fields["name"]` not `hit.Document.Get()`
3. **Method assumptions**: Verify signatures with `goto_definition`
4. **Testing changes**: Must restart Claude Code after building
5. **Type extraction**: Check `type_info` field structure

## 🛠️ Code Patterns

```csharp
// ✅ Path Resolution
_pathResolver.GetIndexPath(workspacePath)

// ✅ Lucene Operations
await _indexService.SearchAsync(...)

// ✅ Response Building
return new AIOptimizedResponse<T> { Data = new AIResponseData<T>(...) }
```

## 🧪 Testing

```bash
# All tests (366+ total)
dotnet test

# Specific tool tests
dotnet test --filter "SymbolSearchToolTests"

# Health check
mcp__codesearch__recent_files --workspacePath "."
```

## 📚 Related

- **COA MCP Framework**: Core MCP framework (v2.1.16)
- **Goldfish MCP**: Session/memory management
- **Tree-sitter bindings**: `C:\source\tree-sitter-dotnet-bindings`

---

_Updated: 2025-09-22 - Restored to stable commit 4b32d04 with TreeSitter.DotNet, 117 files/sec performance_
