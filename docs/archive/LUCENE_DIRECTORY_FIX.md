# ⚠️ ARCHIVED - HISTORICAL CONTEXT ONLY

**This document describes an old issue that has been resolved.**

**For current path resolution guidance, see [docs/PATH_RESOLUTION_CRITICAL.md](../PATH_RESOLUTION_CRITICAL.md)**

---

# Lucene .codesearch Directory Fix - Session Note

## Issue Fixed
The `.codesearch` directory was being created in `COA.CodeSearch.McpServer/.codesearch` instead of the project root.

## Solution Implemented
Updated `LuceneIndexService.cs` to find the project root by looking for the `.git` directory:

```csharp
// Find project root by looking for .git directory
var currentDir = System.IO.Directory.GetCurrentDirectory();
var projectRoot = currentDir;

// Walk up the directory tree to find .git
var dir = new DirectoryInfo(currentDir);
while (dir != null && !System.IO.Directory.Exists(Path.Combine(dir.FullName, ".git")))
{
    dir = dir.Parent;
}

if (dir != null)
{
    projectRoot = dir.FullName;
}
```

## Action Required
Before next session, move the existing `.codesearch` directory to preserve memories:
```bash
mv COA.CodeSearch.McpServer/.codesearch .
```

This will place it at the project root alongside `.git` and `.claude`.

## Files Modified
- `COA.CodeSearch.McpServer/Services/LuceneIndexService.cs` - Fixed GetIndexPath() and CleanupStuckIndexes()

## Other Work Completed This Session
1. Updated all documentation (README.md, CLAUDE.md)
2. Fixed session-end.ps1 hook to properly save summaries
3. Updated user-prompt-submit.ps1 to auto-run init_memory_hooks
4. Updated tool descriptions for accuracy
5. Verified Lucene locking and cleanup mechanisms are working properly