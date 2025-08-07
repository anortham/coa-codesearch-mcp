# COA CodeSearch MCP - Framework Migration Plan

## Overview
This document provides a detailed, step-by-step plan for migrating COA CodeSearch MCP to use the COA MCP Framework, based on the successful migration of CodeNav MCP.

## Pre-Migration Checklist

- [ ] Review migration assessment with team
- [ ] Create migration branch: `feature/framework-migration`
- [ ] Backup current working state
- [ ] Document current tool names and signatures
- [ ] Set up test environment
- [ ] Review CodeNav migration for reference

## Phase 1: Foundation Setup (Days 1-3)

### Day 1: Project Setup
1. **Add Framework NuGet Packages**
   ```xml
   <PackageReference Include="COA.Mcp.Framework" Version="1.1.*" />
   <PackageReference Include="COA.Mcp.Framework.TokenOptimization" Version="1.1.*" />
   <PackageReference Include="COA.Mcp.Framework.Testing" Version="1.1.*" />
   ```

2. **Create Framework Configuration**
   - Copy appsettings.json structure from CodeNav
   - Add token optimization settings
   - Configure resource storage options

3. **Create Compatibility Layer**
   - Keep existing tool interfaces temporarily
   - Create adapter classes for gradual migration

### Day 2: Server Migration
1. **Replace McpServer with Framework Server**
   ```csharp
   // New Program.cs structure
   var builder = new McpServerBuilder()
       .WithServerInfo("COA CodeSearch MCP Server", "3.0.0")
       .ConfigureLogging(...)
       .ConfigureTokenOptimization(...);
   ```

2. **Migrate Service Registration**
   - Keep all existing service registrations
   - Add framework service registrations
   - Ensure compatibility between old and new

3. **Test Basic Server Startup**
   - Verify server starts correctly
   - Check logging works
   - Ensure STDIO communication functions

### Day 3: Tool Discovery Setup
1. **Implement Tool Registration**
   - Keep existing ToolRegistry temporarily
   - Add framework's DiscoverTools
   - Run both in parallel initially

2. **Create Base Tool Adapter**
   ```csharp
   public abstract class FrameworkToolAdapter<TParams, TResult> : McpToolBase<TParams, TResult>
   {
       // Adapter logic to bridge old and new patterns
   }
   ```

3. **Verification**
   - List all tools through MCP
   - Verify tool count matches
   - Test tool discovery

## Phase 2: Tool Migration (Days 4-10)

### Day 4-5: Simple Tools Migration
**Target Tools** (No dependencies, simple I/O):
- [ ] GetVersionTool
- [ ] SystemHealthCheckTool
- [ ] IndexHealthCheckTool
- [ ] SetLoggingTool
- [ ] WorkflowDiscoveryTool

**Migration Pattern**:
1. Create new tool class inheriting from `McpToolBase<TParams, TResult>`
2. Move business logic to `ExecuteInternalAsync`
3. Add framework attributes
4. Test side-by-side with old version
5. Remove old version when verified

### Day 6-7: Search Tools Migration
**Target Tools** (Core search functionality):
- [ ] FastTextSearchToolV2
- [ ] FastFileSearchToolV2
- [ ] FastDirectorySearchTool
- [ ] FastRecentFilesTool
- [ ] FastSimilarFilesTool
- [ ] FastFileSizeAnalysisTool

**Special Considerations**:
- Preserve Lucene integration exactly
- Migrate response builders to use framework's `AIOptimizedResponse`
- Implement token optimization features
- Test search performance thoroughly

### Day 8-9: Memory Tools Migration
**Target Tools** (Memory system):
- [ ] FlexibleMemoryTools
- [ ] ClaudeMemoryTools
- [ ] UnifiedMemoryTool
- [ ] SemanticSearchTool
- [ ] HybridSearchTool
- [ ] MemoryQualityAssessmentTool

**Special Considerations**:
- Preserve memory service architecture
- Enhance with framework's caching
- Add resource storage for large memories
- Maintain backward compatibility

### Day 10: Complex Tools Migration
**Target Tools** (Complex dependencies):
- [ ] IndexWorkspaceTool
- [ ] BatchOperationsToolV2
- [ ] SearchAssistantTool
- [ ] PatternDetectorTool
- [ ] MemoryGraphNavigatorTool

## Phase 3: Infrastructure Cleanup (Days 11-13)

### Day 11: Remove Redundant Code
1. **Delete Replaced Components**
   - [ ] Old McpServer.cs
   - [ ] ToolRegistry (if fully migrated)
   - [ ] AttributeBasedToolDiscovery
   - [ ] ClaudeOptimizedToolBase
   - [ ] Custom response builders

2. **Update Namespaces**
   - Replace old namespaces with framework ones
   - Update all using statements
   - Fix any compilation issues

