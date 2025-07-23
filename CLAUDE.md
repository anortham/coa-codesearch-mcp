# COA CodeSearch MCP Server - Claude AI Assistant Guide

This document provides context and guidelines for AI assistants working on the COA CodeSearch MCP Server project.

## IMPORTANT: Environment Context

**This project is being developed in Claude Code, NOT Claude Desktop.** The MCP server integration will be different:
- Claude Code has its own MCP integration mechanism
- Do not look for Claude Desktop configuration files
- MCP servers in Claude Code are configured differently than in Claude Desktop

## CRITICAL: Understanding Code Versions

**You must be aware of which version of the code you're running vs. editing:**
- **MCP Tools (`mcp__codesearch__*`)**: These execute on the INSTALLED version of the MCP server, not the code you're currently editing
- **Code Changes**: When you modify code in the project, those changes DO NOT affect the running MCP server
- **Testing Changes**: To test code changes, you must:
  1. Build the project: `dotnet build -c Release`
  2. The user must reinstall/restart the MCP server
  3. Start a new Claude Code session to use the updated version
- **Confusion Prevention**: Always remember that editing `ClaudeMemoryService.cs` or any other file won't change how `mcp__codesearch__remember_decision` behaves until the server is restarted with the new build

## ⚠️ CRITICAL: Never Run MCP Server Locally

**NEVER run the MCP server locally during development sessions!**

DO NOT execute commands like:
- `dotnet run --project COA.CodeSearch.McpServer -- stdio`
- `dotnet run -- stdio --test-mode`
- Any variation of running the MCP server directly

**Why this is critical:**
- Running the MCP server locally creates a separate process that locks the Lucene index
- This process may not terminate when Claude session ends, creating an orphaned process
- The orphaned process holds `write.lock` files, preventing the main MCP server from functioning
- Results in complete failure of all search operations until manually resolved

**If you need to test the MCP server:**
- Build it: `dotnet build -c Release`
- Let the user handle installation/restart
- Test through the normal MCP tools interface

## ⚠️ CRITICAL: Build Configuration During Development

**IMPORTANT: Build in Debug mode during development sessions!**

- **During Development**: Claude should build using `dotnet build -c Debug` because the Release DLL is locked by the running session
- **User Workflow**: When user exits Claude Code to load new code, they build in Release mode to update their setup
- **Why**: The Release build would fail due to file locks from the running server
- **Common Mistake**: Trying to build Release mode during active development will fail due to locked files

## Project Overview

COA CodeSearch MCP Server is a high-performance Model Context Protocol (MCP) server built in .NET 9.0 that provides Language Server Protocol (LSP)-like capabilities for navigating and searching codebases across multiple languages. It leverages Roslyn for C# code analysis and includes TypeScript support through automatic tsserver integration. Features blazing-fast text search using Lucene indexing and an intelligent memory system for preserving architectural knowledge. Designed to be significantly faster than Python-based alternatives.

## Key Commands

### Build and Test
```bash
# Build the project
dotnet build

# Run the server
dotnet run

# Build for release with AOT
dotnet publish -c Release -r win-x64

# Run tests
dotnet test

# Check for package updates
dotnet list package --outdated
```

### MCP Server Commands
```bash
# Run server in STDIO mode for Claude Code
dotnet run -- stdio

# Run server with verbose logging
dotnet run -- stdio --log-level debug

# Test with MCP inspector
npx @modelcontextprotocol/inspector dotnet run -- stdio
```

## Project Structure

