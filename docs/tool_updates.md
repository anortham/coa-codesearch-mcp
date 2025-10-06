# Tool Enhancement Checklist

**Goal**: Systematically review and enhance each tool using our new SQLite foundation and semantic capabilities.

## 🚨 CRITICAL DISCOVERY: FTS5 Already Set Up, Lucene Position Hacks Obsolete

### The Hacky Lucene Positional Code We Can Delete

**LineAwareSearchService.cs** (324 lines of complexity):
- Stores `line_data` JSON blobs in Lucene documents with cached term→line mappings
- Lines 87-128: Deserializes JSON to find line numbers for search terms
- Lines 213-251: Hacky query string parsing (strips Lucene syntax: `content:`, `+`, `-`, `()`, etc.)
- Manual content splitting and term matching to calculate positions

**SmartSnippetService.cs** (294 lines, even hackier):
- Lines 223-293: **Very** hacky query string surgery to extract search terms
- Lines 199-220: Searches Lucene TWICE (once for results, again for docId - has TODO about this!)
- Manual content parsing with StringReader to extract context lines
- Reflection to unwrap `MultiFactorScoreQuery` and get base query

### Why This Exists
Lucene.NET doesn't natively provide clean line positions, so we:
1. Store JSON blobs with term→line mappings in each document
2. Parse content manually to find which lines contain matches
3. Do string surgery on query syntax to extract search terms
4. Search the index twice to get document IDs

### SQLite FTS5 to the Rescue

**FTS5 is ALREADY SET UP** with 203 files indexed! ✅

```sql
-- Schema (already exists in workspace.db)
CREATE VIRTUAL TABLE files_fts USING fts5(
    path,
    content
);

-- Auto-sync triggers (already exist)
CREATE TRIGGER files_ai AFTER INSERT ON files ...
CREATE TRIGGER files_ad AFTER DELETE ON files ...
CREATE TRIGGER files_au AFTER UPDATE ON files ...
```

**Verified Working** (tested on workspace.db):
```sql
-- Search
SELECT path FROM files_fts WHERE files_fts MATCH 'SymbolSearch';
-- ✅ Returns: SymbolSearchTool.cs, ToolNames.cs, etc.

-- Highlighted snippets
SELECT snippet(files_fts, 1, '«', '»', '...', 32)
FROM files_fts
WHERE files_fts MATCH 'SymbolSearch';
-- ✅ Returns: ...public override string Name => ToolNames.«SymbolSearch»;...
```

**FTS5 Native Features:**
- `snippet(table, column, startMark, endMark, ellipsis, tokens)` - Smart highlighting
- `offsets(table)` - Returns: `column term_position byte_offset length` for each match
- No JSON serialization, no manual parsing, no query string hacks

**Convert byte offset to line number:**
```csharp
// Simple, clean, no JSON
var content = /* from files.content */;
var lineNumber = content.Take(byteOffset).Count(c => c == '\n') + 1;
```

### Impact: Lucene Might Become Dead Weight

Once we migrate to SQLite:
- **GoToDefinitionTool** → SQLite symbols (exact, <1ms)
- **SymbolSearchTool** → SQLite + embeddings (multi-tier)
- **FindReferencesTool** → SQLite identifiers (already done! ✅)
- **TextSearchTool** → SQLite FTS5 (full-text with clean positions)
- **GetSymbolsOverviewTool** → SQLite symbols (no process spawn)

**Lucene usage shrinks to:** Maybe nothing? Or just fuzzy fallback?

### Code We Can Delete After Migration
- ✅ LineAwareSearchService.cs (324 lines)
- ✅ LineData serialization/deserialization logic
- ✅ Hacky query string parsing in SmartSnippetService
- ✅ Double-search document ID lookup hack
- ✅ JSON blob storage in Lucene documents

**Estimated savings:** ~600+ lines of complex, hacky code replaced by clean SQL.

---

## Available Data Sources

