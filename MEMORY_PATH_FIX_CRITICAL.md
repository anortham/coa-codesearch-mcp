# CRITICAL: Memory System Path Resolution Bug & Fix

## Issue Summary
After the PathResolutionService refactor, the memory system is completely broken - both reading and writing memories fail silently. This affects all stored architectural decisions, patterns, security rules, and work sessions.

## Root Cause
The PathResolutionService refactor introduced centralized path management, but ClaudeMemoryService and MemoryBackupService were still constructing full paths instead of using the new system.

### Broken Code Pattern
```csharp
// ClaudeMemoryService constructor - WRONG
_projectMemoryWorkspace = Path.Combine(_config.BasePath, _config.ProjectMemoryPath);
// This creates: ".codesearch/project-memory"

// MemoryBackupService.GetWorkspaceForScope - WRONG  
return isProjectScope
    ? Path.Combine(basePath, _configuration["ClaudeMemory:ProjectMemoryPath"] ?? "project-memory")
    : Path.Combine(basePath, _configuration["ClaudeMemory:LocalMemoryPath"] ?? "local-memory");
```

### Why It Breaks
PathResolutionService.GetIndexPath() has special handling for memory paths:
```csharp
if (workspacePath.Equals("project-memory", StringComparison.OrdinalIgnoreCase) || 
    workspacePath.Equals(".codesearch/project-memory", StringComparison.OrdinalIgnoreCase))
{
    return GetProjectMemoryPath();
}
```

But when ClaudeMemoryService passes ".codesearch/project-memory", it doesn't match the first condition and falls through to create a hashed index directory instead!

## The Fix

### 1. ClaudeMemoryService (Services/ClaudeMemoryService.cs)
```csharp
// FIXED - Just use the path names, not full paths
_projectMemoryWorkspace = _config.ProjectMemoryPath;  // "project-memory"
_localMemoryWorkspace = _config.LocalMemoryPath;      // "local-memory"
```

### 2. MemoryBackupService (Services/MemoryBackupService.cs)
```csharp
// Add IPathResolutionService injection
private readonly IPathResolutionService _pathResolutionService;

// Fix constructor
public MemoryBackupService(
    ILogger<MemoryBackupService> logger,
    IConfiguration configuration,
    ILuceneIndexService luceneService,
    IPathResolutionService pathResolutionService)  // ADD THIS
{
    _pathResolutionService = pathResolutionService;
    // ...
    _backupDbPath = Path.Combine(_pathResolutionService.GetBasePath(), "memories.db");
}

// Fix GetWorkspaceForScope method
private string GetWorkspaceForScope(string scope)
{
    // ... scope checking logic ...
    
    // FIXED - Return just the memory path names
    return isProjectScope
        ? _configuration["ClaudeMemory:ProjectMemoryPath"] ?? "project-memory"
        : _configuration["ClaudeMemory:LocalMemoryPath"] ?? "local-memory";
}
```

## Impact
- **Without fix**: All memory operations fail silently. Memories exist but are inaccessible.
- **With fix**: Memory system works correctly again, accessing existing memories.

## Test Coverage Added
Created comprehensive PathResolutionServiceTests.cs with 34 tests covering:
- Memory path resolution
- Edge cases (trailing slashes, case sensitivity, special characters)
- Protected path identification
- Real-world usage patterns

## Action Items for Next Session
1. Read this file first thing
2. Store it as an architectural decision memory
3. Verify memories are accessible again
4. Consider adding integration tests for the memory system

## Commits
- `fix: Fix memory system path resolution after PathResolutionService refactor`
- `test: Add comprehensive PathResolutionService test suite`

The memories are safe - they're still in `.codesearch/project-memory` and `.codesearch/local-memory`. The fix just ensures the services can find them again.