```
COA.CodeSearch.McpServer/
├── Program.cs                     # Entry point, MCP server setup
├── Services/
│   ├── CodeAnalysisService.cs     # Manages C# code analysis and workspaces
│   ├── TypeScriptAnalysisService.cs # TypeScript/JavaScript analysis via tsserver
│   ├── TypeScriptInstaller.cs     # Automatic TypeScript installation
│   ├── LuceneIndexService.cs      # Fast text indexing with Lucene
│   └── ClaudeMemoryService.cs     # Architectural knowledge persistence
├── Tools/
│   ├── GoToDefinitionTool.cs      # Navigate to definitions (C# & TypeScript)
│   ├── FindReferencesTool.cs      # Find all references (C# & TypeScript)
│   ├── SearchSymbolsTool.cs       # Search for symbols
│   ├── FastTextSearchTool.cs      # Blazing-fast text search
│   ├── BatchOperationsTool.cs     # Execute multiple operations in parallel
│   ├── ClaudeMemoryTools.cs       # Store/recall architectural decisions
│   └── [V2 Tools]                 # Claude-optimized tools with progressive disclosure
├── Infrastructure/
│   ├── ClaudeOptimizedToolBase.cs # Base class for v2 tools
│   └── ResponseSizeEstimator.cs   # Token-aware response handling
├── Models/
│   └── [Various DTOs]              # Data transfer objects
└── appsettings.json               # Configuration
```

## Architecture Decisions

### 1. **Roslyn Integration**
- Uses Microsoft.CodeAnalysis for all code analysis
- MSBuildWorkspace for loading solutions
- Incremental compilation for performance
- Cached semantic models

### 2. **MCP Implementation**
- STDIO transport for Claude Code integration
- Strongly-typed tools with manual registration via ToolRegistry
- Resources for read-only data access
- Structured error handling with protocol-level wrapping

### 3. **Performance Optimizations**
- Native AOT compilation for faster startup
- Workspace caching with LRU eviction
- Lazy loading of projects
- Parallel symbol search
- Memory-mapped file access for large files
- Lucene indexing for millisecond text search across millions of lines
- Automatic TypeScript server reuse across operations

### 4. **Error Handling**
- Graceful degradation when projects don't compile
- Timeout protection for long operations
- Detailed error messages for debugging
- Fallback to syntax-only analysis

### 5. **Claude Optimization (v2 Tools)**
- Progressive disclosure with token limit protection (25,000 token limit)
- Auto-mode switching at 5,000 token threshold
- Smart analysis with insights, hotspots, and next actions
- Detail request caching for efficient drill-down
- Context-aware suggestions and priority-based recommendations

### 6. **Memory System**
- Persistent architectural knowledge across sessions
- Manual context loading and saving
- Session tracking and work history
- SQLite backup/restore for version control

## Development Guidelines

### ⚠️ CRITICAL: Path Resolution Requirements

**ALL file and directory operations MUST use `IPathResolutionService`.**

See [docs/PATH_RESOLUTION_CRITICAL.md](docs/PATH_RESOLUTION_CRITICAL.md) for the authoritative guide on path handling.

**Key Rules:**
1. **NEVER** construct paths manually (e.g., `Path.Combine(basePath, "logs")`)
2. **NEVER** call `Directory.CreateDirectory()` directly
3. **NEVER** read configuration for paths - always use `IPathResolutionService`
4. **ALWAYS** inject `IPathResolutionService` and use its methods:
   - `GetBasePath()` - Base .codesearch directory
   - `GetIndexPath(workspace)` - Index path for a workspace
   - `GetProjectMemoryPath()` - Project memory location
   - `GetLocalMemoryPath()` - Local memory location
   - `GetLogsPath()` - Logs directory
   - `GetBackupPath()` - Backup directory

This is the SINGLE SOURCE OF TRUTH for all path operations to prevent the recurring path-related bugs.

### Code Style
- Use C# 12 features with nullable reference types
- Follow standard C# naming conventions
- Async all the way down
- Use ILogger for all logging
- Document public APIs with XML comments

### MCP Tool Implementation Pattern

Tools in this project follow a functional registration pattern:

#### 1. Tool Class Implementation
```csharp
public class MyTool
{
    private readonly ILogger<MyTool> _logger;
    private readonly ICodeAnalysisService _codeAnalysisService;
    
    public MyTool(ILogger<MyTool> logger, ICodeAnalysisService codeAnalysisService)
    {
        _logger = logger;
        _codeAnalysisService = codeAnalysisService;
    }
    
    public async Task<MyResult> ExecuteAsync(string param1, string? param2 = null)
    {
        try
        {
            // Tool implementation logic
            return new MyResult { /* results */ };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in MyTool");
            throw; // Let registration layer handle error wrapping
        }
    }
}
```

