# Memory System Guide

The COA CodeSearch MCP Server includes a sophisticated memory system for preserving architectural knowledge, tracking technical debt, and maintaining context across sessions.

## Quick Start

### 1. Start Every Session
```bash
mcp__codesearch__recall_context "what I'm working on"
```
This loads relevant memories from previous sessions.

### 2. Store Important Discoveries
```bash
# Technical debt
mcp__codesearch__store_memory --type "TechnicalDebt" --content "Null reference in payment processing" --files ["PaymentService.cs"]

# Architectural decisions  
mcp__codesearch__store_memory --type "ArchitecturalDecision" --content "Using JWT for authentication"

# Questions for later
mcp__codesearch__store_memory --type "Question" --content "Why does UserService bypass cache?"
```

### 3. Search Memories
```bash
# Natural language search
mcp__codesearch__search_memories --query "authentication bug"

# Filtered search
mcp__codesearch__search_memories --types ["TechnicalDebt"] --facets {"status": "pending"}
```

## Memory Types

| Type | Purpose | Example |
|------|---------|---------|
| `TechnicalDebt` | Code that needs fixing | "Refactor UserService for testability" |
| `ArchitecturalDecision` | Design choices | "Using repository pattern for data access" |
| `Question` | Unresolved questions | "Why is caching disabled here?" |
| `CodePattern` | Reusable patterns | "Factory pattern for service creation" |
| `BugReport` | Known issues | "Race condition in file watcher" |
| `SecurityRule` | Security requirements | "All APIs must validate JWT" |
| `WorkSession` | Session summaries | "Refactored auth system" |
| `DeferredTask` | Postponed work | "Add unit tests after API complete" |

## Core Tools

### Storing Memories

**Generic storage** (recommended):
```bash
mcp__codesearch__store_memory 
  --type "TechnicalDebt" 
  --content "Memory content"
  --files ["file1.cs", "file2.cs"]
  --fields {"status": "pending", "priority": "high"}
```

**Working memory** (temporary):
```bash
mcp__codesearch__store_temporary_memory 
  --content "User wants to refactor auth" 
  --expiresIn "4h"  # or "end-of-session"
```

### Searching Memories

```bash
# Simple search
mcp__codesearch__search_memories --query "authentication"

# Advanced search with filters
mcp__codesearch__search_memories 
  --query "bug" 
  --types ["TechnicalDebt", "BugReport"]
  --dateRange "last-7-days"
  --facets {"status": "pending"}
  --orderBy "priority"
```

#### Lucene Query Syntax

The memory search uses Apache Lucene's query parser, which has special characters and syntax rules. Understanding these will help you write more effective queries.

**Special Characters**

These characters have special meaning in Lucene and may cause query parsing errors if not handled:
- `:` (colon) - Used for field searches (e.g., `type:TechnicalDebt`)
- `+` `-` `&&` `||` `!` `(` `)` `{` `}` `[` `]` `^` `"` `~` `*` `?` `\` `/`

**Query Examples**

```bash
# ❌ AVOID - Special characters can break parsing
mcp__codesearch__search_memories --query "Session Checkpoint:"

# ✅ BETTER - Simple terms without special characters
mcp__codesearch__search_memories --query "Session Checkpoint"

# ✅ WILDCARDS - Use * for flexible matching
mcp__codesearch__search_memories --query "Session*"

# ✅ PHRASES - Use quotes for exact phrases
mcp__codesearch__search_memories --query "\"authentication bug\""

# ✅ FIELD SEARCH - Search specific fields
mcp__codesearch__search_memories --query "type:WorkSession"

# ✅ BOOLEAN - Combine terms with AND, OR, NOT
mcp__codesearch__search_memories --query "authentication AND (bug OR error)"
```

**Searchable Fields**
- `type` - Memory type (e.g., `type:TechnicalDebt`)
- `content` - Main content text
- `file` - Associated files
- `session_id` - Session identifier
- `is_shared` - true/false for shared memories
- Custom fields added via the `fields` parameter

**Tips**
- Keep queries simple - the built-in synonym expansion will handle variations
- Use wildcards (*) instead of trying to match punctuation exactly
- Field searches are exact matches (no synonym expansion)
- The query parser is case-insensitive

### Managing Memories

**Update memory**:
```bash
mcp__codesearch__update_memory 
  --id "memory-123" 
  --fields {"status": "resolved"}
```

**Link memories**:
```bash
mcp__codesearch__link_memories 
  --sourceId "bug-123" 
  --targetId "fix-456" 
  --relationshipType "resolvedBy"
