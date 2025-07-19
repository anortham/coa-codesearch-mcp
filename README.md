# COA CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server that provides Language Server Protocol (LSP)-like capabilities for navigating and searching codebases. Features Roslyn for C# analysis, TypeScript support via automatic tsserver integration, blazing-fast text search with Lucene indexing, and an intelligent memory system for preserving architectural knowledge.

## Overview

COA CodeSearch MCP Server leverages Microsoft's Roslyn compiler platform for C# analysis, TypeScript Language Service for JavaScript/TypeScript support, and Lucene for lightning-fast text search across millions of lines. It includes an intelligent memory system that preserves architectural decisions and work context across sessions. Designed as a native .NET tool with AOT compilation, it offers significant performance advantages over Python-based alternatives.

## Features

### Core Navigation Tools (Claude-Optimized)
- **Go to Definition** - Navigate to symbol definitions (supports C# and TypeScript)
- **Find References** - Find all references with smart insights and progressive disclosure (C# and TypeScript)
- **Symbol Search** - Lightning-fast semantic search for C# symbols using Roslyn
- **TypeScript Search** - Search TypeScript/JavaScript symbols across your codebase
- **Fast Text Search** - Straight blazin' fast text search using Lucene indexing (milliseconds across millions of lines)
- **Fast File Search** - Find files with fuzzy matching and typo tolerance
- **Fast Recent Files** - Find recently modified files (last hour/day/week)
- **Fast Directory Search** - Find directories with fuzzy matching
- **Fast File Size Analysis** - Analyze file sizes and distributions
- **Fast Similar Files** - Find files with similar content
- **Get Diagnostics** - Get compilation errors and warnings with intelligent categorization and hotspot detection
- **Hover Information** - Get detailed symbol information at cursor position
- **Find Implementations** - Find all implementations of interfaces and abstract members
- **Document Symbols** - Get document outline with all types and members
- **Call Hierarchy** - Explore incoming and outgoing call trees
- **Rename Symbol** - Rename symbols across entire codebase with smart preview and impact analysis

### Advanced Analysis Tools (Claude-Optimized)
- **Batch Operations** - Execute multiple operations in parallel for 60-80% performance improvement
- **Advanced Symbol Search** - Filter symbols by accessibility, static/abstract modifiers, return types
- **Dependency Analysis** - Analyze code dependencies with circular dependency detection
- **Project Structure Analysis** - Comprehensive project analysis with hotspot detection
- **Index Workspace** - Build search indexes for optimal performance

### Memory System & Knowledge Persistence
- **Architectural Decisions** - Store and recall design decisions with reasoning
- **Code Patterns** - Document reusable patterns with usage guidance
- **Security Rules** - Track compliance requirements and security patterns
- **Work Sessions** - Automatic session tracking and history
- **Context Recall** - Intelligent search across all stored memories
- **Claude Code Hooks** - Automatic context loading on session start, tool execution, and file edits

### Performance & Architecture  
- **Multi-Language Support** - C# via Roslyn, TypeScript/JavaScript via tsserver, all other files via Lucene
- **Workspace Management** - Efficient caching with LRU eviction for large solutions
- **Native Performance** - Built with .NET 9.0 and AOT compilation support  
- **Parallel Processing** - Multi-threaded symbol analysis and workspace operations
- **Lucene Indexing** - Millisecond search across millions of lines
- **Progressive Disclosure** - Automatic token limit handling with smart summaries
- **Auto-Mode Switching** - Intelligent response optimization for large results

## Claude Optimization Features

This MCP server is specifically optimized for Claude AI with intelligent features designed to enhance the coding experience:

### Smart Response Management
- **Token Limit Protection** - Automatic response size estimation with 25,000 token limit protection
- **Progressive Disclosure** - Start with smart summaries, then drill down to specific details as needed
- **Auto-Mode Switching** - Automatically switches to summary mode when full responses would exceed 5,000 tokens

### Intelligent Analysis
- **Smart Insights** - Contextual insights tailored for common development scenarios
- **Hotspot Detection** - Automatically identifies files, components, or areas requiring attention
- **Impact Analysis** - Understands the broader impact of changes across your codebase
- **Next Actions** - Provides ready-to-use commands for logical next steps

### Optimized Workflows
- **Category-Based Organization** - Groups results by meaningful categories (errors by severity, files by type, etc.)
- **Priority-Based Recommendations** - Suggests actions based on impact and urgency
- **Detail Request Caching** - Efficient drill-down into specific results without re-analysis
- **Context-Aware Suggestions** - Provides relevant suggestions based on project size, complexity, and patterns

### Example Claude Interactions

```
You: "Find all references to UserService"
Response: 
- Summary: Found 47 references across 12 files
- Key Insight: "Heavily used service - changes have wide impact"
- Hotspots: UserController.cs (15 references), AuthService.cs (8 references)
- Next Actions: [Review hotspots] [Get implementation details] [Analyze dependencies]

You: "Review hotspots"
Response: 
- Detailed analysis of UserController.cs and AuthService.cs
- Usage patterns and potential refactoring opportunities
- Impact assessment for proposed changes
```

## Installation

### As a Global Tool

```bash
dotnet tool install --global COA.CodeSearch.McpServer --add-source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json
```

### For Claude Code

1. Install the tool globally (see above)
2. Add the MCP server using Claude Code CLI:

```bash
claude mcp add coa-codesearch-mcp
```

Or manually add to your Claude Code configuration (`%APPDATA%\Claude\claude_code_config.json` on Windows):

```json
{
  "mcpServers": {
    "coa-codesearch-mcp": {
      "command": "coa-codesearch-mcp",
      "args": ["stdio"]
    }
  }
}
```

### For Claude Desktop

Add to your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "codesearch": {
      "command": "coa-codesearch-mcp",
      "args": ["stdio"]
    }
  }
}
```

### For VS Code (via MCP Extension)

1. Install the MCP extension for VS Code (when available)
2. Add to your VS Code settings.json:

```json
{
  "mcp.servers": {
    "coa-codesearch": {
      "command": "coa-codesearch-mcp",
      "args": ["stdio"],
      "languages": ["csharp", "vb", "javascript", "typescript"]
    }
  }
}
```

### For GitHub Copilot (via MCP Bridge)

When GitHub Copilot supports MCP servers, configure it in your `.github/copilot-mcp.json`:

```json
{
  "servers": {
    "codesearch": {
      "command": "coa-codesearch-mcp",
      "args": ["stdio"],
      "filePatterns": ["**/*.cs", "**/*.vb", "**/*.ts", "**/*.js", "**/*.tsx", "**/*.jsx", "**/*.csproj", "**/*.sln"]
    }
  }
}
```

## Usage

### Running the Server

```bash
# Run in STDIO mode (for MCP clients)
coa-codesearch-mcp stdio

