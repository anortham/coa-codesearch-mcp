# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server that provides blazing-fast code search and navigation capabilities across multiple languages. Built with .NET 9.0, it leverages Roslyn for C# analysis, includes TypeScript support via tsserver, and features Lucene-powered text indexing for millisecond search performance.

## 🚀 Features

### Core Capabilities
- **Multi-language Support**: C# (via Roslyn) and TypeScript/JavaScript (via tsserver)
- **Blazing Fast Search**: Lucene indexing for instant text search across millions of lines
- **Smart Memory System**: Persistent architectural knowledge and decision tracking
- **Progressive Disclosure**: Intelligent response handling optimized for AI assistants
- **Cross-Language Navigation**: Go to definition works across C# and TypeScript
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

## 🚀 Quick Start - Building from Source

```bash
# 1. Clone and build
git clone https://github.com/anortham/coa-codesearch-mcp.git
cd coa-codesearch-mcp
dotnet build -c Release

# 2. Add to Claude Code
# Windows
claude mcp add codesearch "C:\path\to\coa-codesearch-mcp\COA.CodeSearch.McpServer\bin\Release\net9.0\COA.CodeSearch.McpServer.exe"

# macOS/Linux
claude mcp add codesearch ~/Source/coa-codesearch-mcp/COA.CodeSearch.McpServer/bin/Release/net9.0/COA.CodeSearch.McpServer

# That's it! Claude Code will restart with the server loaded.
```

## 🌍 Cross-Platform Support

The CodeSearch MCP Server is fully cross-platform and runs on:
- **Windows** (x64, ARM64)
- **Linux** (x64, ARM64) 
- **macOS** (x64, Apple Silicon)

### Platform-Specific Notes
- **Case Sensitivity**: Linux filesystems are case-sensitive. The server handles this gracefully but be aware that `UserService.cs` and `userservice.cs` are different files on Linux.
- **Path Separators**: The server automatically handles path separator differences between platforms.
- **MSBuild/SDK**: On Linux/macOS, ensure you have the .NET SDK installed (Visual Studio not required).

## 🔧 Integration Details

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

### Claude Code - Alternative Methods

#### Manual Configuration
If you prefer to manually configure or the `claude mcp add` command isn't available:

1. Build the project:
   ```bash
   dotnet build -c Release
   ```

2. Add to your Claude Code settings.json:
   
   **Windows:**
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
   
   **macOS/Linux:**
   ```json
   {
     "mcpServers": {
       "codesearch": {
         "type": "stdio",
         "command": "/home/user/coa-codesearch-mcp/COA.CodeSearch.McpServer/bin/Release/net9.0/COA.CodeSearch.McpServer",
         "args": [],
         "env": {}
       }
     }
   }
   ```

3. Restart Claude Code

#### Published Build (Optimized)
For the best performance:
```bash
# Create optimized build
dotnet publish -c Release -r win-x64 --self-contained   # Windows
dotnet publish -c Release -r osx-x64 --self-contained    # macOS Intel
dotnet publish -c Release -r osx-arm64 --self-contained  # macOS Apple Silicon
dotnet publish -c Release -r linux-x64 --self-contained  # Linux

# Use the published executable in claude mcp add
claude mcp add codesearch ~/Source/coa-codesearch-mcp/COA.CodeSearch.McpServer/bin/Release/net9.0/osx-x64/publish/COA.CodeSearch.McpServer
```


## 🛠️ Available Tools

All tools now feature AI-optimized responses with intelligent summaries, progress tracking, and token-aware responses that automatically adapt to prevent overwhelming AI assistants.

### 🚀 AI-Optimized Features
- **Intelligent Summaries**: Automatic insights, hotspots, and actionable next steps
- **Token-Aware**: Auto-switches to summary mode when responses exceed 5,000 tokens
- **Progress Tracking**: Real-time notifications for long-running operations
- **Pattern Recognition**: Identifies trends, anomalies, and optimization opportunities
- **Progressive Disclosure**: Request specific details without re-executing operations

