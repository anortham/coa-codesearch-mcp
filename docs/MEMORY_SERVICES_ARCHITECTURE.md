# Memory Services Architecture

## Overview

The COA CodeSearch MCP memory system consists of multiple interconnected services that work together to provide intelligent memory management, semantic search, quality validation, and automatic lifecycle management. This document provides a comprehensive overview of all memory-related services, their purposes, interactions, and the overall architecture.

## Core Services

### 1. FlexibleMemoryService
**Type**: Core Service  
**Purpose**: Primary memory storage and retrieval service  
**Key Responsibilities**:
- Stores memories with flexible JSON fields
- Manages Lucene-based text indexing for fast search
- Handles CRUD operations for memories
- Maintains both project-wide and session-local memories
- Publishes memory events to the event bus

**Key Features**:
- Flexible schema with custom fields
- Full-text search with Lucene
- Faceted search support
- Memory access tracking
- Circuit breaker protection

### 2. UnifiedMemoryService
**Type**: Orchestration Service  
**Purpose**: Natural language interface for memory operations  
**Key Responsibilities**:
- Intent detection (SAVE, FIND, CONNECT, EXPLORE, SUGGEST, MANAGE)
- Routes commands to appropriate tools and services
- Integrates with multiple memory tools
- Handles memory relationships and connections

**Dependencies**:
- FlexibleMemoryService
- MemoryLinkingTools
- ChecklistTools
- SemanticSearchTool
- HybridSearchTool

### 3. ClaudeMemoryService
**Type**: Context Service  
**Purpose**: Manages claude-specific context and preferences  
**Key Responsibilities**:
- Stores user preferences and context
- Manages session-specific information
- Provides context recall for AI interactions

## Background Services

### 1. SemanticIndexingSubscriber
**Type**: IHostedService  
**Purpose**: Maintains semantic search index  
**Key Responsibilities**:
- Subscribes to memory storage events
- Generates embeddings for new memories
- Updates vector index for semantic search
- Indexes existing memories on startup

**Event Flow**:
```
Memory Created → MemoryStorageEvent → SemanticIndexingSubscriber → Embedding Generation → Vector Index Update
```

### 2. MemoryLifecycleService
**Type**: BackgroundService + IFileChangeSubscriber  
**Purpose**: Manages memory lifecycle based on file changes  
**Key Responsibilities**:
- Monitors file changes that affect memories
- Automatically resolves or updates memories
- Manages memory confidence levels
- Handles stale memory detection
- Periodic cleanup of expired entries

**Key Features**:
- Confidence-based resolution
- Throttled updates to prevent spam
- File change correlation
- Automatic memory archival

### 3. MemoryPressureService
**Type**: Monitoring Service  
**Purpose**: Monitors and manages memory pressure  
**Key Responsibilities**:
- Tracks system memory usage
- Implements memory limits and thresholds
- Triggers cleanup when limits exceeded
- Provides memory usage metrics

## Quality and Validation Services

### 1. MemoryQualityValidationService
**Type**: Validation Service  
**Purpose**: Ensures memory quality standards  
**Key Responsibilities**:
- Validates memory completeness
- Checks relevance and consistency
- Provides quality scores and suggestions
- Integrates multiple validators

**Validators**:
- CompletenessValidator
- RelevanceValidator
- ConsistencyValidator

### 2. MemoryValidationService
**Type**: Input Validation Service  
**Purpose**: Validates memory data before storage  
**Key Responsibilities**:
- Schema validation
- Field type checking
- Content length limits
- Required field validation

## Supporting Services

### 1. MemoryFacetingService
**Type**: Search Enhancement Service  
**Purpose**: Provides faceted search capabilities  
**Key Responsibilities**:
- Generates facet counts for search results
- Handles category aggregation
- Provides filter suggestions
- Caches facet results

### 2. JsonMemoryBackupService
**Type**: Backup/Restore Service  
**Purpose**: Memory backup and restoration  
**Key Responsibilities**:
- Exports memories to JSON format
- Restores memories from backups
- Handles version compatibility
- Manages backup retention

### 3. MemoryEventPublisher
**Type**: Event Bus Service  
**Purpose**: Publishes memory-related events  
**Key Responsibilities**:
- Manages event subscriptions
- Publishes storage events
- Handles event distribution
- Provides async event processing

**Event Types**:
- MemoryStorageEvent (Create, Update, Delete)
- MemoryAccessEvent (Read, Search)
- MemoryLifecycleEvent (Archive, Restore)

## Search Services

### 1. SemanticMemoryIndex
**Type**: Vector Search Service  
**Purpose**: Semantic/conceptual search  
**Key Responsibilities**:
- Manages embeddings and vector index
- Performs similarity searches
- Handles embedding generation
- Provides semantic relevance scoring

