# COA Roslyn MCP Server

A high-performance Model Context Protocol (MCP) server for .NET that provides Language Server Protocol (LSP)-like capabilities for navigating and searching .NET codebases using Roslyn.

## Overview

COA Roslyn MCP Server leverages Microsoft's Roslyn compiler platform to provide powerful code navigation and analysis features for .NET projects. It's designed as a native .NET tool, offering significant performance advantages over Python-based alternatives.

## Features

- **Go to Definition** - Navigate to symbol definitions across your codebase
- **Find References** - Find all references to a symbol
- **Symbol Search** - Search for types, methods, properties, and other symbols
- **Diagnostics** - Get compilation errors and warnings
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

#### SearchSymbols
Search for symbols by name across the workspace.

Parameters:
- `query`: Search query (supports wildcards)
- `kind`: Symbol kind filter (optional) - "type", "method", "property", "field", "namespace"
- `limit`: Maximum results (default: 50)

#### GetDiagnostics
Get compilation diagnostics for a file or project.

Parameters:
- `filePath`: Path to the file or project

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