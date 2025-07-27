# Configuration Guide

This guide covers all configuration options available in the CodeSearch MCP Server.

## Configuration File Location

The server uses `appsettings.json` for configuration. You can also override settings using:
- Environment variables
- Command-line arguments
- User secrets (for development)

## Complete Configuration Reference

### Logging Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",       // Overall log level: Trace, Debug, Information, Warning, Error, Critical
      "Microsoft": "Warning",         // Microsoft framework logging level
      "COA.CodeSearch": "Debug"       // CodeSearch-specific logging level
    }
  }
}
```

### MCP Server Settings

```json
{
  "McpServer": {
    "MaxWorkspaces": 5,               // Maximum number of workspaces to keep in memory
    "WorkspaceTimeout": "00:30:00",   // Time before unused workspaces are evicted (HH:MM:SS)
    "EnableDiagnostics": true,        // Enable detailed diagnostic information
    "ParallelismDegree": 4,           // Number of parallel operations allowed
    "CacheSettings": {
      "MaxCachedWorkspaces": 5,       // Maximum cached search indexes
      "WorkspaceEvictionTime": "00:30:00", // Cache eviction timeout
      "EnableSemanticModelCache": true // Cache semantic models for performance
    }
  }
}
```

### Response Limits

Controls response size and token limits to prevent overwhelming AI assistants:

```json
{
  "ResponseLimits": {
    "MaxTokens": 20000,               // Maximum tokens per response
    "SafetyMargin": 0.8,              // Safety margin (80% of max)
    "DefaultMaxResults": 50,          // Default result limit
    "EnableTruncation": true,         // Enable automatic truncation
    "EnablePagination": true,         // Enable result pagination
    "EnableTokenUsageLogging": true,  // Log token usage
    "ToolSpecificLimits": {           // Override limits for specific tools
      "text_search": 15000,
      "file_search": 10000,
      "batch_operations": 25000
    },
    "ToolSpecificResultLimits": {     // Result count limits per tool
      "text_search": 100,
      "file_search": 100,
      "recent_files": 50,
      "directory_search": 20
    }
  }
}
```

### Lucene Index Settings

```json
{
  "Lucene": {
    "IndexBasePath": ".codesearch",   // Base path for all indexes and data
    "StuckLockTimeoutMinutes": 15,    // Timeout for stuck index locks
    "MaintenanceIntervalMinutes": 30, // Index maintenance interval
    "EnablePeriodicMaintenance": true, // Enable automatic maintenance
    "SupportedExtensions": [          // File extensions to index
      ".cs", ".razor", ".cshtml", ".json", ".xml", ".md", ".txt", 
      ".js", ".ts", ".jsx", ".tsx", ".vue", ".css", ".scss", ".html", 
      ".yml", ".yaml", ".csproj", ".sln", ".d.ts"
    ],
    "ExcludePatterns": [              // Patterns to exclude from indexing
      "bin", "obj", "node_modules", ".git", ".vs", "packages", 
      "TestResults", ".codesearch"
    ],
    "ExcludedDirectories": [          // Directories to skip
      "bin", "obj", "node_modules", ".git", ".vs", "packages", 
      "TestResults", ".codesearch"
    ],
    "IndexSettings": {
      "MaxFieldLength": 1000000,      // Maximum field length
      "RAMBufferSizeMB": 256,         // RAM buffer size for indexing
      "MaxBufferedDocs": 1000,        // Documents to buffer before flush
      "CommitInterval": "00:01:00"    // Index commit interval
    }
  }
}
```

### Memory System Configuration

```json
{
  "Memory": {
    "BasePath": ".codesearch",        // Base directory for memory storage
    "ProjectMemoryPath": "project-memory", // Shared memory location
    "LocalMemoryPath": "local-memory",     // Personal memory location
    "MaxSearchResults": 50,           // Maximum memory search results
    "TemporaryNoteRetentionDays": 7, // Days to keep temporary notes
    "MinConfidenceLevel": 50,         // Minimum confidence for suggestions
    "EnableAutoSummary": true,        // Auto-summarize old memories
    "EnablePatternDetection": true,   // Detect patterns in memories
    "HooksSettings": {
      "EnableAutoContext": true,      // Auto-load relevant context
      "EnableSessionTracking": true,  // Track work sessions
      "EnablePatternSuggestions": true // Suggest patterns
    }
  }
}
```

### TypeScript Configuration

```json
{
  "TypeScript": {
    "ServerPath": null,               // Custom tsserver path (auto-detected if null)
    "EnableSemanticAnalysis": true,   // Enable semantic TypeScript analysis
    "MaxProjectsOpen": 10,            // Maximum concurrent TypeScript projects
    "RequestTimeout": "00:00:30",     // TypeScript request timeout
    "SkipInitialization": false       // Skip TypeScript installation check
  }
}
```

### File Watcher Settings

```json
{
  "FileWatcher": {
    "Enabled": true,                  // Enable file watching
    "DebounceMilliseconds": 500,      // Debounce file changes
    "BatchSize": 50,                  // Batch size for updates
    "ExcludePatterns": [              // Patterns to ignore
      "bin", "obj", "node_modules", ".git", ".vs", 
      "packages", "TestResults", ".codesearch"
    ]
  }
}
```

### Workspace Auto-Index

```json
{
  "WorkspaceAutoIndex": {
    "Enabled": true,                  // Auto-index on startup
    "StartupDelayMilliseconds": 3000  // Delay before auto-indexing
  }
}
```

## Environment Variable Overrides

You can override any setting using environment variables with the format:
`CODESEARCH__{Section}__{Key}`

Examples:
```bash
# Override log level
CODESEARCH__Logging__LogLevel__Default=Debug

