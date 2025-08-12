# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server for blazing-fast code search and file discovery. Built with .NET 9.0 and COA MCP Framework 1.7.0, featuring Lucene-powered millisecond search with AI-optimized architecture.

## 🚀 Features

- **⚡ Lightning-Fast Search**: Lucene indexing enables instant search across millions of lines
- **🔍 Smart Code Analysis**: Custom analyzer preserves code patterns like `: ITool`, `[Fact]`, generic types
- **📁 File Discovery**: Pattern-based file and directory search with fuzzy matching  
- **⚡ Batch Operations**: Execute multiple searches efficiently in a single request
- **⏱️ Recent Files**: Track and find recently modified files
- **🔗 Similar Files**: Content-based similarity detection
- **🎯 Real-time Updates**: File watchers automatically update indexes on changes
- **📊 AI-Optimized**: Token-efficient responses with confidence-based result limiting
- **🌐 Centralized Storage**: All indexes in `~/.coa/codesearch` for cross-session sharing

### Performance
- Startup: < 500ms 
- Text search: < 10ms indexed
- File search: < 50ms  
- Memory usage: < 200MB typical
- Index size: ~1MB per 1000 files

### 🎯 Token Optimization
- **60-85% token reduction** through confidence-based limiting
- **Progressive disclosure**: Essential results first, full data via resource URIs
- **Smart context handling**: Fewer results when context lines included
- **Standardized responses**: Consistent format across all tools

## 📋 Prerequisites

- .NET 9.0 SDK or later

## 🚀 Quick Start

### Installation as Global Tool

```bash
# Install from NuGet (recommended)
dotnet tool install -g COA.CodeSearch --version 2.0.0

# Verify installation
codesearch --version

# Or build from source
git clone https://github.com/anortham/coa-codesearch-mcp.git
cd coa-codesearch-mcp
dotnet build -c Release
dotnet pack -c Release
dotnet tool install -g --add-source ./nupkg COA.CodeSearch
```

### Uninstall

```bash
# Remove global tool
dotnet tool uninstall -g COA.CodeSearch
```

### Claude Code Integration

Add to your Claude Code MCP configuration file:

**Configuration File Locations:**
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Linux**: `~/.config/Claude/claude_desktop_config.json`

**Configuration:**
```json
{
  "mcpServers": {
    "codesearch": {
      "command": "codesearch",
      "args": ["stdio"]
    }
  }
}
```

**After adding the configuration:**
1. Restart Claude Code completely
2. The tool will be available as `mcp__codesearch__*` in your Claude Code session

## 🛠️ Available Tools

### Core Search Tools

| Tool | Purpose | Parameters |
|------|---------|------------|
| `index_workspace` | Index files for search | `workspacePath`, `forceRebuild` |
| `text_search` | Search file contents | `query`, `workspacePath`, `maxTokens` |
| `file_search` | Find files by name pattern | `pattern`, `workspacePath`, `useRegex` |
| `directory_search` | Find directories by pattern | `pattern`, `workspacePath`, `includeHidden` |
| `batch_operations` | Execute multiple searches in batch | `workspacePath`, `operations`, `maxTokens` |
| `recent_files` | Get recently modified files | `workspacePath`, `timeFrame` |
| `similar_files` | Find content-similar files | `filePath`, `workspacePath`, `minScore` |

### System Tools

| Tool | Purpose | Parameters |
|------|---------|------------|
| `hello_world` | Test connectivity | `name`, `includeTime` |
| `get_system_info` | System diagnostics | `includeEnvironment` |

## 📖 Usage Examples

### Basic Search Operations

```bash
# Index your workspace
mcp__codesearch__index_workspace \
  --workspacePath "C:\source\MyProject"

# Search for code patterns
mcp__codesearch__text_search \
  --query "async Task" \
  --workspacePath "C:\source\MyProject"

# Find files by pattern
mcp__codesearch__file_search \
  --pattern "*.cs" \
  --workspacePath "C:\source\MyProject"

# Get recent changes
mcp__codesearch__recent_files \
  --workspacePath "C:\source\MyProject" \
  --timeFrame "24h"
```

### Advanced Operations

