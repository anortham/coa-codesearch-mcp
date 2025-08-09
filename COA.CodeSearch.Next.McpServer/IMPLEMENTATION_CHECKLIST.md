# CodeSearch.Next Implementation Checklist

## Foundation ✅ COMPLETED
- [x] Create project from COA MCP Framework template
- [x] Configure for .NET 9.0 with Web SDK
- [x] Add Lucene.NET 4.8.0-beta00017 packages
- [x] Configure Serilog for file-only logging
- [x] Set up dual-mode support (STDIO/HTTP)
- [x] Configure appsettings.json with CodeSearch settings
- [x] Set up global dotnet tool packaging

## Core Services Migration (Priority 1)
### Path Resolution Service ✅ COMPLETED
- [x] Create `Services/IPathResolutionService.cs` interface
- [x] Implement `Services/PathResolutionService.cs`
- [x] Use `~/.coa/codesearch` as base path
- [x] Implement workspace hash computation
- [x] Add workspace metadata support

### Lucene Index Service ✅ COMPLETED
- [x] Port `Services/LuceneIndexService.cs` from original (split into multiple files)
- [x] Update paths to use `~/.coa/codesearch/indexes`
- [x] Keep existing workspace metadata tracking
- [x] Maintain thread-safe index management (using SemaphoreSlim)
- [x] Port index health check methods

### Code Analyzer ✅ COMPLETED
- [x] Port `Services/Analysis/CodeAnalyzer.cs` unchanged
- [x] Port `CodeTokenizer` class
- [x] Port `CamelCaseFilter` class
- [x] Port `CodeLengthFilter` class
- [x] Keep all code pattern preservation logic

### File Indexing Service
- [ ] Port `Services/FileIndexingService.cs`
- [ ] Update to use new PathResolutionService
- [ ] Keep supported extensions configuration
- [ ] Maintain excluded directories list
- [ ] Port batch indexing logic

### File Watcher Service
- [ ] Port `Services/FileWatcherService.cs` as BackgroundService
- [ ] Register as hosted service in DI
- [ ] Keep debouncing logic
- [ ] Maintain multi-workspace watching
- [ ] Port file change event handling

### Support Services ✅ COMPLETED
- [x] Port `Services/QueryCacheService.cs` (improved version)
- [x] Port `Services/CircuitBreakerService.cs`
- [x] Port `Services/MemoryPressureService.cs`
- [ ] Port `Services/FieldSelectorService.cs`
- [ ] Port `Services/ErrorRecoveryService.cs`

## Search Tools Implementation (Priority 2)
### IndexWorkspaceTool
- [ ] Create `Tools/IndexWorkspaceTool.cs`
- [ ] Inherit from `McpToolBase<IndexWorkspaceParams, IndexWorkspaceResult>`
- [ ] Use LuceneIndexService for indexing
- [ ] Return index statistics
- [ ] Support force rebuild option

### TextSearchTool
- [ ] Create `Tools/TextSearchTool.cs`
- [ ] Use framework's BaseResponseBuilder for progressive disclosure
- [ ] Support search types: standard, wildcard, fuzzy, regex, phrase
- [ ] Implement context lines support
- [ ] Store results in SearchResultResourceProvider

### FileSearchTool
- [ ] Create `Tools/FileSearchTool.cs`
- [ ] Support file name pattern matching
- [ ] Use framework's response optimization
- [ ] Include directory search option
- [ ] Return sorted by relevance

### DirectorySearchTool
- [ ] Create `Tools/DirectorySearchTool.cs`
- [ ] Support directory pattern matching
- [ ] Include file count per directory
- [ ] Support glob patterns
- [ ] Group by unique directories

### RecentFilesTool
- [ ] Create `Tools/RecentFilesTool.cs`
- [ ] Support time frame specification
- [ ] Include file size information
- [ ] Support extension filtering
- [ ] Sort by modification time

