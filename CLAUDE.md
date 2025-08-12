# CodeSearch MCP Server - Development Guide

## 🚨 CRITICAL: This is CodeSearch v2.0

Built on COA MCP Framework 1.7.0. Clean search-only architecture - memory management is handled by ProjectKnowledge MCP.

## 🚨 CRITICAL WARNINGS

### NEVER Run MCP Server Locally
```bash
❌ dotnet run --project COA.CodeSearch.McpServer -- stdio
```
**Why**: Creates orphaned processes that lock Lucene indexes.

### Before Testing Changes
⚠️  **USER MUST RESTART CLAUDE CODE** after any code changes - MCP tools run from installed version, not your edits.

### Build Configuration  
- **Development**: `dotnet build -c Debug`
- **Before Tests**: ALWAYS build first: `dotnet build -c Debug && dotnet test`

## 🏗️ Project Status

### ✅ Working Tools (7 total)
- `index_workspace` (indexing)
- `text_search`, `file_search`, `directory_search` (search)
- `batch_operations` (batch processing)  
- `recent_files`, `similar_files` (discovery)

### ✅ Recently Completed
- All tools now use proper ILuceneIndexService interface
- BatchOperationsTool implemented and registered
- FileWatcher integrated with background services
- All tools building and testing successfully

## ⚙️ Development Guidelines

### Framework Usage
Built on COA MCP Framework 1.7.0:
```csharp
public class MyTool : McpToolBase<TParams, TResult>
{
    protected override async Task<TResult> ExecuteInternalAsync(...)
    {
        // Use ValidateRequired, ValidateRange from base
    }
}
```

### Service Interfaces
**ALWAYS verify interface methods** - don't assume they exist:

```csharp
// ILuceneIndexService provides:
✅ InitializeIndexAsync, IndexDocumentAsync, SearchAsync, etc.
❌ GetIndexWriterAsync() - does NOT exist
```

### Path Resolution  
**ALWAYS use IPathResolutionService** for path computation:
```csharp
❌ Path.Combine("~/.coa", "indexes")  
✅ _pathResolver.GetIndexPath(workspacePath)
```

## 🎯 Current Focus

1. ✅ All core search tools implemented and working
2. ✅ BatchOperationsTool for bulk operations
3. ✅ Clean architecture with proper service interfaces
4. Ready for production use and further enhancements

## 🔧 Essential Commands

```bash
# Development workflow
dotnet build -c Debug && dotnet test

# Tool names (all implemented and working)
mcp__codesearch__index_workspace
mcp__codesearch__text_search
mcp__codesearch__file_search
mcp__codesearch__batch_operations
mcp__codesearch__directory_search
mcp__codesearch__recent_files
mcp__codesearch__similar_files
```

## 🐛 Troubleshooting

**Build errors**: Check COA.Mcp.Framework references and verify interface methods exist
**Stuck locks**: Exit Claude Code, delete `~/.coa/codesearch/indexes/*/write.lock`  
**Changes not working**: User must restart Claude Code after rebuild

---

**Key Point**: This is a clean rebuild - verify everything, don't carry over assumptions from old CodeSearch!