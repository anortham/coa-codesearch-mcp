# Memory System Enhancement Plan

## Current Pain Points

1. **Rigid categorization** - I often struggle to decide if something is an "Architectural Decision" vs a "Code Pattern" vs something else entirely
2. **No cross-referencing** - Can't link related memories or create memory chains
3. **Limited search** - The scopeFilter doesn't work reliably, and I can't do complex queries
4. **No status tracking** - For actionable items, I can't mark them as "done", "in progress", "blocked"
5. **No temporal navigation** - Can't ask "what did we do last week" or "show me all December memories"

## What Would Make It More Useful

### 1. Flexible Memory Schema with Core + Extended Fields

```csharp
public class Memory {
    // Core fields (indexed, required)
    public string Id { get; set; }
    public string Type { get; set; }  // Can be anything, not just predefined
    public string Content { get; set; }
    public DateTime Created { get; set; }

    // Extended fields (dynamic, indexed)
    public Dictionary<string, object> Fields { get; set; }
    // Examples:
    // - Status: "pending", "in-progress", "done", "blocked"
    // - Priority: "critical", "high", "medium", "low"
    // - DueDate: DateTime
    // - RelatedTo: ["memory-id-1", "memory-id-2"]
    // - Tags: ["performance", "security", "refactor"]
    // - Context: "working on TypeScript integration"
}
```

### 2. Lucene Features We Should Leverage

**Faceted Search** - Let me filter by multiple dimensions:
```bash
recall_context --query "typescript"
              --facets "type:TechnicalDebt,status:pending"
              --date-range "last-7-days"
```

**Boosted Fields** - Make certain fields more important:
- Boost recent memories
- Boost memories I've accessed frequently
- Boost memories with high priority

**More Like This** - Find related memories:
```bash
find_similar_memories --id "memory-123"
```

**Term Highlighting** - Show me exactly what matched:
```json
{
  "content": "Fix the <em>TypeScript</em> server hanging issue",
  "matched_terms": ["typescript", "server"]
}
```

**Custom Analyzers** - Better search understanding:
- Stem words: "running" matches "run", "runs"
- Synonyms: "bug" matches "defect", "issue"
- Code-aware: "IUserService" matches "UserService"

### 3. New Memory Types I'd Find Useful

- **TechnicalDebt** - Things that need fixing but aren't urgent
- **DeferredTask** - Things explicitly postponed
- **Question** - Unresolved questions or unknowns
- **Assumption** - Things we're assuming that need validation
- **Experiment** - Things to try with expected outcomes
- **Learning** - Things I've learned about the codebase behavior
- **Blocker** - Things preventing progress
- **Idea** - Future enhancements or possibilities

### 4. Memory Relationships

```bash
# Link memories together
remember_linked --type "TechnicalDebt"
               --content "Refactor user service for better testing"
               --related-to "decision-123"  # Links to an architectural decision
               --blocked-by "debt-456"      # Links to another tech debt item
```

### 5. Smart Memory Queries

```bash
# Temporal queries
recall_context --when "last-week"
recall_context --when "2024-12-15 to 2024-12-20"

# Status queries
recall_context --type "TechnicalDebt" --status "pending" --order-by "priority"

# Relationship queries
recall_context --related-to "memory-123" --depth 2  # Find memories up to 2 links away

# Context-aware queries
recall_context --context "current-file:UserService.cs"  # Memories relevant to what I'm looking at
```

### 6. Memory Lifecycle Management

```bash
# Update memory status
update_memory --id "debt-123" --status "in-progress" --assigned-to "current-session"

# Archive old memories
archive_memories --type "WorkSession" --older-than "30-days"

# Memory templates
create_memory_from_template --template "code-review" --file "UserService.cs"
```

### 7. AI-Specific Features

**Working Memory** - Temporary memories for current session:
```bash
remember_working --content "User wants to refactor auth system"
                --expires "end-of-session"
```

**Memory Summarization** - Compress old memories:
```bash
summarize_memories --type "WorkSession" --older-than "7-days"
```

**Smart Recall** - Let me describe what I need:
```bash
recall_context "I need to find that security issue we discussed about user tokens"
# AI-powered search that understands intent, not just keywords
```

### 8. Integration Features

**Git Integration:**
```bash
remember_commit --sha "abc123" --description "Major refactor of auth system"
```

**File Context:**
```bash
memories_for_file --path "UserService.cs"  # All memories that mention this file
```

**Memory Dashboard:**
```bash
memory_dashboard  # Shows statistics, pending items, recent activity
```

## Key Insight