### Search & Navigation
- `go_to_definition` - Navigate to symbol definitions (auto-detects C# or TypeScript)
- `find_references` - 🔍 Find ALL C# usages instantly with AI-optimized summaries
- `search_symbols` - 🔍 AI-optimized symbol search with distribution insights
- `advanced_symbol_search` - Find C# symbols with semantic filters
- `search_typescript` - 🔍 Find TypeScript symbols FAST
- `get_hover_info` - Get type information and documentation

### Fast Search Tools (Lucene-powered)
- `index_workspace` - 🏗️ Build search index with progress notifications (required first!)
- `fast_text_search` - 🔍 AI-optimized text search with file distribution and hotspot analysis
- `fast_file_search` - 🔍 AI-optimized file search with directory insights
- `fast_recent_files` - 🔍 Find recently modified files with time context
- `fast_similar_files` - Find files with similar content using ML algorithms
- `fast_directory_search` - Search for directories with fuzzy matching
- `fast_file_size_analysis` - Analyze files by size with distribution insights

### Code Analysis
- `get_implementations` - 🔍 AI-optimized implementation discovery with inheritance pattern analysis
- `get_call_hierarchy` - 🔍 AI-optimized call hierarchy with circular dependency detection
- `get_document_symbols` - Get file structure outline
- `get_diagnostics` - 🔍 AI-optimized error analysis with priority recommendations
- `dependency_analysis` - 🔍 AI-optimized dependency insights with refactoring suggestions
- `project_structure_analysis` - 🔍 AI-optimized project analysis with architectural insights
- `rename_symbol` - 🔍 Safe renaming with AI-powered impact analysis

### Memory System
- `recall_context` - 🧠 Load relevant context (use at session start!)
- `flexible_store_memory` - Store any type of memory with custom fields
- `flexible_search_memories` - Search stored memories with AI analysis, insights, and patterns
- `flexible_update_memory` - Update existing memories
- `flexible_get_memory` - Retrieve specific memory by ID
- `flexible_link_memories` - Create relationships between memories
- `flexible_unlink_memories` - Remove memory relationships
- `flexible_get_related_memories` - Traverse memory relationships
- `flexible_store_working_memory` - Temporary session memories with auto-expiration
- `flexible_find_similar_memories` - Find memories with similar content
- `flexible_archive_memories` - Archive old memories by type and age
- `flexible_summarize_memories` - Compress old memories into summaries
- `flexible_store_git_commit` - Link memories to Git commits
- `flexible_memories_for_file` - Get all memories for a specific file
- `flexible_store_technical_debt` - Track technical debt items
- `flexible_store_question` - Store questions for follow-up
- `flexible_store_deferred_task` - Track deferred tasks
- `flexible_mark_memory_resolved` - Mark memories as resolved
- `flexible_get_memory_suggestions` - Get context-aware suggestions
- `flexible_list_templates` - List memory templates
- `flexible_create_from_template` - Create memories from templates
- `memory_dashboard` - View memory system statistics
- `memory_timeline` - 📅 View memories in chronological timeline
- `create_checklist` - 📝 Create persistent task lists
- `add_checklist_item` - Add items to checklists
- `toggle_checklist_item` - Toggle checklist item completion
- `update_checklist_item` - Update checklist item details
- `view_checklist` - View checklist with progress
- `list_checklists` - List all available checklists
- `backup_memories` - Export memories to JSON for version control
- `restore_memories` - Restore memories from JSON backup

### TypeScript-specific
- `search_typescript` - Find TypeScript symbols by name
- `typescript_go_to_definition` - Navigate to TypeScript symbol definitions
- `typescript_find_references` - Find all TypeScript symbol usages
- `typescript_rename_symbol` - Rename TypeScript symbols across the codebase
- `typescript_hover_info` - 💡 Get TypeScript type information and docs like IDE hover tooltips

### Utilities
- `batch_operations` - 🚀 AI-optimized batch execution with pattern analysis
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
- `backups/` - JSON memory backups (created by `backup_memories` tool)
- `logs/` - Debug logs (when enabled)

Add `.codesearch/` to your `.gitignore` to exclude these files from version control, except for `.codesearch/backups/*.json` files which can be committed for team sharing.

### Memory Backup System

The memory system uses two storage mechanisms with a clear separation between shared team knowledge and private developer memories:

**Lucene Indexes** (Primary Storage):
- Located in `.codesearch/project-memory/` and `.codesearch/local-memory/`
- High-performance full-text search indexes
- Not suitable for version control (binary files)
- Automatically maintained by the memory system

**JSON Backup** (`.codesearch/backups/memories_YYYYMMDD_HHMMSS.json`):
- Timestamped, human-readable JSON files created by `backup_memories`
- Perfect for version control and team sharing
- Easy to inspect, debug, and merge
- Can be restored on any machine with `restore_memories`

#### Two Memory Workspaces

1. **Project Memory** (`project-memory/`)
   - **Shared with team** via version control
   - Contains memories where `IsShared = true`
   - Default types: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight
   
2. **Local Memory** (`local-memory/`)
   - **Private to developer**
   - Contains memories where `IsShared = false`
   - Includes: WorkSession, LocalInsight, WorkingMemory, personal notes

#### What Gets Backed Up by Default

When you run `backup_memories` without parameters:
- ✅ **Backs up**: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight
- ❌ **Excludes**: WorkSession, LocalInsight, WorkingMemory, any custom types with `IsShared = false`

#### How Memory Storage is Determined

```csharp
// FlexibleMemoryService determines storage location:
var workspacePath = memory.IsShared ? _projectMemoryWorkspace : _localMemoryWorkspace;
```

Examples:
- `flexible_store_memory --type "ArchitecturalDecision" --isShared true` → project-memory/
- `flexible_store_memory --type "PersonalNote" --isShared false` → local-memory/
- `flexible_store_working_memory` → Always local-memory/ (IsShared = false)

#### Backup Command Examples

```bash
# Default: Backs up only shared project memories
backup_memories

# Include both project AND local memories
backup_memories --includeLocal true

# Backup specific memory types
backup_memories --scopes ["TechnicalDebt", "Question"]

# Full backup including all local developer memories
backup_memories --scopes ["ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight", "WorkSession", "LocalInsight"] --includeLocal true

# Restore from backup (auto-finds most recent)
restore_memories

# Restore from specific backup file
restore_memories --backupFile "memories_20250724_225355.json"

# Restore including local memories
restore_memories --includeLocal true
```

#### Version Control Strategy

**Recommended workflow:**
1. Run `backup_memories` (project memories only)
2. Commit `.codesearch/backups/memories_*.json` to version control
3. Team members pull and run `restore_memories`
4. Everyone has the same shared architectural knowledge

**Privacy preserved:**
- Local memories stay on developer's machine
- Working memories expire automatically
- Personal insights never leave your workspace unless explicitly backed up with `--includeLocal true`

## 🚀 Quick Start Guide

1. **First Time Setup**
   ```bash
   # Build the server
   dotnet build -c Release
   
   # Add to Claude Code (see Quick Start section above)
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

3. **AI-Optimized Tools Example**
   ```
   # All tools now use AI-optimized responses
   find_references --filePath "IUserService.cs" --line 10 --column 15
   
   # Response includes:
   # - Summary: "47 references across 12 files"
   # - Key Insight: "Heavy usage in Controllers (65%)"
   # - Hotspots: UserController.cs (15), AuthController.cs (8)
   # - Next Action: "Consider interface segregation"
   ```

4. **Memory System Usage**
   
   The memory system helps you capture and recall important decisions, patterns, and insights. Here are common user phrases and what they accomplish:
   
   **"Remember this decision"**
   ```
   flexible_store_memory --type "ArchitecturalDecision" 
     --content "Using JWT for authentication instead of sessions for better scalability"
     --files ["AuthService.cs", "JwtMiddleware.cs"]
   ```
   
   **"Note this technical debt"**
   ```
   flexible_store_memory --type "TechnicalDebt" 
     --content "UserService has grown too large - needs refactoring into smaller services"
     --fields '{"priority": "high", "status": "identified"}'
     --files ["Services/UserService.cs"]
   ```
   
   **"Track this pattern I discovered"**
   ```
   flexible_store_memory --type "CodePattern" 
     --content "All API controllers follow the same validation pattern using FluentValidation"
     --files ["Controllers/UserController.cs", "Controllers/OrderController.cs"]
   ```
   
   **"Remember to check this later"**
   ```
   flexible_store_working_memory 
     --content "Check if the new caching implementation affects memory usage"
     --expiresIn "24h"
   ```
   
   **"What was I working on?"**
   ```
   recall_context "authentication system implementation"
   ```
   
   **"Find my notes about caching"**
   ```
   flexible_search_memories --query "caching performance redis"
   ```
   
   **"Create a task list"**
   ```
   create_checklist --title "API Refactoring Tasks"
   add_checklist_item --checklistId "abc123" 
     --itemText "Extract validation logic to separate classes"
     --relatedFiles ["Controllers/BaseController.cs"]
   ```
   
   **"Link this bug to its fix"**
   ```
   flexible_link_memories --sourceId "bug-001" --targetId "fix-002" 
     --relationshipType "resolvedBy"
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