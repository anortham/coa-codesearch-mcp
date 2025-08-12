# CodeSearch Testing Checklist

## Pre-Test Setup
- [ ] Build Release mode: `dotnet build -c Release`
- [ ] User exits Claude Code completely
- [ ] User starts new Claude Code session
- [ ] Check logs directory exists: `~/.coa/codesearch/logs`

## Tool Testing Protocol

### 1. IndexWorkspaceTool Tests

#### Test 1.1: Basic Indexing
```
mcp__codesearch__index_workspace
Parameters:
- workspacePath: "C:\source\COA CodeSearch MCP"
- forceRebuild: false
```
**Expected Results:**
- ✅ Should return success with file count
- ✅ Should show indexing statistics
- ✅ Should create index at `~/.coa/codesearch/indexes/[hash]/`
- ✅ FileWatcher should auto-start for this workspace

**Check Logs For:**
- "Initialized index for workspace"
- "Indexed workspace"
- "FileWatcher started monitoring"
- No errors or exceptions

#### Test 1.2: Force Rebuild
```
mcp__codesearch__index_workspace
Parameters:
- workspacePath: "C:\source\COA CodeSearch MCP"
- forceRebuild: true
```
**Expected Results:**
- ✅ Should clear existing index
- ✅ Should re-index all files
- ✅ File count should be consistent

#### Test 1.3: Invalid Path
```
mcp__codesearch__index_workspace
Parameters:
- workspacePath: "C:\NonExistentPath"
- forceRebuild: false
```
**Expected Results:**
- ✅ Should return error with clear message
- ✅ Should suggest recovery steps
- ✅ No crash or unhandled exception

### 2. TextSearchTool Tests

#### Test 2.1: Simple Search
```
mcp__codesearch__text_search
Parameters:
- query: "LuceneIndexService"
- workspacePath: "C:\source\COA CodeSearch MCP"
- responseMode: "summary"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find LuceneIndexService.cs files
- ✅ Should return snippets with context
- ✅ Results should be ranked by relevance
- ✅ Should include insights and actions

#### Test 2.2: Complex Query
```
mcp__codesearch__text_search
Parameters:
- query: "async AND (index OR search)"
- workspacePath: "C:\source\COA CodeSearch MCP"
- responseMode: "full"
- maxTokens: 10000
- noCache: true
```
**Expected Results:**
- ✅ Should handle boolean operators correctly
- ✅ Should find methods with async and index/search
- ✅ Full mode should return more results than summary

#### Test 2.3: Code Pattern Search
```
mcp__codesearch__text_search
Parameters:
- query: "Task<.*Result>"
- workspacePath: "C:\source\COA CodeSearch MCP"
- responseMode: "adaptive"
- maxTokens: 8000
- noCache: false
```
**Expected Results:**
- ✅ Should find async methods returning Result types
- ✅ Cache should work on second call
- ✅ Adaptive mode should balance detail and tokens

### 3. FileSearchTool Tests

#### Test 3.1: Extension Filter
```
mcp__codesearch__file_search
Parameters:
- pattern: "*.cs"
- workspacePath: "C:\source\COA CodeSearch MCP"
- extensionFilter: ".cs"
- maxResults: 50
- responseMode: "summary"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should return only .cs files
- ✅ Should respect maxResults limit
- ✅ Should show total count vs returned count

#### Test 3.2: Name Pattern
```
mcp__codesearch__file_search
Parameters:
- pattern: "*Tool*"
- workspacePath: "C:\source\COA CodeSearch MCP"
- maxResults: 100
- responseMode: "full"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find all files with "Tool" in name
- ✅ Should include full paths and metadata

#### Test 3.3: Regex Pattern
```
mcp__codesearch__file_search
Parameters:
- pattern: ".*Service\\.cs$"
- workspacePath: "C:\source\COA CodeSearch MCP"
- useRegex: true
- maxResults: 50
- responseMode: "adaptive"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find files ending with Service.cs
- ✅ Regex should be properly interpreted

### 4. DirectorySearchTool Tests

#### Test 4.1: Named Directory
```
mcp__codesearch__directory_search
Parameters:
- pattern: "Services"
- workspacePath: "C:\source\COA CodeSearch MCP"
- includeSubdirectories: true
- maxResults: 20
- responseMode: "full"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find all "Services" directories
- ✅ Should include file and subdirectory counts
- ✅ Should show directory hierarchy

#### Test 4.2: Wildcard Pattern
```
mcp__codesearch__directory_search
Parameters:
- pattern: "*Test*"
- workspacePath: "C:\source\COA CodeSearch MCP"
- includeSubdirectories: true
- includeHidden: false
- maxResults: 50
- responseMode: "summary"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find directories with "Test" in name
- ✅ Should exclude hidden directories
- ✅ Should respect max results

### 5. RecentFilesTool Tests

#### Test 5.1: Hour Time Frame
```
mcp__codesearch__recent_files
Parameters:
- workspacePath: "C:\source\COA CodeSearch MCP"
- timeFrame: "1h"
- maxResults: 20
- responseMode: "full"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should return files modified in last hour
- ✅ Should sort by modification time (newest first)
- ✅ Should include timestamps

#### Test 5.2: Day Time Frame with Filter
```
mcp__codesearch__recent_files
Parameters:
- workspacePath: "C:\source\COA CodeSearch MCP"
- timeFrame: "1d"
- extensionFilter: ".cs,.json"
- maxResults: 50
- responseMode: "summary"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should return only .cs and .json files
- ✅ Should be from last 24 hours
- ✅ Summary should be concise

