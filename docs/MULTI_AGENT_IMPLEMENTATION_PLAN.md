# Multi-Agent Implementation Plan

## Overview

This document provides a comprehensive plan for adding multi-agent support to COA CodeSearch MCP Server, allowing multiple AI agents to use the same project simultaneously without Lucene index locking conflicts.

## Current State

### Architecture
- **Single writer constraint**: Lucene allows only one IndexWriter per directory
- **Multiple readers**: DirectoryReader supports concurrent access via snapshots
- **Lock cleanup**: Automatic cleanup of stuck locks on startup (with age checks)
- **AsyncLock**: Thread-safe coordination within single process

### Constraints
- Each agent runs its own MCP server process (STDIO mode)
- Cannot share IndexWriter across processes
- Need coordination for write operations
- Must maintain backward compatibility

## Proposed Solution: Service Discovery Pattern

### Architecture
```
First Agent:
  MCP Client → STDIO → MCP Server (Embedded Mode + Service)
                            ↓
                        Starts TCP Service
                            ↓
                    Creates service.json

Subsequent Agents:
  MCP Client → STDIO → MCP Server (Client Mode)
                            ↓
                     Reads service.json
                            ↓
                      Connects to Service
```

### Key Components

1. **Service Discovery File** (`.codesearch/service.json`)
```json
{
  "pid": 12345,
  "port": 9876,
  "started": "2025-08-01T10:00:00Z",
  "version": "1.0.0",
  "hostname": "localhost"
}
```

2. **Service Lock File** (`.codesearch/service.lock`)
- Prevents multiple services from starting
- Contains PID for validation

3. **Coordination Service**
- Lightweight TCP server
- Routes requests to LuceneIndexService
- Manages write operation queue

## Implementation Checklist

### Phase 1: Service Discovery Infrastructure
- [ ] Create `ICoordinationService` interface
- [ ] Implement `ServiceDiscovery` class
  - [ ] Check for existing service.json
  - [ ] Validate service is alive (TCP health check)
  - [ ] Clean up stale service files
  - [ ] Create service.json on startup
- [ ] Add configuration for service port range
- [ ] Create `ServiceMode` enum (Embedded, Client, Standalone)

### Phase 2: Coordination Service
- [ ] Create `CoordinationService` class
  - [ ] TCP listener implementation
  - [ ] Simple request/response protocol
  - [ ] Health check endpoint
  - [ ] Graceful shutdown handling
- [ ] Create `CoordinationClient` class
  - [ ] Connect to service
  - [ ] Retry logic with backoff
  - [ ] Connection pooling
  - [ ] Timeout handling

### Phase 3: Request Routing
- [ ] Create `IRequestRouter` interface
- [ ] Implement routing logic
  - [ ] Read operations: Direct to local Lucene (fast path)
  - [ ] Write operations: Route through coordinator
  - [ ] Tool execution: Determine routing based on operation type
- [ ] Add operation type detection
  - [ ] Mark tools as ReadOnly or ReadWrite
  - [ ] Auto-detect based on tool implementation

### Phase 4: Integration
- [ ] Modify `Program.cs`
  - [ ] Add service discovery on startup
  - [ ] Determine service mode
  - [ ] Start coordination service if first
  - [ ] Connect as client if service exists
- [ ] Update `McpServer.cs`
  - [ ] Inject IRequestRouter
  - [ ] Route tool calls based on mode
  - [ ] Handle connection failures gracefully
- [ ] Update tool implementations
  - [ ] Add operation type attributes
  - [ ] Ensure serializable parameters/results

### Phase 5: Error Handling & Recovery
- [ ] Implement circuit breaker for service calls
- [ ] Add fallback to embedded mode
- [ ] Handle service crashes
  - [ ] Detect dead service
  - [ ] Clean up service files
  - [ ] Promote client to service
- [ ] Add comprehensive logging
- [ ] Create diagnostics tool

### Phase 6: Testing
- [ ] Unit tests
  - [ ] Service discovery logic
  - [ ] Coordination protocol
  - [ ] Request routing
  - [ ] Error scenarios
- [ ] Integration tests
  - [ ] Multi-process scenarios
  - [ ] Service failover
  - [ ] Lock cleanup
  - [ ] Performance benchmarks
- [ ] Manual testing
  - [ ] 2 agents, same project
  - [ ] 5 agents, concurrent operations
  - [ ] Service crash scenarios
  - [ ] Network failures

### Phase 7: Documentation
- [ ] Update README with multi-agent setup
- [ ] Create troubleshooting guide
- [ ] Document configuration options
- [ ] Add architecture diagrams
- [ ] Update CLAUDE.md with usage notes

## Configuration

### New Settings
```json
{
  "MultiAgent": {
    "Enabled": true,
    "ServicePortRange": "9800-9899",
    "HealthCheckInterval": 5000,
    "ConnectionTimeout": 10000,
    "MaxRetries": 3
  }
}
```

### Environment Variables
- `CODESEARCH_MULTIAGENT_ENABLED` - Enable/disable multi-agent support
- `CODESEARCH_SERVICE_PORT` - Fixed port (overrides auto-discovery)
- `CODESEARCH_SERVICE_MODE` - Force specific mode

## Migration Strategy

1. **Default Off**: Multi-agent disabled by default
2. **Opt-in**: Users enable via config or env var
3. **Backward Compatible**: Single agent works unchanged
4. **Progressive Rollout**: Test with power users first

## Performance Considerations

### Optimizations
- Read operations remain local (no network overhead)
- Write operations batched when possible
- Connection pooling for clients
- Lazy service startup (only when needed)

### Benchmarks to Track
- Single agent performance (must not regress)
- Multi-agent write throughput
- Service startup time
- Memory overhead per agent

## Security Considerations

- Service binds to localhost only
- No authentication (local use only)
- Port scanning mitigation
- PID validation for service ownership

## Alternative Approaches Considered

1. **Full HTTP MCP**: Too complex, breaks compatibility
2. **Shared Memory**: Platform-specific, complex
3. **Database Queue**: Adds dependency, slower
4. **File-based Locking**: Already have this, not sufficient

## Success Criteria

- [ ] 2+ agents can index same project simultaneously
- [ ] No performance regression for single agent
- [ ] Automatic recovery from service crashes
- [ ] Zero configuration for basic use
- [ ] Clean error messages for failures

## Rollback Plan

If issues discovered:
1. Set `CODESEARCH_MULTIAGENT_ENABLED=false`
2. Kill any coordination services
3. Delete `.codesearch/service.*` files
4. Each agent reverts to embedded mode

## Future Enhancements

- WebSocket support for real-time updates
- Distributed mode (multiple machines)
- Read replica support
- Admin UI for monitoring
- Prometheus metrics endpoint

---

**Document Version**: 1.0  
**Created**: August 1, 2025  
**Status**: Planning Phase  
**Estimated Effort**: 3-5 days for POC, 2 weeks for production-ready