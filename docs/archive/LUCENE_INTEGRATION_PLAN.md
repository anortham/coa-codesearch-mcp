# Lucene.NET Integration Plan for COA CodeSearch MCP

## Overview
This document outlines the plan for integrating Lucene.NET into the COA CodeSearch MCP Server to provide fast, language-agnostic text search capabilities that complement the existing Roslyn-based semantic search.

## Goals
1. **Fast Text Search**: Sub-second search across large codebases
2. **Language Agnostic**: Support all file types (C#, Razor, JavaScript, CSS, etc.)
3. **Real-time Updates**: Keep index synchronized with file changes
4. **Minimal Memory Footprint**: Efficient memory usage for large codebases
5. **Rich Query Support**: Wildcards, fuzzy search, proximity queries

## Architecture Design

### 1. Core Components

#### LuceneIndexService
```csharp
public class LuceneIndexService : IHostedService, IDisposable
{
    // Manages the Lucene index lifecycle
    // - Creates and maintains IndexWriter
    // - Handles index optimization
    // - Manages searchers with SearcherManager
    // - Provides thread-safe access to index
}
```

#### FileWatcherService
```csharp
public class FileWatcherService
{
    // Monitors file system changes
    // - Uses FileSystemWatcher for real-time updates
    // - Integrates with Git for batch updates
    // - Queues changes for incremental indexing
}
```

#### GitIntegrationService
```csharp
public class GitIntegrationService
{
    // Leverages Git for efficient change detection
    // - Detects branch switches
    // - Finds modified files since last index
    // - Respects .gitignore rules
}
```

### 2. Index Schema

#### Document Fields
- **id**: Unique document identifier (file path hash)
- **path**: Full file path (stored, not analyzed)
- **filename**: File name only (analyzed)
- **extension**: File extension (keyword field)
- **content**: File content (analyzed with StandardAnalyzer)
- **language**: Programming language (keyword field)
- **lastModified**: File modification timestamp
- **size**: File size in bytes
- **project**: Associated project name (if applicable)

#### Analyzers
- **StandardAnalyzer**: For general content
- **WhitespaceAnalyzer**: For paths
- **Custom CamelCaseAnalyzer**: For code identifiers

### 3. Implementation Phases

#### Phase 1: Basic Text Search (MVP)
1. Create LuceneIndexService
2. Implement initial indexing on startup
3. Create `fast_text_search` MCP tool
4. Support basic query syntax

#### Phase 2: Real-time Updates
1. Implement FileWatcherService
2. Add incremental indexing
3. Handle file deletions/renames
4. Add index optimization scheduling

#### Phase 3: Advanced Features
1. Git integration for efficient updates
2. Custom analyzers for code
3. Language-specific tokenization
4. Faceted search (by file type, project, etc.)

#### Phase 4: Performance Optimization
1. Memory-mapped index storage
2. Parallel indexing
3. Query result caching
4. Index sharding for very large codebases

## Implementation Details

### 1. MCP Tool: fast_text_search

```json
{
  "name": "fast_text_search",
  "description": "Lightning-fast text search across all files in the workspace",
  "inputSchema": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Search query (supports wildcards, fuzzy search)"
      },
      "workspacePath": {
        "type": "string", 
        "description": "Path to workspace"
      },
      "filePatterns": {
        "type": "array",
        "items": { "type": "string" },
        "description": "File patterns to include (e.g., '*.cs', '*.razor')"
      },
      "excludePatterns": {
        "type": "array",
        "items": { "type": "string" },
        "description": "Patterns to exclude"
      },
      "maxResults": {
        "type": "integer",
        "default": 100
      },
      "includeContext": {
        "type": "boolean",
        "default": true,
        "description": "Include surrounding lines"
      }
    },
    "required": ["query", "workspacePath"]
  }
}
```

### 2. Indexing Strategy

#### Initial Indexing
```csharp
public async Task IndexWorkspaceAsync(string workspacePath)
{
    // 1. Find all files respecting .gitignore
    // 2. Parallel process files in batches
    // 3. Create documents with all fields
    // 4. Commit in batches for performance
}
```

#### Incremental Updates
```csharp
public async Task UpdateFileAsync(string filePath, FileChangeType changeType)
{
    switch (changeType)
    {
        case FileChangeType.Created:
        case FileChangeType.Modified:
            // Delete old version and add new
            break;
        case FileChangeType.Deleted:
            // Remove from index
            break;
        case FileChangeType.Renamed:
            // Update path field
            break;
    }
}
```

### 3. Search Implementation

#### Query Types
- **Simple**: Direct text match
- **Wildcard**: `Config*`, `*Service`
- **Fuzzy**: `Configuraiton~` (finds Configuration)
- **Proximity**: `"async await"~5` (within 5 words)
- **Boolean**: `+required -excluded`
- **Field-specific**: `extension:cs AND content:async`

#### Result Format
```json
{
  "success": true,
  "totalHits": 42,
  "results": [
    {
      "path": "/src/Services/UserService.cs",
      "filename": "UserService.cs",
      "score": 0.95,
      "lineNumber": 47,
      "lineContent": "public async Task<User> GetUserAsync(int id)",
      "context": {
        "before": ["    {", "        _logger.LogInformation(\"Getting user {Id}\", id);"],
        "after": ["        {", "            var user = await _repository.GetByIdAsync(id);"]
      }
    }
  ]
}
```

### 4. Configuration

```json
{
  "Lucene": {
    "IndexPath": "./lucene-index",
    "MaxIndexSizeMB": 1000,
    "CommitIntervalSeconds": 30,
    "OptimizeIntervalHours": 24,
    "MaxFieldLength": 1000000,
    "IncludeFileExtensions": ["*"],
    "ExcludeFileExtensions": [".exe", ".dll", ".pdb"],
    "ExcludeDirectories": ["bin", "obj", "node_modules", ".git"],
    "EnableFileWatcher": true,
    "EnableGitIntegration": true
  }
}
```

### 5. Performance Considerations

#### Memory Management
- Use SearcherManager for efficient searcher reuse
- Implement LRU cache for frequent queries
- Memory-mapped directory for large indexes
- Configurable RAM buffer size

#### Indexing Performance
- Parallel document creation
- Batch commits (every N documents or T seconds)
- Separate thread for indexing operations
- Use NRT (Near Real Time) reader

#### Search Performance
- Warm up searcher on startup
- Cache filter results
- Use early termination for large result sets
- Implement pagination efficiently

### 6. Error Handling

- Graceful handling of locked files
- Recovery from corrupted index
- Automatic re-indexing on critical errors
- Detailed logging for troubleshooting

### 7. Testing Strategy

#### Unit Tests
- Index creation and management
- Document field extraction
- Query parsing and execution
- File watcher event handling

#### Integration Tests
- Full workspace indexing
- Search accuracy tests
- Performance benchmarks
- Concurrent access tests

#### Performance Tests
- Index 100K+ files
- Search response times < 100ms
- Memory usage under load
- Index update latency

## Future Enhancements

1. **Semantic Search**: Combine Lucene results with Roslyn analysis
2. **Code Intelligence**: Extract and index symbols separately
3. **History Search**: Index git history for temporal queries
4. **Distributed Search**: Support for sharded indexes
5. **ML-Enhanced Ranking**: Use machine learning for result ranking
6. **IDE Integration**: Real-time search-as-you-type

## Success Metrics

- Search latency < 100ms for 95% of queries
- Index update latency < 1 second
- Memory usage < 500MB for typical solution
- Support for 1M+ files in a single index
- Zero data loss during updates

## Dependencies

- Lucene.Net 4.8.0-beta00017
- Lucene.Net.Analysis.Common
- Lucene.Net.QueryParser
- Lucene.Net.Highlighter (for context extraction)
- LibGit2Sharp (for git integration)

## Timeline Estimate

- Phase 1 (MVP): 2-3 days
- Phase 2 (Real-time): 2-3 days  
- Phase 3 (Advanced): 3-4 days
- Phase 4 (Optimization): 2-3 days

Total: ~2 weeks for full implementation