### 6. SimilarFilesTool Tests

#### Test 6.1: Similar to Service File
```
mcp__codesearch__similar_files
Parameters:
- filePath: "C:\source\COA CodeSearch MCP\COA.CodeSearch.McpServer\Services\Lucene\LuceneIndexService.cs"
- workspacePath: "C:\source\COA CodeSearch MCP"
- maxResults: 10
- minScore: 0.1
- responseMode: "full"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find other service files
- ✅ Should rank by similarity score
- ✅ Should exclude the source file itself
- ✅ Scores should be between 0 and 1

#### Test 6.2: Similar to Tool File
```
mcp__codesearch__similar_files
Parameters:
- filePath: "C:\source\COA CodeSearch MCP\COA.CodeSearch.McpServer\Tools\TextSearchTool.cs"
- workspacePath: "C:\source\COA CodeSearch MCP"
- maxResults: 5
- minScore: 0.2
- responseMode: "adaptive"
- maxTokens: 8000
- noCache: true
```
**Expected Results:**
- ✅ Should find other tool files
- ✅ Should respect minScore threshold
- ✅ Should show why files are similar

### 7. FileWatcher Tests

#### Test 7.1: File Modification
1. After indexing, modify a .cs file
2. Save the file
3. Wait 1-2 seconds (debounce)
4. Search for new content

**Expected Results:**
- ✅ Log should show "File changed detected"
- ✅ Log should show "Re-indexing file"
- ✅ Search should find new content

#### Test 7.2: File Addition
1. Create a new .cs file with unique content
2. Save the file
3. Wait 1-2 seconds
4. Search for the unique content

**Expected Results:**
- ✅ Log should show "File created"
- ✅ New file should be indexed
- ✅ Search should find the new file

#### Test 7.3: File Deletion
1. Delete a file
2. Wait 1-2 seconds
3. Search for content from deleted file

**Expected Results:**
- ✅ Log should show "File deleted"
- ✅ File should be removed from index
- ✅ Search should not return deleted file

## Log Monitoring Checklist

Check logs at: `~/.coa/codesearch/logs/codesearch-[date].log`

### Critical Errors to Watch For:
- [ ] No "Index is corrupt" messages
- [ ] No "write.lock" timeout errors
- [ ] No "OutOfMemoryException"
- [ ] No "UnauthorizedAccessException"
- [ ] No unhandled exceptions

### Performance Indicators:
- [ ] Indexing time reasonable (< 30 seconds for ~500 files)
- [ ] Search response times < 500ms
- [ ] Memory usage stable (no continuous growth)
- [ ] No circuit breaker trips

### FileWatcher Behavior:
- [ ] "Started monitoring workspace" appears after indexing
- [ ] File changes detected within 1-2 seconds
- [ ] Batch processing works for multiple changes
- [ ] No duplicate indexing of same file

## Response Quality Checklist

### For All Tools:
- [ ] Results are relevant and accurate
- [ ] Token optimization works (no truncation)
- [ ] Progressive disclosure at appropriate thresholds
- [ ] Error messages are helpful and actionable
- [ ] Caching works when noCache=false
- [ ] Response times are acceptable

### Search Quality Metrics:
- [ ] Code analyzer preserves code structure
- [ ] CamelCase tokenization works
- [ ] Relevance ranking is logical
- [ ] Snippets contain useful context
- [ ] File paths are correct and complete

## Performance Benchmarks

Expected performance for ~500 file workspace:

| Operation | Expected Time | Max Acceptable |
|-----------|--------------|----------------|
| Initial Index | 10-20s | 30s |
| Re-index Single File | < 100ms | 500ms |
| Text Search | 100-300ms | 1s |
| File Search | 50-150ms | 500ms |
| Directory Search | 50-150ms | 500ms |
| Recent Files | 100-200ms | 500ms |
| Similar Files | 200-500ms | 2s |

## Issue Resolution Guide

### If write.lock persists:
1. Exit Claude Code
2. Delete `~/.coa/codesearch/indexes/*/write.lock`
3. Restart Claude Code

### If searches return no results:
1. Check if index exists
2. Force rebuild index
3. Check logs for indexing errors
4. Verify file extensions are supported

### If FileWatcher not working:
1. Check logs for "Started monitoring"
2. Verify debounce time (500ms default)
3. Check file system permissions
4. Ensure workspace was indexed first

### If memory issues occur:
1. Check RAM buffer size in config (256MB)
2. Monitor with System Health Check
3. Reduce batch sizes if needed
4. Check for memory leaks in logs

## Final Validation

After all tests:
- [ ] All 6 tools functioning correctly
- [ ] FileWatcher detecting changes
- [ ] No critical errors in logs
- [ ] Performance within acceptable ranges
- [ ] Search quality meets expectations
- [ ] Memory usage stable
- [ ] Circuit breakers not tripping

## Sign-off

- [ ] All tests completed successfully
- [ ] Ready for production use
- [ ] Documentation updated if needed
- [ ] Known issues documented