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

## 🔨 Building from Source

The project uses project references for all internal dependencies. To build:

```bash
# Clone the repository
git clone https://github.com/anortham/coa-codesearch-mcp.git
cd coa-codesearch-mcp

# Build the solution
dotnet build

# Or build specific projects
dotnet build COA.CodeSearch.McpServer/COA.CodeSearch.McpServer.csproj
```

All internal dependencies (like COA.Mcp.Protocol) use project references, not NuGet packages.

## 🌍 Cross-Platform Support

The CodeSearch MCP Server is fully cross-platform and runs on:
- **Windows** (x64, ARM64)
- **Linux** (x64, ARM64) 
- **macOS** (x64, Apple Silicon)

### Platform-Specific Notes
- **Case Sensitivity**: Linux filesystems are case-sensitive. The server handles this gracefully but be aware that `UserService.cs` and `userservice.cs` are different files on Linux.
- **Path Separators**: The server automatically handles path separator differences between platforms.
- **MSBuild/SDK**: On Linux/macOS, ensure you have the .NET SDK installed (Visual Studio not required).

## 🏗️ Building from Source

```bash
# Clone the repository
git clone https://github.com/anortham/coa-codesearch-mcp.git
cd coa-codesearch-mcp

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

#### Method 1: Using Claude MCP Add Command (Easiest)
1. Build the project in Release mode:
   ```bash
   cd "C:\path\to\coa-codesearch-mcp"
   dotnet build -c Release
   ```

2. Use Claude Code's built-in command:
   ```bash
   claude mcp add "C:\path\to\coa-codesearch-mcp\COA.CodeSearch.McpServer\bin\Release\net9.0\COA.CodeSearch.McpServer.exe" --name codesearch
   ```

3. Claude Code will automatically restart with the server loaded

#### Method 2: Manual Configuration
1. Build the project in Release mode:
   ```bash
   cd "C:\path\to\coa-codesearch-mcp"
   dotnet build -c Release
   ```

2. Add to your Claude Code settings.json:
   ```json
   {
     "mcpServers": {
       "codesearch": {
         "type": "stdio",
         "command": "C:\\path\\to\\coa-codesearch-mcp\\COA.CodeSearch.McpServer\\bin\\Release\\net9.0\\COA.CodeSearch.McpServer.exe",
         "args": [],
         "env": {}
       }
     }
   }
   ```

3. Restart Claude Code to load the MCP server

#### Method 3: Using Published Build (Production)
1. Create a published build:
   ```bash
   cd "C:\path\to\coa-codesearch-mcp"
   dotnet publish -c Release -r win-x64 --self-contained
   ```

2. Add to your Claude Code settings.json:
   ```json
   {
     "mcpServers": {
       "codesearch": {
         "type": "stdio",
         "command": "C:\\path\\to\\coa-codesearch-mcp\\COA.CodeSearch.McpServer\\bin\\Release\\net9.0\\win-x64\\publish\\COA.CodeSearch.McpServer.exe",
         "args": [],
         "env": {}
       }
     }
   }
   ```

**Note**: Replace `C:\path\to\coa-codesearch-mcp` with your actual project path.

#### For GitHub Clones
If you cloned from GitHub:

**Using claude mcp add:**
```bash
claude mcp add "C:\path\to\coa-codesearch-mcp\COA.CodeSearch.McpServer\bin\Release\net9.0\COA.CodeSearch.McpServer.exe" --name codesearch
```

**Or manually edit settings.json:**
```json
{
  "mcpServers": {
    "codesearch": {
      "type": "stdio",
      "command": "C:\\path\\to\\coa-codesearch-mcp\\COA.CodeSearch.McpServer\\bin\\Release\\net9.0\\COA.CodeSearch.McpServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

### Testing with MCP Inspector
```bash
npx @modelcontextprotocol/inspector dotnet run --project COA.CodeSearch.McpServer -- stdio
```

## 🛠️ Available Tools (52 Total)

The server provides both standard tools and **AI-optimized V2 versions**. V2 tools feature intelligent summaries, progress tracking, and token-aware responses that automatically adapt to prevent overwhelming AI assistants.

### 🚀 V2 Tool Features
- **Intelligent Summaries**: Automatic insights, hotspots, and actionable next steps
- **Token-Aware**: Auto-switches to summary mode when responses exceed 5,000 tokens
- **Progress Tracking**: Real-time notifications for long-running operations
- **Pattern Recognition**: Identifies trends, anomalies, and optimization opportunities
- **Progressive Disclosure**: Request specific details without re-executing operations

### Search & Navigation
- `go_to_definition` - Navigate to symbol definitions (C# & TypeScript)
- `find_references` - 🔍 Find ALL usages instantly with AI-optimized summaries (V2)
- `search_symbols` - Search C# symbols by name
- `search_symbols_v2` - 🔍 AI-optimized symbol search with distribution insights
- `search_typescript` - 🔍 Find TypeScript symbols FAST
- `get_hover_info` - Get type information and documentation

### Fast Search Tools (Lucene-powered)
- `index_workspace` - 🏗️ Build search index with progress notifications (required first!)
- `fast_text_search` - Blazing fast text search (<50ms)
- `fast_text_search_v2` - 🔍 AI-optimized with file distribution and hotspot analysis
- `fast_file_search` - Find files with fuzzy matching
- `fast_file_search_v2` - 🔍 AI-optimized file search with directory insights
- `fast_recent_files` - 🔍 Find recently modified files with time context
- `fast_similar_files` - Find files with similar content using ML algorithms
- `fast_directory_search` - Search for directories with fuzzy matching
- `fast_file_size_analysis` - Analyze files by size with distribution insights

### Code Analysis
- `get_implementations` - Find all implementations of interfaces/abstract classes
- `get_implementations_v2` - 🔍 AI-optimized with inheritance pattern analysis
- `get_call_hierarchy` - Trace method call chains
- `get_call_hierarchy_v2` - 🔍 AI-optimized with circular dependency detection
- `get_document_symbols` - Get file structure outline
- `get_diagnostics` - 🔍 AI-optimized error analysis with priority recommendations (V2)
- `dependency_analysis` - 🔍 AI-optimized dependency insights with refactoring suggestions (V2)
- `project_structure_analysis` - 🔍 AI-optimized project analysis with architectural insights (V2)
- `rename_symbol` - 🔍 Safe renaming with AI-powered impact analysis (V2)

### Memory System
- `recall_context` - 🧠 Load relevant context (use at session start!)
- `flexible_store_memory` - Store any type of memory with custom fields
- `flexible_search_memories` - Search stored memories with natural language
- `flexible_search_memories_v2` - 🔍 AI-optimized memory search with insights and patterns
- `flexible_update_memory` - Update existing memories
- `flexible_link_memories` - Create relationships between memories
- `flexible_store_working_memory` - Temporary session memories with auto-expiration
- `create_checklist` - 📝 Create persistent task lists
- `backup_memories_to_sqlite` - Backup for version control

### TypeScript-specific
- `search_typescript` - 🔍 Find TypeScript symbols FAST
- `typescript_go_to_definition` - ⚡ Jump to TypeScript definitions instantly
- `typescript_find_references` - Find all TypeScript usages with tsserver accuracy
- `typescript_rename_symbol` - 🔧 Rename TypeScript symbols across the entire codebase

### Utilities
- `batch_operations` - Run multiple operations in parallel
- `batch_operations_v2` - 🚀 AI-optimized batch execution with pattern analysis
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
- `project-memory/` - Shared architectural decisions and team knowledge
- `local-memory/` - Personal work sessions and notes
- `backups/` - SQLite backup storage (manual backups via `backup_memories_to_sqlite` tool)
- `memories.db` - SQLite backup database (created by `backup_memories_to_sqlite` tool)
- `logs/` - Debug logs (when enabled)

Add `.codesearch/` to your `.gitignore` to exclude these files from version control.

### Memory Backup System

The memory system uses two storage mechanisms:

**Lucene Indexes** (Primary Storage):
- Located in `.codesearch/project-memory/` and `.codesearch/local-memory/`
- High-performance full-text search indexes
- Not suitable for version control (binary files)
- Automatically maintained by the memory system

**SQLite Backup** (`.codesearch/memories.db`):
- Single portable database file created by `backup_memories_to_sqlite`
- Perfect for version control and team sharing
- Contains only essential memory data (no index structures)
- Can be restored on any machine with `restore_memories_from_sqlite`

Key features:
- **Manual Backups**: Use `backup_memories_to_sqlite` to create/update the SQLite backup
- **Incremental**: Only backs up memories modified since last backup
- **Team Sharing**: By default, backs up only project-level memories (architectural decisions, patterns, etc.)
- **Selective Restore**: Restored memories don't overwrite existing ones

Example backup workflow:
```bash
# Create a backup before major changes
backup_memories_to_sqlite

# Include local memories in backup (work sessions, personal notes)
backup_memories_to_sqlite --includeLocal true

# Restore from backup on new machine
restore_memories_from_sqlite
```

## 🚀 Quick Start Guide

1. **First Time Setup**
   ```bash
   # Build the server
   dotnet build -c Release
   
   # Run with a test project
   dotnet run --project COA.CodeSearch.McpServer -- stdio
   ```

2. **Basic Usage**
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

3. **V2 Tools Example**
   ```
   # Use V2 tools for AI-optimized results
   find_references --filePath "IUserService.cs" --line 10 --column 15
   
   # Response includes:
   # - Summary: "47 references across 12 files"
   # - Key Insight: "Heavy usage in Controllers (65%)"
   # - Hotspots: UserController.cs (15), AuthController.cs (8)
   # - Next Action: "Consider interface segregation"
   ```

4. **Memory System Usage**
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