#### 2. Tool Registration (in AllToolRegistrations.cs)
```csharp
private static void RegisterMyTool(ToolRegistry registry, MyTool tool)
{
    registry.RegisterTool<MyToolParams>(
        name: "my_tool",
        description: "Clear description of what this tool does",
        inputSchema: new
        {
            type = "object",
            properties = new
            {
                param1 = new { type = "string", description = "Parameter description" },
                param2 = new { type = "string", description = "Optional parameter" }
            },
            required = new[] { "param1" }
        },
        handler: async (parameters, ct) =>
        {
            var result = await tool.ExecuteAsync(parameters.Param1, parameters.Param2);
            return CreateSuccessResult(result);
        }
    );
}
```

#### 3. Parameter Model
```csharp
public class MyToolParams
{
    public required string Param1 { get; set; }
    public string? Param2 { get; set; }
}
```

### Base Classes for Tools

#### McpToolBase
For tools that need token limit handling and response optimization:
```csharp
public class MyTool : McpToolBase
{
    protected override int MaxTokens => 10000; // Override default limit
    
    public async Task<object> ExecuteAsync(params)
    {
        // Implementation
        return CreateResponse(data, estimatedTokens);
    }
}
```

#### ClaudeOptimizedToolBase
For v2 tools with progressive disclosure and smart summaries:
```csharp
public class MyToolV2 : ClaudeOptimizedToolBase<MyData>
{
    protected override async Task<MyData> GetFullDataAsync(params)
    {
        // Get all data
    }
    
    protected override SummaryData CreateSummary(MyData fullData, int estimatedTokens)
    {
        // Create smart summary with insights
    }
}
```

### Adding New Tools

1. Create a new tool class in the Tools folder
   - Inherit from `McpToolBase` or `ClaudeOptimizedToolBase` for advanced features (optional)
   - Implement an `ExecuteAsync` method with your tool logic
   - Inject required services via constructor

2. Create a parameter model class for strongly-typed parameters

3. Add registration method in `AllToolRegistrations.cs`:
   - Define the tool name, description, and JSON schema
   - Create handler that calls your tool's ExecuteAsync method
   - Wrap results using `CreateSuccessResult` or `CreateErrorResult`

4. Register the tool in DI container in `Program.cs`:
   ```csharp
   services.AddSingleton<MyTool>();
   ```

5. Call your registration method in `AllToolRegistrations.RegisterAll()`

6. Add integration tests

### Testing Strategy

- Unit tests for individual services
- Integration tests for MCP tools
- Performance benchmarks for large codebases
- Test with various project types (.NET Core, Framework, etc.)
- Memory usage tests for long-running scenarios

## File Watcher and Auto-Indexing

### Auto-Indexing on Startup
The `WorkspaceAutoIndexService` automatically re-indexes all previously indexed workspaces on startup:
- Runs 3 seconds after server startup (configurable)
- Re-indexes each workspace to detect changes made while the server was off
- Automatically starts file watching after indexing
- Can be disabled via `WorkspaceAutoIndex:Enabled` in appsettings.json

### File Watcher Improvements
- **Windows Compatibility**: Fixed file modification detection with expanded NotifyFilter flags
- **Auto-Start**: File watchers are started automatically after workspace indexing
- **Real-time Updates**: Index updates as files are created, modified, or deleted
- **Debouncing**: Batches rapid file changes to avoid excessive re-indexing

### Configuration
```json
{
  "FileWatcher": {
    "Enabled": true,
    "DebounceMilliseconds": 500,
    "BatchSize": 50
  },
  "WorkspaceAutoIndex": {
    "Enabled": true,
    "StartupDelayMilliseconds": 3000
  }
}
```

## Common Tasks

### 1. **Adding a new navigation feature**
```bash
# Create new tool class
# Update RoslynWorkspaceService if needed
# Add tests
# Update MCP manifest
```

