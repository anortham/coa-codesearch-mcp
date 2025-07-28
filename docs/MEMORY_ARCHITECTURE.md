# Memory System Architecture

## Overview
The COA CodeSearch MCP memory system is a sophisticated knowledge management layer built on top of Lucene, designed specifically for AI agents to maintain context and learn from interactions.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────┐
│                   MCP Tools Layer                    │
│  (store_memory, search_memories, recall_context...)  │
└─────────────────────┬───────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────┐
│              FlexibleMemoryService                   │
│         (Core memory operations & search)            │
└─────────┬───────────────────────┬───────────────────┘
          │                       │
┌─────────▼──────────┐  ┌────────▼────────────────────┐
│ QueryExpansionSvc  │  │   MemoryLifecycleSvc         │
│ (Query enhancement)│  │ (Auto-surfacing & lifecycle) │
└────────────────────┘  └─────────────────────────────┘
          │                       │
┌─────────▼───────────────────────▼───────────────────┐
│              LuceneIndexService                      │
│         (Lucene 4.8.0 index management)              │
└─────────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────┐
│          .codesearch/memory/index/                   │
│            (Physical Lucene indexes)                 │
└─────────────────────────────────────────────────────┘
```

## Core Components

### 1. FlexibleMemoryService
**Location**: `Services/FlexibleMemoryService.cs`
**Purpose**: Central service for all memory operations
**Key Responsibilities**:
- Memory CRUD operations
- Lucene index management via SearcherManager
- Query building and execution
- Custom field storage as JSON
- Relationship management

**Key Methods**:
```csharp
- StoreMemoryAsync(): Create/update memories
- SearchMemoriesAsync(): Complex memory search
- GetMemoryAsync(): Retrieve by ID
- UpdateMemoryAsync(): Partial updates
- LinkMemoriesAsync(): Create relationships
```

### 2. LuceneIndexService
**Location**: `Services/LuceneIndexService.cs`
**Purpose**: Low-level Lucene operations
**Key Features**:
- FSDirectory management
- IndexWriter lifecycle
- SearcherManager for NRT search
- Index locking and corruption handling
- Analyzer configuration

### 3. QueryExpansionService
**Location**: `Services/QueryExpansionService.cs`
**Purpose**: Enhance search queries for better recall
**Features**:
- Synonym expansion
- Domain-specific term mapping
- Multi-term query handling
- Context-aware expansions

**Example Expansions**:
- "auth" → "authentication", "authorization", "login"
- "bug" → "defect", "issue", "problem", "error"

### 4. MemoryLifecycleService
**Location**: `Services/MemoryLifecycleService.cs`
**Purpose**: Intelligent memory management
**Features**:
- Auto-resolution of technical debt
- Context-aware memory surfacing
- File change monitoring
- Confidence-based recommendations

## Memory Data Model

### Core Fields (Indexed)
```csharp
doc.Add(new StringField("Id", memory.Id, Field.Store.YES));
doc.Add(new StringField("Type", memory.Type, Field.Store.YES));
doc.Add(new TextField("Content", memory.Content, Field.Store.YES));
doc.Add(new StringField("Hash", memory.Hash, Field.Store.YES));
doc.Add(new StringField("Scope", memory.Scope.ToString(), Field.Store.YES));
```

### Custom Fields (JSON)
```json
{
  "importance": "critical",
  "status": "pending",
  "assignee": "backend-team",
  "customField": "customValue"
}
```

### Relationships
```json
{
  "relationships": [
    {
      "targetId": "mem_123",
      "type": "implements",
      "bidirectional": true
    }
  ]
}
```

## Search Flow

### 1. Query Reception
```
User Query → MCP Tool → FlexibleMemoryService
```

### 2. Query Enhancement
```
Original Query → QueryExpansionService → Expanded Terms
"auth bug" → ["auth", "authentication", "bug", "defect", "issue"]
```

### 3. Lucene Query Building
```csharp
BooleanQuery.Builder queryBuilder = new BooleanQuery.Builder();
// Add term queries for each field with boosts
queryBuilder.Add(new TermQuery(new Term("Content", term)), Occur.SHOULD);
```

### 4. Search Execution
```
Lucene Query → SearcherManager → TopDocs → Memory Results
```

### 5. Post-Processing
```
Raw Results → Score Filtering → Metadata Enrichment → Final Results
```

## Storage Structure

### File System Layout
```
.codesearch/
├── memory/
│   ├── index/
│   │   ├── _0.cfe          # Lucene compound file
│   │   ├── _0.cfs          # Lucene compound segments
│   │   ├── _0.si           # Segment info
│   │   ├── segments_1      # Segments file
│   │   └── write.lock      # Index write lock
│   └── backups/
│       └── memory_backup_YYYYMMDD_HHMMSS.json
```

### Index Document Structure
Each memory is stored as a Lucene document with:
- **Indexed fields**: For searching
- **Stored fields**: For retrieval
- **Term vectors**: Not currently used
- **Payloads**: Not currently used

## Memory Types

### Project-Level (Shared)
- **ArchitecturalDecision**: Design choices
- **TechnicalDebt**: Known issues
- **CodePattern**: Reusable patterns
- **SecurityRule**: Security requirements
- **ProjectInsight**: General knowledge

### Session-Level (Temporary)
- **WorkSession**: Current session context
- **LocalInsight**: Developer-specific knowledge

## Advanced Features

### 1. Memory Relationships
- Bidirectional linking
- Multiple relationship types
- Graph traversal up to N depth
- Cycle detection

### 2. Lifecycle Management
- Auto-archiving old memories
- Temporary memory expiration
- File-based memory updates
- Confidence-based surfacing

### 3. Templates
Pre-defined structures for consistent memory creation:
- Security findings
- Performance issues
- Architectural decisions
- Code reviews

### 4. Backup/Restore
- JSON export for version control
- Selective backup by scope
- Cross-machine memory transfer
- Incremental backups

## Performance Characteristics

### Index Performance
- **Write**: ~10-50ms per memory
- **Search**: ~10-100ms depending on query complexity
- **Update**: ~20-60ms (delete + reindex)

### Scalability
- Tested with 10,000+ memories
- Linear search performance degradation
- No built-in sharding (single index)

### Memory Usage
- Base index: ~10MB
- Per memory: ~1-5KB indexed
- SearcherManager caching: ~50MB

## Integration Points

### 1. With Search Tools
- Shared Lucene infrastructure
- Common analyzer configuration
- Unified path resolution

### 2. With File System
- File path tracking
- Change monitoring
- Relative path normalization

### 3. With AI Agents
- Context loading on startup
- Progressive memory discovery
- Natural language queries

## Known Limitations

### 1. Search Limitations
- No semantic search (exact/fuzzy terms only)
- Limited to Lucene 4.8 features
- No query learning/optimization

### 2. Scale Limitations
- Single-node only
- No distributed search
- Memory-bound for large indexes

### 3. Feature Gaps
- No automatic deduplication
- Limited merge capabilities
- No versioning/history

## Future Considerations

### 1. Lucene Upgrades
- Current: 4.8.0-beta00017
- Latest: 9.x with significant improvements
- Migration path needed

### 2. Advanced Features
- Vector search for semantic similarity
- Graph database integration
- Machine learning for relevance

### 3. Performance Optimizations
- Index partitioning
- Caching strategies
- Async operations