### Day 12: Optimize Token Management
1. **Configure Token Optimization**
   ```csharp
   .ConfigureTokenOptimization(options =>
   {
       options.DefaultTokenLimit = 10000;
       options.Level = TokenOptimizationLevel.Balanced;
       options.EnableAdaptiveLearning = true;
       options.EnableResourceStorage = true;
       options.EnableCaching = true;
   })
   ```

2. **Implement Caching Strategy**
   - Configure cache for search results
   - Set up memory result caching
   - Add cache invalidation logic

### Day 13: Resource Management
1. **Migrate Resource Providers**
   - Update to use framework's IResourceProvider
   - Implement resource storage for large results
   - Test resource URIs work correctly

2. **Update Prompt Registry**
   - Migrate to framework's prompt system
   - Ensure all prompts still function

## Phase 4: Testing & Validation (Days 14-15)

### Day 14: Comprehensive Testing
1. **Unit Tests**
   - [ ] Update existing tests for new tool structure
   - [ ] Add framework-specific tests
   - [ ] Verify all tests pass

2. **Integration Tests**
   - [ ] Test all tools through MCP protocol
   - [ ] Verify search functionality
   - [ ] Test memory operations
   - [ ] Validate resource access

3. **Performance Tests**
   - [ ] Measure response times
   - [ ] Check memory usage
   - [ ] Validate token optimization
   - [ ] Test under load

### Day 15: Final Validation
1. **End-to-End Testing**
   - [ ] Test with Claude Code client
   - [ ] Verify all workflows function
   - [ ] Check error handling
   - [ ] Validate logging

2. **Documentation Updates**
   - [ ] Update README
   - [ ] Update CLAUDE.md
   - [ ] Document any breaking changes
   - [ ] Create migration notes

## Post-Migration Tasks

### Week 3: Optimization & Enhancement
1. **Performance Tuning**
   - Profile application
   - Optimize hot paths
   - Fine-tune caching

2. **Feature Enhancement**
   - Leverage new framework features
   - Implement advanced token strategies
   - Add new insights/actions

3. **Code Cleanup**
   - Remove any remaining legacy code
   - Standardize patterns
   - Update documentation

## Tool Migration Tracking

| Tool | Status | Migrated By | Tested | Notes |
|------|--------|-------------|--------|-------|
| GetVersionTool | ⏳ Pending | - | - | Simple, start here |
| SystemHealthCheckTool | ⏳ Pending | - | - | Good second tool |
| FastTextSearchToolV2 | ⏳ Pending | - | - | Critical, test thoroughly |
| FastFileSearchToolV2 | ⏳ Pending | - | - | Similar to text search |
| FlexibleMemoryTools | ⏳ Pending | - | - | Complex, needs care |
| IndexWorkspaceTool | ⏳ Pending | - | - | Important for functionality |
| ... | ... | ... | ... | ... |

## Risk Mitigation Strategies

1. **Side-by-Side Migration**
   - Keep old tools active during migration
   - Test new tools thoroughly before removing old ones
   - Ability to rollback if issues arise

2. **Incremental Approach**
   - Migrate simplest tools first
   - Learn patterns before complex tools
   - Build confidence with each success

3. **Comprehensive Testing**
   - Test each tool individually
   - Run full integration tests
   - Performance benchmarking

4. **Documentation**
   - Document each change
   - Keep migration log
   - Note any issues and resolutions

## Success Criteria

- [ ] All 50+ tools successfully migrated
- [ ] All tests passing (unit, integration, e2e)
- [ ] Performance equal or better than current
- [ ] Memory usage reduced by 20%+
- [ ] Code reduction of 30%+ achieved
- [ ] No breaking changes for end users

## Communication Plan

1. **Daily Updates**
   - Progress on tool migration
   - Any blockers encountered
   - Next day's plan

2. **Weekly Review**
   - Overall progress assessment
   - Risk evaluation
   - Timeline adjustments if needed

3. **Stakeholder Updates**
   - End of each phase
   - Major milestones
   - Final completion

## Rollback Plan

If critical issues arise:
1. **Immediate**: Switch back to pre-migration branch
2. **Day 1**: Restore from backup
3. **Week 1**: Document lessons learned
4. **Week 2**: Revise approach and retry

## Estimated Timeline

- **Phase 1**: 3 days (Foundation)
- **Phase 2**: 7 days (Tool Migration)
- **Phase 3**: 3 days (Cleanup)
- **Phase 4**: 2 days (Testing)
- **Total**: 15 working days (3 weeks)

## Next Steps

1. Review this plan with the team
2. Get approval to proceed
3. Create migration branch
4. Begin Phase 1 on approved start date
5. Daily progress tracking using this document

---

**Document Version**: 1.0
**Created**: January 2025
**Last Updated**: January 2025
**Status**: Ready for Review