### SimilarFilesTool
- [ ] Create `Tools/SimilarFilesTool.cs`
- [ ] Use Lucene's MoreLikeThis functionality
- [ ] Include similarity scores
- [ ] Support extension exclusion
- [ ] Configure similarity parameters

## Resource Providers (Priority 3)
### SearchResultResourceProvider
- [ ] Create `Resources/SearchResultResourceProvider.cs`
- [ ] Implement `IResourceProvider` interface
- [ ] Use scheme `codesearch-search://`
- [ ] Store search results for persistence
- [ ] Support paginated resource reading
- [ ] Implement cleanup of old results

## Prompts Implementation (Priority 4)
### CodeExplorerPrompt
- [ ] Create `Prompts/CodeExplorerPrompt.cs`
- [ ] Inherit from `PromptBase`
- [ ] Define exploration workflow
- [ ] Include search strategies
- [ ] Add navigation guidance

### BugFinderPrompt
- [ ] Create `Prompts/BugFinderPrompt.cs`
- [ ] Define bug detection workflow
- [ ] Include common bug patterns
- [ ] Add severity assessment
- [ ] Provide fix suggestions

### RefactoringAssistantPrompt
- [ ] Create `Prompts/RefactoringAssistantPrompt.cs`
- [ ] Define refactoring analysis workflow
- [ ] Include code smell detection
- [ ] Add improvement suggestions
- [ ] Provide impact assessment

## Controllers for HTTP Mode (Priority 5)
### SearchController
- [ ] Create `Controllers/SearchController.cs`
- [ ] Add health check endpoint
- [ ] Add workspace listing endpoint
- [ ] Add cross-workspace search endpoint
- [ ] Add index management endpoints

## Testing (Priority 6)
### Unit Tests
- [ ] Port relevant tests from original CodeSearch
- [ ] Remove memory-related tests
- [ ] Add framework integration tests
- [ ] Test FileWatcher functionality
- [ ] Test resource persistence

### Integration Tests
- [ ] Test STDIO mode with Claude Code
- [ ] Test HTTP federation mode
- [ ] Test auto-service start
- [ ] Test multi-workspace support
- [ ] Test progressive disclosure

## Deployment (Priority 7)
### Package as Global Tool
- [ ] Test `dotnet pack` command
- [ ] Verify tool manifest
- [ ] Test installation: `dotnet tool install -g`
- [ ] Test uninstall/reinstall
- [ ] Document installation process

### Service Configuration
- [ ] Create Windows service wrapper (optional)
- [ ] Create systemd service file (Linux)
- [ ] Document auto-start configuration
- [ ] Test service restart on failure
- [ ] Verify logging to `~/.coa/codesearch/logs`

## Migration from Old CodeSearch (Priority 8)
### Data Migration
- [ ] Create migration tool for existing indexes
- [ ] Move indexes to `~/.coa/codesearch/indexes`
- [ ] Update workspace metadata paths
- [ ] Test with existing workspaces

### Documentation
- [ ] Update README.md
- [ ] Create migration guide
- [ ] Document API changes
- [ ] Update Claude Code configuration examples

## Final Steps
### Rename to Production
- [ ] Change namespace from CodeSearch.Next to CodeSearch
- [ ] Update package name to COA.CodeSearch
- [ ] Update tool command to `codesearch`
- [ ] Archive old CodeSearch project
- [ ] Update all documentation references

## Notes
- All tools use `McpToolBase<TParams, TResult>` pattern from framework
- Use framework's `BaseResponseBuilder` for automatic token optimization
- File paths use `~/.coa/codesearch` for centralized storage
- Maintain backward compatibility with workspace.metadata.json
- Keep existing hash-based index directory structure

## Success Criteria
- [ ] Builds without warnings
- [ ] All tests pass
- [ ] Works with Claude Code in STDIO mode
- [ ] HTTP federation mode functional
- [ ] FileWatcher updates indexes automatically
- [ ] Progressive disclosure works at 5K tokens
- [ ] Resource URIs persist search results
- [ ] Prompts provide guided workflows
- [ ] Memory usage < 100MB typical
- [ ] Startup time < 200ms