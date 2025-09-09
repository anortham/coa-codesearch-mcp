# CodeSearch MCP Server - Developer Guide

## 🎯 Quick Reference

Lucene.NET-powered code search with Tree-sitter type extraction. Local workspace indexing with cross-platform support.

**Version**: 2.1.8+ | **Status**: Production Ready | **Warnings**: Zero | **Framework**: Local Dev Mode

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
**Global Logs**: `~/.coa/codesearch/logs/` (centralized)  
**Token Optimization**: Active via `BaseResponseBuilder<T>` with 40% safety budget
**Test Framework**: NUnit (456+ tests, zero warnings)
**Framework**: Local project references for active development

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

- **COA MCP Framework**: Core MCP framework (v2.1.8)
- **Goldfish MCP**: Session/memory management  
- **Tree-sitter bindings**: `C:\source\tree-sitter-dotnet-bindings`

---
_Updated: 2025-09-08 - Production ready v2.1.8 with zero warnings, enhanced file search, token optimization_
