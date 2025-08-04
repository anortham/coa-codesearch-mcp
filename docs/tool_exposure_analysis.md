# Tool Exposure Analysis - COA CodeSearch MCP

## Tool Exposure Matrix

| Tool Name | Registered in Program.cs | Has [McpServerToolType] | Via Unified Memory | Notes |
|-----------|-------------------------|------------------------|-------------------|-------|
| BatchOperationsToolV2 | ✓ | ✓ | | |
| ChecklistTools | ✓ | | ✓ | SAVE/MANAGE intents |
| CheckpointTools | ✓ | ✓ | | |
| ClaudeMemoryTools | ✓ | ✓ | | |
| ExampleTypedTool | | | | Not registered |
| FastDirectorySearchTool | ✓ | ✓ | | |
| FastFileSearchToolV2 | ✓ | ✓ | | Declared in UnifiedMemory but not used |
| FastFileSizeAnalysisTool | ✓ | ✓ | | |
| FastRecentFilesTool | ✓ | ✓ | | |
| FastSimilarFilesTool | ✓ | ✓ | | |
| FastTextSearchToolV2 | ✓ | ✓ | | Declared in UnifiedMemory but not used |
| FlexibleMemorySearchToolV2 | ✓ | ✓ | ✓ | FIND intent primary |
| FlexibleMemoryTools | ✓ | ✓ | ✓ | SAVE/FIND/SUGGEST/MANAGE intents |
| GetVersionTool | ✓ | ✓ | | |
| HybridSearchTool | ✓ | ✓ | ✓ | FIND intent |
| IndexHealthCheckTool | ✓ | ✓ | | |
| IndexWorkspaceTool | ✓ | ✓ | | |
| ITool | | | | Interface, not a tool |
| LoadContextTool | ✓ | ✓ | | |
| MemoryGraphNavigatorTool | ✓ | ✓ | ✓ | EXPLORE intent |
| MemoryLinkingTools | ✓ | | ✓ | CONNECT intent |
| MemoryQualityAssessmentTool | ✓ | ✓ | | |
| PatternDetectorTool | ✓ | ✓ | | |
| SearchAssistantTool | ✓ | ✓ | | |
| SemanticSearchTool | ✓ | ✓ | ✓ | FIND intent |
| SetLoggingTool | ✓ | ✓ | | |
| StreamingTextSearchTool | ✓ | | | Not exposed at all |
| SystemHealthCheckTool | ✓ | ✓ | | |
| TimelineTool | ✓ | | ✓ | TIMELINE intent |
| ToolUsageAnalyticsTool | ✓ | ✓ | | |
| UnifiedMemoryTool | ✓ | ✓ | | The interface itself |
| WorkflowDiscoveryTool | ✓ | ✓ | | |

## Additional MCP Endpoints from FlexibleMemoryTools

FlexibleMemoryTools also exposes these methods as individual MCP tools via `[McpServerTool]`:
- `store_memory`
- `delete_memory` 
- `bulk_delete_memories`
- `suggest_quality_based_archiving`


## Summary

### Statistics
- **Total MCP endpoints**: 30 (26 tools + 4 FlexibleMemoryTools methods)
- **Tools only via Unified Memory**: 3 (ChecklistTools, MemoryLinkingTools, TimelineTool)
- **Tools not exposed**: 1 (StreamingTextSearchTool)
- **Tools exposed both ways**: 5 (marked with ✓ in both columns)

### Timeline Implementation Status
✅ **Timeline is now fully functional** through the unified memory interface:
- Detects timeline intent from keywords: "timeline", "chronological", "history", "recent", etc.
- Extracts time periods from natural language (e.g., "last 3 days", "this week", "today")
- Filters by memory type if specified (e.g., "timeline of technical debt")
- Returns formatted chronological view of memories

### Key Findings

1. **Timeline Fixed**: TimelineTool is now accessible via unified memory:
   - Still missing `[McpServerToolType]` attribute (by design - only via unified)
   - Timeline intent added to enum and routing implemented in UnifiedMemoryService

2. **Hidden Functionality**: ChecklistTools and MemoryLinkingTools only accessible through unified_memory

3. **Orphaned Tool**: StreamingTextSearchTool is registered but completely inaccessible

4. **Redundancy**: 7 tools are exposed both directly and through unified_memory

### Unified Memory Intent Routing

| Intent | Routes To |
|--------|-----------|
| SAVE | FlexibleMemoryTools, ChecklistTools |
| FIND | FlexibleMemoryTools, SemanticSearchTool, HybridSearchTool |
| CONNECT | MemoryLinkingTools |
| EXPLORE | MemoryGraphNavigatorTool |
| SUGGEST | FlexibleMemoryTools |
| MANAGE | FlexibleMemoryTools, ChecklistTools, JsonMemoryBackupService |
| TIMELINE | TimelineTool |