### 2. **Improving performance**
- Profile with dotTrace or PerfView
- Check for unnecessary allocations
- Consider caching strategies
- Use ValueTask where appropriate

### 3. **Debugging MCP communication**
- Enable debug logging in appsettings.json
- Use MCP Inspector tool
- Check STDIO input/output
- Validate JSON-RPC messages

## Claude-Optimized Tools Usage

### Progressive Disclosure Pattern

The v2 tools implement a progressive disclosure pattern designed specifically for Claude:

1. **Auto-Mode Switching**: Tools automatically switch to summary mode when responses would exceed 5,000 tokens
2. **Smart Summaries**: Provide key insights, hotspots, and categorized data
3. **Next Actions**: Ready-to-use commands for logical follow-up operations
4. **Detail Requests**: Efficient drill-down into specific results

### Example Workflow

```
Claude: "Find all references to UserService"
→ Summary: 47 references across 12 files
→ Key Insight: "Heavily used service - changes have wide impact"
→ Hotspots: UserController.cs (15 refs), AuthService.cs (8 refs)
→ Next Action: "Review hotspots for detailed analysis"

Claude: "Review hotspots" (uses cached detail request)
→ Detailed analysis of UserController.cs and AuthService.cs
→ Usage patterns and refactoring opportunities
→ Impact assessment for proposed changes
```

### v2 Tools Available

- **FindReferencesToolV2**: Smart reference analysis with impact insights
- **RenameSymbolToolV2**: Intelligent rename preview with risk assessment  
- **GetDiagnosticsToolV2**: Categorized diagnostics with priority recommendations
- **DependencyAnalysisToolV2**: Architecture analysis with circular dependency detection
- **ProjectStructureAnalysisToolV2**: Solution insights with hotspot identification

## Configuration

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "COA.Roslyn": "Debug"
    }
  },
  "McpServer": {
    "MaxWorkspaces": 5,
    "WorkspaceTimeout": "00:30:00",
    "EnableDiagnostics": true,
    "ParallelismDegree": 4
  },
  "ResponseLimits": {
    "MaxTokens": 25000,
    "SafetyMargin": 0.8,
    "DefaultMaxResults": 50,
    "EnableTruncation": true,
    "EnablePagination": true,
    "AutoModeSwitchThreshold": 5000
  }
}
```

### Environment Variables
- `MCP_LOG_LEVEL`: Override log level
- `MCP_WORKSPACE_PATH`: Default workspace path
- `MCP_MAX_MEMORY_MB`: Memory limit for AOT

## Performance Targets

- Startup time: < 100ms (with AOT)
- GoToDefinition: < 50ms for cached workspace
- FindReferences: < 200ms for average project
- Symbol search: < 100ms for prefix match
- Memory usage: < 500MB for typical solution

## Integration with Claude Code

The MCP server is integrated with Claude Code differently than Claude Desktop:

1. Build the server: `dotnet publish -c Release`
2. Follow Claude Code's MCP server integration process
3. The server runs in STDIO mode for communication

Note: Claude Code manages MCP servers through its own configuration system, not through manual JSON configuration files.

## Troubleshooting

### Common Issues

1. **MSBuild not found**
   - Install Build Tools for Visual Studio
   - Run MSBuildLocator.RegisterDefaults()

2. **High memory usage**
   - Check workspace cache settings
   - Enable workspace eviction
   - Reduce ParallelismDegree

3. **Slow performance**
   - Enable AOT compilation
   - Check for compilation errors in target projects
   - Profile with performance tools

## Progressive Disclosure Pattern (Optimized for Claude)

The MCP server includes smart response handling designed specifically for Claude's workflow:

## Auto-Mode Switching
Tools automatically switch to summary mode when responses exceed 5,000 tokens:
```json
{
  "success": true,
  "mode": "summary",
  "autoModeSwitch": true,  // Indicates automatic switch
  "data": { /* summary data */ }
}
```

## Smart Summaries
Summary responses include:
- **Key Insights**: "CmsController.cs has 30% of all changes"
- **Hotspots**: Files with highest concentration of results
- **Categories**: Results grouped by file type (controllers, services, etc.)
- **Impact Analysis**: Risk factors and suggestions

## Efficient Drill-Down
Request specific details without re-executing operations:
```json
// Get details for high-impact files
{
  "detailLevel": "hotspots",
  "detailRequestToken": "..."
}

