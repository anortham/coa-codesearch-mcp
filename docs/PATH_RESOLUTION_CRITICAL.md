# PathResolutionService - CRITICAL DOCUMENTATION

## ⚠️ IMPORTANT: SINGLE SOURCE OF TRUTH FOR ALL PATHS

**ALL path operations in the COA CodeSearch MCP Server MUST go through `IPathResolutionService`.**

## Why This Matters

We've wasted significant time debugging path-related issues because different services were:
- Constructing paths manually
- Reading configuration directly
- Creating directories in multiple places
- Using inconsistent path resolution logic

## The Golden Rules

1. **NEVER** construct paths manually. **ALWAYS** use `IPathResolutionService` for path computation.
2. **PathResolutionService ONLY computes paths** - it does NOT create directories.
3. **Services are responsible for creating their own directories** when needed.

## Directory Structure

```
.codesearch/
├── index/                    # Code search indexes (hash-based subdirectories)
│   ├── {workspace}_{hash}/   # Individual workspace indexes
│   └── workspace_metadata.json
├── project-memory/           # Shared team memories (version controlled)
├── local-memory/             # Local developer memories (not shared)
├── logs/                     # Debug log files
└── backups/                  # Backup directories
    └── backup_{timestamp}/
```

## IPathResolutionService Methods

### Core Methods

```csharp
// Get the base .codesearch directory
string GetBasePath()
// Example: "C:\source\project\.codesearch"

// Get index path for a workspace
string GetIndexPath(string workspacePath)
// Example: "C:\source\project\.codesearch\index\myproject_a1b2c3d4"

// Get logs directory
string GetLogsPath()
// Example: "C:\source\project\.codesearch\logs"

// Get workspace metadata file path
string GetWorkspaceMetadataPath()
// Example: ".codesearch\index\workspace_metadata.json"

// Get workspace-specific metadata file path
string GetWorkspaceMetadataPath(string workspacePath)
// Example: ".codesearch\index\myproject_a1b2c3d4\workspace_metadata.json"

// Resolve original workspace path from index directory (THREAD-SAFE)
string? TryResolveWorkspacePath(string indexDirectory)
// Returns actual workspace path, not hashed name

// Store workspace metadata during indexing (THREAD-SAFE)
void StoreWorkspaceMetadata(string workspacePath)
// Creates metadata for path resolution

// Compute hash for workspace directory naming
string ComputeWorkspaceHash(string workspacePath)
// Returns 8-character hash for uniqueness
```

### Thread-Safe File System Operations

```csharp
// Safe directory operations
bool DirectoryExists(string path)
void EnsureDirectoryExists(string path)
IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)

// Safe file operations  
bool FileExists(string path)
IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)

// Safe path operations
string GetFullPath(string path)
string GetFileName(string path)
string GetExtension(string path)
string GetDirectoryName(string path)
string GetRelativePath(string relativeTo, string path)
```

## Implementation Details

### Directory Creation
- **PathResolutionService does NOT create directories** - it only computes paths
- **Services MUST create their own directories** using `Directory.CreateDirectory()`
- Services should create directories when first needed (lazy initialization)

### Path Normalization
- All paths are converted to absolute paths
- Paths are normalized for the current OS
- Hash-based paths ensure uniqueness across workspaces

### Path Validation & Security
- **Directory traversal protection**: Blocks ".." sequences in paths
- **Path length validation**: Prevents paths over 240 characters
- **Input sanitization**: All workspace paths validated before use
- **Cross-platform support**: Handles tilde expansion and path separators

### Thread Safety & Concurrency
- **Metadata locking**: Per-file semaphores prevent concurrent access corruption
- **Atomic operations**: Metadata updates use temp files with atomic replacement
- **Lock management**: Concurrent dictionary manages semaphores per metadata file
- **Safe I/O operations**: All file system operations include proper error handling

### Workspace Path Resolution
- **Metadata-first approach**: Reads `workspace_metadata.json` for original paths
- **Fallback reconstruction**: Attempts path reconstruction from directory names
- **Hash verification**: Confirms reconstructed paths match expected workspace hash
- **Common location search**: Checks standard workspace locations during fallback

#### Metadata Fallback Process

1. **Primary Resolution**: Attempt to read `workspace_metadata.json`
   ```json
   {
     "originalPath": "C:\\source\\COA CodeSearch MCP",
     "hashPath": "4785ab0f",
     "createdAt": "2025-01-26T14:30:15Z",
     "lastAccessed": "2025-01-26T15:45:22Z"
   }
   ```