### SQLite Database (via `SQLiteSymbolService`)
- ✅ **symbols** table: 7,653 symbols (name, kind, signature, line positions, file_id, parent_id)
- ✅ **files** table: Full file content, language, hashes, timestamps
- ✅ **files_fts** virtual table: FTS5 full-text search on 203 files (path + content) ⭐ NEW!
- ✅ **identifiers** table: 20,875+ identifiers (unresolved references) across 25 languages
- ✅ **relationships** table: Symbol relationships (calls, inheritance, etc.)
- ✅ Fast queries: <1ms for exact lookups
- ✅ FTS5 features: `snippet()`, `offsets()`, `highlight()` - no manual parsing needed!

### Lucene Index (via `IIndexService`)
- ✅ Full-text search with CamelCase tokenization
- ✅ Fuzzy matching
- ✅ ~20ms typical query time
- ⚠️ May contain redundant data now that SQLite has full content

### Semantic Intelligence (via `SemanticIntelligenceService`)
- ✅ **embedding_vectors** table: 7,653 embeddings (bge-small, 384D)
- ✅ **HNSW index**: Fast similarity search <30ms
- ✅ Incremental updates via file watcher
- ✅ Cross-language semantic understanding

### Julie Integration (via `JulieCodeSearchService`, `ReferenceResolverService`)
- ✅ On-demand identifier resolution
- ✅ Parallel extraction
- ✅ Reference analysis

---

## Tool Enhancement Checklist

### 🔍 Core Search Tools

#### ✅ **SymbolSearchTool** (`SymbolSearchTool.cs`) - Tier 1 COMPLETE
- **Current**: Multi-tier search with SQLite fast path
  - ✅ **Tier 1: SQLite exact match** - 4ms (25x faster than Lucene)
    - Implemented via `TryExactMatchAsync()` → `GetSymbolsByNameAsync()`
    - Uses `GetIdentifierCountByNameAsync()` for reference counts (COUNT query, not fetch-all)
    - Connection pooling via `GetConnectionString()` with `Cache=Shared`
    - First query: 20ms (connection setup), subsequent: 4ms
  - ☐ Tier 2: SQLite prefix match (`WHERE name LIKE ? || '%'`)
  - ✅ Tier 3: Lucene fuzzy (existing fallback)
  - ☐ Tier 4: Semantic via embeddings (NEW!)
- **Impact**: ✅ 25x faster for exact matches (100ms → 4ms)
- **Notes**:
  - Fixed N+1 query problem (was fetching all identifiers to count, now uses COUNT(*))
  - Logs show consistent `✅ Tier 1 HIT` confirmations
  - **Next**: Add Tier 2 prefix matching, then Tier 4 semantic search

#### ☐ **TextSearchTool** (`TextSearchTool.cs`)
- **Current**: Lucene full-text search with hacky positional code
- **Potential**:
  - **PRIMARY**: SQLite FTS5 (already indexed with 203 files!)
  - Use `snippet()` for highlighting (replaces SmartSnippetService hacks)
  - Use `offsets()` for line positions (replaces LineAwareSearchService JSON blobs)
  - Lucene becomes fuzzy fallback only (or delete entirely?)
- **Impact**: Delete ~600 lines of hacky code, clean line positions, no escaping hell
- **Priority**: ⭐⭐⭐ HIGH - Proves FTS5 value, huge code deletion win
- **Notes**: FTS5 verified working, just needs C# integration

#### ☐ **LineSearchTool** (`LineSearchTool.cs`)
- **Current**: Lucene line-by-line search (grep replacement)
- **Potential**:
  - SQLite FTS5 for exact line matching
  - File content in SQLite means no file I/O
- **Impact**: Faster, more reliable than Lucene for literal patterns
- **Notes**:

---

### 🎯 Definition & Reference Tools

#### ✅ **GoToDefinitionTool** (`GoToDefinitionTool.cs`) - SQLite COMPLETE
- **Current**: SQLite-only implementation (no Lucene fallback)
  - Uses `GetSymbolsByNameAsync()` directly
  - 3ms SQLite query time
  - Total execution: 8ms (includes response building)
  - 100% accuracy - no false positives