Memories aren't just static notes - they're living entities that need to be tracked, updated, linked, and queried in sophisticated ways. By leveraging Lucene's full power and adding these features, the memory system would become a true "second brain" that helps maintain context and continuity across sessions.

## Implementation Priority

1. **Phase 1: Core Schema Flexibility**
   - Implement flexible schema with extended fields
   - Add new memory types
   - Update storage and retrieval logic

2. **Phase 2: Advanced Search**
   - Implement faceted search
   - Add temporal queries
   - Enable More Like This functionality

3. **Phase 3: Relationships & Lifecycle**
   - Add memory linking
   - Implement status tracking
   - Create update/archive capabilities

4. **Phase 4: AI Features**
   - Add working memory
   - Implement smart recall
   - Create memory summarization

5. **Phase 5: Integrations**
   - Git integration
   - File context awareness
   - Memory dashboard

## Implementation Progress Checklist

### Phase 1: Core Schema Flexibility ✅ COMPLETED
- [x] Implement flexible schema with extended fields (`FlexibleMemoryEntry` with `Fields` dictionary)
- [x] Add new memory types (TechnicalDebt, Question, DeferredTask, etc.)
- [x] Update storage and retrieval logic (`FlexibleMemoryService`)
- [x] Create specialized storage tools (`flexible_store_technical_debt`, `flexible_store_question`, etc.)
- [x] Implement `flexible_store_memory` for arbitrary memory types
- [x] Add `flexible_update_memory` for modifying existing memories
- [x] Add `flexible_mark_memory_resolved` for status updates

### Phase 2: Advanced Search ✅ COMPLETED
- [x] Implement faceted search with `flexible_search_memories`
- [x] Add temporal queries (date ranges like "last-7-days")
- [x] Enable "More Like This" functionality with `flexible_find_similar_memories`
- [x] Add query boosting (boost recent, boost frequent)
- [x] Implement smart insights generation
- [x] Add facet counts in search results
- [x] Support custom field filtering

### Phase 3: Relationships & Lifecycle ✅ COMPLETED
- [x] Add memory linking with `flexible_link_memories`
- [x] Implement relationship traversal with `flexible_get_related_memories`
- [x] Add `flexible_unlink_memories` for removing relationships
- [x] Implement status tracking (pending, in-progress, done, etc.)
- [x] Create archive capabilities with `flexible_archive_memories`
- [x] Add memory lifecycle fields (created, modified, accessed, etc.)

### Phase 4: AI Features 🚧 PARTIALLY COMPLETED
- [x] Add working memory with `flexible_store_working_memory`
- [x] Implement memory expiration (end-of-session, time-based)
- [x] Create memory summarization with `flexible_summarize_memories`
- [x] Add smart recall with natural language understanding in search
- [ ] Implement semantic search with embeddings
- [ ] Add memory templates/snippets
- [ ] Create context-aware memory suggestions

### Phase 5: Integrations ❌ NOT STARTED
- [ ] Git integration (link memories to commits)
- [ ] File context awareness (memories_for_file)
- [ ] Memory dashboard/statistics
- [ ] Export/import functionality
- [ ] Memory analytics and insights
- [ ] Integration with other tools (PRs, issues, etc.)

### Additional Features Implemented
- [x] SQLite backup/restore for version control (`backup_memories_to_sqlite`, `restore_memories_from_sqlite`)
- [x] Diagnostic tools (`diagnose_memory_index`)
- [x] Session tracking and work history
- [x] Custom analyzers for better search

### Known Issues/TODOs
- [ ] Fix date range filtering in archive operations (NumericRangeQuery issue)
- [ ] Implement memory templates for common patterns
- [ ] Add bulk operations for memory management
- [ ] Improve performance for large memory stores
- [ ] Add memory export formats (JSON, Markdown, etc.)
- [ ] Implement memory versioning/history
- [x] **Implement persistent checklist feature (Option 3)** ✅ COMPLETED
  - Created dedicated checklist tools for cross-session task tracking
  - Support hierarchical checklists with parent-child relationships
  - Includes progress tracking and markdown export
  - Tools implemented:
    - `create_checklist` - Create persistent checklists (personal or shared)
    - `add_checklist_item` - Add items with notes and file references
    - `toggle_checklist_item` - Mark items complete/incomplete with tracking
    - `update_checklist_item` - Update item text, notes, or custom fields
    - `view_checklist` - View with progress and optional markdown export
    - `list_checklists` - List all checklists with filtering options
  - Checklist items are automatically linked to parent checklists
  - Complements in-memory TodoWrite with persistent cross-session storage