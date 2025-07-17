# COA Roslyn MCP Server

A high-performance Model Context Protocol (MCP) server for .NET that provides Language Server Protocol (LSP)-like capabilities for navigating and searching .NET codebases using Roslyn.

## Features

- **Go to Definition**: Navigate to symbol definitions
- **Find References**: Find all references to a symbol
- **Search Symbols**: Search for symbols by name pattern with wildcard and fuzzy matching support
- **Roslyn-powered**: Leverages Microsoft's Roslyn compiler platform for accurate code analysis
- **Workspace caching**: Efficient caching with LRU eviction for better performance
- **MSBuild integration**: Loads and analyzes complete .NET solutions and projects

## Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or Build Tools for Visual Studio (for MSBuild)

## Building

```bash
# Clone the repository
git clone <repository-url>
cd "COA Roslyn MCP"

# Restore packages
dotnet restore

# Build the project
dotnet build

# Build for release
dotnet publish -c Release
```

## Running the Server

The server runs in STDIO mode for MCP compatibility:

```bash
# Run in development
dotnet run --project COA.Roslyn.McpServer -- stdio

# Run the built executable
./COA.Roslyn.McpServer/bin/Debug/net9.0/COA.Roslyn.McpServer.exe stdio
```

## Testing with MCP Inspector

You can test the server using the MCP Inspector tool:

```bash
npx @modelcontextprotocol/inspector dotnet run --project COA.Roslyn.McpServer -- stdio
```

## Integrating with Claude Desktop

Add the following to your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "C:\\path\\to\\COA.Roslyn.McpServer.exe",
      "args": ["stdio"]
    }
  }
}
```

## Available Tools

### go_to_definition
Navigate to the definition of a symbol at a specific position in a file.

**Parameters:**
- `filePath` (string, required): The absolute path to the file
- `line` (integer, required): The line number (1-based)
- `column` (integer, required): The column number (1-based)

### find_references
Find all references to a symbol at a specific position in a file.

**Parameters:**
- `filePath` (string, required): The absolute path to the file
- `line` (integer, required): The line number (1-based)
- `column` (integer, required): The column number (1-based)
- `includePotential` (boolean, optional): Include potential references (may include false positives)

### search_symbols
Search for symbols in the workspace by name pattern.

**Parameters:**
- `pattern` (string, required): The search pattern (supports wildcards: * and ?)
- `workspacePath` (string, required): The path to the solution or project file to search in
- `kinds` (array of strings, optional): Symbol kinds to include (e.g., 'class', 'method', 'property', 'interface', 'enum')
- `fuzzy` (boolean, optional): Use fuzzy matching instead of wildcard matching
- `maxResults` (integer, optional): Maximum number of results to return (default: 100)

## Configuration

The server can be configured using `appsettings.json`:

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

## Environment Variables

- `MCP_LOG_LEVEL`: Override the default log level
- `MCP_MAX_WORKSPACES`: Maximum number of cached workspaces
- `MCP_WORKSPACE_TIMEOUT`: Timeout for workspace eviction

## Troubleshooting

### MSBuild not found
Ensure you have Visual Studio 2022 or Build Tools for Visual Studio installed.

### High memory usage
Adjust the `MaxWorkspaces` setting in `appsettings.json` to limit cached workspaces.

### Slow performance
- Enable release mode builds: `dotnet build -c Release`
- Check for compilation errors in the target projects
- Reduce `ParallelismDegree` if CPU usage is too high

## Development

### Project Structure
```
COA.Roslyn.McpServer/
├── Program.cs                     # Entry point and MCP server setup
├── Services/
│   └── RoslynWorkspaceService.cs  # Manages Roslyn workspaces
├── Tools/
│   ├── GoToDefinitionTool.cs      # Navigate to definitions
│   ├── FindReferencesTool.cs      # Find all references
│   └── SearchSymbolsTool.cs       # Search for symbols
├── Models/
│   ├── LocationInfo.cs            # Location information model
│   └── SymbolInfo.cs              # Symbol information model
└── appsettings.json               # Configuration
```

### Adding New Tools

1. Create a new tool class in the `Tools` folder
2. Implement the tool logic using Roslyn APIs
3. Register the tool in `Program.cs`
4. Add tests in the test project

## License

[Your License Here]