- **Impact**: ✅ Already optimized, SQLite-first from the start
- **Notes**:
  - Already uses connection pooling via SQLiteSymbolService
  - Returns exact matches only - suggests symbol_search for fuzzy matching
  - **Status**: No work needed - already optimal!

#### ✅ **FindReferencesTool** (`FindReferencesTool.cs`) - SQLite COMPLETE
- **Current**: SQLite-powered reference resolution
  - Uses `ReferenceResolverService.FindReferencesAsync()`
  - Queries identifiers table for LSP-quality references
  - Fast-path logs: `✅ Found {Count} references using identifier fast-path`
  - <10ms typical execution time
- **Potential enhancements** (future):
  - ☐ Add semantic references via embeddings (find semantically similar usages)
  - ☐ Cross-language references via semantic search
- **Impact**: ✅ Already LSP-quality and optimized
- **Notes**:
  - Already uses connection pooling via SQLiteSymbolService
  - Benefits from N+1 query fix (GetIdentifierCountByNameAsync)
  - **Status**: Core optimization complete, semantic features could be added later

#### ✅ **GetSymbolsOverviewTool** (`GetSymbolsOverviewTool.cs`) - SQLite COMPLETE
- **Current**: SQLite-only implementation (no process spawn)
  - Uses `GetSymbolsForFileAsync()` to query symbols table
  - 1.4ms extraction time, 4ms total execution
  - Zero process overhead - pure SQL query
  - Always up-to-date from indexed data
- **Impact**: ✅ Already optimized, SQLite-first from the start
- **Notes**:
  - Already uses connection pooling via SQLiteSymbolService
  - Returns complete symbol hierarchy with line numbers
  - **Status**: No work needed - already optimal!

#### ✅ **TraceCallPathTool** (`TraceCallPathTool.cs`) - Tier 1 Validation COMPLETE
- **Current**: SQLite-powered call path tracing with fast validation
  - ✅ **Tier 1: Symbol existence validation** - 3-4ms
    - Implemented via `TryVerifySymbolExistsAsync()` → `GetSymbolsByNameAsync()`
    - Validates symbol exists before expensive call tracing
    - Logs `✅ Tier 1 HIT` on success, `⏭️ Tier 1 MISS` with graceful fallback
    - Uses same connection pooling as SymbolSearchTool
- **Potential**:
  - ☐ SQLite recursive CTEs on relationships table for true call hierarchies
  - ☐ Semantic bridging for cross-language calls
  - ☐ Build complete call hierarchies (not just identifier matching)
- **Impact**: ✅ 3-4ms validation before call tracing (24x faster overall)
- **Notes**:
  - Current implementation still uses CallPathTracerService for actual tracing
  - Tier 1 validation prevents wasted work on non-existent symbols
  - **Next**: Implement true SQLite-based call hierarchy using relationships table

---

### 📁 File Discovery Tools

#### ☐ **FileSearchTool** (`FileSearchTool.cs`)
- **Current**: File system traversal with patterns
- **Potential**:
  - SQLite query on files table for instant results
  - No I/O, just database query
- **Impact**: Faster file discovery
- **Notes**: May not be worth changing if FS traversal is fast enough

#### ✅ **RecentFilesTool** (`RecentFilesTool.cs`) - SQLite COMPLETE + Bug Fixed
- **Current**: SQLite-powered recent file discovery
  - Uses `GetRecentFilesAsync()` to query files table with time filtering
  - 1-17ms execution time (sub-millisecond when pooled)
  - Extension filtering via SQLite WHERE clauses
  - ✅ **Bug fixed**: Timestamp conversion (Unix seconds ↔ DateTime)
- **Impact**: ✅ Already optimized, instant results from database
- **Notes**:
  - Fixed critical timestamp bug (2 parts):
    1. Query cutoff: Convert to Unix seconds via `ToUnixTimeSeconds()`
    2. Display: Convert from Unix seconds via `FromUnixTimeSeconds()`
  - Now shows correct dates (2025-10-05) instead of (0001-01-01)
  - Now shows reasonable "ago" values (1min) instead of (739528 days)
  - **Status**: Fully functional and performant!

