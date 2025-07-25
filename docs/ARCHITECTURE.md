# Architecture Decisions

## Core Design Principles

### 1. Performance First
- Native .NET 9.0 for speed (vs Python alternatives)
- Lucene indexing for millisecond searches
- Workspace caching with LRU eviction
- Parallel operations where possible

### 2. Progressive Disclosure
- Auto-switch to summary at 5,000 tokens
- Smart summaries with insights
- Cached detail requests
- Token-aware responses

### 3. Single Source of Truth
- `IPathResolutionService` for ALL paths
- No manual path construction
- Centralized configuration

## Key Architectural Decisions

### Roslyn Integration
**Decision**: Use Microsoft.CodeAnalysis directly
- **Why**: Native C# understanding, accurate analysis
- **Trade-off**: Larger memory footprint
- **Alternative considered**: Basic text parsing (rejected - too limited)

### MCP Implementation  
**Decision**: STDIO transport with manual tool registration
- **Why**: Simple, reliable, works with Claude Code
- **Trade-off**: No WebSocket support (yet)
- **Alternative considered**: HTTP transport (rejected - complexity)

### Memory Storage
**Decision**: Lucene indexes for everything (search + memory)
- **Why**: Unified storage, fast queries, proven reliability
- **Trade-off**: Not a traditional database
- **Alternative considered**: SQLite (removed - added complexity)

### JSON Backup System
**Decision**: Direct JSON serialization for backups
- **Why**: Simple, human-readable, version-control friendly
- **Previous approach**: SQLite intermediate (removed - unnecessary complexity)

### TypeScript Support
**Decision**: Integrate tsserver directly
- **Why**: Official TypeScript language server, accurate analysis
- **Trade-off**: Requires npm for installation
- **Alternative considered**: Custom parser (rejected - maintenance burden)

### Tool Registration Pattern
**Decision**: Functional registration with static methods
- **Why**: Clean separation, easy testing, clear dependencies
- **Trade-off**: More boilerplate
- **Alternative considered**: Attribute-based (rejected - less explicit)

## Service Architecture

### Core Services

```
┌─────────────────────────────────────────────────────────┐
│                    MCP Protocol Layer                     │
├─────────────────────────────────────────────────────────┤
│                  Tool Registration Layer                  │
├─────────────────────────────────────────────────────────┤
│   Code Analysis  │  Search/Index  │   Memory System      │
├──────────────────┼────────────────┼─────────────────────┤
│ CodeAnalysisService │ LuceneIndexService │ FlexibleMemoryService │
│ TypeScriptService   │ FileWatcherService │ JsonBackupService    │
│ RoslynWorkspace     │ IndexLifecycle     │ MemoryLifecycle      │
└─────────────────────────────────────────────────────────┘
```

### Dependency Flow

1. **Tools** depend on **Services**
2. **Services** depend on **Infrastructure**
3. **Infrastructure** depends on **Core Interfaces**
4. **PathResolutionService** injected everywhere

### Workspace Management

- **Cached Workspaces**: LRU eviction after timeout
- **Lazy Loading**: Projects loaded on demand
- **Incremental Compilation**: Reuse compilation data
- **File Watching**: Auto-update on changes

## Error Handling Strategy

### Graceful Degradation
1. Try full analysis
2. Fall back to syntax-only
3. Return partial results
4. Never crash the server

### Error Wrapping
- Service layer throws specific exceptions
- Tool layer catches and wraps in MCP format
- User sees helpful error messages

## Performance Optimizations

### Indexing Strategy
- One-time index build per workspace
- Incremental updates on file changes
- Memory-mapped files for large content
- Batch indexing for efficiency

### Response Optimization
- Token estimation before sending
- Auto-mode switching
- Summary generation for large results
- Detail request caching

### Memory Management
- Workspace timeout and eviction
- Dispose patterns everywhere
- Large object heap awareness
- Native AOT compilation option

## Security Considerations

### Path Traversal Prevention
- All paths validated through PathResolutionService
- No user-supplied paths used directly
- Workspace boundaries enforced

### Process Isolation
- TypeScript in separate process
- Timeouts on all operations
- Resource limits enforced

## Future Architecture Plans

### Planned Enhancements
1. **WebSocket Transport**: For remote deployment
2. **Semantic Search**: Vector embeddings for code
3. **Multi-Language**: F#, VB.NET support
4. **Distributed Caching**: Redis for shared state

### Scaling Considerations
- Horizontal scaling via workspace sharding
- Read replicas for search operations
- Queue-based indexing for large codebases

## Testing Strategy

### Test Levels
1. **Unit Tests**: Service logic
2. **Integration Tests**: Tool operations
3. **Performance Tests**: Large codebase handling
4. **Protocol Tests**: MCP compliance

### Test Infrastructure
- In-memory Lucene for fast tests
- Mock file systems
- Deterministic workspace creation
- Benchmark baselines

## Deployment Patterns

### Local Development
- Debug builds with full symbols
- File-based logging
- Aggressive timeouts

### Production
- Release builds with AOT
- Structured logging
- Conservative resource limits
- Health checks

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2024-01 | Use Lucene for all storage | Unified approach, proven scale |
| 2024-02 | Add TypeScript via tsserver | Official support, accuracy |
| 2024-03 | Progressive disclosure pattern | Token limits, better UX |
| 2024-04 | Remove SQLite backup | Unnecessary complexity |
| 2024-05 | Manual tool registration | Explicit, testable |