// Get category-specific details
{
  "detailLevel": "smart_batch",
  "criteria": {
    "categories": ["controllers"],
    "maxTokens": 8000
  }
}
```

## Token Awareness
All responses include token estimates:
- `estimatedTokens`: Current response size
- `estimatedFullResponseTokens`: Full data size
- Available detail levels show token cost

## Usage Example
```bash
# 1. Initial request - auto-switches to summary if large
rename_symbol --file ICmsService.cs --line 10 --newName IContentManagementService

# 2. Response includes smart analysis
# - Key insights about the changes
# - Recommended next actions with exact commands
# - Token estimates for each action

# 3. Drill down as needed using provided commands
```

## Future Enhancements

- WebSocket transport for remote deployment
- Integration with OmniSharp for additional features
- Support for F# and VB.NET
- Real-time file watching and incremental updates
- Code lens and inline hints
- Integration with .NET CLI tools
- Semantic code search with embeddings

## Memory System Quick Start

**SIMPLIFIED: Tool count reduced from 60+ to 49, then increased to 51 with new Git and file context tools**

### Session Startup
To use the memory system effectively:
1. Start each session with `mcp__codesearch__recall_context "what I'm working on"`
2. This loads recent work sessions and architectural decisions
3. Provides relevant context for your current work

### Available Memory Tools

#### Flexible Memory System (Primary API)
- `flexible_store_memory` - Store ANY type of memory with custom fields
  - Use `--type "TechnicalDebt"` instead of specialized tools
  - Use `--type "Question"` for questions
  - Use `--type "DeferredTask"` for deferred tasks
  - Or create your own types!
- `flexible_search_memories` - Advanced search with faceting and filtering
- `flexible_update_memory` - Update existing memories (including marking as resolved)
- `flexible_get_memory` - Retrieve specific memory by ID
- `flexible_find_similar_memories` - Find related memories using AI
- `flexible_archive_memories` - Archive old memories by type and age
- `flexible_store_git_commit` - Link memories to specific Git commits (NEW!)
- `flexible_memories_for_file` - Find all memories related to a specific file (NEW!)

#### Memory Linking (New!)
- `flexible_link_memories` - Create relationships between memories
- `flexible_get_related_memories` - Traverse memory relationships
- `flexible_unlink_memories` - Remove relationships

#### Working Memory (Phase 4 - New!)
- `flexible_store_working_memory` - Store temporary memories with expiration
  - Default: expires at end of session
  - Time-based: '1h', '4h', '24h', '7d' 
  - Automatically filtered out when expired
  - Always local (not shared with team)

#### Persistent Checklists (New!)
- `create_checklist` - Create persistent checklists for cross-session task tracking
- `add_checklist_item` - Add items with notes and file references
- `toggle_checklist_item` - Mark items complete/incomplete with tracking
- `update_checklist_item` - Update item text, notes, or custom fields
- `view_checklist` - View checklist with progress and optional markdown export
- `list_checklists` - List all checklists with filtering options

#### Essential Tools
- `recall_context` - Load relevant context at session start (ALWAYS use this first!)
- `backup_memories_to_sqlite` - Backup memories for version control
- `restore_memories_from_sqlite` - Restore memories from backup
- **Smart Recall (Phase 4)**: The flexible_search_memories tool now includes automatic natural language understanding
  - Detects natural language queries and expands them semantically
  - Synonym expansion (e.g., "bug" → "defect", "issue", "error")
  - Code term extraction (camelCase, PascalCase, snake_case)
  - Flexible matching with boosted relevance

#### Memory Summarization (Phase 4 - New!)
- `flexible_summarize_memories` - Compress old memories into summaries
  - Groups memories by time period and extracts key themes
  - Preserves important files and insights
  - Type-specific analysis (e.g., resolution rates for TechnicalDebt)
  - Optionally archives originals after summarization

### Manual Backup/Restore
- `backup_memories_to_sqlite` - Backup project memories to SQLite for version control
- `restore_memories_from_sqlite` - Restore memories from SQLite (useful for new machines)

Example workflow:
1. Before major changes: `mcp__codesearch__backup_memories_to_sqlite`
2. Check in `memories.db` to source control
3. On new machine: `mcp__codesearch__restore_memories_from_sqlite`

### Working Memory Examples

Store temporary session memories:
```bash
# Store a working memory for current session
flexible_store_working_memory --content "User wants to refactor the auth system - start with UserService" 

