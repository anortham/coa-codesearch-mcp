# COA CodeSearch MCP - Memory System Architecture

## Overview

The COA CodeSearch MCP Server implements a sophisticated **Advanced Memory Intelligence** system designed for AI agents. This document describes the actual implemented architecture after Phase 3 completion.

## Core Architecture

### Memory Storage
- **Primary Index**: Lucene-based search index with custom analyzers
- **Memory Types**: Flexible schema supporting custom fields and metadata
- **Storage Format**: JSON documents with full-text search capabilities
- **Path Management**: Centralized through `IPathResolutionService`

### Key Services

#### FlexibleMemoryService
- **Purpose**: Core memory storage and retrieval
- **Features**: Custom fields, faceted search, temporal scoring
- **Dependencies**: LuceneIndexService, IScoringService, IMemoryValidationService

#### Scoring & Relevance (Phase 3)
- **IScoringService**: Temporal scoring with time-decay algorithms
- **ScoringService**: Implements recency boost and access frequency scoring
- **Temporal Factors**: Configurable decay rates for memory aging

#### Semantic Search Layer (Phase 3)
- **IEmbeddingService**: Vector embeddings for semantic similarity
- **InMemoryVectorIndex**: Fast cosine similarity search
- **HybridMemorySearch**: Combines Lucene text + semantic vector search
- **SemanticMemoryIndex**: Manages memory embedding lifecycle

#### Memory Quality Validation (Phase 3)
- **IMemoryQualityValidator**: Pluggable validation framework
- **Validators**:
  - `CompletenessValidator`: Checks for required fields and content depth
  - `RelevanceValidator`: Validates relevance and context appropriateness  
  - `ConsistencyValidator`: Ensures formatting and structural consistency
- **Quality Scoring**: 0-100 score with actionable improvement suggestions

#### Unified Memory Interface (Phase 3)
- **UnifiedMemoryService**: Natural language command processing
- **Intent Detection**: Keyword-based routing to appropriate operations
- **Supported Intents**: SAVE, FIND, CONNECT, EXPLORE, SUGGEST, MANAGE
- **AI Integration**: Single tool replacing multiple memory operations

## Memory Types

### Core Types
- **TechnicalDebt**: Code issues requiring future attention
- **ArchitecturalDecision**: Design choices and rationale
- **CodePattern**: Reusable code patterns and best practices
- **ProjectInsight**: High-level project observations
- **SecurityRule**: Security guidelines and requirements

### Session Types  
- **WorkSession**: Temporary session-specific notes
- **LocalInsight**: Developer-specific observations
- **TemporaryMemory**: Auto-expiring reminders and notes

### Specialized Types
- **Question**: Unanswered questions for later research
- **GitCommitMemory**: Insights tied to specific commits
- **ChecklistItem**: Task tracking with progress management

## Search Capabilities

### Text Search
- **Lucene Integration**: Full-text search with custom analyzers
- **Query Types**: Phrase, wildcard, fuzzy, boolean combinations
- **Faceted Search**: Filter by type, date ranges, custom fields
- **Progressive Disclosure**: Summary mode with detail expansion

### Semantic Search
- **Vector Similarity**: Cosine similarity on embeddings
- **Hybrid Approach**: Combines text relevance + semantic similarity
- **Embedding Caching**: Efficient vector storage and retrieval

### Temporal Scoring
- **Time Decay**: Recent memories score higher
- **Access Frequency**: Frequently accessed memories boosted
- **Configurable Weights**: Adjust recency vs. relevance balance

## Quality Management

### Validation Pipeline
1. **Completeness Check**: Required fields, content depth
2. **Relevance Assessment**: Context appropriateness, duplicate detection
3. **Consistency Validation**: Format standards, structural requirements
4. **Quality Scoring**: Aggregated 0-100 score with breakdown

### Improvement Suggestions
- **Actionable Recommendations**: Specific steps to improve quality
- **Template Suggestions**: Recommend better memory templates
- **Field Enhancement**: Suggest additional metadata fields

## Caching Strategy

### Simple, Effective Caches (Retained)
- **QueryCacheService**: Caches parsed Lucene queries for performance
- **DetailRequestCache**: Caches large search results for progressive disclosure
- **IMemoryCache**: .NET built-in caching for frequently accessed data