### 2. HybridMemorySearch
**Type**: Combined Search Service  
**Purpose**: Combines text and semantic search  
**Key Responsibilities**:
- Merges Lucene and vector search results
- Applies configurable weighting
- Handles result deduplication
- Provides unified scoring

## Event Flow and Interactions

### Memory Creation Flow
```
1. User Command → UnifiedMemoryService
2. Intent Detection → SAVE
3. Create Memory → FlexibleMemoryService
4. Index in Lucene → Full-text search ready
5. Publish Event → MemoryEventPublisher
6. Generate Embedding → SemanticIndexingSubscriber
7. Update Vector Index → Semantic search ready
8. Track Quality → MemoryQualityValidationService
```

### Memory Search Flow
```
1. User Query → UnifiedMemoryService
2. Intent Detection → FIND
3. Route to Search → HybridMemorySearch
4. Parallel Search:
   - Lucene Search → Text matching
   - Vector Search → Semantic matching
5. Merge Results → Weighted scoring
6. Return Results → With facets and suggestions
```

### Memory Lifecycle Flow
```
1. File Change → FileWatcherService
2. Notify Subscribers → MemoryLifecycleService
3. Check Affected Memories → Confidence calculation
4. Update/Archive → Based on confidence
5. Publish Event → Lifecycle changes
```

## Configuration and Limits

### Memory Limits (MemoryLimitsConfiguration)
- **MaxProjectMemories**: 10,000 (default)
- **MaxSessionMemories**: 1,000 (default)
- **MaxMemorySizeMB**: 500 (default)
- **CleanupThresholdPercentage**: 90%
- **MaxFileReferencesPerMemory**: 50

### Lifecycle Configuration
- **EnableAutoResolution**: true/false
- **StalenessThresholdDays**: 30
- **ConfidenceDecayRate**: 0.1
- **MinConfidenceForAction**: 0.7

### Search Configuration
- **MaxSearchResults**: 100
- **EnableSemanticSearch**: true
- **SemanticSearchThreshold**: 0.3
- **HybridSearchWeights**: Lucene=0.6, Semantic=0.4

## Testing and Monitoring

### Current Test Coverage
- ✅ FlexibleMemoryService: Unit tests
- ✅ MemoryLifecycleService: Unit tests
- ✅ Memory validation: Unit tests
- ⚠️ SemanticIndexingSubscriber: Limited tests
- ⚠️ Event flow: Integration tests needed
- ❌ Background service interactions: No tests
- ❌ Memory pressure scenarios: No tests

### Key Metrics to Monitor
1. **Memory Storage**
   - Total memories stored
   - Storage rate (memories/minute)
   - Average memory size
   - Index size growth

2. **Search Performance**
   - Query response time
   - Cache hit rate
   - Semantic vs text search usage
   - Result relevance scores

3. **Background Processing**
   - Event processing lag
   - Embedding generation time
   - Lifecycle update frequency
   - Memory pressure events

4. **Quality Metrics**
   - Average quality scores
   - Validation failure rate
   - Memory staleness percentage
   - Relationship density

## Common Issues and Troubleshooting

### 1. "Index is locked" Errors
**Cause**: Stale Lucene write.lock files  
**Solution**: LuceneLifecycleService should clean these automatically

### 2. Semantic Search Not Working
**Cause**: SemanticIndexingSubscriber not running or embeddings not generated  
**Check**: Logs for "semantic indexing subscriber started"

### 3. Memory Lifecycle Not Updating
**Cause**: MemoryLifecycleService disabled or confidence thresholds too high  
**Check**: Configuration and confidence scores

### 4. High Memory Usage
**Cause**: Too many memories or large embeddings  
**Solution**: MemoryPressureService should trigger cleanup

## Future Enhancements

1. **Memory Clustering**: Automatic grouping of related memories
2. **Temporal Analysis**: Time-based memory insights
3. **Cross-Project Memory Sharing**: Federated memory system
4. **Memory Visualization**: Graph-based memory explorer UI
5. **Advanced Quality Metrics**: ML-based quality assessment

## Summary

The memory services architecture provides a comprehensive system for intelligent memory management with:
- **Multiple storage backends** (Lucene for text, vectors for semantic)
- **Event-driven architecture** for loose coupling
- **Background processing** for maintenance and enhancement
- **Quality assurance** through validation and scoring
- **Lifecycle management** for keeping memories relevant
- **Natural language interface** for easy interaction

The system is designed to be resilient, scalable, and provide both immediate text search and advanced semantic understanding of stored knowledge.