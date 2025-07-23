# CodeSearch MCP Server - Tool Reference

Complete documentation for all 52 tools available in the CodeSearch MCP Server.

## 🚀 V2 Tools - AI-Optimized Features

Many tools have V2 versions that provide AI-optimized responses with:
- **Intelligent Summaries**: Automatic insights, patterns, and actionable recommendations
- **Token Management**: Auto-switches to summary mode for large results (>5,000 tokens)
- **Progressive Disclosure**: Request specific details without re-running operations
- **Pattern Recognition**: Identifies trends, hotspots, and optimization opportunities
- **Progress Tracking**: Real-time notifications for long-running operations

V2 tools are marked with 🔍 in the documentation below.

## Table of Contents

- [Search & Navigation (Core Tools)](#search--navigation-core-tools)
  - [go_to_definition](#go_to_definition)
  - [find_references](#find_references) 🔍 V2
  - [search_symbols](#search_symbols)
  - [search_symbols_v2](#search_symbols_v2) 🔍
  - [get_implementations](#get_implementations)
  - [get_implementations_v2](#get_implementations_v2) 🔍
- [Fast Search Tools (Lucene-powered)](#fast-search-tools-lucene-powered)
  - [index_workspace](#index_workspace) 🏗️ Progress Tracking
  - [fast_text_search](#fast_text_search)
  - [fast_text_search_v2](#fast_text_search_v2) 🔍
  - [fast_file_search](#fast_file_search)
  - [fast_file_search_v2](#fast_file_search_v2) 🔍
  - [fast_recent_files](#fast_recent_files)
  - [fast_file_size_analysis](#fast_file_size_analysis)
  - [fast_similar_files](#fast_similar_files)
  - [fast_directory_search](#fast_directory_search)
- [Code Analysis](#code-analysis)
  - [get_hover_info](#get_hover_info)
  - [get_document_symbols](#get_document_symbols)
  - [get_diagnostics](#get_diagnostics) 🔍 V2
  - [get_call_hierarchy](#get_call_hierarchy)
  - [get_call_hierarchy_v2](#get_call_hierarchy_v2) 🔍
  - [rename_symbol](#rename_symbol) 🔍 V2
  - [batch_operations](#batch_operations) 🏗️ Progress Tracking
  - [batch_operations_v2](#batch_operations_v2) 🔍
  - [advanced_symbol_search](#advanced_symbol_search)
  - [dependency_analysis](#dependency_analysis) 🔍 V2
  - [project_structure_analysis](#project_structure_analysis) 🔍 V2
- [Memory System](#memory-system)
  - [recall_context](#recall_context)
  - [flexible_store_memory](#flexible_store_memory)
  - [flexible_search_memories](#flexible_search_memories)
  - [flexible_search_memories_v2](#flexible_search_memories_v2) 🔍
  - [flexible_update_memory](#flexible_update_memory)
  - [flexible_get_memory](#flexible_get_memory)
  - [flexible_store_working_memory](#flexible_store_working_memory)
  - [flexible_find_similar_memories](#flexible_find_similar_memories)
  - [flexible_archive_memories](#flexible_archive_memories)
  - [flexible_summarize_memories](#flexible_summarize_memories)
  - [flexible_get_memory_suggestions](#flexible_get_memory_suggestions)
  - [flexible_list_templates](#flexible_list_templates)
  - [flexible_create_from_template](#flexible_create_from_template)
  - [flexible_store_git_commit](#flexible_store_git_commit)
  - [flexible_memories_for_file](#flexible_memories_for_file)
  - [flexible_link_memories](#flexible_link_memories)
  - [flexible_get_related_memories](#flexible_get_related_memories)
  - [flexible_unlink_memories](#flexible_unlink_memories)
  - [memory_dashboard](#memory_dashboard)
  - [backup_memories_to_sqlite](#backup_memories_to_sqlite)
  - [restore_memories_from_sqlite](#restore_memories_from_sqlite)
- [Checklist System](#checklist-system)
  - [create_checklist](#create_checklist)
  - [add_checklist_item](#add_checklist_item)
  - [toggle_checklist_item](#toggle_checklist_item)
  - [update_checklist_item](#update_checklist_item)
  - [view_checklist](#view_checklist)
  - [list_checklists](#list_checklists)
- [TypeScript-specific](#typescript-specific)
  - [search_typescript](#search_typescript)
  - [typescript_go_to_definition](#typescript_go_to_definition)
  - [typescript_find_references](#typescript_find_references)
  - [typescript_rename_symbol](#typescript_rename_symbol)
- [Utilities](#utilities)
  - [set_logging](#set_logging)
  - [get_version](#get_version)

## Search & Navigation (Core Tools)

### go_to_definition

Navigate to the definition of a symbol at a specific position. Works for both C# and TypeScript.

**Parameters:**
- `filePath` (string, required): The absolute path to the file
- `line` (integer, required): The line number (1-based)
- `column` (integer, required): The column number (1-based)

**Example:**
```
go_to_definition --filePath "C:/project/UserService.cs" --line 25 --column 15
```

**Returns:**
```json
{
  "success": true,
  "locations": [
    {
      "filePath": "C:/project/Models/User.cs",
      "line": 10,
      "column": 14,
      "preview": "public class User",
      "symbolName": "User",
      "kind": "Class"
    }
  ]
}
```

**Tips:**
- Automatically delegates to TypeScript for .ts/.js files
- Returns multiple locations for partial classes
- ~50ms response time for cached workspaces

### find_references 🔍

Find all references to a symbol. Supports both C# and TypeScript. This is the V2 AI-optimized version with intelligent summaries and insights.

**Parameters:**
- `filePath` (string, required): The absolute path to the file
- `line` (integer, required): The line number (1-based)
- `column` (integer, required): The column number (1-based)
- `includeDeclaration` (boolean, optional): Include the declaration (default: true)
- `responseMode` (string, optional): "full" or "summary" (auto-switches for large results)

**Example:**
```
find_references --filePath "C:/project/Models/User.cs" --line 10 --column 14
```

**Returns:**
```json
{
  "success": true,
  "mode": "full",
  "references": [
    {
      "filePath": "C:/project/UserService.cs",
      "line": 25,
      "column": 15,
      "preview": "var user = new User();",
      "kind": "ObjectCreation"
    }
  ],
  "totalCount": 47,
  "estimatedTokens": 3500
}
```

**V2 Summary Mode Example:**
```json
{
  "success": true,
  "mode": "summary",
  "autoModeSwitch": true,
  "data": {
    "totalReferences": 47,
    "fileCount": 12,
    "summary": "UserService is heavily referenced across the codebase",
    "insights": [
      "Primary usage in Controllers (65% of references)",
      "Consider interface segregation - only 3 methods actually used"
    ],
    "distribution": {
      "Controllers": 31,
      "Services": 12,
      "Tests": 4
    },
    "hotspots": [
      {"file": "UserController.cs", "count": 15, "percentage": 31.9}
    ],
    "nextActions": [
      "Review UserController.cs for potential service injection optimization"
    ]
  }
}
```

**Tips:**
- Auto-switches to summary mode if results exceed 5000 tokens
- Use `responseMode: "summary"` for large codebases
- Includes semantic understanding (not just text matching)
- Provides actionable insights and recommendations

### search_symbols

Search for C# symbols by name pattern. Supports wildcards and fuzzy matching.

**Parameters:**
- `workspacePath` (string, required): Path to solution or project
- `searchPattern` (string, required): Search pattern (supports wildcards)
- `searchType` (string, optional): "exact", "contains", "startsWith", "wildcard", "fuzzy"
- `symbolTypes` (array, optional): Filter by types ["Class", "Interface", "Method", etc.]
- `caseSensitive` (boolean, optional): Case sensitive search (default: false)
- `maxResults` (integer, optional): Maximum results (default: 100)

**Example:**
```
search_symbols --workspacePath "C:/project/MyApp.sln" --searchPattern "*Service" --searchType "wildcard"
```

**Returns:**
```json
{
  "success": true,
  "symbols": [
    {
      "name": "UserService",
      "fullName": "MyApp.Services.UserService",
      "kind": "Class",
      "filePath": "C:/project/Services/UserService.cs",
      "line": 10,
      "column": 14
    }
  ],
  "totalCount": 15
}
```

**Tips:**
- ~100ms response time
- Use fuzzy matching for typo tolerance
- C# only - use `search_typescript` for TypeScript

### get_implementations

Find all implementations of an interface or abstract class (C# only).

**Parameters:**
- `filePath` (string, required): Path to the source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)

**Example:**
```
get_implementations --filePath "C:/project/IUserService.cs" --line 5 --column 18
```

**Returns:**
```json
{
  "success": true,
  "implementations": [
    {
      "filePath": "C:/project/Services/UserService.cs",
      "line": 10,
      "column": 14,
      "preview": "public class UserService : IUserService",
      "symbolName": "UserService"
    }
  ]
}
```

## Fast Search Tools (Lucene-powered)

### index_workspace

Build or rebuild the Lucene search index. Required before using fast_* tools!

**Parameters:**
- `workspacePath` (string, required): The workspace path to index
- `forceRebuild` (boolean, optional): Force rebuild even if index exists (default: false)

**Example:**
```
index_workspace --workspacePath "C:/project" --forceRebuild false
```

**Returns:**
```json
{
  "success": true,
  "message": "Workspace indexed successfully",
  "stats": {
    "filesIndexed": 1250,
    "indexSizeBytes": 15728640,
    "indexingTimeMs": 8500,
    "workspacePath": "C:/project",
    "indexPath": "C:/project/.codesearch/index/project_49f28c554fe686c2"
  }
}
```

**Tips:**
- Run this first before any fast_* searches
- Takes 5-60 seconds depending on codebase size
- Auto-reindexes on startup if configured
- File watcher updates index automatically

### fast_text_search

Blazing fast text search across entire codebase. Searches millions of lines in <50ms.

**Parameters:**
- `query` (string, required): Text to search for
- `workspacePath` (string, required): Path to workspace
- `searchType` (string, optional): "standard", "wildcard", "fuzzy", "phrase", "regex"
- `caseSensitive` (boolean, optional): Case sensitive search (default: false)
- `filePattern` (string, optional): Filter by file pattern (e.g., "*.cs")
- `extensions` (array, optional): Filter by extensions [".cs", ".ts"]
- `contextLines` (integer, optional): Lines of context to show (default: 0)
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
fast_text_search --query "authentication" --workspacePath "C:/project" --contextLines 2
```

**Returns:**
```json
{
  "success": true,
  "query": "authentication",
  "totalResults": 156,
  "results": [
    {
      "filePath": "C:/project/Services/AuthService.cs",
      "fileName": "AuthService.cs",
      "line": 45,
      "content": "    public async Task<bool> ValidateAuthentication(string token)",
      "context": [
        {"lineNumber": 43, "content": "    /// <summary>", "isMatch": false},
        {"lineNumber": 44, "content": "    /// Validates user authentication token", "isMatch": false},
        {"lineNumber": 45, "content": "    public async Task<bool> ValidateAuthentication(string token)", "isMatch": true},
        {"lineNumber": 46, "content": "    {", "isMatch": false},
        {"lineNumber": 47, "content": "        if (string.IsNullOrEmpty(token))", "isMatch": false}
      ],
      "score": 0.8765
    }
  ],
  "searchDurationMs": 42,
  "performance": "blazin' fast"
}
```

**Tips:**
- Supports wildcards (*), fuzzy (~), phrases ("exact match")
- Use filePattern for faster searches in specific areas
- Regex support for complex patterns
- Results include relevance scores

### fast_file_search

Find files by name with fuzzy matching and typo correction.

**Parameters:**
- `query` (string, required): File name to search for
- `workspacePath` (string, required): Path to workspace
- `searchType` (string, optional): "standard", "fuzzy", "wildcard", "exact", "regex"
- `includeDirectories` (boolean, optional): Include directory names (default: false)
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
fast_file_search --query "UserServce~" --workspacePath "C:/project" --searchType "fuzzy"
```

**Returns:**
```json
{
  "success": true,
  "query": "UserServce~",
  "searchType": "fuzzy",
  "results": [
    {
      "path": "C:/project/Services/UserService.cs",
      "filename": "UserService.cs",
      "relativePath": "Services/UserService.cs",
      "extension": ".cs",
      "size": 5234,
      "lastModified": "2025-01-22T10:30:00",
      "score": 0.95,
      "language": "csharp"
    }
  ],
  "searchDurationMs": 8,
  "performance": "blazin' fast"
}
```

**Tips:**
- Fuzzy search finds files despite typos
- < 10ms response time typically
- Wildcard support: User*.cs
- Shows file size and last modified

### fast_recent_files

Find recently changed files in the last 30min/24h/7d/etc.

**Parameters:**
- `workspacePath` (string, required): Path to workspace
- `timeFrame` (string, optional): "30m", "24h", "7d", "4w" (default: "24h")
- `extensions` (array, optional): Filter by extensions
- `filePattern` (string, optional): Filter by pattern
- `includeSize` (boolean, optional): Include file size info (default: true)
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
fast_recent_files --workspacePath "C:/project" --timeFrame "4h" --extensions [".cs", ".ts"]
```

**Returns:**
```json
{
  "success": true,
  "timeFrame": "4h",
  "cutoffTime": "2025-01-22T12:00:00Z",
  "results": [
    {
      "path": "C:/project/Services/AuthService.cs",
      "filename": "AuthService.cs",
      "lastModified": "2025-01-22T15:45:00",
      "timeAgo": "15 minutes ago",
      "size": 8234,
      "sizeFormatted": "8.04 KB"
    }
  ],
  "searchDurationMs": 12
}
```

**Tips:**
- Perfect for resuming work
- Shows friendly "time ago" format
- Instant results from index

### fast_file_size_analysis

Analyze files by size - find large files, empty files, or size distributions.

**Parameters:**
- `workspacePath` (string, required): Path to analyze
- `mode` (string, optional): "largest", "smallest", "range", "zero", "distribution"
- `minSize` (integer, optional): Minimum size in bytes (for "range" mode)
- `maxSize` (integer, optional): Maximum size in bytes (for "range" mode)
- `extensions` (array, optional): Filter by extensions
- `includeAnalysis` (boolean, optional): Include distribution analysis (default: true)
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
fast_file_size_analysis --workspacePath "C:/project" --mode "largest" --maxResults 10
```

**Returns:**
```json
{
  "success": true,
  "mode": "largest",
  "results": [
    {
      "path": "C:/project/Data/LargeDataset.json",
      "size": 15728640,
      "sizeFormatted": "15.0 MB",
      "relativePath": "Data/LargeDataset.json"
    }
  ],
  "analysis": {
    "totalFiles": 1250,
    "totalSize": 52428800,
    "averageSize": 41943,
    "distribution": {
      "0-1KB": 450,
      "1KB-10KB": 600,
      "10KB-100KB": 150,
      "100KB-1MB": 45,
      "1MB+": 5
    }
  }
}
```

### fast_similar_files

Find files with similar content using "More Like This" algorithm.

**Parameters:**
- `sourceFilePath` (string, required): Path to source file
- `workspacePath` (string, required): Path to workspace
- `minTermFreq` (integer, optional): Min term frequency (default: 2)
- `minDocFreq` (integer, optional): Min document frequency (default: 2)
- `minWordLength` (integer, optional): Min word length (default: 4)
- `maxWordLength` (integer, optional): Max word length (default: 30)
- `excludeExtensions` (array, optional): Extensions to exclude
- `includeScore` (boolean, optional): Include similarity scores (default: true)
- `maxResults` (integer, optional): Maximum results (default: 10)

**Example:**
```
fast_similar_files --sourceFilePath "C:/project/UserService.cs" --workspacePath "C:/project"
```

**Returns:**
```json
{
  "success": true,
  "sourceFile": "C:/project/UserService.cs",
  "similarFiles": [
    {
      "path": "C:/project/Services/CustomerService.cs",
      "relativePath": "Services/CustomerService.cs",
      "score": 0.876,
      "matchingTerms": ["service", "repository", "async", "validation"],
      "topTerms": {
        "service": 15,
        "user": 12,
        "async": 8
      }
    }
  ]
}
```

**Tips:**
- Great for finding duplicate code
- Shows why files are similar
- Adjust term frequencies for precision

### fast_directory_search

Search for directories/folders with fuzzy matching.

**Parameters:**
- `query` (string, required): Directory name to search
- `workspacePath` (string, required): Path to workspace
- `searchType` (string, optional): "standard", "fuzzy", "wildcard", "exact", "regex"
- `includeFileCount` (boolean, optional): Include file counts (default: true)
- `groupByDirectory` (boolean, optional): Group by unique directories (default: true)
- `maxResults` (integer, optional): Maximum results (default: 30)

**Example:**
```
fast_directory_search --query "Servces~" --workspacePath "C:/project" --searchType "fuzzy"
```

**Returns:**
```json
{
  "success": true,
  "query": "Servces~",
  "results": [
    {
      "path": "C:/project/Services",
      "name": "Services",
      "relativePath": "Services",
      "fileCount": 25,
      "score": 0.95
    }
  ]
}
```

## Code Analysis

### get_hover_info

Get detailed type information and documentation (like IDE hover tooltips).

**Parameters:**
- `filePath` (string, required): Path to source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)

**Example:**
```
get_hover_info --filePath "C:/project/UserService.cs" --line 25 --column 20
```

**Returns:**
```json
{
  "success": true,
  "info": {
    "symbolName": "CreateUserAsync",
    "type": "Task<User>",
    "documentation": "Creates a new user in the system",
    "signature": "public async Task<User> CreateUserAsync(UserDto dto)",
    "namespace": "MyApp.Services"
  }
}
```

**Tips:**
- Works for both C# and TypeScript
- Shows XML documentation when available
- Includes parameter information

### get_document_symbols

Get complete outline of all symbols in a file (C# only).

**Parameters:**
- `filePath` (string, required): Path to source file
- `includeMembers` (boolean, optional): Include class members (default: true)

**Example:**
```
get_document_symbols --filePath "C:/project/UserService.cs"
```

**Returns:**
```json
{
  "success": true,
  "symbols": [
    {
      "name": "UserService",
      "kind": "Class",
      "line": 10,
      "children": [
        {
          "name": "CreateUserAsync",
          "kind": "Method",
          "line": 25,
          "modifiers": ["public", "async"]
        }
      ]
    }
  ]
}
```

### get_diagnostics

Check for compilation errors and warnings (C# only).

**Parameters:**
- `path` (string, required): File, project, or solution path
- `severities` (array, optional): Filter by ["Error", "Warning", "Info"]
- `includeSuppressions` (boolean, optional): Include suppressed diagnostics
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
get_diagnostics --path "C:/project/MyApp.sln" --severities ["Error", "Warning"]
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "summary": {
    "totalDiagnostics": 15,
    "byServerity": {
      "Error": 2,
      "Warning": 13
    },
    "hotspots": [
      {
        "file": "UserService.cs",
        "count": 5
      }
    ]
  }
}
```

### get_call_hierarchy

Trace method call chains to understand execution flow (C# only).

**Parameters:**
- `filePath` (string, required): Path to source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)
- `direction` (string, optional): "incoming", "outgoing", or "both"
- `maxDepth` (integer, optional): Maximum depth to traverse (default: 3)

**Example:**
```
get_call_hierarchy --filePath "C:/project/UserService.cs" --line 25 --column 20 --direction "incoming"
```

**Returns:**
```json
{
  "success": true,
  "hierarchy": {
    "root": {
      "name": "CreateUserAsync",
      "file": "UserService.cs",
      "callers": [
        {
          "name": "RegisterUser",
          "file": "AuthController.cs",
          "line": 45,
          "callers": []
        }
      ]
    }
  }
}
```

### rename_symbol

Safely rename symbols across entire codebase (C# only).

**Parameters:**
- `filePath` (string, required): Path to source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)
- `newName` (string, required): New name for the symbol
- `preview` (boolean, optional): Preview without applying (default: true)
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
rename_symbol --filePath "C:/project/User.cs" --line 10 --column 14 --newName "Customer" --preview true
```

**Returns:**
```json
{
  "success": true,
  "preview": true,
  "changes": [
    {
      "filePath": "C:/project/User.cs",
      "changes": [
        {
          "line": 10,
          "oldText": "public class User",
          "newText": "public class Customer"
        }
      ]
    }
  ],
  "totalFiles": 15,
  "totalChanges": 47
}
```

**Tips:**
- Always preview first
- Uses Roslyn refactoring engine
- Updates all references automatically

### batch_operations

Run multiple operations in parallel for 10x faster analysis.

**Parameters:**
- `workspacePath` (string, required): Path to workspace
- `operations` (array, required): Array of operations to execute

**Example:**
```
batch_operations --workspacePath "C:/project" --operations [
  {"type": "text_search", "query": "TODO", "filePattern": "*.cs"},
  {"type": "find_references", "filePath": "User.cs", "line": 10, "column": 14},
  {"type": "search_symbols", "searchPattern": "*Service"}
]
```

**Returns:**
```json
{
  "success": true,
  "results": [
    {
      "operation": "text_search",
      "success": true,
      "result": { /* text search results */ }
    },
    {
      "operation": "find_references",
      "success": true,
      "result": { /* references */ }
    }
  ],
  "executionTimeMs": 250
}
```

**Tips:**
- Dramatically faster than sequential
- Supports most major tool types
- Great for comprehensive analysis

### advanced_symbol_search

Search C# symbols with semantic filters.

**Parameters:**
- `workspacePath` (string, required): Path to workspace
- `query` (string, required): Search query
- `filters` (object, optional): Advanced filters
  - `accessibility`: ["public", "private", "internal"]
  - `modifiers`: ["static", "abstract", "virtual"]
  - `symbolKinds`: ["Class", "Interface", "Method"]
  - `namespaces`: ["MyApp.Services"]
  - `hasAttributes`: ["Authorize", "Obsolete"]
  - `returnType`: "Task<*>"

**Example:**
```
advanced_symbol_search --workspacePath "C:/project" --query "Service" --filters {
  "accessibility": ["public"],
  "symbolKinds": ["Class"],
  "modifiers": ["abstract"]
}
```

### dependency_analysis

Analyze code dependencies to understand coupling (C# only).

**Parameters:**
- `symbol` (string, required): Symbol to analyze
- `workspacePath` (string, required): Path to workspace
- `direction` (string, optional): "incoming", "outgoing", or "both"
- `depth` (integer, optional): Analysis depth (default: 3)
- `includeExternalDependencies` (boolean, optional): Include external deps
- `includeTests` (boolean, optional): Include test projects
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
dependency_analysis --symbol "UserService" --workspacePath "C:/project" --direction "both"
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "analysis": {
    "symbol": "UserService",
    "directDependencies": 5,
    "transitiveDependencies": 15,
    "circularDependencies": ["UserService -> AuthService -> UserService"],
    "couplingScore": 0.72,
    "recommendations": [
      "Consider extracting interface for UserService",
      "Circular dependency detected with AuthService"
    ]
  }
}
```

### project_structure_analysis

Analyze .NET project structure with metrics (.NET only).

**Parameters:**
- `workspacePath` (string, required): Path to solution/project
- `includeFiles` (boolean, optional): Include file listings
- `includeMetrics` (boolean, optional): Include code metrics
- `includeNuGetPackages` (boolean, optional): Include packages
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
project_structure_analysis --workspacePath "C:/project/MyApp.sln" --responseMode "summary"
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "summary": {
    "totalProjects": 8,
    "totalFiles": 1250,
    "totalLinesOfCode": 125000,
    "languages": {
      "C#": 95,
      "TypeScript": 3,
      "Other": 2
    },
    "largestProjects": [
      {
        "name": "MyApp.Core",
        "files": 450,
        "linesOfCode": 45000
      }
    ],
    "metrics": {
      "averageComplexity": 3.2,
      "highComplexityMethods": 15
    }
  }
}
```

## Memory System

### recall_context

Load relevant context at session start. Essential first step!

**Parameters:**
- `query` (string, required): What you're working on
- `maxResults` (integer, optional): Maximum results (default: 10)
- `scopeFilter` (string, optional): Filter by type

**Example:**
```
recall_context --query "authentication refactoring"
```

**Returns:**
```json
{
  "success": true,
  "message": "🧠 RECALLED MEMORIES FOR: authentication refactoring",
  "memories": [
    {
      "id": "abc123",
      "type": "ArchitecturalDecision",
      "content": "Decided to use JWT tokens for authentication",
      "created": "2025-01-20T10:00:00",
      "relevanceScore": 0.95
    }
  ]
}
```

**Tips:**
- ALWAYS use at session start
- Loads past decisions and context
- Natural language queries work best

### flexible_store_memory

Store any type of memory with custom fields.

**Parameters:**
- `type` (string, required): Memory type (e.g., "ArchitecturalDecision", "TechnicalDebt")
- `content` (string, required): Main content
- `fields` (object, optional): Custom fields as JSON
- `files` (array, optional): Related files
- `isShared` (boolean, optional): Share with team (default: true)
- `sessionId` (string, optional): Session ID

**Example:**
```
flexible_store_memory --type "ArchitecturalDecision" --content "Using repository pattern for data access" --fields {"status": "approved", "impact": "high"} --files ["IRepository.cs"]
```

**Returns:**
```json
{
  "success": true,
  "memoryId": "def456",
  "message": "Successfully stored ArchitecturalDecision memory"
}
```

**Common Types:**
- ArchitecturalDecision
- TechnicalDebt
- CodePattern
- SecurityRule
- ProjectInsight
- Question
- DeferredTask

### flexible_search_memories

Search stored memories with advanced filtering.

**Parameters:**
- `query` (string, optional): Search query (* for all)
- `types` (array, optional): Filter by memory types
- `dateRange` (string, optional): "last-week", "last-month", etc.
- `facets` (object, optional): Field filters {"status": "pending"}
- `includeArchived` (boolean, optional): Include archived (default: false)
- `orderBy` (string, optional): Sort field
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
flexible_search_memories --query "authentication" --types ["ArchitecturalDecision"] --facets {"status": "approved"}
```

**Returns:**
```json
{
  "memories": [
    {
      "id": "abc123",
      "type": "ArchitecturalDecision",
      "content": "JWT authentication implementation",
      "fields": {"status": "approved"},
      "created": "2025-01-20T10:00:00"
    }
  ],
  "totalFound": 5,
  "facetCounts": {
    "type": {"ArchitecturalDecision": 3, "TechnicalDebt": 2},
    "status": {"approved": 3, "pending": 2}
  }
}
```

### flexible_update_memory

Update existing memory content and fields.

**Parameters:**
- `id` (string, required): Memory ID to update
- `content` (string, optional): New content
- `fieldUpdates` (object, optional): Field updates (null removes field)
- `addFiles` (array, optional): Files to add
- `removeFiles` (array, optional): Files to remove

**Example:**
```
flexible_update_memory --id "abc123" --fieldUpdates {"status": "implemented", "completedDate": "2025-01-22"}
```

### flexible_get_memory

Retrieve specific memory by ID.

**Parameters:**
- `id` (string, required): Memory ID

**Example:**
```
flexible_get_memory --id "abc123"
```

### flexible_store_working_memory

Store temporary memories with auto-expiration.

**Parameters:**
- `content` (string, required): What to remember
- `expiresIn` (string, optional): "end-of-session", "1h", "4h", "24h", "7d"
- `fields` (object, optional): Custom fields
- `files` (array, optional): Related files
- `sessionId` (string, optional): Session ID

**Example:**
```
flexible_store_working_memory --content "User wants OAuth2 instead of JWT" --expiresIn "4h"
```

**Tips:**
- Perfect for temporary notes
- Auto-expires, no cleanup needed
- Always local (not shared)

### flexible_find_similar_memories

Find memories with similar content.

**Parameters:**
- `memoryId` (string, required): Source memory ID
- `maxResults` (integer, optional): Maximum results (default: 10)

**Example:**
```
flexible_find_similar_memories --memoryId "abc123"
```

### flexible_archive_memories

Archive old memories to reduce clutter.

**Parameters:**
- `type` (string, required): Memory type to archive
- `daysOld` (integer, required): Archive older than N days

**Example:**
```
flexible_archive_memories --type "WorkSession" --daysOld 30
```

### flexible_summarize_memories

Summarize old memories to save space.

**Parameters:**
- `type` (string, required): Memory type
- `daysOld` (integer, required): Summarize older than N days
- `batchSize` (integer, optional): Memories per summary (default: 10)
- `preserveOriginals` (boolean, optional): Keep originals (default: false)

**Example:**
```
flexible_summarize_memories --type "WorkSession" --daysOld 30 --batchSize 10
```

**Returns:**
```json
{
  "success": true,
  "summariesCreated": 3,
  "memoriesSummarized": 30,
  "summaries": [
    {
      "id": "summary123",
      "content": "Work sessions from Jan 1-10: Focused on authentication refactoring...",
      "keyThemes": ["authentication", "JWT", "security"],
      "dateRange": "2025-01-01 to 2025-01-10"
    }
  ]
}
```

### flexible_get_memory_suggestions

Get context-aware suggestions based on current work.

**Parameters:**
- `currentContext` (string, required): What you're working on
- `currentFile` (string, optional): Current file path
- `recentFiles` (array, optional): Recently accessed files
- `maxSuggestions` (integer, optional): Maximum suggestions (default: 5)

**Example:**
```
flexible_get_memory_suggestions --currentContext "implementing user authentication" --currentFile "AuthService.cs"
```

### flexible_list_templates

List available memory templates.

**Parameters:** None

**Example:**
```
flexible_list_templates
```

**Returns:**
```json
{
  "templates": [
    {
      "id": "code-review",
      "name": "Code Review Finding",
      "type": "CodeReview",
      "placeholders": ["fileName", "issue", "severity", "suggestion"]
    }
  ]
}
```

### flexible_create_from_template

Create memory from template.

**Parameters:**
- `templateId` (string, required): Template ID
- `placeholders` (object, required): Placeholder values
- `additionalFields` (object, optional): Extra fields
- `files` (array, optional): Related files

**Example:**
```
flexible_create_from_template --templateId "code-review" --placeholders {
  "fileName": "UserService.cs",
  "issue": "Missing null checks",
  "severity": "medium",
  "suggestion": "Add null validation"
}
```

### flexible_store_git_commit

Link memory to a Git commit.

**Parameters:**
- `sha` (string, required): Git commit SHA
- `message` (string, required): Commit message
- `description` (string, required): Additional insights
- `author` (string, optional): Commit author
- `branch` (string, optional): Branch name
- `commitDate` (string, optional): ISO date
- `filesChanged` (array, optional): Changed files
- `additionalFields` (object, optional): Extra fields

**Example:**
```
flexible_store_git_commit --sha "abc123def" --message "Refactor authentication" --description "Implemented JWT tokens for better security"
```

### flexible_memories_for_file

Find all memories related to a specific file.

**Parameters:**
- `filePath` (string, required): File path (absolute or relative)
- `includeArchived` (boolean, optional): Include archived (default: false)

**Example:**
```
flexible_memories_for_file --filePath "Services/AuthService.cs"
```

### flexible_link_memories

Create relationship between memories.

**Parameters:**
- `sourceId` (string, required): Source memory ID
- `targetId` (string, required): Target memory ID
- `relationshipType` (string, optional): Type of relationship (default: "relatedTo")
- `bidirectional` (boolean, optional): Create in both directions (default: false)

**Relationship Types:**
- relatedTo (default)
- blockedBy / blocks
- implements / implementedBy
- supersedes / supersededBy
- parentOf / childOf
- resolves / resolvedBy
- duplicates / duplicatedBy

**Example:**
```
flexible_link_memories --sourceId "bug123" --targetId "fix456" --relationshipType "resolvedBy"
```

### flexible_get_related_memories

Get all memories related to a given memory.

**Parameters:**
- `memoryId` (string, required): Memory ID
- `maxDepth` (integer, optional): Traversal depth (default: 2)
- `relationshipTypes` (array, optional): Filter by types

**Example:**
```
flexible_get_related_memories --memoryId "epic001" --maxDepth 3
```

### flexible_unlink_memories

Remove relationship between memories.

**Parameters:**
- `sourceId` (string, required): Source memory ID
- `targetId` (string, required): Target memory ID
- `relationshipType` (string, optional): Type to remove (default: "relatedTo")
- `bidirectional` (boolean, optional): Remove in both directions

**Example:**
```
flexible_unlink_memories --sourceId "bug123" --targetId "fix456"
```

### memory_dashboard

Get memory system statistics and health.

**Parameters:** None

**Example:**
```
memory_dashboard
```

**Returns:**
```json
{
  "success": true,
  "stats": {
    "totalMemories": 1250,
    "byType": {
      "ArchitecturalDecision": 45,
      "TechnicalDebt": 120,
      "WorkSession": 850
    },
    "storageSize": "15.2 MB",
    "oldestMemory": "2024-01-15",
    "recentActivity": {
      "last24h": 25,
      "last7d": 150
    }
  },
  "health": {
    "indexHealth": "healthy",
    "recommendations": [
      "Consider archiving WorkSessions older than 30 days"
    ]
  }
}
```

### backup_memories_to_sqlite

Backup memories for version control.

**Parameters:**
- `scopes` (array, optional): Memory types to backup
- `includeLocal` (boolean, optional): Include local memories (default: false)

**Example:**
```
backup_memories_to_sqlite --scopes ["ArchitecturalDecision", "CodePattern"]
```

**Returns:**
```json
{
  "success": true,
  "backupPath": "C:/project/.codesearch/backups/backup_20250122_103000",
  "stats": {
    "memoriesBackedUp": 165,
    "backupSizeBytes": 524288,
    "tablesCreated": ["memories", "relationships", "metadata"]
  }
}
```

**Tips:**
- Default backs up only project-level memories
- Check in memories.db to version control
- Use for team sharing

### restore_memories_from_sqlite

Restore memories from backup.

**Parameters:**
- `scopes` (array, optional): Memory types to restore
- `includeLocal` (boolean, optional): Include local memories (default: false)

**Example:**
```
restore_memories_from_sqlite
```

## Checklist System

### create_checklist

Create persistent checklist for task tracking.

**Parameters:**
- `title` (string, required): Checklist title
- `description` (string, optional): Description
- `isShared` (boolean, optional): Share with team (default: true)
- `customFields` (object, optional): Custom fields
- `sessionId` (string, optional): Session ID

**Example:**
```
create_checklist --title "API Refactoring Tasks" --description "Breaking changes for v2.0"
```

**Returns:**
```json
{
  "success": true,
  "checklistId": "checklist789",
  "message": "Checklist created successfully"
}
```

### add_checklist_item

Add item to existing checklist.

**Parameters:**
- `checklistId` (string, required): Checklist ID
- `itemText` (string, required): Item description
- `notes` (string, optional): Additional notes
- `relatedFiles` (array, optional): Related files
- `customFields` (object, optional): Custom fields

**Example:**
```
add_checklist_item --checklistId "checklist789" --itemText "Update authentication endpoints" --notes "Switch to JWT"
```

### toggle_checklist_item

Toggle item completion status.

**Parameters:**
- `itemId` (string, required): Item ID
- `isCompleted` (boolean, optional): Set specific status
- `completedBy` (string, optional): Who completed it

**Example:**
```
toggle_checklist_item --itemId "item123" --isCompleted true --completedBy "John"
```

### update_checklist_item

Update checklist item details.

**Parameters:**
- `itemId` (string, required): Item ID
- `newText` (string, optional): New text
- `notes` (string, optional): Updated notes
- `customFields` (object, optional): Field updates

**Example:**
```
update_checklist_item --itemId "item123" --notes "Postponed to next sprint"
```

### view_checklist

View checklist with progress.

**Parameters:**
- `checklistId` (string, required): Checklist ID
- `includeCompleted` (boolean, optional): Show completed items (default: true)
- `exportAsMarkdown` (boolean, optional): Export as markdown (default: false)

**Example:**
```
view_checklist --checklistId "checklist789" --exportAsMarkdown true
```

**Returns:**
```json
{
  "success": true,
  "checklist": {
    "id": "checklist789",
    "title": "API Refactoring Tasks",
    "status": "in-progress",
    "progressPercentage": 60,
    "items": [
      {
        "id": "item123",
        "text": "Update authentication endpoints",
        "isCompleted": true,
        "completedAt": "2025-01-22T10:00:00"
      }
    ]
  },
  "markdown": "# API Refactoring Tasks\n\n- [x] Update authentication endpoints"
}
```

### list_checklists

List all available checklists.

**Parameters:**
- `includeCompleted` (boolean, optional): Include completed (default: true)
- `onlyShared` (boolean, optional): Only shared checklists (default: false)
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
list_checklists --onlyShared true
```

## TypeScript-specific

### search_typescript

Search for TypeScript symbols.

**Parameters:**
- `symbolName` (string, required): Symbol name to search
- `workspacePath` (string, required): Path to workspace
- `mode` (string, optional): "definition", "references", or "both"
- `maxResults` (integer, optional): Maximum results (default: 50)

**Example:**
```
search_typescript --symbolName "UserComponent" --workspacePath "C:/project" --mode "both"
```

**Returns:**
```json
{
  "success": true,
  "results": [
    {
      "name": "UserComponent",
      "kind": "class",
      "filePath": "C:/project/components/UserComponent.tsx",
      "line": 15,
      "column": 7
    }
  ]
}
```

### typescript_go_to_definition

Navigate to TypeScript definitions using tsserver.

**Parameters:**
- `filePath` (string, required): Path to source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)

**Example:**
```
typescript_go_to_definition --filePath "C:/project/app.ts" --line 10 --column 15
```

**Tips:**
- Uses real TypeScript language server
- Handles imports and aliases correctly
- ~100ms response time

### typescript_find_references

Find all TypeScript references using tsserver.

**Parameters:**
- `filePath` (string, required): Path to source file
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)
- `includeDeclaration` (boolean, optional): Include declaration (default: true)

**Example:**
```
typescript_find_references --filePath "C:/project/models/User.ts" --line 5 --column 10
```

### typescript_rename_symbol

Rename TypeScript symbols with preview of all affected locations.

**Parameters:**
- `filePath` (string, required): Path to source file  
- `line` (integer, required): Line number (1-based)
- `column` (integer, required): Column number (1-based)
- `newName` (string, required): New name for the symbol
- `preview` (boolean, optional): Preview mode (default: true)

**Example:**
```
typescript_rename_symbol --filePath "C:/project/models/User.ts" --line 10 --column 5 --newName "Customer"
```

**Returns:**
```json
{
  "success": true,
  "preview": true,
  "symbol": {
    "name": "User",
    "kind": "interface",
    "fullName": "User"
  },
  "newName": "Customer",
  "locations": [
    {
      "filePath": "C:/project/models/User.ts",
      "line": 10,
      "column": 5
    }
  ],
  "fileChanges": [
    {
      "filePath": "C:/project/models/User.ts",
      "changes": [
        {
          "line": 10,
          "column": 5,
          "oldText": "User",
          "newText": "Customer"
        }
      ]
    }
  ],
  "metadata": {
    "totalChanges": 15,
    "filesAffected": 5,
    "language": "typescript"
  }
}
```

**Tips:**
- Validates TypeScript identifiers
- Shows all locations that would be renamed
- Preview mode doesn't modify files
- Uses tsserver for accurate rename analysis

## Utilities

### set_logging

Control file-based logging for debugging.

**Parameters:**
- `action` (string, required): "start", "stop", "status", "list", "setlevel", "cleanup"
- `level` (string, optional): For start/setlevel: "Verbose", "Debug", "Information", "Warning", "Error"
- `cleanup` (boolean, optional): For cleanup action

**Example:**
```
set_logging --action start --level Debug
```

**Returns:**
```json
{
  "success": true,
  "message": "Logging started at Debug level",
  "logDirectory": "C:/project/.codesearch/logs"
}
```

**Tips:**
- Logs never go to stdout (MCP safe)
- Auto-rotates hourly
- Location: `.codesearch/logs/`

### get_version

Get server version and build info.

**Parameters:** None

**Example:**
```
get_version
```

**Returns:**
```json
{
  "success": true,
  "version": {
    "assembly": "1.5.0.0",
    "file": "1.5.202.1440",
    "informational": "1.5.202.1440+local.abc123",
    "semantic": "1.5.202"
  },
  "build": {
    "date": "2025-01-22T10:00:00Z",
    "compiledFrom": "C:/source/codesearch",
    "configuration": "Release"
  },
  "runtime": {
    "framework": ".NET 9.0.0",
    "os": "Microsoft Windows 10.0.19045",
    "serverStarted": "2025-01-22T09:00:00Z",
    "uptime": "1h 30m"
  }
}
```

## Common Patterns

### Session Startup Pattern
```
1. recall_context "working on authentication"
2. index_workspace --workspacePath "C:/project"
3. set_logging --action start --level Debug
```

### Code Investigation Pattern
```
1. fast_text_search --query "TODO" --workspacePath "C:/project"
2. find_references --filePath "found-file.cs" --line X --column Y
3. get_call_hierarchy --filePath "found-file.cs" --line X --column Y
4. flexible_store_memory --type "TechnicalDebt" --content "Found TODO that needs attention"
```

### Refactoring Pattern
```
1. search_symbols --searchPattern "OldName*"
2. find_references (for each symbol found)
3. rename_symbol --newName "NewName" --preview true
4. rename_symbol --newName "NewName" --preview false
5. flexible_store_memory --type "ArchitecturalDecision" --content "Renamed X to Y because..."
```

## Performance Tips

1. **Always index first**: Run `index_workspace` before using fast_* tools
2. **Use batch operations**: 10x faster than sequential operations
3. **Leverage summary mode**: For large results, use `responseMode: "summary"`
4. **Cache workspaces**: The server caches up to 5 workspaces by default
5. **Filter early**: Use filePattern and extensions to narrow searches

## Language-Specific Notes

### C# Only Tools
- search_symbols
- get_implementations
- get_document_symbols
- get_diagnostics
- get_call_hierarchy
- rename_symbol
- advanced_symbol_search
- dependency_analysis
- project_structure_analysis

### TypeScript Only Tools
- search_typescript
- typescript_go_to_definition
- typescript_find_references
- typescript_rename_symbol

### Both C# and TypeScript
- go_to_definition
- find_references
- get_hover_info
- All fast_* search tools
- All memory tools
- All utility tools

## 🔍 V2 Tools - Detailed Documentation

### Overview
V2 tools are AI-optimized versions that provide intelligent analysis beyond raw data. They automatically adapt their responses based on result size and complexity.

### Available V2 Tools

#### Search & Navigation V2
- **find_references** - Already using V2 with AI summaries
- **search_symbols_v2** - Enhanced symbol search with distribution analysis
- **get_implementations_v2** - Implementation discovery with inheritance insights

#### Fast Search V2
- **fast_text_search_v2** - Text search with file distribution and pattern analysis
- **fast_file_search_v2** - File search with directory hotspot detection
- **flexible_search_memories_v2** - Memory search with trend analysis

#### Code Analysis V2
- **get_diagnostics** - Already using V2 with priority recommendations
- **get_call_hierarchy_v2** - Call analysis with circular dependency detection
- **rename_symbol** - Already using V2 with impact assessment
- **batch_operations_v2** - Batch execution with operation pattern analysis
- **dependency_analysis** - Already using V2 with architectural insights
- **project_structure_analysis** - Already using V2 with complexity metrics

### V2 Response Structure

All V2 tools follow a consistent response pattern:

```json
{
  "success": true,
  "mode": "summary" | "full",
  "autoModeSwitch": true,  // If auto-switched due to size
  "data": {
    // Core results
    "totalCount": 150,
    "summary": "High-level description",
    
    // AI Analysis
    "insights": [
      "Key finding 1",
      "Optimization opportunity"
    ],
    "patterns": {
      "trend": "description",
      "anomaly": "unusual finding"
    },
    "hotspots": [
      {"location": "file.cs", "metric": 45, "reason": "High concentration"}
    ],
    
    // Actionable Recommendations
    "nextActions": [
      "Specific action with command",
      "Follow-up analysis suggestion"
    ],
    "risks": ["Potential issue"],
    
    // Token Management
    "estimatedTokens": 4500,
    "detailRequestToken": "abc123"  // For progressive disclosure
  }
}
```

### Progressive Disclosure

Request specific details without re-running operations:

```json
{
  "detailRequest": {
    "detailRequestToken": "abc123",
    "detailLevel": "hotspots" | "full" | "category"
  }
}
```

### Progress Tracking

Tools with 🏗️ emit progress notifications:
- **index_workspace** - Reports indexing progress
- **batch_operations** - Progress for each operation

Progress notification format:
```json
{
  "progressToken": "operation-id",
  "progress": 50,
  "total": 100,
  "message": "Processing file 50/100"
}
```

## Best Practices

1. **Start with context**: Always use `recall_context` at session start
2. **Index before searching**: Run `index_workspace` for fast searches
3. **Use V2 tools**: They provide better insights for AI assistants
4. **Store decisions**: Use memory system to track important decisions
5. **Leverage summaries**: Let V2 tools auto-summarize large results
6. **Follow recommendations**: V2 tools provide actionable next steps
7. **Batch when possible**: Use batch_operations_v2 for multiple queries
8. **Preview destructive ops**: Always preview renames before applying