### Complex Caching (Removed)
- ~~Multi-level L1/L2 cache~~ - Removed as over-engineered
- ~~Cache invalidation strategies~~ - Avoided complexity
- ~~File change invalidation~~ - Unnecessary with current architecture

## Tool Integration

### MCP Tools
All memory capabilities exposed as MCP tools for AI agent consumption:

- **unified_memory**: Natural language memory operations
- **search_memories**: Advanced search with faceting and temporal scoring
- **store_memory**: Store memories with quality validation
- **semantic_search**: Pure semantic similarity search
- **hybrid_search**: Combined text + semantic search
- **memory_quality_assessment**: Quality validation and suggestions

### Progressive Disclosure
- **Summary Mode**: Token-efficient overviews with key insights
- **Detail Requests**: Expand specific sections on demand
- **Hotspot Detection**: Identify high-concentration areas
- **Action Suggestions**: AI-friendly next steps

## Performance Characteristics

### Target Metrics
- **Memory Search**: < 50ms for typical queries
- **Quality Validation**: < 100ms per memory
- **Semantic Search**: < 200ms including embedding generation
- **Memory Storage**: < 10ms for typical memory objects

### Scalability
- **Memory Count**: Tested with 10,000+ memories
- **Concurrent Access**: Thread-safe operations throughout
- **Index Size**: Efficient storage with minimal overhead
- **Memory Usage**: < 200MB baseline, scales with data

## Configuration

### Key Settings
```json
{
  "MemoryLimits": {
    "MaxMemoriesPerQuery": 1000,
    "MaxCustomFields": 50
  },
  "Scoring": {
    "TemporalDecayFactor": 0.1,
    "RecencyBoostFactor": 1.5,
    "AccessFrequencyWeight": 0.3
  },
  "Embedding": {
    "ModelName": "all-MiniLM-L6-v2",
    "MaxTokens": 512,
    "CacheEmbeddings": true
  },
  "Quality": {
    "MinimumScore": 60,
    "RequiredFields": ["content", "memoryType"],
    "EnableSuggestions": true
  }
}
```

## Integration Points

### File System Integration
- **FileWatcherService**: Monitors code changes
- **Auto-reindexing**: Keeps index synchronized
- **Path Resolution**: Centralized path management

### Git Integration  
- **Commit Memories**: Link insights to specific commits
- **Branch Awareness**: Context-aware memory relevance
- **Change Detection**: Invalidate relevant memories on code changes

### AI Agent Integration
- **Natural Language**: Unified interface accepts plain English commands
- **Context Awareness**: Understands current work context
- **Proactive Suggestions**: Recommends relevant memories and actions
- **Error Recovery**: Helpful guidance when operations fail

## Security & Privacy

### Data Protection
- **Local Storage**: All memories stored locally in `.codesearch/`
- **No External Calls**: No data sent to external services
- **Access Control**: File system permissions control access
- **Encryption**: Sensitive data can be encrypted at rest

### Memory Scoping
- **Project Memories**: Shared across team (committed to git)
- **Local Memories**: Developer-specific (git-ignored)
- **Session Memories**: Temporary (auto-expire)

## Development Patterns

### Adding New Memory Types
1. Define type string constant
2. Add to validation rules if needed
3. Create template if complex structure required
4. Update search facets if special handling needed

### Extending Quality Validation
1. Implement `IQualityValidator` interface
2. Register in DI container
3. Add to quality validation pipeline
4. Configure scoring weights

### Custom Scoring Factors
1. Implement `IScoringFactor` interface
2. Register with `IScoringService`
3. Configure weight and priority
4. Test impact on search relevance

## Future Considerations

### Potential Enhancements
- **Multi-project Support**: Cross-project memory sharing
- **Advanced Analytics**: Memory usage patterns and insights
- **Integration APIs**: External tool integration points
- **Backup/Restore**: Memory export/import capabilities

### Performance Optimizations
- **Index Sharding**: Split large indexes for better performance
- **Streaming Results**: Handle very large result sets
- **Batch Operations**: Bulk memory operations
- **Precomputed Aggregations**: Cache expensive calculations

---

*This document reflects the actual implemented architecture as of Phase 3 completion. For usage instructions, see [MEMORY_SYSTEM.md](MEMORY_SYSTEM.md).*