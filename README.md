# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server for blazing-fast code search and navigation across multiple languages. Built with .NET 9.0, featuring Roslyn-based C# analysis, TypeScript support via tsserver, and Lucene-powered millisecond search.

## 🚀 Features

- **Multi-language Support**: C# (Roslyn) and TypeScript/JavaScript (tsserver)
- **Blazing Fast Search**: Lucene indexing enables instant search across millions of lines
- **Smart Memory System**: Persistent architectural knowledge and decision tracking
- **Progressive Disclosure**: AI-optimized responses with automatic summarization
- **Real-time Updates**: File watchers automatically update indexes on changes

### Performance
- Startup: < 100ms (with AOT)
- Text search: < 50ms indexed
- Go to definition: < 50ms cached
- Memory usage: < 500MB typical

## 📋 Prerequisites

- .NET 9.0 SDK or later
- Node.js/npm (for TypeScript support)
- Visual Studio 2022 or Build Tools (for MSBuild)

## 🚀 Quick Start

```bash
# Clone and build
git clone https://github.com/anortham/coa-codesearch-mcp.git
cd coa-codesearch-mcp
dotnet build -c Release

# Add to Claude Code
# Windows
claude mcp add codesearch "C:\path\to\coa-codesearch-mcp\COA.CodeSearch.McpServer\bin\Release\net9.0\COA.CodeSearch.McpServer.exe"

# macOS/Linux  
claude mcp add codesearch ~/coa-codesearch-mcp/COA.CodeSearch.McpServer/bin/Release/net9.0/COA.CodeSearch.McpServer
```

## 🌍 Cross-Platform Support

Fully supports Windows, Linux, and macOS (x64/ARM64). The server handles platform differences automatically.

## 🛠️ Available Tools

### Search & Navigation
- `index_workspace` - Build search index (required for fast search tools)
- `text_search` - Search text across codebase
- `file_search` - Find files by name with fuzzy matching
- `directory_search` - Find directories
- `recent_files` - Find recently modified files
- `similar_files` - Find files with similar content
- `file_size_analysis` - Analyze file sizes

### C# Analysis
- `search_symbols` - Find C# symbols by name
- `advanced_symbol_search` - Search with semantic filters
- `go_to_definition` - Navigate to definitions
- `find_references` - Find all usages
- `get_implementations` - Find interface implementations
- `get_call_hierarchy` - Analyze call chains
- `get_hover_info` - Get type information
- `get_document_symbols` - Get file structure
- `get_diagnostics` - Find compilation errors
- `dependency_analysis` - Analyze dependencies
- `project_structure_analysis` - Analyze solution structure
- `rename_symbol` - Safely rename symbols

### TypeScript Support
- `search_typescript` - Find TypeScript symbols
- `typescript_go_to_definition` - Navigate to definitions
- `typescript_find_references` - Find all usages
- `typescript_rename_symbol` - Rename across codebase
- `typescript_hover_info` - Get type information

### Memory System
- `recall_context` - Load relevant context (use at session start!)
- `store_memory` - Store any type of memory with custom fields
- `store_temporary_memory` - Store session-only memories
- `search_memories` - Search with AI-powered understanding
- `update_memory` - Update existing memories
- `get_memory` - Retrieve by ID
- `find_similar_memories` - Find related memories
- `archive_memories` - Archive old memories
- `summarize_memories` - Compress old memories
- `memory_dashboard` - View statistics
- `memory_timeline` - Chronological view
- `backup_memories` - Export to JSON
- `restore_memories` - Import from JSON

### Memory Tools (Specialized)
- `store_git_commit_memory` - Link memories to commits
- `get_memories_for_file` - Get file-related memories
- `link_memories` - Create memory relationships
- `get_related_memories` - Traverse relationships
- `unlink_memories` - Remove relationships
- `list_memory_templates` - View templates
- `create_memory_from_template` - Use templates
- `get_memory_suggestions` - Context-aware suggestions

### Task Management
- `create_checklist` - Create persistent task lists
- `add_checklist_item` - Add tasks
- `toggle_checklist_item` - Mark complete/incomplete
- `update_checklist_item` - Update task details
- `view_checklist` - View with progress
- `list_checklists` - List all checklists

### Utilities
- `batch_operations` - Execute multiple operations in parallel
- `log_diagnostics` - Manage debug logs
- `get_version` - Get server version info

## ⚙️ Configuration

### Essential Settings (appsettings.json)
```json
{
  "ResponseLimits": {
    "MaxTokens": 25000,
    "AutoModeSwitchThreshold": 5000
  },
  "FileWatcher": {
    "Enabled": true
  },
  "WorkspaceAutoIndex": {
    "Enabled": true
  }
}
```

### Environment Variables
- `MCP_LOG_LEVEL` - Override log level
- `MCP_WORKSPACE_PATH` - Default workspace path

## 📁 Data Storage

The server creates `.codesearch/` in your workspace:
- `index/` - Lucene search indexes
- `project-memory/` - Shared team knowledge
- `local-memory/` - Personal notes
- `backups/` - JSON memory exports
- `logs/` - Debug logs

Add `.codesearch/` to `.gitignore`, except `backups/*.json` for team sharing.

## 🚀 Common Workflows

### First Time Setup
```bash
# Index your workspace
index_workspace --workspacePath "C:/YourProject"

# Load previous context  
recall_context "what I was working on"
```

### Daily Usage
```bash
# Search for code
text_search --query "authentication" --workspacePath "C:/YourProject"

# Navigate to definition
go_to_definition --filePath "Auth.cs" --line 25 --column 15

# Find all usages
find_references --filePath "IUserService.cs" --line 10 --column 15
```

### Memory System
```bash
# Store architectural decision
store_memory --type "ArchitecturalDecision" \
  --content "Using JWT for authentication" \
  --files ["AuthService.cs"]

# Track technical debt  
store_memory --type "TechnicalDebt" \
  --content "UserService needs refactoring" \
  --fields '{"priority": "high"}'

# Search memories
search_memories --query "authentication decisions"

# Backup for version control
backup_memories  # Creates JSON in .codesearch/backups/
```

## 🐛 Troubleshooting

**TypeScript tools failing**
- Install Node.js from https://nodejs.org/

**MSBuild not found**
- Install Visual Studio 2022 or Build Tools

**Stuck indexes**
- Delete `.codesearch/index/*/write.lock` files

**Debug logging**
```bash
log_diagnostics --action status
log_diagnostics --action cleanup --cleanup true
```

## 📄 License

MIT License - see [LICENSE](LICENSE) file

## 🙏 Acknowledgments

Built with [Roslyn](https://github.com/dotnet/roslyn), [Lucene.NET](https://lucenenet.apache.org/), [TypeScript](https://github.com/microsoft/TypeScript), and implements [Model Context Protocol](https://modelcontextprotocol.io/).