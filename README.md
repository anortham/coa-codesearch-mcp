# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server that provides blazing-fast code search and navigation capabilities across multiple languages. Built with .NET 9.0, it leverages Roslyn for C# analysis, includes TypeScript support via tsserver, and features Lucene-powered text indexing for millisecond search performance.

## 🚀 Features

### Core Capabilities
- **Multi-language Support**: C# (via Roslyn) and TypeScript/JavaScript (via tsserver)
- **Blazing Fast Search**: Lucene indexing for instant text search across millions of lines
- **Smart Memory System**: Persistent architectural knowledge and decision tracking
- **Progressive Disclosure**: Intelligent response handling optimized for AI assistants
- **Cross-Language Navigation**: Go to definition, find references across C# and TypeScript
- **Workspace Intelligence**: Automatic project structure analysis and dependency tracking

### Performance
- Startup time: < 100ms (with AOT compilation)
- Text search: < 50ms across entire codebases
- Go to definition: < 50ms for cached workspaces
- Memory usage: < 500MB for typical solutions

## 📋 Prerequisites

- .NET 9.0 SDK or later
- Node.js (for TypeScript support - automatically installed if not present)
- Visual Studio 2022 or Build Tools for Visual Studio (for MSBuild)

## 🏗️ Building from Source

```bash
# Clone the repository
git clone https://github.com/YOUR-ORG/codesearch-mcp-server.git
cd codesearch-mcp-server

# Restore dependencies
dotnet restore

# Build in debug mode
dotnet build

# Build in release mode for better performance
dotnet build -c Release

# Create a published executable
dotnet publish -c Release -r win-x64  # For Windows
dotnet publish -c Release -r linux-x64  # For Linux
dotnet publish -c Release -r osx-x64    # For macOS
```

## 🏃 Running from Local Build

### Method 1: Using dotnet run (Development)
```bash
# From the repository root
dotnet run --project COA.CodeSearch.McpServer -- stdio

# Or from the project directory
cd COA.CodeSearch.McpServer
dotnet run -- stdio
```

### Method 2: Using the built executable (Faster startup)
```bash
# Windows
./COA.CodeSearch.McpServer/bin/Debug/net9.0/COA.CodeSearch.McpServer.exe stdio

# Linux/macOS
./COA.CodeSearch.McpServer/bin/Debug/net9.0/COA.CodeSearch.McpServer stdio
```

### Method 3: Using the published executable (Production)
```bash
# After running dotnet publish
./COA.CodeSearch.McpServer/bin/Release/net9.0/win-x64/publish/COA.CodeSearch.McpServer.exe stdio
```

## 🔧 Integration with AI Assistants

### Claude Desktop
Add to your Claude Desktop configuration:

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

### Claude Code
For Claude Code integration, build the server and follow Claude Code's MCP server integration process. The server runs in STDIO mode for communication.

### Testing with MCP Inspector
```bash
npx @modelcontextprotocol/inspector dotnet run --project COA.CodeSearch.McpServer -- stdio
```

## 🛠️ Available Tools (51 Total)

