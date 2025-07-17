# COA Roslyn MCP Server

A high-performance Model Context Protocol (MCP) server for .NET that provides Language Server Protocol (LSP)-like capabilities for navigating and searching .NET codebases using Roslyn.

## Overview

COA Roslyn MCP Server leverages Microsoft's Roslyn compiler platform to provide powerful code navigation and analysis features for .NET projects. It's designed as a native .NET tool, offering significant performance advantages over Python-based alternatives.

## Features

- **Go to Definition** - Navigate to symbol definitions across your codebase
- **Find References** - Find all references to a symbol
- **Symbol Search** - Search for types, methods, properties, and other symbols
- **Get Diagnostics** - Get compilation errors and warnings for files, projects, or solutions
- **Hover Information** - Get detailed symbol information at cursor position
- **Find Implementations** - Find all implementations of interfaces and abstract members
- **Document Symbols** - Get document outline with all types and members
- **Call Hierarchy** - Explore incoming and outgoing call trees
- **Rename Symbol** - Rename symbols across entire codebase with preview
- **Workspace Management** - Efficient caching with LRU eviction for large solutions
- **Native Performance** - Built with .NET 9.0 and AOT compilation support

## Installation

### As a Global Tool

```bash
dotnet tool install --global COA.Roslyn.McpServer --add-source https://childrensal.pkgs.visualstudio.com/_packaging/COA/nuget/v3/index.json
```

### For Claude Desktop

1. Install the tool globally (see above)
2. Add to your Claude Desktop configuration:

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

## Usage

### Running the Server

```bash
# Run in STDIO mode (for Claude Desktop)
coa-roslyn-mcp stdio

# Run with verbose logging
coa-roslyn-mcp stdio --log-level debug
```

### Available Tools

#### GoToDefinition
Navigate to the definition of a symbol at a specific location.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

#### FindReferences
Find all references to a symbol at a specific location.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `includePotential`: Include potential references (optional, default: false)

#### SearchSymbols
Search for symbols by name across the workspace.

Parameters:
- `pattern`: Search pattern (supports wildcards)
- `workspacePath`: Path to solution or project
- `kinds`: Symbol kind filter (optional) - array of "class", "interface", "method", "property", "field", "event", "namespace"
- `fuzzy`: Enable fuzzy matching (optional, default: false)
- `maxResults`: Maximum results (optional, default: 100)

#### GetDiagnostics
Get compilation errors and warnings for a file, project, or solution.

Parameters:
- `path`: Path to the file, project, or solution
- `severities`: Filter by severity (optional) - array of "error", "warning", "info", "hidden"

#### GetHoverInfo
Get detailed symbol information at a specific position (like IDE hover tooltips).

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

#### GetImplementations
Find implementations of interfaces and abstract members.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)

#### GetDocumentSymbols
Get the outline of a document showing all types and members.

Parameters:
- `filePath`: Path to the source file
- `includeMembers`: Include class members (optional, default: true)

#### GetCallHierarchy
Get incoming and/or outgoing call hierarchy for a method or property.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `direction`: "incoming", "outgoing", or "both" (optional, default: "both")
- `maxDepth`: Maximum depth to traverse (optional, default: 2)

#### RenameSymbol
Rename a symbol across the entire codebase.

Parameters:
- `filePath`: Path to the source file
- `line`: Line number (1-based)
- `column`: Column number (1-based)
- `newName`: The new name for the symbol
- `preview`: Preview changes without applying (optional, default: true)

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

## Contributing

This is an internal COA tool. For issues or feature requests, please use the Azure DevOps project.

## License

Internal use only - Children of America proprietary software.