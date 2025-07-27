# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server for blazing-fast code search and intelligent memory management. Built with .NET 9.0, featuring Lucene-powered millisecond search and AI-optimized architecture for pattern matching and content analysis.

## 🚀 Features

- **Lightning-Fast Search**: Lucene indexing enables instant search across millions of lines
- **Smart Memory System**: Persistent architectural knowledge and decision tracking
- **AI-Optimized Architecture**: Pattern matching and content analysis for AI assistants
- **Progressive Disclosure**: Intelligent summarization with drill-down capabilities
- **Real-time Updates**: File watchers automatically update indexes on changes

### Performance
- Startup: < 500ms (simplified architecture)
- Text search: < 10ms indexed
- File search: < 50ms
- Memory usage: < 200MB typical

## 📋 Prerequisites

- .NET 9.0 SDK or later

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

### Text Search & Analysis
- `index_workspace` - Build search index (required for fast search tools)
- `text_search` - Search text across codebase with advanced filters
- `file_search` - Find files by name with fuzzy matching
- `directory_search` - Find directories with pattern matching
- `recent_files` - Find recently modified files
- `similar_files` - Find files with similar content
- `file_size_analysis` - Analyze file sizes and distributions

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
- `add_checklist_items` - Add one or more tasks
- `toggle_checklist_item` - Mark complete/incomplete
- `update_checklist_item` - Update task details
- `view_checklist` - View with progress
- `list_checklists` - List all checklists

### Utilities
- `batch_operations` - Execute multiple search operations in parallel
- `index_health_check` - Check index status and performance
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
# Search for code patterns
text_search --query "authentication" --workspacePath "C:/YourProject"

# Find similar implementations
similar_files --sourceFilePath "AuthService.cs" --workspacePath "C:/YourProject"

# Analyze recent changes
recent_files --timeFrame "24h" --workspacePath "C:/YourProject"
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

Built with [Lucene.NET](https://lucenenet.apache.org/) and implements [Model Context Protocol](https://modelcontextprotocol.io/). Optimized for AI assistant workflows with pattern matching and intelligent memory management.