# Override max workspaces
CODESEARCH__McpServer__MaxWorkspaces=10

# Override Lucene RAM buffer
CODESEARCH__Lucene__IndexSettings__RAMBufferSizeMB=512

# Override TypeScript timeout
CODESEARCH__TypeScript__RequestTimeout=00:01:00
```

## Performance Tuning

### For Large Codebases
```json
{
  "McpServer": {
    "MaxWorkspaces": 3,
    "ParallelismDegree": 2
  },
  "Lucene": {
    "IndexSettings": {
      "RAMBufferSizeMB": 512,
      "MaxBufferedDocs": 2000
    }
  }
}
```

### For Limited Memory Systems
```json
{
  "McpServer": {
    "MaxWorkspaces": 2,
    "WorkspaceTimeout": "00:10:00"
  },
  "Lucene": {
    "IndexSettings": {
      "RAMBufferSizeMB": 128
    }
  },
  "ResponseLimits": {
    "MaxTokens": 10000,
    "DefaultMaxResults": 25
  }
}
```

### For Fast Response Times
```json
{
  "McpServer": {
    "EnableDiagnostics": false,
    "CacheSettings": {
      "EnableSemanticModelCache": true
    }
  },
  "FileWatcher": {
    "DebounceMilliseconds": 1000
  }
}
```

## Security Considerations

1. **Path Configuration**: The `Memory.BasePath` is relative to the current working directory by default. Use absolute paths for production deployments.

2. **File Access**: The server has access to all files in the indexed workspace. Ensure proper file system permissions.

3. **Memory Storage**: Project memories are shared, while local memories are personal. Configure based on your team's needs.

4. **Excluded Patterns**: Always exclude sensitive directories like `.git`, `node_modules`, and build outputs.

## Troubleshooting Configuration Issues

1. **Enable Debug Logging**:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Debug",
         "COA.CodeSearch": "Trace"
       }
     }
   }
   ```

2. **Check Configuration Loading**:
   The server logs the loaded configuration on startup when debug logging is enabled.

3. **Validate JSON**:
   Ensure your `appsettings.json` is valid JSON. Use a JSON validator if needed.

4. **Default Values**:
   If a setting is not specified, the server uses sensible defaults. Check the source code for default values.