#### ☐ **DirectorySearchTool** (`DirectorySearchTool.cs`)
- **Current**: File system traversal
- **Potential**:
  - SQLite query on file paths (extract directories)
  - May not add much value
- **Impact**: Minimal
- **Notes**: Probably leave as-is

#### ☐ **SimilarFilesTool** (`SimilarFilesTool.cs`)
- **Current**: Content-based similarity (hash? text comparison?)
- **Potential**:
  - **Semantic embeddings**: Find semantically similar files
  - Vector similarity search via HNSW
  - Cross-language similar patterns
- **Impact**: True semantic similarity vs text-based
- **Priority**: ⭐⭐ MEDIUM - Great showcase for embeddings
- **Notes**: Check current implementation first

---

### 🔧 File Editing Tools

#### ☐ **InsertAtLineTool** (`InsertAtLineTool.cs`)
- **Current**: Direct file manipulation
- **Potential**:
  - Use SQLite file content for validation/preview
  - No enhancement needed (works well)
- **Impact**: Minimal
- **Notes**: Leave as-is

#### ☐ **ReplaceLinesTool** (`ReplaceLinesTool.cs`)
- **Current**: Direct file manipulation
- **Potential**: Same as InsertAtLineTool
- **Impact**: Minimal
- **Notes**: Leave as-is

#### ☐ **DeleteLinesTool** (`DeleteLinesTool.cs`)
- **Current**: Direct file manipulation
- **Potential**: Same as InsertAtLineTool
- **Impact**: Minimal
- **Notes**: Leave as-is

#### ☐ **SearchAndReplaceTool** (`SearchAndReplaceTool.cs`)
- **Current**: Lucene search + file edits
- **Potential**:
  - SQLite FTS5 for finding matches (faster, more accurate)
  - Preview from SQLite content (no file I/O)
- **Impact**: Faster, more reliable previews
- **Notes**:

---

### 🔨 Utility Tools

#### ☐ **BatchOperationsTool** (`BatchOperationsTool.cs`)
- **Current**: Orchestrates multiple operations
- **Potential**:
  - No changes needed (just coordinates other tools)
- **Impact**: N/A
- **Notes**: Benefits automatically from other tool improvements

#### ☐ **FindPatternsTool** (`FindPatternsTool.cs`)
- **Current**: Tree-sitter pattern detection (empty catches, magic numbers, etc.)
- **Potential**:
  - Use SQLite symbols for structural pattern detection
  - Semantic patterns via embeddings (find similar anti-patterns)
- **Impact**: More powerful pattern detection
- **Notes**: Check if already using julie extraction

#### ☐ **IndexWorkspaceTool** (`IndexWorkspaceTool.cs`)
- **Current**: Orchestrates indexing (Lucene + SQLite + embeddings)
- **Potential**:
  - Review what goes into Lucene (redundant with SQLite?)
  - Optimize indexing pipeline
- **Impact**: Cleaner architecture, less duplication
- **Notes**: Consider if Lucene is still needed for all data

---

## Enhancement Strategy

### Phase 1: High-Impact, Low-Risk (Week 1)
1. ⭐⭐⭐ **GoToDefinitionTool** - Prove SQLite speed (<1ms)
2. ⭐⭐⭐ **GetSymbolsOverviewTool** - Eliminate process spawn (no search quality risk)
3. ⭐⭐ **SmartSnippetService** - Use FTS5 offsets() for positions (delete LineAwareSearchService)
4. ⭐ **RecentFilesTool** - Simple SQLite query win

**DEFERRED**: TextSearchTool Lucene replacement - See `docs/lucene_vs_fts5_analysis.md`
- Risk: Lose CamelCase splitting, fuzzy search, code pattern preservation
- Decision: Keep Lucene for search quality, use FTS5 only for positions

### Phase 2: Search Enhancement (Week 2)
5. **SymbolSearchTool** - Multi-tier search (exact → fuzzy → semantic)
6. **LineSearchTool** - FTS5 for line-level searches (grep replacement)
7. **FindReferencesTool** - Verify optimization, add semantic layer