### Search & Navigation (Core Tools)
- `go_to_definition` - Navigate to symbol definitions (C# & TypeScript)
- `find_references` - Find all usages of a symbol
- `search_symbols` - Search for C# symbols by name
- `search_typescript` - Search for TypeScript symbols
- `get_hover_info` - Get type information and documentation

### Fast Search Tools (Lucene-powered)
- `index_workspace` - Build search index (required first!)
- `fast_text_search` - Blazing fast text search (<50ms)
- `fast_file_search` - Find files with fuzzy matching
- `fast_recent_files` - Find recently modified files
- `fast_similar_files` - Find files with similar content
- `fast_directory_search` - Search for directories
- `fast_file_size_analysis` - Analyze files by size

### Code Analysis
- `get_implementations` - Find all implementations of interfaces/abstract classes
- `get_call_hierarchy` - Trace method call chains
- `get_document_symbols` - Get file structure outline
- `get_diagnostics` - Check compilation errors
- `dependency_analysis` - Analyze code dependencies
- `project_structure_analysis` - Analyze solution structure
- `rename_symbol` - Safely rename symbols across codebase

### Memory System
- `recall_context` - Load relevant context (use at session start!)
- `flexible_store_memory` - Store any type of memory
- `flexible_search_memories` - Search stored memories
- `flexible_update_memory` - Update existing memories
- `flexible_link_memories` - Create relationships
- `flexible_store_working_memory` - Temporary session memories
- `create_checklist` - Create persistent task lists
- `backup_memories_to_sqlite` - Backup for version control

### TypeScript-specific
- `typescript_go_to_definition` - Navigate in TypeScript
- `typescript_find_references` - Find TypeScript references

### Utilities
- `batch_operations` - Run multiple operations in parallel
- `set_logging` - Control file-based logging
- `get_version` - Get server version info

## ⚙️ Configuration

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "COA.CodeSearch": "Debug"
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
    "AutoModeSwitchThreshold": 5000,
    "DefaultMaxResults": 50
  },
  "FileWatcher": {
    "Enabled": true,
    "DebounceMilliseconds": 500
  },
  "WorkspaceAutoIndex": {
    "Enabled": true,
    "StartupDelayMilliseconds": 3000
  }
}
```

### Environment Variables
- `MCP_LOG_LEVEL` - Override log level
- `MCP_WORKSPACE_PATH` - Default workspace path
- `CODESEARCH_INDEX_PATH` - Custom index location

## 📁 Data Storage

The server creates a `.codesearch` directory in your workspace containing:
- `index/` - Lucene search indexes
- `project-memory/` - Shared architectural decisions
- `local-memory/` - Personal work sessions
- `memories.db` - SQLite backup for version control
- `logs/` - Debug logs (when enabled)

Add `.codesearch/` to your `.gitignore` to exclude these files from version control.

## 🚀 Quick Start Guide

1. **First Time Setup**
   ```bash
   # Build the server
   dotnet build -c Release
   
   # Run with a test project
   dotnet run --project COA.CodeSearch.McpServer -- stdio
   ```

2. **In your AI Assistant**
   ```
   # First, index your workspace (one-time setup)
   index_workspace --workspacePath "C:/YourProject"
   
   # Load previous context
   recall_context "working on authentication"
   
   # Search for code
   fast_text_search --query "login" --workspacePath "C:/YourProject"
   
   # Navigate to definitions
   go_to_definition --filePath "Auth.cs" --line 25 --column 15
   ```

3. **Memory System Usage**
   ```
   # Store architectural decision
   flexible_store_memory --type "ArchitecturalDecision" 
     --content "Using JWT for authentication"
   
   # Create a checklist
   create_checklist --title "Implement Auth System"
   ```

## 🐛 Troubleshooting

### Common Issues

**MSBuild not found**
- Install Visual Studio 2022 or Build Tools for Visual Studio
- The server will auto-detect MSBuild location

**High memory usage**
- Reduce `MaxWorkspaces` in configuration
- Enable workspace eviction
- Use `index_workspace` selectively

**Slow startup**
- Use Release build: `dotnet build -c Release`
- First startup installs TypeScript (one-time)
- Consider AOT compilation for production

**Index not updating**
- File watcher auto-updates indexes
- Manual refresh: re-run `index_workspace`
- Check logs with `set_logging --action start`

### Debug Logging
```bash
# Enable debug logging
set_logging --action start --level Debug

# View logs
set_logging --action list

# Logs location: %LOCALAPPDATA%\COA.CodeSearch\.codesearch\logs\
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Ensure all tests pass: `dotnet test`
5. Submit a pull request

See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed guidelines.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [Roslyn](https://github.com/dotnet/roslyn) for C# analysis
- Uses [Lucene.NET](https://lucenenet.apache.org/) for search indexing
- Implements the [Model Context Protocol](https://modelcontextprotocol.io/)
- TypeScript support via [tsserver](https://github.com/microsoft/TypeScript)

## 📚 Documentation

- [Configuration Guide](docs/CONFIGURATION.md)
- [Tool Reference](docs/TOOLS.md)
- [Memory System Guide](docs/MEMORY.md)
- [API Documentation](docs/API.md)