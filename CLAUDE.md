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
# Run server in STDIO mode for Claude Desktop
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
│   ├── ClaudeMemoryService.cs     # Architectural knowledge persistence
│   └── MemoryHookManager.cs       # Claude Code hook integration
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
├── appsettings.json               # Configuration
└── .claude/hooks/                 # Automatic context loading hooks
    ├── user-prompt-submit.ps1     # Loads context on session start
    ├── tool-call.ps1              # Loads context before tool execution
    ├── file-edit.ps1              # Detects patterns in edits
    └── session-end.ps1            # Saves session summary
```

## Architecture Decisions

### 1. **Roslyn Integration**
- Uses Microsoft.CodeAnalysis for all code analysis
- MSBuildWorkspace for loading solutions
- Incremental compilation for performance
- Cached semantic models

### 2. **MCP Implementation**
- STDIO transport for Claude Desktop integration
- Strongly-typed tools using [McpTool] attributes
- Resources for read-only data access
- Structured error handling

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

### 6. **Memory System & Hooks**
- Persistent architectural knowledge across sessions
- Automatic context loading via Claude Code hooks
- Pattern detection in file edits
- Session tracking and work history
- Zero-effort memory management

## Development Guidelines

### Code Style
- Use C# 12 features with nullable reference types
- Follow standard C# naming conventions
- Async all the way down
- Use ILogger for all logging
- Document public APIs with XML comments

### MCP Tool Implementation Pattern
```csharp
[McpTool("tool_name")]
[Description("Clear description of what this tool does")]
public async Task<ToolResult> ToolName(
    [Description("Parameter description")] string param1,
    [Description("Optional parameter")] string? param2 = null)
{
    try
    {
        // Implementation
        return new ToolResult { Success = true, Data = result };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in ToolName");
        return new ToolResult { Success = false, Error = ex.Message };
    }
}
```

### Adding New Tools

1. Create a new class in the Tools folder
2. Inherit from `McpToolBase` or implement `IMcpTool`
3. Add the `[McpTool]` attribute with a unique name
4. Implement the tool logic using Roslyn APIs
5. Register in DI container in Program.cs
6. Add integration tests

### Testing Strategy

- Unit tests for individual services
- Integration tests for MCP tools
- Performance benchmarks for large codebases
- Test with various project types (.NET Core, Framework, etc.)
- Memory usage tests for long-running scenarios

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

## Integration with Claude Desktop

1. Build the server: `dotnet publish -c Release`
2. Add to Claude Desktop config:
```json
{
  "mcpServers": {
    "codesearch": {
      "command": "C:\\path\\to\\COA.CodeSearch.McpServer.exe",
      "args": ["stdio"]
    }
  }
}
```

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

# Future Enhancements

- WebSocket transport for remote deployment
- Integration with OmniSharp for additional features
- Support for F# and VB.NET
- Real-time file watching and incremental updates
- Code lens and inline hints
- Integration with .NET CLI tools
- Semantic code search with embeddings

## Memory System Quick Start

### Session Startup
The memory system automatically activates when you start a new conversation:
1. **user-prompt-submit hook** runs automatically
2. Initializes memory hooks for the session
3. Loads recent work sessions and architectural decisions
4. Searches for context based on your first message

### Available Memory Tools

#### Store Knowledge
- `remember_decision` - Store architectural decisions with reasoning
- `remember_pattern` - Document reusable code patterns
- `remember_security_rule` - Track security requirements
- `remember_session` - Save work session summary (automatic via hook)

#### Recall Knowledge
- `recall_context` - Search all memories for relevant context
- `list_memories_by_type` - List specific types of memories
- `init_memory_hooks` - Initialize hooks (automatic on session start)
- `test_memory_hooks` - Verify hooks are working

### Hook Strategy (Updated)
The memory system uses a targeted hook approach:
- **PreToolUse hook**: Suggests loading relevant context only when using MCP tools (reduces noise)
- **Stop hook**: Tracks work units after each Claude response, suggests documenting significant changes
- **file-edit hook**: Detects architectural patterns and suggests memory storage
- **Manual backup/restore**: User controls when to save/load memories via SQLite

### Manual Backup/Restore
- `backup_memories_to_sqlite` - Backup project memories to SQLite for version control
- `restore_memories_from_sqlite` - Restore memories from SQLite (useful for new machines)

Example workflow:
1. Before major changes: `mcp__codesearch__backup_memories_to_sqlite`
2. Check in `memories.db` to source control
3. On new machine: `mcp__codesearch__restore_memories_from_sqlite`

## TypeScript Support

### Automatic Installation
TypeScript support is automatically configured on first use:
1. Checks for existing TypeScript installation
2. Downloads and installs TypeScript + tsserver if needed
3. Caches installation for future sessions

### TypeScript Tools
- `search_typescript` - Search for TypeScript symbols
- `GoToDefinition` - Automatically delegates to TypeScript for .ts/.js files
- `FindReferences` - Works seamlessly across C# and TypeScript

### Fast Text Search - Straight Blazin' Performance

#### Index Management
- `index_workspace` - Build search index (required before fast_text_search)
- Indexes are cached in `.codesearch/index/{hash}` directories
- Automatic index updates on file changes

#### Blazin' Fast Search Tools
- `fast_text_search` - Straight blazin' fast text search across millions of lines in milliseconds
  - Supports wildcards (*), fuzzy (~), exact phrases (""), and regex patterns
  - Context lines for better understanding
  - File pattern filtering (e.g., *.cs, src/**/*.ts)

- `fast_file_search` - Straight blazin' fast file search with fuzzy matching and typo correction
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