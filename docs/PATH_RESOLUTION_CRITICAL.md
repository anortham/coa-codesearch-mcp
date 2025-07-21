# PathResolutionService - CRITICAL DOCUMENTATION

## ⚠️ IMPORTANT: SINGLE SOURCE OF TRUTH FOR ALL PATHS

**ALL path operations in the COA CodeSearch MCP Server MUST go through `IPathResolutionService`.**

## Why This Matters

We've wasted significant time debugging path-related issues because different services were:
- Constructing paths manually
- Reading configuration directly
- Creating directories in multiple places
- Using inconsistent path resolution logic

## The Golden Rule

**NEVER** construct paths manually. **ALWAYS** use `IPathResolutionService`.

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

// Get memory paths
string GetProjectMemoryPath()  // ".codesearch\project-memory"
string GetLocalMemoryPath()    // ".codesearch\local-memory"

// Get metadata file path
string GetWorkspaceMetadataPath()
// Example: ".codesearch\index\workspace_metadata.json"

// Check if path is protected
bool IsProtectedPath(string indexPath)

// Get backup directory
string GetBackupPath(string? timestamp = null)
// Example: ".codesearch\backups\backup_20240121_143052"
```

## Implementation Details

### Directory Creation
- **ALL directories are created automatically** by PathResolutionService methods
- **NEVER call `Directory.CreateDirectory()` directly**
- Each getter method ensures its directory exists before returning the path

### Path Normalization
- All paths are converted to absolute paths
- Paths are normalized for the current OS
- Hash-based paths ensure uniqueness across workspaces

### Protected Paths
- `project-memory` and `local-memory` directories are protected
- These cannot be deleted through normal index cleanup operations

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

### ❌ DON'T: Create directories manually
```csharp
// WRONG - Never do this!
Directory.CreateDirectory(indexPath);
System.IO.Directory.CreateDirectory(logsPath);
```

### ✅ DO: Always use IPathResolutionService
```csharp
// CORRECT - Always do this!
var indexPath = _pathResolution.GetIndexPath(workspacePath);
var logsPath = _pathResolution.GetLogsPath();
var projectMemoryPath = _pathResolution.GetProjectMemoryPath();
var backupPath = _pathResolution.GetBackupPath();
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
2. Implement in `PathResolutionService` with automatic directory creation
3. Update this documentation
4. **NEVER** create paths outside of PathResolutionService

## Remember

**PathResolutionService is the ONLY place where paths should be constructed and directories should be created.**

This is not a suggestion - it's a requirement to prevent the path-related bugs we've been dealing with.