```bash
# Regex file search
mcp__codesearch__file_search \
  --pattern ".*Service\.cs$" \
  --useRegex true \
  --workspacePath "C:\source\MyProject"

# Find similar files
mcp__codesearch__similar_files \
  --filePath "C:\source\MyProject\Services\UserService.cs" \
  --workspacePath "C:\source\MyProject" \
  --minScore 0.3

# Directory search with hidden folders
mcp__codesearch__directory_search \
  --pattern "*test*" \
  --includeHidden true \
  --workspacePath "C:\source\MyProject"

# Batch operations - multiple searches at once
mcp__codesearch__batch_operations \
  --workspacePath "C:\source\MyProject" \
  --operations '[
    {"operation": "text_search", "query": "async Task", "id": "async_methods"},
    {"operation": "file_search", "pattern": "*.cs", "id": "cs_files"}
  ]'
```

## ⚙️ Configuration

Configuration via `appsettings.json`:

```json
{
  "CodeSearch": {
    "BasePath": "~/.coa/codesearch",
    "LogsPath": "~/.coa/codesearch/logs", 
    "Lucene": {
      "IndexRootPath": "~/.coa/codesearch/indexes",
      "MaxIndexingConcurrency": 8,
      "RAMBufferSizeMB": 256,
      "SupportedExtensions": [".cs", ".js", ".ts", ".py", ".java", ...]
    },
    "FileWatcher": {
      "Enabled": true,
      "DebounceMilliseconds": 500
    },
    "QueryCache": {
      "Enabled": true,
      "MaxCacheSize": 1000,
      "CacheDuration": "00:15:00"
    }
  }
}
```

## 🏗️ Architecture

### Centralized Storage
- **Indexes**: `~/.coa/codesearch/indexes/[workspace-hash]/`
- **Logs**: `~/.coa/codesearch/logs/`
- **Configuration**: Workspace-specific settings

### Framework Integration
Built on **COA MCP Framework 1.7.0**:
- Automatic tool discovery
- Token optimization
- Progressive response disclosure
- Circuit breaker patterns
- Memory pressure management

### Search Engine
- **Lucene.NET 4.8.0** backend
- **Custom CodeAnalyzer** for programming language patterns
- **Multi-factor scoring** with path relevance, recency, and type matching
- **Configurable analyzers** per file type

## 🧪 Development

### Building

```bash
# Debug build
dotnet build -c Debug

# Release build  
dotnet build -c Release

# Run tests
dotnet test
```

### Testing

```bash
# Test indexing
mcp__codesearch__index_workspace --workspacePath "."

# Test search
mcp__codesearch__text_search --query "LuceneIndexService"

# Check system health
mcp__codesearch__get_system_info
```

## 🔧 Troubleshooting

### Common Issues

**Index locks:**
```bash
# Remove stale locks
rm ~/.coa/codesearch/indexes/*/write.lock
```

**Missing results:**
```bash
# Force rebuild index
mcp__codesearch__index_workspace --forceRebuild true
```

**Memory issues:**
- Reduce `RAMBufferSizeMB` in config
- Lower `MaxIndexingConcurrency`
- Check `MemoryPressure` settings

### Logging

Set log levels in `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "COA.CodeSearch": "Information"  // Debug, Trace for more detail
    }
  }
}
```

## 📚 Integration

### With ProjectKnowledge MCP

CodeSearch complements ProjectKnowledge MCP when both are configured in Claude Code. They work as separate MCP servers:

- **CodeSearch**: Provides fast search and code analysis capabilities
- **ProjectKnowledge**: Handles knowledge storage, checkpoints, and technical debt tracking

**Example workflow:**
```bash
# 1. Search for code patterns using CodeSearch
mcp__codesearch__text_search --query "Thread.Sleep"

# 2. Store findings using ProjectKnowledge (separate tool)
mcp__projectknowledge__store_knowledge \
  --content "Found Thread.Sleep anti-pattern in 5 files" \
  --type "TechnicalDebt"
```

See [Integration Guide](docs/INTEGRATION_WITH_PROJECTKNOWLEDGE.md) for complete workflows.

## 📄 License

MIT License - see [LICENSE](LICENSE) file.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/anortham/coa-codesearch-mcp/issues)
- **Documentation**: [docs/](docs/) folder
- **Framework**: [COA MCP Framework 1.7.0](https://www.nuget.org/packages/COA.Mcp.Framework)
- **NuGet Package**: [COA.CodeSearch](https://www.nuget.org/packages/COA.CodeSearch)

---

**Built with** [COA MCP Framework 1.7.0](https://www.nuget.org/packages/COA.Mcp.Framework) • **Powered by** [Lucene.NET](https://lucenenet.apache.org/)