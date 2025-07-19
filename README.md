# COA Roslyn MCP Server

A high-performance Model Context Protocol (MCP) server for .NET that provides Language Server Protocol (LSP)-like capabilities for navigating and searching .NET codebases using Roslyn.

## Overview

COA Roslyn MCP Server leverages Microsoft's Roslyn compiler platform to provide powerful code navigation and analysis features for .NET projects. It's designed as a native .NET tool, offering significant performance advantages over Python-based alternatives.

## Features

### Core Navigation Tools (Claude-Optimized)
- **Go to Definition** - Navigate to symbol definitions across your codebase
- **Find References** - Find all references to a symbol with smart insights and progressive disclosure
- **Symbol Search** - Search for types, methods, properties, and other symbols
- **Get Diagnostics** - Get compilation errors and warnings with intelligent categorization and hotspot detection
- **Hover Information** - Get detailed symbol information at cursor position
- **Find Implementations** - Find all implementations of interfaces and abstract members
- **Document Symbols** - Get document outline with all types and members
- **Call Hierarchy** - Explore incoming and outgoing call trees
- **Rename Symbol** - Rename symbols across entire codebase with smart preview and impact analysis

### Advanced Analysis Tools (Claude-Optimized)
- **Batch Operations** - Execute multiple Roslyn operations in a single call for 60-80% performance improvement
- **Advanced Symbol Search** - Filter symbols by accessibility, static/abstract modifiers, return types, containing types/namespaces
- **Dependency Analysis** - Analyze incoming/outgoing code dependencies with smart insights, circular dependency detection, and architecture pattern analysis
- **Project Structure Analysis** - Comprehensive project analysis with intelligent categorization, hotspot detection, and solution-level insights

### Performance & Architecture  
- **Workspace Management** - Efficient caching with LRU eviction for large solutions
- **Native Performance** - Built with .NET 9.0 and AOT compilation support  
- **Parallel Processing** - Multi-threaded symbol analysis and workspace operations
- **Progressive Disclosure** - Automatic token limit handling with smart summaries and drill-down capabilities
- **Auto-Mode Switching** - Intelligent response optimization for large results (automatically switches to summary mode for responses >5,000 tokens)

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
dotnet tool install --global COA.Roslyn.McpServer --add-source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json
```

### For Claude Code

1. Install the tool globally (see above)
2. Add the MCP server using Claude Code CLI:

```bash
claude mcp add coa-roslyn-mcp
```

Or manually add to your Claude Code configuration (`%APPDATA%\Claude\claude_code_config.json` on Windows):

```json
{
  "mcpServers": {
    "coa-roslyn-mcp": {
      "command": "coa-roslyn-mcp",
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
    "roslyn": {
      "command": "coa-roslyn-mcp",
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
    "coa-roslyn": {
      "command": "coa-roslyn-mcp",
      "args": ["stdio"],
      "languages": ["csharp", "vb"]
    }
  }
}
```

### For GitHub Copilot (via MCP Bridge)

When GitHub Copilot supports MCP servers, configure it in your `.github/copilot-mcp.json`:

```json
{
  "servers": {
    "roslyn": {
      "command": "coa-roslyn-mcp",
      "args": ["stdio"],
      "filePatterns": ["**/*.cs", "**/*.vb", "**/*.csproj", "**/*.sln"]
    }
  }
}
```

## Usage

### Running the Server

```bash
# Run in STDIO mode (for MCP clients)
coa-roslyn-mcp stdio

# Run with verbose logging
coa-roslyn-mcp stdio --log-level debug
```

### Usage Examples in Claude Code

Once configured, you can use natural language to navigate your C# codebase:

```
"Go to the definition of the UserService class"
"Find all references to the GetUserById method"
"Show me all classes that implement IRepository"
"What errors are in the current project?"
"Rename the Calculate method to ComputeTotal"
"Show me the call hierarchy for ProcessOrder"
"Get the outline of the current file"
```

Claude will automatically use the appropriate MCP tools to fulfill your requests.

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

#### Core Navigation Tools

##### GoToDefinition
Navigate to the definition of a symbol at a specific location.

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
Search for symbols by name across the workspace.

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
npx @modelcontextprotocol/inspector coa-roslyn-mcp stdio
```

## Architecture

The server is built with:
- **Roslyn** - Microsoft.CodeAnalysis for code analysis
- **MSBuildWorkspace** - For loading .NET solutions and projects
- **JSON-RPC 2.0** - Standard MCP communication protocol
- **LRU Cache** - Efficient workspace management
- **Native AOT** - For fast startup and reduced memory usage

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

3. **Authentication errors with COA feed**
   - Set the `COA_NUGET_PAT` environment variable with your Personal Access Token
   - Or authenticate using: `dotnet nuget add source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json -n COA -u YOUR_USERNAME -p YOUR_PAT`

4. **Server not responding in Claude Code**
   - Check that the tool is installed: `dotnet tool list -g`
   - Verify the tool runs: `coa-roslyn-mcp stdio` (should wait for input)
   - Check Claude Code logs: `claude logs`

### Updating the Tool

```bash
# Update to latest version
dotnet tool update --global COA.Roslyn.McpServer --add-source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json
```

## Contributing

This is an internal COA tool. For issues or feature requests, please use the Azure DevOps project.

## License

Internal use only - Children of America proprietary software.