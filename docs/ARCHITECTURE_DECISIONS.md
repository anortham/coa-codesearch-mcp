# Architecture Decision Records

This document captures important architectural decisions made during the development of COA CodeSearch MCP Server.

## ADR-001: Fix Global Dotnet Tool Index Location

**Date**: 2025-07-20

**Status**: Implemented

**Context**: 
When COA CodeSearch is installed as a global dotnet tool, `AppContext.BaseDirectory` points to the .NET tool installation directory (e.g., `~/.dotnet/tools/.store/...`) instead of the current working directory. This caused indexes to be created in the wrong location, specifically in the tool's installation directory rather than the project's `.codesearch` directory.

**Decision**: 
Changed from using `AppContext.BaseDirectory` to properly detecting the project root by searching for the `.git` directory. This ensures indexes are always created in the project's `.codesearch` directory regardless of how the tool is invoked.

**Implementation**:
The fix was implemented in the codebase to traverse up the directory tree from the current working directory until a `.git` directory is found, which indicates the project root.

**Consequences**:
- ✅ Indexes are now correctly created in the project's `.codesearch` directory
- ✅ Works correctly whether run as a global tool or locally
- ✅ Consistent behavior across different invocation methods
- ⚠️ Assumes projects use Git (which is standard for most projects)

**Alternatives Considered**:
1. **Use Environment.CurrentDirectory** - Rejected because it can be unreliable depending on how the tool is invoked
2. **Pass project root as parameter** - Rejected to maintain simplicity and avoid breaking changes  
3. **Use Directory.GetCurrentDirectory()** - Rejected for same reasons as Environment.CurrentDirectory

**Tags**: bug-fix, indexing, global-tool, project-root