2. **Fallback Strategy**: If metadata is missing or corrupted
   - Parse directory name format: `workspacename_hash`
   - Extract workspace name and hash components
   - Search common workspace locations:
     - `C:\source\{workspacename}` (with spaces and underscores)
     - `%USERPROFILE%\source\{workspacename}`
     - `%USERPROFILE%\Desktop\{workspacename}`
     - Current directory relative paths

3. **Hash Verification**: For each potential path
   - Compute expected hash using `ComputeWorkspaceHash()`
   - Compare with hash from directory name
   - Only return path if hashes match exactly

4. **Graceful Degradation**: If resolution fails
   - Return `null` to indicate unresolvable path
   - Log warning with index directory details
   - HTTP API marks as `[Unresolved: {dirname}]`

## Common Mistakes to Avoid

### ❌ DON'T: Construct paths manually
```csharp
// WRONG - Never do this!
var indexPath = Path.Combine(Environment.CurrentDirectory, ".codesearch", "index");
var logsPath = Path.Combine(basePath, "logs");
var memoryPath = ".codesearch/project-memory";
```

### ❌ DON'T: Read configuration directly for paths
```csharp
// WRONG - Never do this!
var basePath = _configuration["Lucene:IndexBasePath"] ?? ".codesearch";
var memoryPath = _configuration["MemoryConfiguration:BasePath"];
```

### ❌ DON'T: Create directories without using PathResolutionService paths
```csharp
// WRONG - Never construct the path yourself!
var logsPath = Path.Combine(basePath, "logs");
Directory.CreateDirectory(logsPath);

// WRONG - Don't hardcode paths!
Directory.CreateDirectory(".codesearch/logs");
```

### ✅ DO: Always use IPathResolutionService for paths, then create directories as needed
```csharp
// CORRECT - Get path from PathResolutionService
var logsPath = _pathResolution.GetLogsPath();
// Then create directory when needed
Directory.CreateDirectory(logsPath);

// CORRECT - For file operations
var indexPath = _pathResolution.GetIndexPath(workspacePath);
Directory.CreateDirectory(indexPath);
var indexFile = Path.Combine(indexPath, "index.dat");

// CORRECT - Lazy initialization pattern
private void EnsureLogDirectory()
{
    var logsPath = _pathResolution.GetLogsPath();
    Directory.CreateDirectory(logsPath);
}
```

## Service Dependencies

All services that need path access MUST inject `IPathResolutionService`:

```csharp
public class MyService
{
    private readonly IPathResolutionService _pathResolution;
    
    public MyService(IPathResolutionService pathResolution)
    {
        _pathResolution = pathResolution;
    }
    
    public void DoSomething()
    {
        // Always use PathResolutionService for paths
        var indexPath = _pathResolution.GetIndexPath("workspace");
        
        // Create directory when needed
        Directory.CreateDirectory(indexPath);
        
        // Now use the path
        var file = Path.Combine(indexPath, "data.json");
        File.WriteAllText(file, jsonData);
    }
}
```

## Services That MUST Use PathResolutionService

- ✅ LuceneIndexService
- ✅ FlexibleMemoryService
- ✅ FileLoggingService
- ✅ MemoryMigrationService
- ✅ SetLoggingTool
- ✅ Any future service that needs path access

## Testing

When writing tests, mock `IPathResolutionService`:

```csharp
var mockPathResolution = new Mock<IPathResolutionService>();
mockPathResolution.Setup(x => x.GetBasePath()).Returns(testBasePath);
mockPathResolution.Setup(x => x.GetIndexPath(It.IsAny<string>()))
    .Returns<string>(ws => Path.Combine(testBasePath, "index", GetHashedName(ws)));
```

## Configuration

PathResolutionService reads ONE configuration value:
```json
{
  "Lucene": {
    "IndexBasePath": ".codesearch"  // Can be absolute or relative
  }
}
```

If not specified, defaults to `.codesearch` in the current directory.

## Future Changes

If you need to add new paths:
1. Add the method to `IPathResolutionService` interface
2. Implement in `PathResolutionService` to compute the path (NO directory creation)
3. Update this documentation
4. **NEVER** compute paths outside of PathResolutionService
5. Services using the new path are responsible for creating the directory

## Remember

**PathResolutionService is the ONLY place where paths should be computed.**
**Services are responsible for creating their own directories.**

This separation of concerns is critical:
- PathResolutionService = Path computation only
- Services = Directory creation and file operations

This is not a suggestion - it's a requirement to prevent the path-related bugs we've been dealing with.