# Run with verbose logging
coa-codesearch-mcp stdio --log-level debug
```

### Usage Examples in Claude Code

Once configured, you can use natural language to navigate your codebase:

#### Code Navigation (C# and TypeScript)
```
"Go to the definition of the UserService class"
"Find all references to the GetUserById method"
"Show me all TypeScript interfaces that extend BaseModel"
"Search for 'TODO' comments across the entire codebase"
"What errors are in the current project?"
"Rename the Calculate method to ComputeTotal"
```

#### Memory System (Automatic on Session Start)
```
"init_memory_hooks" - Initialize hooks (automatic via user-prompt-submit hook)
"remember_decision why we chose microservices architecture" - Store architectural decision
"recall_context authentication" - Load relevant memories about authentication
"list_memories_by_type ArchitecturalDecision" - View all architectural decisions
```

#### Straight Blazin' Fast Search Tools
```
"index_workspace" - Build search index (required before fast_text_search)
"fast_text_search 'connection string'" - Search across millions of lines instantly
"fast_file_search 'UserServce'" - Find files with typo tolerance (finds UserService.cs)
"fast_recent_files '24h'" - Find files modified in last 24 hours
"fast_directory_search 'Servces'" - Find directories with fuzzy matching
"fast_file_size_analysis 'largest'" - Find large files or analyze size distribution
"fast_similar_files 'UserService.cs'" - Find files with similar content
```

The memory system automatically:
- Loads relevant context when you use tools
- Detects architectural patterns in your edits
- Saves session summaries when you finish

### Usage Examples in VS Code

With the MCP extension installed, you can:

1. **Command Palette** (Ctrl+Shift+P):
   - `MCP: Go to Definition`
   - `MCP: Find All References`
   - `MCP: Show Call Hierarchy`

2. **Context Menu** (right-click):
   - Navigate to Definition
   - Find All Implementations
   - Rename Symbol

3. **Hover** for symbol information powered by Roslyn

### Available Tools

#### Fast Text Search Tools

##### IndexWorkspace
Build or rebuild search index for fast text search operations.

Parameters:
- `workspacePath`: Path to index
- `forceRebuild`: Force rebuild even if index exists (optional)

##### FastTextSearch
Blazingly-fast text search across millions of lines using Lucene indexing.

Parameters:
- `query`: Text to search for - supports wildcards (*), fuzzy (~), phrases ("exact")
- `workspacePath`: Path to search
- `filePattern`: Optional file pattern filter (e.g., '*.cs', 'src/**/*.ts')
- `extensions`: Optional array of extensions (e.g., ['.cs', '.ts'])
- `contextLines`: Show N lines before/after matches (optional)
- `searchType`: 'standard', 'wildcard', 'fuzzy', or 'phrase' (optional)

##### FastFileSearch
⚡ Straight blazin' fast file search with fuzzy matching and typo tolerance.

Parameters:
- `query`: File name to search for - supports wildcards (*), fuzzy (~), regex
- `workspacePath`: Path to search
- `searchType`: 'standard', 'fuzzy', 'wildcard', 'exact', 'regex' (optional)
- `maxResults`: Maximum results to return (default: 50)

##### FastRecentFiles
⚡ Find recently modified files using indexed timestamps - straight blazin' fast!

Parameters:
- `workspacePath`: Path to search
- `timeFrame`: '30m', '24h', '7d', '4w' for minutes, hours, days, weeks (default: '24h')
- `filePattern`: Optional file pattern filter
- `extensions`: Optional array of extensions to filter
- `maxResults`: Maximum results (default: 50)

##### FastFileSizeAnalysis
⚡ Analyze files by size - find large files, empty files, or size distributions.

Parameters:
- `workspacePath`: Path to analyze
- `mode`: 'largest', 'smallest', 'range', 'zero', 'distribution' (default: 'largest')
- `minSize`: Minimum size in bytes (for 'range' mode)
- `maxSize`: Maximum size in bytes (for 'range' mode)
- `filePattern`: Optional file pattern filter
- `maxResults`: Maximum results (default: 50)

##### FastSimilarFiles
⚡ Find files with similar content using Lucene's "More Like This" feature.

Parameters:
- `sourceFilePath`: Source file to find similar files for
- `workspacePath`: Path to search
- `maxResults`: Maximum similar files to return (default: 10)
- `excludeExtensions`: Optional array of extensions to exclude

##### FastDirectorySearch
⚡ Find directories/folders with fuzzy matching - locate namespaces and project folders.

Parameters:
- `query`: Directory name to search for - supports wildcards, fuzzy matching
- `workspacePath`: Path to search
- `searchType`: 'standard', 'fuzzy', 'wildcard', 'exact', 'regex' (default: 'standard')
- `maxResults`: Maximum results (default: 30)
- `groupByDirectory`: Group results by unique directories (default: true)

#### TypeScript Tools

##### SearchTypeScript
Search for TypeScript symbols (interfaces, types, classes, functions).

Parameters:
- `symbolName`: Symbol to search for
- `workspacePath`: Path to search
- `mode`: 'definition', 'references', or 'both' (optional)

#### Memory System Tools

##### InitMemoryHooks
Initialize Claude memory hooks (automatic on session start).

Parameters:
- `projectRoot`: Project root directory (optional)

##### RememberDecision
Store architectural decisions with reasoning.

Parameters:
- `decision`: The decision made
- `reasoning`: Why this decision was made
- `affectedFiles`: Optional array of affected files
- `tags`: Optional tags for categorization

##### RememberPattern
Document reusable code patterns.

Parameters:
- `pattern`: Description of the pattern
- `location`: Where it's implemented
- `usage`: When and how to use it
- `relatedFiles`: Optional array of example files

##### RememberSecurityRule
Track security requirements and compliance rules.

Parameters:
- `rule`: The security rule or requirement
- `reasoning`: Why this rule exists
- `affectedFiles`: Optional array of affected files
- `compliance`: Optional compliance framework (HIPAA, SOX, etc.)

##### RecallContext
Search memories for relevant context.

Parameters:
- `query`: What to search for
- `scopeFilter`: Optional filter by memory type
- `maxResults`: Maximum results to return

##### ListMemoriesByType
List all memories of a specific type.

Parameters:
- `scope`: Type of memories (ArchitecturalDecision, CodePattern, SecurityRule, etc.)
- `maxResults`: Maximum results (optional)

#### Core Navigation Tools

##### GoToDefinition
Navigate to the definition of a symbol at a specific location. Automatically delegates to TypeScript analysis for .ts/.js files.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

##### FindReferences (Claude-Optimized)
Find all references to a symbol with smart insights and progressive disclosure.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `includeDeclaration`: Include the symbol declaration (optional, default: true)
- `responseMode`: Response mode - 'full' or 'summary' (auto-switches for large results)

Features:
- **Smart Insights**: "Heavily used service - changes have wide impact"
- **Hotspot Detection**: Identifies files with highest reference concentrations  
- **Progressive Disclosure**: Summary → Hotspots → Full details
- **Impact Analysis**: Assesses change impact and provides risk factors

##### SearchSymbols
Search for C# symbols by name across the workspace using Roslyn.

Parameters:
- `pattern`: Search pattern (supports wildcards)
- `workspacePath`: Path to solution or project
- `kinds`: Symbol kind filter (optional) - array of "class", "interface", "method", "property", "field", "event", "namespace"
- `fuzzy`: Enable fuzzy matching (optional, default: false)
- `maxResults`: Maximum results (optional, default: 100)

##### GetDiagnostics (Claude-Optimized)  
Get compilation errors and warnings with intelligent categorization and hotspot detection.

Parameters:
- `path`: Path to the file, project, or solution
- `severities`: Filter by severity (optional) - array of "Error", "Warning", "Info", "Hidden"
- `responseMode`: Response mode - 'full' or 'summary' (auto-switches for large results)

Features:
- **Smart Categorization**: Groups diagnostics by category and severity
- **Hotspot Detection**: Identifies files with the most issues
- **Priority Recommendations**: "🚨 5 errors preventing compilation - must fix immediately"
- **Progressive Disclosure**: Summary → Categories → Individual diagnostics

##### GetHoverInfo
Get detailed symbol information at a specific position (like IDE hover tooltips).

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

##### GetImplementations
Find implementations of interfaces and abstract members.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

##### GetDocumentSymbols
Get the outline of a document showing all types and members.

Parameters:
- `filePath`: Path to the source file
- `includeMembers`: Include class members (optional, default: true)

##### GetCallHierarchy
Get incoming and/or outgoing call hierarchy for a method or property.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `direction`: "incoming", "outgoing", or "both" (optional, default: "both")
- `maxDepth`: Maximum depth to traverse (optional, default: 2)

##### RenameSymbol (Claude-Optimized)
Rename a symbol across the entire codebase with smart preview and impact analysis.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `newName`: The new name for the symbol
- `preview`: Preview changes without applying (optional, default: true)
- `responseMode`: Response mode - 'full' or 'summary' (auto-switches for large renames)

Features:
- **Impact Analysis**: "High-impact rename: 47 occurrences across 12 files"
- **Smart Insights**: "Renaming public interface will affect all implementations"
- **Hotspot Detection**: Identifies files with the most changes
- **Progressive Disclosure**: Summary → Hotspots → Full change preview

#### Advanced Analysis Tools

##### BatchOperations
Execute multiple Roslyn operations in a single call for significant performance improvements.

Parameters:
- `operations`: Array of operation objects, each containing:
  - `type`: Operation type ("search_symbols", "find_references", "go_to_definition", etc.)
  - Additional parameters specific to each operation type

##### AdvancedSymbolSearch
Advanced symbol search with comprehensive filtering capabilities.

Parameters:
- `pattern`: Search pattern
- `workspacePath`: Path to workspace or solution
- `kinds`: Symbol kinds filter (optional) - array of "Method", "Property", "Class", etc.
- `accessibility`: Accessibility levels filter (optional) - array of "Public", "Private", "Internal", etc.
- `isStatic`: Filter by static members (optional)
- `isAbstract`: Filter by abstract members (optional)
- `isVirtual`: Filter by virtual members (optional)
- `isOverride`: Filter by override members (optional)
- `returnType`: Filter by return type for methods (optional)
- `containingType`: Filter by containing type (optional)
- `containingNamespace`: Filter by containing namespace (optional)
- `fuzzy`: Use fuzzy matching (optional, default: false)
- `maxResults`: Maximum results to return (optional, default: 100)

##### DependencyAnalysis (Claude-Optimized)
Analyze code dependencies with smart insights, circular dependency detection, and architecture analysis.

Parameters:
- `symbol`: Symbol name to analyze
- `workspacePath`: Path to workspace or solution
- `direction`: "incoming", "outgoing", or "both" (optional, default: "both")
- `depth`: Analysis depth (optional, default: 3)
- `includeTests`: Include test projects (optional, default: false)
- `includeExternalDependencies`: Include external dependencies (optional, default: false)
- `responseMode`: Response mode - 'full' or 'summary' (auto-switches for complex graphs)

Features:
- **Circular Dependency Detection**: Identifies problematic dependency cycles
- **Architecture Insights**: "Layer violation detected - UI directly depends on Data"
- **Coupling Analysis**: Measures instability and identifies refactoring opportunities
- **Smart Insights**: "Stable component - many depend on it but it has few dependencies"

##### ProjectStructureAnalysis (Claude-Optimized)
Comprehensive project analysis with intelligent categorization, hotspot detection, and solution-level insights.

Parameters:
- `workspacePath`: Path to workspace or solution
- `includeMetrics`: Include code metrics (optional, default: true)
- `includeFiles`: Include source file listing (optional, default: false)
- `includeNuGetPackages`: Include NuGet package analysis (optional, default: false)
- `responseMode`: Response mode - 'full' or 'summary' (auto-switches for large solutions)

Features:
- **Solution-Level Insights**: "Large enterprise solution with 47 projects"
- **Project Categorization**: Groups projects by type (Web, API, Tests, Libraries)
- **Hotspot Detection**: Identifies largest projects requiring attention
- **Circular Reference Detection**: Finds problematic project dependencies
- **NuGet Analysis**: Detects version conflicts and suggests updates

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

- `MCP_LOG_LEVEL` - Override log level (Debug, Information, Warning, Error)
- `MCP_WORKSPACE_PATH` - Default workspace path
- `MCP_MAX_MEMORY_MB` - Memory limit for AOT builds

## Development

### Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- MSBuild (included with Visual Studio or Build Tools)

### Building

```bash
# Build the project
dotnet build