### Phase 3: Advanced Features (Week 3)
8. **SimilarFilesTool** - Semantic similarity via embeddings
9. **TraceCallPathTool** - SQLite recursive queries
10. **SearchAndReplaceTool** - FTS5 for match finding

### Phase 4: Architecture Review (Week 4)
11. **IndexWorkspaceTool** - Review Lucene necessity (might delete entirely!)
12. Consider tool consolidation based on learnings
13. Document findings and patterns
14. **Delete Lucene?** - Evaluate if FTS5 + SQLite replace all Lucene use cases

---

## Success Metrics

For each enhanced tool:
- [ ] TDD: Write tests BEFORE implementation
- [ ] Benchmark: Measure before/after performance
- [ ] Dogfood: Use in real workspace
- [ ] Document: Note what worked, what didn't
- [ ] Token efficiency: Verify response size optimized

---

## Notes & Insights

### 2025-10-05: SQLite Tier 1 Fast Path + Connection Pooling

**What we accomplished:**
- ✅ Implemented Tier 1 SQLite fast path for SymbolSearchTool and TraceCallPathTool
- ✅ Added connection pooling throughout SQLiteSymbolService (`Cache=Shared`)
- ✅ Fixed N+1 query problem with new `GetIdentifierCountByNameAsync()` method
- ✅ Achieved 4ms exact symbol lookups (25x faster than Lucene's 100ms)

**Infrastructure improvements:**
1. **GetConnectionString() helper** - Returns pooled connection string with `Cache=Shared`
   - First query: 20ms (connection setup)
   - Subsequent queries: 4ms (connection reused)
   - Applied to all 17 connection creation points in SQLiteSymbolService

2. **GetIdentifierCountByNameAsync()** - Optimized reference counting
   - Old: Fetched ALL identifier objects, counted in memory
   - New: `SELECT COUNT(*) FROM identifiers WHERE name = ?`
   - Saves ~10-15ms per symbol lookup

3. **TryExactMatchAsync() pattern** - Reusable Tier 1 implementation
   - Query SQLite first for exact matches
   - Return immediately on hit with `✅ Tier 1 HIT` log
   - Fall back to Lucene on miss
   - Easy to add to other tools

**Learnings:**
- Connection pooling is CRITICAL for SQLite performance (3-7x improvement)
- N+1 queries are insidious - always use COUNT/EXISTS when you don't need rows
- Tier 1 validation (symbol exists check) prevents wasted work in expensive operations
- Logs should clearly show which tier/path was taken for debugging

**Next targets** (from checklist above):
- GoToDefinitionTool - Exact SQLite lookup (should be trivial now with pattern established)
- GetSymbolsOverviewTool - Eliminate process spawn by querying SQLite directly
- SymbolSearchTool Tier 2 - Prefix matching (`WHERE name LIKE ?`)

### 2025-10-05 (continued): RecentFilesTool Timestamp Bug Fixed

**Bug discovered during tool audit:**
- RecentFilesTool was finding 0 files for any timeframe
- Investigation revealed timestamps were completely wrong (showing year 0001)

**Root cause** (2-part timestamp conversion mismatch):
1. **Query side**: Tool passed `DateTime.Ticks` (~638 trillion) to SQLite which stores `Unix seconds` (~1.7 billion)
   - Result: No files ever matched because cutoff was astronomically large
2. **Display side**: Tool treated Unix seconds as .NET Ticks when creating DateTime
   - Result: Dates showed as "0001-01-01" and "739,528 days ago"

**Fix applied:**
1. Query: Convert cutoff to Unix seconds via `DateTimeOffset.ToUnixTimeSeconds()`
2. Display: Convert Unix seconds back via `DateTimeOffset.FromUnixTimeSeconds().UtcDateTime`

**Result:**
- ✅ Correct file discovery (finds files in specified timeframe)
- ✅ Accurate dates: "2025-10-05T22:51:40Z" instead of "0001-01-01T00:02:55Z"
- ✅ Reasonable "ago" values: "00:01:15" (1 min) instead of "739528.22:48:18" (2 million years!)

**Lesson:** Always verify timestamp unit assumptions when crossing storage boundaries.

