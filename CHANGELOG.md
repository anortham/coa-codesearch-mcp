# Changelog

## Version 1.2.0

### New Features

1. **Index Workspace Tool** (`index_workspace`)
   - New tool to explicitly build or rebuild search indexes
   - Parameters:
     - `workspacePath` (required): The workspace path to index
     - `forceRebuild` (optional): Force rebuild even if index exists
   - Useful for pre-indexing workspaces before searching

### Improvements

1. **Improved Call Hierarchy Tool**
   - More forgiving cursor positioning - no longer requires exact positioning on method name
   - Now searches for enclosing method/property if cursor is on return type or parameters
   - Will find any callable symbol on the same line as a fallback
   - Better error messages to guide users

### Bug Fixes

1. **Test Infrastructure**
   - Fixed failing integration tests for FastTextSearch
   - Added proper test file copying to output directory
   - Improved test isolation to prevent file locking issues
   - 5 tests temporarily skipped due to Lucene file locking in parallel execution

### Technical Improvements

1. **Version Bump**
   - Updated to version 1.2.0 from 1.1.0
   - Updated CI/CD pipeline to use new version

## Previous Versions

See git history for changes in earlier versions.