# Store with specific expiration
flexible_store_working_memory --content "Remember to check performance after cache implementation" --expiresIn "4h"

# Store with context fields
flexible_store_working_memory --content "Debugging null reference in payment processing" --expiresIn "1h" --files ["PaymentService.cs"] --fields {"category": "debugging", "priority": "high"}
```

### Memory Summarization Examples

Compress old memories into insightful summaries:
```bash
# Summarize old work sessions
flexible_summarize_memories --type "WorkSession" --daysOld 30 --batchSize 10

# Summarize technical debt with preservation
flexible_summarize_memories --type "TechnicalDebt" --daysOld 90 --preserveOriginals true

# Summarize architectural decisions
flexible_summarize_memories --type "ArchitecturalDecision" --daysOld 180 --batchSize 20
```

The summarization includes:
- Key themes and word frequency analysis
- Most referenced files
- Type-specific insights (resolution rates, patterns)
- Date ranges and memory counts

### Memory Linking Examples

Create relationships between related memories:
```bash
# Link a bug report to its resolution
flexible_link_memories --sourceId "bug-123" --targetId "fix-456" --relationshipType "resolvedBy"

# Create bidirectional parent-child relationship
flexible_link_memories --sourceId "epic-001" --targetId "task-002" --relationshipType "parentOf" --bidirectional true

# Link related architectural decisions
flexible_link_memories --sourceId "caching-decision" --targetId "performance-analysis" --relationshipType "implements"
```

### Git Integration Examples

Store memories linked to Git commits:
```bash
# Store architectural decision tied to a commit
flexible_store_git_commit --sha "abc123def" --message "Refactor authentication system" --description "Implemented JWT-based authentication to improve security and scalability" --author "John Doe" --branch "feature/auth-refactor" --filesChanged ["AuthService.cs", "JwtHandler.cs"]

# Track important bug fix
flexible_store_git_commit --sha "def456ghi" --message "Fix null reference in payment processing" --description "Critical bug fix - payment service was crashing when processing refunds with null metadata" --filesChanged ["PaymentService.cs"]
```

### File Context Examples

Find all memories related to a specific file:
```bash
# Find all memories for a file
flexible_memories_for_file --filePath "Services/AuthService.cs"

# Include archived memories
flexible_memories_for_file --filePath "Services/AuthService.cs" --includeArchived true
```

The tool will return memories grouped by type (e.g., ArchitecturalDecision, TechnicalDebt, GitCommit) that reference the file.

Traverse memory relationships:
```bash
# Find all memories related to a specific memory (depth 2)
flexible_get_related_memories --memoryId "epic-001" --maxDepth 2

# Find only specific relationship types
flexible_get_related_memories --memoryId "bug-123" --relationshipTypes ["resolvedBy", "causedBy"]
```

Common relationship types:
- `relatedTo` - General relationship
- `blockedBy`/`blocks` - Dependency tracking
- `implements`/`implementedBy` - Implementation relationships
- `supersedes`/`supersededBy` - Replacement tracking
- `parentOf`/`childOf` - Hierarchical relationships
- `resolves`/`resolvedBy` - Problem/solution tracking
- `duplicates`/`duplicatedBy` - Duplicate tracking

### Persistent Checklist Examples

Create and manage checklists that persist across sessions:
```bash
# Create a new checklist
create_checklist --title "Implement Authentication System" --description "Full auth implementation with JWT" --isShared true