# Run tests
dotnet test

# Build for release with AOT
dotnet publish -c Release -r win-x64
```

### Testing with MCP Inspector

```bash
npx @modelcontextprotocol/inspector coa-codesearch-mcp stdio
```

## Architecture

The server is built with:
- **Roslyn** - Microsoft.CodeAnalysis for C# code analysis
- **TypeScript Language Service** - Via automatic tsserver integration
- **Lucene.NET** - High-performance text indexing and search
- **MSBuildWorkspace** - For loading .NET solutions and projects
- **JSON-RPC 2.0** - Standard MCP communication protocol
- **LRU Cache** - Efficient workspace management
- **Native AOT** - For fast startup and reduced memory usage
- **Claude Code Hooks** - PowerShell/Bash scripts for automatic context management

## Performance

Target metrics:
- Startup time: < 100ms (with AOT)
- GoToDefinition: < 50ms for cached workspace
- FindReferences: < 200ms for average project
- Symbol search: < 100ms for prefix match
- Memory usage: < 500MB for typical solution

### Performance Enhancements
- **Batch Operations**: Reduce tool calls by 60-80% when performing multiple operations
- **Advanced Filtering**: Eliminate manual result filtering with server-side symbol filtering
- **Workspace Caching**: Intelligent LRU caching for faster subsequent operations
- **Parallel Processing**: Multi-threaded symbol analysis for large codebases

## Troubleshooting

### Common Issues

1. **"MSBuild not found" error**
   - Install Visual Studio Build Tools or Visual Studio
   - The server automatically registers MSBuild on startup

2. **"Could not load workspace" error**
   - Ensure the solution/project builds successfully in Visual Studio
   - Check that all NuGet packages are restored
   - Try opening the solution in Visual Studio first

3. **TypeScript not working**
   - Ensure Node.js is installed: `node --version`
   - The server will auto-install TypeScript on first use
   - Check `%LOCALAPPDATA%\COA.CodeSearch.McpServer\typescript` for installation
   - See TYPESCRIPT_SETUP.md for manual installation options

4. **Authentication errors with COA feed**
   - Set the `COA_NUGET_PAT` environment variable with your Personal Access Token
   - Or authenticate using: `dotnet nuget add source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json -n COA -u YOUR_USERNAME -p YOUR_PAT`

5. **Server not responding in Claude Code**
   - Check that the tool is installed: `dotnet tool list -g`
   - Verify the tool runs: `coa-codesearch-mcp stdio` (should wait for input)
   - Check Claude Code logs: `claude logs`

6. **Memory hooks not working**
   - Run `init_memory_hooks` to initialize (automatic on session start)
   - Check `.claude/hooks/` directory exists
   - Test with `test_memory_hooks tool-call`
   - Ensure PowerShell execution policy allows scripts

### Updating the Tool

```bash
# Update to latest version
dotnet tool update --global COA.CodeSearch.McpServer --add-source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json
```

## Contributing

This is an internal COA tool. For issues or feature requests, please use the Azure DevOps project.

## License

Internal use only - Children of America proprietary software.