# Memory System Path Resolution Fix - Critical Changes

## Date: 2025-07-20

## Problem Summary

The memory system was using hash-based directory names (e.g., `.codesearch/index/ad439a13`) instead of user-friendly names like `local-memory` and `project-memory`. Additionally, there were multiple sources of truth for path resolution, creating a dangerous situation where memory indexes could be accidentally wiped.

## Root Causes Identified

1. **Duplicate Path Resolution Logic**
   - `LuceneIndexService.GetIndexPath()` was computing paths
   - `ClaudeMemoryService.GetIndexPath()` had its own implementation
   - Both were using SHA256 hashing for ALL paths, including memory paths

2. **Path Mismatch Issues**
   - `GetBasePath()` returns absolute path: `C:\source\COA Roslyn MCP\.codesearch`
   - ClaudeMemoryService was passing relative paths: `.codesearch/project-memory`
   - The comparison `workspacePath.StartsWith(basePath)` always failed
   - This caused memory paths to fall through to hash-based naming

3. **Dangerous Auto-Indexing**
   - `WorkspaceAutoIndexService` was re-indexing ALL workspaces on startup
   - This included memory directories, which would wipe them out
   - No protection existed to skip memory paths

## Changes Made

### 1. Fixed LuceneIndexService.GetIndexPath() - Lines 413-452

```csharp
private string GetIndexPath(string workspacePath)
{
    var basePath = GetBasePath(); // Returns absolute path like C:\source\COA Roslyn MCP\.codesearch
    
    // Check if this is a memory-related path
    if (workspacePath.Contains("memory", StringComparison.OrdinalIgnoreCase))
    {
        // Memory paths should use friendly directory names
        
        // If it's already an absolute path, use it
        if (Path.IsPathRooted(workspacePath))
        {
            return workspacePath;
        }
        
        // If it starts with .codesearch, strip it since basePath already includes it
        if (workspacePath.StartsWith(".codesearch", StringComparison.OrdinalIgnoreCase))
        {
            // Remove .codesearch/ or .codesearch\ prefix
            var memoryPart = workspacePath.Substring(".codesearch".Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.Combine(basePath, memoryPart);
        }
        
        // Otherwise just append to basePath
        return Path.Combine(basePath, workspacePath);
    }
    
    // For code project indexes, use hash-based naming for consistency
    var normalizedPath = NormalizeToWorkspaceRoot(workspacePath);
    
    // Use hash-based directory name for code indexes
    var indexRoot = Path.Combine(basePath, "index");
    var hashPath = GenerateHashPath(normalizedPath);
    var fullPath = Path.Combine(indexRoot, hashPath);
    
    // Update metadata with the normalized path
    UpdateMetadata(normalizedPath, hashPath);
    
    return fullPath;
}
```

### 2. Added Single Source of Truth - ILuceneIndexService

Added to interface:
```csharp
/// <summary>
/// Get the physical index path for a workspace - single source of truth for path resolution
/// </summary>
string GetPhysicalIndexPath(string workspacePath);
```

Implementation in LuceneIndexService:
```csharp
public string GetPhysicalIndexPath(string workspacePath)
{
    return GetIndexPath(workspacePath);
}
```

### 3. Fixed WorkspaceAutoIndexService - Lines 70-75

```csharp
// CRITICAL: Skip memory indexes - they should NEVER be re-indexed as code directories
if (workspacePath.Contains("memory", StringComparison.OrdinalIgnoreCase))
{
    _logger.LogInformation("Skipping memory index path from auto-indexing: {WorkspacePath}", workspacePath);
    continue;
}
```

### 4. Refactored ClaudeMemoryService

- Changed from `ILuceneWriterManager` to `ILuceneIndexService`
- Removed duplicate `GetIndexPath()` method completely
- Updated `SearchIndex()` to use `_indexService.GetIndexSearcherAsync()`
- Updated `StoreMemoryAsync()` to use proper async methods

### 5. Existing Protections (Verified Working)

- `IsProtectedMemoryIndex()` checks for memory paths
- `ClearIndex()` throws exception if attempting to clear memory indexes
- FileWatcher excludes `.codesearch` directory
- Memory backup/restore doesn't clear existing data

## Migration Steps Performed

1. Created missing directories:
   ```
   .codesearch/project-memory/
   .codesearch/local-memory/
   ```

2. Copied existing memory data from hash directories:
   - `index/ad439a13/` → `local-memory/`
   - `index/2d96a667/` → `project-memory/`

## Final Directory Structure

```
.codesearch/
├── project-memory/        # Project-level memories (version controlled)
│   ├── _0.cfe
│   ├── _0.cfs
│   ├── _0.si
│   ├── segments.gen
│   └── segments_3
├── local-memory/          # Local developer memories
│   ├── _0.cfe
│   ├── _0.cfs
│   ├── _0.si
│   ├── segments.gen
│   └── segments_1
├── index/                 # Code project indexes (hash-based)
│   ├── 2fc7017b/         # Main project index
│   └── metadata.json
└── memories.db           # SQLite backup
```

## Critical Notes

1. **The old hash-based memory indexes are still in place** - Do not delete them until verified the new system works
2. **Memory system now has single source of truth** - All path resolution goes through LuceneIndexService
3. **Auto-indexing protection is critical** - Without it, memories would be wiped on every server restart
4. **TypeScript fixes are included** - Column to offset conversion and file synchronization

## Testing Checklist

- [ ] Restart MCP server with new build
- [ ] Verify memory storage works: `mcp__codesearch__remember_session`
- [ ] Verify memory recall works: `mcp__codesearch__recall_context`
- [ ] Check that auto-indexing skips memory paths in logs
- [ ] Verify TypeScript tools work correctly
- [ ] Ensure no new hash directories are created for memories

## Rollback Plan

If issues occur:
1. The old hash-based indexes are preserved in `index/ad439a13/` and `index/2d96a667/`
2. Can revert code changes and rebuild
3. Memory data is also backed up in `memories.db`