# Add items to the checklist
add_checklist_item --checklistId "abc123" --itemText "Create login endpoint" --notes "Use JWT tokens"
add_checklist_item --checklistId "abc123" --itemText "Implement password reset" --relatedFiles ["AuthService.cs"]
add_checklist_item --checklistId "abc123" --itemText "Add user registration"

# Mark items as complete
toggle_checklist_item --itemId "item456" --completedBy "claude"

# View checklist with progress
view_checklist --checklistId "abc123" --exportAsMarkdown true

# List all active checklists
list_checklists --includeCompleted false --onlyShared true
```

Checklists automatically:
- Track progress percentage
- Link items to parent checklist
- Update status when all items complete
- Support markdown export for documentation
- Can be personal (local) or shared (team)

## TypeScript Support

### Automatic Installation
TypeScript support is automatically configured on server startup:
1. `TypeScriptInitializationService` checks for existing TypeScript installation
2. Downloads and installs TypeScript + tsserver if needed
3. Caches installation in local app data for future sessions
4. Validates installation and starts tsserver process

### TypeScript Tools
- `search_typescript` - Search for TypeScript symbols
- `typescript_go_to_definition` - Navigate to TypeScript definitions using tsserver
- `typescript_find_references` - Find TypeScript references using tsserver
- `typescript_rename_symbol` - Rename TypeScript symbols across the entire codebase with preview
- `GoToDefinition` - Automatically delegates to TypeScript for .ts/.js files
- `FindReferences` - Works seamlessly across C# and TypeScript
- `GetHoverInfo` - Shows type information and documentation for TypeScript symbols

## Debugging with File Logging

### Dynamic File Logging Control
The MCP server includes a file-based logging system that can be enabled/disabled at runtime without affecting the MCP protocol:

#### Using the set_logging Tool
```bash
# Start file logging with debug level
mcp__codesearch__set_logging --action start --level debug

# Check logging status
mcp__codesearch__set_logging --action status

# List all log files
mcp__codesearch__set_logging --action list

# Change log level on the fly
mcp__codesearch__set_logging --action setlevel --level verbose

# Stop logging
mcp__codesearch__set_logging --action stop

