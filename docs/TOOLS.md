# CodeSearch MCP Server - Tool Reference

Complete documentation for all tools available in the CodeSearch MCP Server.

## Tool Categories

All code analysis tools now provide AI-optimized responses with:
- **Intelligent Summaries**: Automatic insights, patterns, and actionable recommendations
- **Token Management**: Auto-switches to summary mode for large results (>5,000 tokens)
- **Progressive Disclosure**: Request specific details without re-running operations
- **Pattern Recognition**: Identifies trends, hotspots, and optimization opportunities
- **Progress Tracking**: Real-time notifications for long-running operations

## Table of Contents

- [Text Search & Analysis (Core Tools)](#text-search--analysis-core-tools)
  - [index_workspace](#index_workspace) 🏗️
  - [text_search](#text_search)
  - [file_search](#file_search)
  - [recent_files](#recent_files)
  - [file_size_analysis](#file_size_analysis)
  - [similar_files](#similar_files)
  - [directory_search](#directory_search)
  - [batch_operations](#batch_operations)
- [Advanced AI Tools](#advanced-ai-tools)
  - [search_assistant](#search_assistant) 🤖
  - [pattern_detector](#pattern_detector) 🔍
  - [memory_graph_navigator](#memory_graph_navigator) 🗺️
  - [workflow_discovery](#workflow_discovery) ⚡ **NEW**
  - [tool_usage_analytics](#tool_usage_analytics) 📊
- [Memory System](#memory-system)
  - [recall_context](#recall_context)
  - [flexible_store_memory](#flexible_store_memory)
  - [flexible_search_memories](#flexible_search_memories)
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
  - [backup_memories](#backup_memories)
  - [restore_memories](#restore_memories)
- [Checklist System](#checklist-system)
  - [create_checklist](#create_checklist)
  - [add_checklist_items](#add_checklist_items)
  - [toggle_checklist_item](#toggle_checklist_item)
  - [update_checklist_item](#update_checklist_item)
  - [view_checklist](#view_checklist)
  - [list_checklists](#list_checklists)
- [Utilities](#utilities)
  - [index_health_check](#index_health_check)
  - [system_health_check](#system_health_check)
  - [log_diagnostics](#log_diagnostics)
  - [get_version](#get_version)

## Text Search & Analysis (Core Tools)


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

### text_search

Blazing fast text search across entire codebase with AI-optimized insights. Searches millions of lines in <50ms.

**Parameters:**
- `query` (string, required): Text to search for
- `workspacePath` (string, required): Path to workspace
- `searchType` (string, optional): "standard", "wildcard", "fuzzy", "phrase", "regex"
- `caseSensitive` (boolean, optional): Case sensitive search (default: false)
- `filePattern` (string, optional): Filter by file pattern (e.g., "*.cs")
- `extensions` (array, optional): Filter by extensions [".cs", ".ts"]
- `contextLines` (integer, optional): Lines of context to show (default: 0)
- `maxResults` (integer, optional): Maximum results (default: 50)
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
fast_text_search --query "authentication" --workspacePath "C:/project" --responseMode "summary"
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "data": {
    "totalMatches": 156,
    "fileCount": 23,
    "distribution": {
      "byExtension": {".cs": 120, ".ts": 36},
      "byDirectory": {"Services": 85, "Controllers": 45}
    },
    "insights": [
      "Authentication logic concentrated in Services layer",
      "Consider centralizing auth constants"
    ],
    "hotspots": [
      {"file": "AuthService.cs", "matches": 32}
    ]
  }
}
```

**Tips:**
- AI-optimized with pattern analysis
- Supports wildcards (*), fuzzy (~), phrases ("exact match")
- Use filePattern for faster searches in specific areas
- Provides insights on code organization

### file_search

Find files by name with fuzzy matching, typo correction, and AI-optimized insights.

**Parameters:**
- `query` (string, required): File name to search for
- `workspacePath` (string, required): Path to workspace
- `searchType` (string, optional): "standard", "fuzzy", "wildcard", "exact", "regex"
- `includeDirectories` (boolean, optional): Include directory names (default: false)
- `maxResults` (integer, optional): Maximum results (default: 50)
- `responseMode` (string, optional): "full" or "summary"

**Example:**
```
fast_file_search --query "UserServce~" --workspacePath "C:/project" --searchType "fuzzy"
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "data": {
    "totalFiles": 15,
    "topMatch": "UserService.cs",
    "distribution": {
      "byDirectory": {"Services": 8, "Tests": 5},
      "byExtension": {".cs": 12, ".csproj": 3}
    },
    "insights": [
      "Service files well-organized in Services directory",
      "Consider consistent file naming convention"
    ],
    "suggestions": [
      "Did you mean: UserService.cs?"
    ]
  }
}
```

**Tips:**
- AI-powered typo suggestions
- < 10ms response time typically
- Wildcard support: User*.cs
- Provides directory organization insights

### recent_files

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

### file_size_analysis

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

### similar_files

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

### directory_search

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


### batch_operations

Run multiple operations in parallel with AI-optimized pattern analysis and correlation insights.

**Parameters:**
- `operations` (array, required): Array of operations to execute
- `workspacePath` (string, optional): Default workspace path
- `mode` (string, optional): "summary" or "full"

**Example:**
```
batch_operations --operations [
  {"operation": "text_search", "query": "TODO", "filePattern": "*.cs"},
  {"operation": "find_references", "filePath": "User.cs", "line": 10, "column": 14},
  {"operation": "search_symbols", "pattern": "*Service"}
]
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "executionTime": 250,
  "summary": {
    "totalOperations": 3,
    "successful": 3,
    "failed": 0
  },
  "insights": [
    "TODO comments concentrated in Service layer",
    "User class heavily referenced - consider interface extraction"
  ],
  "correlations": [
    "Files with TODOs also have high reference counts"
  ],
  "results": {
    "text_search": {"matches": 15, "files": 8},
    "find_references": {"refs": 47, "files": 12},
    "search_symbols": {"symbols": 23}
  }
}
```

**Tips:**
- AI analyzes patterns across operations
- 10x faster than sequential execution
- Provides cross-operation insights
- Great for comprehensive codebase analysis

## Advanced AI Tools

### search_assistant

🤖 Orchestrates multi-step search operations with AI guidance and maintains context across searches.

**Parameters:**
- `goal` (string, required): The search objective (e.g., "Find all error handling patterns")
- `workspacePath` (string, required): Path to workspace
- `previousContext` (string, optional): Resource URI from previous search to build upon
- `constraints` (object, optional): Search constraints
  - `fileTypes` (array, optional): File types to include [".cs", ".ts", ".js"]
  - `excludePaths` (array, optional): Paths to exclude
  - `maxResults` (integer, optional): Maximum results per operation (default: 50)
- `responseMode` (string, optional): "summary" or "full" (default: "summary")

**Example:**
```
search_assistant --goal "Find all authentication patterns and vulnerabilities" --workspacePath "C:/project"
```

**Returns:**
```json
{
  "success": true,
  "strategy": {
    "searchApproach": "Multi-phase authentication analysis",
    "operations": ["text_search", "pattern_detector", "similar_files"],
    "rationale": "Comprehensive security-focused search strategy"
  },
  "findings": {
    "summary": "Found 23 authentication-related patterns across 15 files",
    "patterns": [
      {"type": "JWT_implementation", "locations": 8, "risk": "medium"},
      {"type": "password_validation", "locations": 5, "risk": "low"}
    ],
    "hotspots": [
      {"file": "AuthService.cs", "concerns": ["weak_validation", "jwt_expiry"]}
    ]
  },
  "insights": [
    "Authentication logic is centralized but lacks consistent error handling",
    "Consider implementing OAuth2 for better security"
  ],
  "nextSteps": [
    "Review JWT token expiry settings in AuthService.cs",
    "Audit password validation rules for security compliance"
  ],
  "resourceUri": "codesearch-search-assistant://session_abc123"
}
```

**Tips:**
- Automatically creates search strategies based on goals
- Maintains context between related searches
- Provides security-focused insights for authentication queries
- Returns resource URIs for continuing exploration

### pattern_detector

🔍 Analyzes codebase for architectural patterns, anti-patterns, security issues, and performance problems with severity classification.

**Parameters:**
- `workspacePath` (string, required): Directory path to analyze
- `patternTypes` (array, required): Types to detect ["architecture", "security", "performance", "testing"]
- `depth` (string, optional): "shallow" or "deep" analysis (default: "shallow")
- `createMemories` (boolean, optional): Auto-create memories for significant findings (default: false)
- `responseMode` (string, optional): "summary" or "full" (default: "summary")

**Example:**
```
pattern_detector --workspacePath "C:/project" --patternTypes ["security", "performance"] --depth "deep"
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "analysisType": "deep",
  "summary": {
    "patternsDetected": 47,
    "antiPatternsFound": 12,
    "severityBreakdown": {
      "critical": 2,
      "high": 8,
      "medium": 15,
      "low": 22
    }
  },
  "findings": {
    "security": [
      {
        "pattern": "SQL_injection_risk",
        "severity": "critical",
        "locations": ["UserRepository.cs:45", "OrderService.cs:123"],
        "description": "Direct string concatenation in SQL queries",
        "remediation": "Use parameterized queries or ORM"
      }
    ],
    "performance": [
      {
        "pattern": "N+1_query_problem", 
        "severity": "high",
        "locations": ["ProductService.cs:67"],
        "description": "Database queries in loops",
        "remediation": "Use eager loading or batch queries"
      }
    ]
  },
  "recommendations": {
    "immediate": [
      "Fix critical SQL injection vulnerabilities",
      "Implement input validation"
    ],
    "longTerm": [
      "Consider implementing query optimization patterns",
      "Add performance monitoring"
    ]
  },
  "metrics": {
    "codeQualityScore": 72,
    "securityScore": 45,
    "performanceScore": 68
  }
}
```

**Tips:**
- Provides severity-based prioritization
- Includes specific remediation guidance
- Supports both quick scans and deep analysis
- Can automatically create memories for tracking issues

### memory_graph_navigator

🗺️ Explores memory relationships visually with graph insights and discovers knowledge connections.

**Parameters:**
- `startPoint` (string, required): Memory ID or search query to start from
- `depth` (integer, optional): Maximum relationship traversal depth 1-5 (default: 2)
- `filterTypes` (array, optional): Filter by memory types ["TechnicalDebt", "ArchitecturalDecision"]
- `includeOrphans` (boolean, optional): Include memories with no relationships (default: false)
- `responseMode` (string, optional): "summary" or "full" (default: "summary")

**Example:**
```
memory_graph_navigator --startPoint "authentication patterns" --depth 3 --filterTypes ["ArchitecturalDecision", "SecurityRule"]
```

**Returns:**
```json
{
  "success": true,
  "mode": "summary",
  "graphSummary": {
    "totalNodes": 15,
    "totalEdges": 23,
    "clusters": 3,
    "orphans": 2
  },
  "clusters": [
    {
      "theme": "Authentication Architecture",
      "nodes": 8,
      "keyMemories": [
        {"id": "auth001", "type": "ArchitecturalDecision", "title": "JWT vs Sessions"},
        {"id": "sec003", "type": "SecurityRule", "title": "Password Policy"}
      ],
      "strength": "high"
    }
  ],
  "relationships": [
    {
      "source": "auth001",
      "target": "sec003", 
      "type": "implements",
      "strength": 0.85
    }
  ],
  "insights": [
    "Authentication decisions form a tightly connected cluster",
    "Security rules are well-linked to architectural decisions",
    "Consider linking orphaned performance memories"
  ],
  "explorationPaths": [
    "Follow security patterns to performance implications",
    "Explore testing strategies for authentication"
  ]
}
```

**Recovery for Empty State:**
When no memories match the start point, provides actionable guidance:
```json
{
  "success": false,
  "emptyState": true,
  "message": "No memories found for 'authentication patterns'",
  "suggestions": [
    "Create a memory first using store_memory tool",
    "Search for existing memories using search_memories tool",
    "Try a broader search term like 'auth' or 'security'"
  ],
  "exampleCommands": [
    "store_memory --type 'ArchitecturalDecision' --content 'Authentication approach decision'",
    "search_memories --query 'auth' --types ['ArchitecturalDecision']"
  ]
}
```

### workflow_discovery

⚡ **NEW** - Discovers tool dependencies and provides suggested workflows for AI agents to understand tool chains and prerequisites.

**Parameters:**
- `toolName` (string, optional): Get workflow for specific tool
- `goal` (string, optional): Get workflows for specific goal like "search code" or "analyze patterns"

**Examples:**
```
# Discover workflows for a goal
workflow_discovery --goal "search code"

# Get workflow for specific tool
workflow_discovery --toolName "text_search"

# Get all available workflows
workflow_discovery
```

**Returns:**
```json
{
  "success": true,
  "workflows": [
    {
      "name": "Text Search Workflow",
      "description": "Search for text content within files",
      "category": "Search",
      "steps": [
        {
          "tool": "index_workspace",
          "required": true,
          "description": "Create search index for the workspace",
          "estimatedTime": "10-60 seconds",
          "parameters": {"workspacePath": "{workspace_path}"}
        },
        {
          "tool": "text_search",
          "required": true,
          "description": "Search for text patterns",
          "parameters": {
            "query": "{search_term}",
            "workspacePath": "{workspace_path}"
          }
        }
      ],
      "useCases": [
        "Find code patterns",
        "Search for error messages", 
        "Locate TODO comments",
        "Find configuration values"
      ]
    },
    {
      "name": "Memory Management Workflow",
      "description": "Store and retrieve project knowledge",
      "category": "Memory",
      "steps": [
        {
          "tool": "store_memory",
          "required": true,
          "description": "Store important findings or decisions",
          "parameters": {
            "memoryType": "{type}",
            "content": "{description}"
          }
        },
        {
          "tool": "search_memories",
          "required": false,
          "description": "Find related memories",
          "parameters": {"query": "{search_term}"}
        }
      ],
      "useCases": [
        "Track architectural decisions",
        "Document technical debt",
        "Store code patterns",
        "Maintain project insights"
      ]
    }
  ]
}
```

**Workflow Categories:**
- **Search**: Text and file discovery workflows
- **Analysis**: Code pattern and quality analysis
- **Memory**: Knowledge storage and retrieval
- **Batch**: Multi-operation workflows

**Tips:**
- Use at session start to understand available workflows
- Essential for AI agents to learn tool dependencies
- Provides estimated timing for planning operations
- Shows real-world use cases for each workflow

### tool_usage_analytics

📊 Provides insights into tool performance, usage patterns, error analysis, and optimization recommendations.

**Parameters:**
- `action` (string, optional): "summary", "detailed", "tool_specific", "export", "reset" (default: "summary")
- `toolName` (string, optional): Name of specific tool to analyze (required for "tool_specific" action)
- `responseMode` (string, optional): "summary" or "full" (default: "summary")

**Examples:**
```
# Get high-level analytics overview
tool_usage_analytics --action "summary"

# Get detailed analytics for specific tool
tool_usage_analytics --action "tool_specific" --toolName "text_search"

# Export all analytics data
tool_usage_analytics --action "export"
```

**Returns:**
```json
{
  "success": true,
  "timeRange": "last 30 days",
  "summary": {
    "totalToolCalls": 1247,
    "uniqueTools": 23,
    "averageResponseTime": "145ms",
    "errorRate": "2.3%"
  },
  "topTools": [
    {"name": "text_search", "calls": 234, "avgTime": "89ms", "errorRate": "1.2%"},
    {"name": "search_memories", "calls": 189, "avgTime": "67ms", "errorRate": "0.5%"},
    {"name": "index_workspace", "calls": 45, "avgTime": "12.3s", "errorRate": "4.4%"}
  ],
  "performance": {
    "fastestTools": ["search_memories", "file_search"],
    "slowestTools": ["index_workspace", "pattern_detector"],
    "mostReliable": ["search_memories", "get_memory"]
  },
  "recommendations": [
    "Consider caching for frequently used text_search queries",
    "Monitor index_workspace for timeout issues",
    "Optimize pattern_detector for large codebases"
  ],
  "trends": {
    "usage": "increasing",
    "performance": "stable",
    "reliability": "improving"
  }
}
```

**Tips:**
- Monitor tool performance for optimization opportunities  
- Identify frequently used tools for caching strategies
- Track error patterns to improve reliability
- Export data for external analysis tools

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

### backup_memories

Backup memories to JSON for version control.

**Parameters:**
- `scopes` (array, optional): Memory types to backup
- `includeLocal` (boolean, optional): Include local memories (default: false)

**Example:**
```
backup_memories --scopes ["ArchitecturalDecision", "CodePattern"]
```

**Returns:**
```json
{
  "success": true,
  "backupPath": "C:/project/.codesearch/backups/memories_20250124_143000.json",
  "stats": {
    "memoriesBackedUp": 165,
    "backupSizeBytes": 524288,
    "format": "json"
  }
}
```

**Tips:**
- Default backs up only project-level memories
- Check in JSON files to version control
- Use for team sharing
- JSON format is human-readable

### restore_memories

Restore memories from JSON backup.

**Parameters:**
- `scopes` (array, optional): Memory types to restore
- `includeLocal` (boolean, optional): Include local memories (default: false)

**Example:**
```
restore_memories
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

### add_checklist_items

Add one or more items to existing checklist. Pass a single item in the array to add just one item.

**Parameters:**
- `checklistId` (string, required): Checklist ID
- `items` (array, required): Array of items to add (can be a single item)
  - `itemText` (string, required): Item description
  - `notes` (string, optional): Additional notes
  - `relatedFiles` (array, optional): Related files
  - `customFields` (object, optional): Custom fields

**Examples:**
```
# Add single item
add_checklist_items --checklistId "checklist789" --items [{"itemText": "Update authentication endpoints", "notes": "Switch to JWT"}]

# Add multiple items
add_checklist_items --checklistId "checklist789" --items [
  {"itemText": "Update authentication endpoints", "notes": "Switch to JWT"},
  {"itemText": "Add rate limiting", "relatedFiles": ["/api/middleware.cs"]},
  {"itemText": "Update API documentation"}
]
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


## Utilities

### index_health_check

Perform comprehensive health check of Lucene indexes with detailed metrics and recommendations.

**Parameters:**
- `includeMetrics` (boolean, optional): Include performance metrics (default: true)
- `includeCircuitBreakerStatus` (boolean, optional): Include circuit breaker status (default: true)
- `includeAutoRepair` (boolean, optional): Automatically repair corrupted indexes (default: false)

**Example:**
```
index_health_check --includeAutoRepair true
```

**Returns:**
```json
{
  "success": true,
  "status": "healthy",
  "diagnostics": {
    "indexCount": 3,
    "totalSize": "156.2 MB", 
    "healthyIndexes": 3,
    "corruptIndexes": 0,
    "circuitBreakerStatus": "closed"
  },
  "performanceMetrics": {
    "averageSearchTime": "15ms",
    "indexingThroughput": "1250 files/minute",
    "memoryUsage": "125MB"
  },
  "recommendations": [
    "All indexes are healthy",
    "Consider optimization for improved search performance"
  ]
}
```

### system_health_check

Perform comprehensive system health check covering all major services and components with overall assessment.

**Parameters:**
- `includeIndexHealth` (boolean, optional): Include index health status (default: true)
- `includeMemoryPressure` (boolean, optional): Include memory pressure monitoring (default: true)
- `includeSystemMetrics` (boolean, optional): Include system performance metrics (default: true)
- `includeConfiguration` (boolean, optional): Include configuration validation (default: false)

**Example:**
```
system_health_check --includeConfiguration true
```

**Returns:**
```json
{
  "success": true,
  "overallStatus": "healthy",
  "systemMetrics": {
    "uptime": "2h 45m",
    "memoryUsage": "145MB",
    "cpuUsage": "12%",
    "diskUsage": "245MB"
  },
  "componentHealth": {
    "luceneIndexes": "healthy",
    "memorySystem": "healthy", 
    "cacheServices": "healthy",
    "circuitBreakers": "operational"
  },
  "memoryPressure": {
    "level": "normal",
    "gcPressure": "low",
    "largeObjectHeap": "stable"
  },
  "recommendations": [
    "System operating within normal parameters",
    "Memory usage is optimal",
    "Consider cache warm-up for better initial performance"
  ],
  "alerts": []
}
```

**Tips:**
- Use for comprehensive system monitoring
- Provides actionable recommendations
- Identifies potential issues before they impact performance
- Includes memory pressure analysis for large codebases

### log_diagnostics

View and manage log files with actions for status, list, and cleanup.

**Parameters:**
- `action` (string, required): "status", "list", "cleanup"
- `cleanup` (boolean, optional): For cleanup action, confirm deletion of old logs

**Example:**
```
log_diagnostics --action status
```

**Returns:**
```json
{
  "success": true,
  "action": "status",
  "logDirectory": "C:/project/.codesearch/logs",
  "currentLogFiles": 5,
  "totalLogSize": "12.5 MB",
  "oldestLog": "2025-01-20T10:00:00Z"
}
```

**Tips:**
- Logs written to `.codesearch/logs` directory
- Auto-rotates to prevent disk space issues
- Use cleanup to remove old log files

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
3. log_diagnostics --action status
```

### Code Investigation Pattern
```
1. text_search --query "TODO" --workspacePath "C:/project"
2. similar_files --sourceFilePath "found-file.cs" --workspacePath "C:/project"
3. file_size_analysis --workspacePath "C:/project" --mode "largest"
4. store_memory --type "TechnicalDebt" --content "Found TODO that needs attention"
```

### Pattern Analysis Workflow
```
1. batch_operations --operations [
     {"operation": "text_search", "query": "authentication"},
     {"operation": "file_search", "query": "*Service.cs"},
     {"operation": "recent_files", "timeFrame": "24h"}
   ]
2. store_memory --type "CodePattern" --content "Authentication patterns analysis"
```

## Performance Tips

1. **Always index first**: Run `index_workspace` before using search tools
2. **Use batch operations**: Execute multiple searches in parallel for faster results
3. **Leverage summary mode**: For large results, use `responseMode: "summary"`
4. **Filter early**: Use filePattern and extensions to narrow searches
5. **Monitor health**: Use `index_health_check` to ensure optimal performance

## Tool Categories

### Text Search Tools (Language Agnostic)
- text_search
- file_search
- directory_search
- recent_files
- similar_files
- file_size_analysis
- batch_operations

### Memory Management Tools
- All memory system tools work with any language
- Content analysis is pattern-based, not language-specific
- Works with any text-based files

### Utility Tools
- index_workspace
- index_health_check
- log_diagnostics
- get_version

## AI-Optimized Response Structure

All code analysis tools follow a consistent AI-optimized response pattern:

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
3. **Leverage AI insights**: All tools provide intelligent analysis
4. **Store decisions**: Use memory system to track important decisions
5. **Use summary mode**: Tools auto-summarize large results
6. **Follow recommendations**: Tools provide actionable next steps
7. **Batch when possible**: Use batch_operations for multiple queries
8. **Preview destructive ops**: Always preview renames before applying

## AI UX Optimizations (NEW)

The CodeSearch MCP Server has been specifically optimized for AI agent workflows based on comprehensive AI UX review. Key improvements include:

### Parameter Standardization

All search tools now use consistent parameter naming for better AI agent experience:

- **Unified Query Parameter**: All search operations use `query` instead of tool-specific names
- **Backward Compatibility**: Legacy parameters (`searchQuery`, `nameQuery`, `directoryQuery`) still supported
- **Runtime Validation**: Clear error messages when required parameters are missing

**Before:**
```json
// Inconsistent parameter names caused confusion
{"operation": "text_search", "searchQuery": "authentication"}
{"operation": "file_search", "nameQuery": "AuthService"}
{"operation": "directory_search", "directoryQuery": "Services"}
```

**After:**
```json
// Consistent parameter naming across all tools
{"operation": "text_search", "query": "authentication"}
{"operation": "file_search", "query": "AuthService"}  
{"operation": "directory_search", "query": "Services"}
```

### Response Format Consistency

Enhanced `UnifiedToolResponse` provides predictable response structure:

```json
{
  "success": true,
  "format": "structured|markdown|mixed",
  "display": "Optional human-readable representation",
  "data": { /* Structured data */ },
  "metadata": {
    "estimatedTokens": 3200,
    "detailRequestToken": "cache_token",
    "resourceUri": "codesearch://session_123"
  }
}
```

**Benefits for AI Agents:**
- Predictable parsing strategies
- Token estimation for context management
- Progressive disclosure through detail tokens
- Resource URIs for stateful exploration

### Enhanced Error Handling

Tools now provide actionable recovery guidance instead of generic error messages:

**Before:**
```json
{"success": false, "error": "Index not found"}
```

**After:**
```json
{
  "success": false,
  "errorCode": "INDEX_NOT_FOUND",
  "message": "No search index found for workspace",
  "recovery": {
    "steps": [
      "Run index_workspace with workspacePath='C:/project'",
      "Wait for indexing to complete (typically 10-60 seconds)",
      "Retry your search"
    ],
    "suggestedActions": [
      {
        "tool": "index_workspace",
        "params": {"workspacePath": "C:/project"},
        "description": "Create search index for this workspace"
      }
    ]
  }
}
```

### Workflow Discovery System

The new `workflow_discovery` tool provides proactive guidance for AI agents:

- **Tool Dependencies**: Shows prerequisite relationships between tools
- **Workflow Templates**: Pre-defined workflows for common scenarios
- **Estimated Timing**: Helps AI agents plan operations
- **Use Case Examples**: Real-world applications for each workflow

### Memory System Enhancements

Improved memory system with AI-friendly features:

- **Reserved Field Detection**: Clear alternatives when using reserved field names
- **Relationship Management**: Visual graph navigation with empty state handling  
- **Template System**: Structured memory creation for consistent data
- **Context Awareness**: Smart suggestions based on current work

### Token Efficiency Features

- **Automatic Mode Switching**: Responses auto-switch to summary mode at 5,000 tokens
- **Progressive Disclosure**: Request details without re-running expensive operations
- **Token Estimation**: All responses include estimated token counts
- **Smart Summarization**: AI-optimized summaries with key insights

### AI Agent Onboarding

These optimizations make the MCP server ideal for AI agents by:

1. **Reducing Cognitive Load**: Consistent patterns across all tools
2. **Enabling Learning**: Clear error recovery and workflow guidance
3. **Supporting Context Management**: Token awareness and progressive disclosure
4. **Providing Actionable Insights**: Every response includes next steps
5. **Maintaining State**: Resource URIs for continued exploration

The system has been tested extensively with AI agents and provides a seamless experience for complex multi-step operations while maintaining full backward compatibility with existing integrations.