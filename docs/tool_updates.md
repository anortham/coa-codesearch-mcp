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

#### ☐ **SymbolSearchTool** (`SymbolSearchTool.cs`)
- **Current**: Lucene CamelCase tokenization only
- **Potential**: Multi-tier search
  - Tier 1: SQLite exact match (`WHERE name = ?`)
  - Tier 2: SQLite prefix match (`WHERE name LIKE ? || '%'`)
  - Tier 3: Lucene fuzzy (keep existing)
  - Tier 4: Semantic via embeddings (NEW!)
- **Impact**: Exact matches always first, semantic "find auth code" queries
- **Notes**:

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

#### ☐ **GoToDefinitionTool** (`GoToDefinitionTool.cs`)
- **Current**: Lucene fuzzy matching (~20ms, some false positives)
- **Potential**:
  - **PRIMARY**: SQLite exact lookup (`SELECT * FROM symbols WHERE name = ?`)
  - Target: <1ms response time
  - 100% accuracy for exact symbol names
- **Impact**: 20x faster, zero false positives
- **Priority**: ⭐⭐⭐ HIGH - Proves SQLite value immediately
- **Notes**:

#### ☐ **FindReferencesTool** (`FindReferencesTool.cs`)
- **Current**: Uses `ReferenceResolverService` → SQLite identifiers (already optimized!)
- **Potential**:
  - ✅ Already has fast-path via `GetIdentifiersByNameAsync()`
  - Add semantic references via embeddings (find semantically similar usages)
  - Cross-language references via semantic search
- **Impact**: Already LSP-quality, embeddings add cross-language intelligence
- **Notes**: Check if current implementation is optimal

#### ☐ **GetSymbolsOverviewTool** (`GetSymbolsOverviewTool.cs`)
- **Current**: Spawns julie-extract process (~50ms overhead)
- **Potential**:
  - **PRIMARY**: SQLite query (`SELECT * FROM symbols WHERE file_id = ?`)
  - Target: <5ms (no process spawn)
  - Always up-to-date (no extraction needed)
- **Impact**: 10x+ speed improvement, zero process overhead
- **Priority**: ⭐⭐⭐ HIGH - Easy win
- **Notes**:

#### ☐ **TraceCallPathTool** (`TraceCallPathTool.cs`)
- **Current**: Text-based call path tracing (misses indirect calls)
- **Potential**:
  - SQLite recursive CTEs on relationships table
  - Semantic bridging for cross-language calls
  - Build complete call hierarchies
- **Impact**: True LSP-quality call graphs
- **Notes**:

---

### 📁 File Discovery Tools

#### ☐ **FileSearchTool** (`FileSearchTool.cs`)
- **Current**: File system traversal with patterns
- **Potential**:
  - SQLite query on files table for instant results
  - No I/O, just database query
- **Impact**: Faster file discovery
- **Notes**: May not be worth changing if FS traversal is fast enough

#### ☐ **RecentFilesTool** (`RecentFilesTool.cs`)
- **Current**: File system stats + sorting
- **Potential**:
  - SQLite query: `SELECT * FROM files ORDER BY last_modified DESC LIMIT ?`
  - Instant results, no FS stats needed
- **Impact**: Faster, always accurate
- **Notes**:

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

(Add learnings as you work through tools)