```

**Archive old memories**:
```bash
mcp__codesearch__archive_memories 
  --type "WorkSession" 
  --daysOld 30
```

## Advanced Features

### Memory Relationships

Create connections between related memories:

```bash
# Link bug to its fix
mcp__codesearch__link_memories --sourceId "bug-123" --targetId "fix-456" --relationshipType "resolvedBy"

# Create parent-child relationship
mcp__codesearch__link_memories --sourceId "epic-001" --targetId "task-002" --relationshipType "parentOf" --bidirectional true

# Find related memories
mcp__codesearch__get_related_memories --memoryId "epic-001" --maxDepth 2
```

**Relationship Types**:
- `relatedTo` - General connection
- `blockedBy`/`blocks` - Dependencies
- `implements`/`implementedBy` - Implementation
- `supersedes`/`supersededBy` - Replacement
- `parentOf`/`childOf` - Hierarchy
- `resolves`/`resolvedBy` - Problem/solution
- `duplicates` - Duplicate tracking

### Git Integration

Link memories to specific commits:

```bash
mcp__codesearch__store_git_commit_memory 
  --sha "abc123" 
  --message "Refactor auth system"
  --description "Implemented JWT authentication"
  --filesChanged ["AuthService.cs", "JwtHandler.cs"]
```

### File Context

Find all memories related to a file:

```bash
mcp__codesearch__get_memories_for_file --filePath "Services/AuthService.cs"
```

### Persistent Checklists

Track tasks across sessions:

```bash
# Create checklist
mcp__codesearch__create_checklist --title "Implement Auth System" --isShared true

# Add items (single item)
mcp__codesearch__add_checklist_items --checklistId "abc123" --items [{"itemText": "Create login endpoint"}]

# Add multiple items at once
mcp__codesearch__add_checklist_items --checklistId "abc123" --items [
  {"itemText": "Create login endpoint", "notes": "Use JWT auth"},
  {"itemText": "Add validation", "relatedFiles": ["/api/validators.cs"]},
  {"itemText": "Write unit tests"}
]

# Mark complete
mcp__codesearch__toggle_checklist_item --itemId "item456"

# View progress
mcp__codesearch__view_checklist --checklistId "abc123" --exportAsMarkdown true
```

### Memory Summarization

Compress old memories:

```bash
mcp__codesearch__summarize_memories 
  --type "WorkSession" 
  --daysOld 30 
  --batchSize 10
```

## Backup and Restore

### Backup to JSON
```bash
# Backup project memories (default)
mcp__codesearch__backup_memories

# Include local/personal memories
mcp__codesearch__backup_memories --includeLocal true

# Specific types only
mcp__codesearch__backup_memories --scopes ["ArchitecturalDecision", "CodePattern"]
```

Creates timestamped JSON file in `.codesearch/backups/`

### Restore from JSON
```bash
# Auto-finds most recent backup
mcp__codesearch__restore_memories

# Restore specific types
mcp__codesearch__restore_memories --scopes ["TechnicalDebt"]
```

## Best Practices

1. **Start sessions with context**: Always use `recall_context` first
2. **Be specific**: Include file paths and detailed descriptions
3. **Use appropriate types**: Choose the right memory type for clarity
4. **Link related items**: Connect bugs to fixes, tasks to epics
5. **Archive regularly**: Keep active memories relevant
6. **Backup important decisions**: Commit memory backups to version control
7. **Review pending items**: Search for unresolved technical debt regularly

## Memory Fields

All memories support custom fields:

```json
{
  "status": "pending|in-progress|resolved|blocked",
  "priority": "critical|high|medium|low", 
  "assignee": "username",
  "dueDate": "2024-12-31",
  "tags": ["performance", "security"],
  "category": "backend|frontend|infrastructure",
  "effort": "small|medium|large",
  "risk": "low|medium|high"
}
```

## Smart Search Features

The search tool includes AI-powered features:

- **Natural language understanding**: "authentication bug" finds auth-related issues
- **Synonym expansion**: "bug" matches "defect", "issue", "error"
- **Code term extraction**: Finds camelCase, PascalCase, snake_case
- **Boost recent/frequent**: Prioritizes recently accessed memories
- **Context awareness**: Considers current file when searching

## Performance Tips

- Index before searching: Use `index_workspace` first
- Batch operations: Search once, filter results in memory
- Use facets: Filter by fields for faster results
- Archive old data: Keep active set small
- Leverage relationships: Navigate connections instead of searching