# Clean up old logs (keeps most recent 10)
mcp__codesearch__set_logging --action cleanup --cleanup true
```

#### Log File Location
Logs are written to: `%LOCALAPPDATA%\COA.CodeSearch\.codesearch\logs\`

#### Important Notes
- Logs are ONLY written to files, never to stdout (to avoid MCP protocol contamination)
- Log files are automatically rotated hourly and limited to 50MB each
- TypeScript server logs are written to `%TEMP%\tsserver.log`
- The `.codesearch\logs\` directory is excluded from git

## Fast Text Search - Blazing Performance

#### Index Management
- `index_workspace` - Build search index (required before fast_text_search)
- Indexes are cached in `.codesearch/index/{hash}` directories
- Automatic index updates on file changes

### Fast Search Tools
- `fast_text_search` - Blazing fast text search across millions of lines in milliseconds
  - Supports wildcards (*), fuzzy (~), exact phrases (""), and regex patterns
  - Context lines for better understanding
  - File pattern filtering (e.g., *.cs, src/**/*.ts)

- `fast_file_search` - Blazing fast file search with fuzzy matching and typo correction
  - Find files by name with typo tolerance (e.g., "UserServce" finds "UserService.cs")
  - Supports wildcards, fuzzy matching, exact, and regex modes
  - Performance: < 10ms for most searches

- `fast_recent_files` - Find recently modified files using indexed timestamps
  - Time frames: '30m', '24h', '7d', '4w' for minutes, hours, days, weeks
  - Filter by file patterns and extensions
  - Shows friendly "time ago" format

- `fast_file_size_analysis` - Analyze files by size with multiple modes
  - Modes: 'largest', 'smallest', 'range', 'zero', 'distribution'
  - Find storage hogs, empty files, or analyze size distributions
  - Provides detailed statistics by file type

- `fast_similar_files` - Find files with similar content using Lucene's "More Like This"
  - Perfect for finding duplicate code, related implementations, or similar patterns
  - Configurable similarity thresholds
  - Shows top matching terms

- `fast_directory_search` - Find directories/folders with fuzzy matching
  - Locate project folders, namespaces, or any directory by name
  - Supports typo correction (e.g., "Sevices" finds "Services" folder)
  - Shows file counts and types per directory

### Batch Operations
The `batch_operations` tool allows executing multiple operations in parallel:
```json
{
  "operations": [
    {"type": "text_search", "query": "TODO", "filePattern": "*.cs"},
    {"type": "search_symbols", "searchPattern": "*Service"},
    {"type": "find_references", "filePath": "User.cs", "line": 10, "column": 5}
  ]
}
```

## Cross-Language Analysis for Full-Stack Applications

### Important Language Boundaries

When working with full-stack applications (e.g., ASP.NET backend + TypeScript/React/Vue frontend), be aware of these language boundaries:

#### Language-Specific Tools
- **C# Only**: `search_symbols`, `get_implementations`, `get_diagnostics`, `get_call_hierarchy`, `dependency_analysis`, `rename_symbol`, `project_structure_analysis`, `get_document_symbols`
- **TypeScript Only**: `search_typescript`, `typescript_go_to_definition`, `typescript_find_references`, `typescript_rename_symbol`
- **Both C# and TypeScript**: `find_references`, `go_to_definition`, `get_hover_info`
- **All Languages**: `fast_text_search`, `fast_file_search`, and other Lucene-based tools

#### Key Limitation: No Cross-Language Reference Tracking
- Finding references to a C# `UserProfile` class will NOT show TypeScript usages
- Finding references to a TypeScript `UserProfile` interface will NOT show C# API endpoints
- Each language is analyzed in isolation by its respective language server

### Best Practices for Full-Stack Analysis

#### 1. **Context-Aware Tool Usage**
Developers typically work in language-specific contexts:
- Frontend work: Use TypeScript tools for component analysis
- Backend work: Use C# tools for API analysis
- Switch tools when switching contexts

#### 2. **System-Wide Analysis**
For comprehensive analysis across the full stack:
```bash
# Example: Find all usages of UserProfile across C# and TypeScript
1. mcp__codesearch__find_references --file Models/UserProfile.cs --line 5 --column 10
2. mcp__codesearch__typescript_find_references --file models/UserProfile.ts --line 3 --column 15
3. mcp__codesearch__fast_text_search --query "UserProfile" --workspacePath "C:/project"
```

#### 3. **API Contract Analysis**
When analyzing API boundaries:
- Check C# controller return types
- Check TypeScript service method signatures
- Use `fast_text_search` to find API endpoint URLs in both codebases

#### 4. **Future Composite Tools**
Planned tools will provide unified views:
- `FindReferencesAcrossLanguages` - Combines C#, TypeScript, and text search results
- `AnalyzeFullStackProject` - Shows both .NET and frontend project structure
- Automatic result deduplication and organization

### Example Workflow for Full-Stack Refactoring

When renaming a model that spans backend and frontend:

1. **Analyze Impact**:
   ```bash
   # Find C# usages
   mcp__codesearch__find_references --file Models/User.cs
   
   # Find TypeScript usages
   mcp__codesearch__find_references --file models/User.ts
   
   # Find string references (API routes, etc.)
   mcp__codesearch__fast_text_search --query "\"User\"" --searchType phrase
   ```

2. **Rename Backend** (C# only):
   ```bash
   mcp__codesearch__rename_symbol --file Models/User.cs --newName Customer
   ```

3. **Rename Frontend** (TypeScript):
   ```bash
   mcp__codesearch__typescript_rename_symbol --file models/User.ts --newName Customer
   ```

4. **Verify Consistency**:
   ```bash
   mcp__codesearch__fast_text_search --query "User" --extensions [".cs",".ts"]
   ```

### Tips for AI Assistants

- Always clarify which language/layer the user is working with
- When asked about "all references", explain the language boundary
- Suggest using multiple tools for comprehensive analysis
- Set expectations: cross-stack operations require multiple steps