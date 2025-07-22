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
- **PathResolutionService does NOT create directories** - it only computes paths
- **Services MUST create their own directories** using `Directory.CreateDirectory()`
- Services should create directories when first needed (lazy initialization)

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