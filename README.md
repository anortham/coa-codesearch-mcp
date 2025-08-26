# CodeSearch MCP Server

A lightning-fast code search tool for Claude Code that helps you find files, search code, and understand your projects instantly. Just ask Claude to "find all my React components" or "show me recent changes" and get results in milliseconds.

Built with .NET 9.0 and COA MCP Framework 1.7.19, featuring Lucene-powered search with AI-optimized responses.

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
dotnet tool install -g COA.CodeSearch --version 2.1.0

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
2. Claude will now have powerful search capabilities - just ask naturally!

## 🌟 What Makes This Special

Unlike basic file search, CodeSearch understands your code:

- **Smart Pattern Recognition**: Finds `async Task`, `[Fact]`, `interface IService` patterns
- **Context-Aware**: Knows the difference between C# classes and JavaScript functions  
- **Instant Results**: Millisecond search across millions of lines of code
- **Fuzzy Matching**: Finds files even with typos in names
- **Content Similarity**: "Find files like this one" using advanced analysis
- **Recent Activity**: Tracks what you've been working on lately
- **Cross-Project**: Search across multiple workspaces from one place

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

## 💬 How to Use with Claude Code

Once installed, just chat naturally with Claude Code! Here are examples of what you can say:

### Finding Files and Code

**"Find all my TypeScript files"**
```
Claude will search for *.ts files in your project
```

**"Show me all the async functions in my codebase"**
```  
Claude will search for patterns like "async function" and "async Task"
```

**"Find files containing 'UserService'"**
```
Claude will search file contents for the term "UserService"
```

**"What files have been changed in the last 2 days?"**
```
Claude will show recently modified files with timestamps
```

### Project Understanding

**"Find all my React components"**
```
Claude will look for .jsx, .tsx files and React patterns
```

**"Show me all the test files"**
```
Claude will find files with "test", "spec" in names or paths
```

**"Find files similar to UserController.cs"**
```
Claude will use content analysis to find structurally similar files
```

**"Search for all database queries in my project"**
```
Claude will look for SQL patterns, ORM calls, etc.
```

### Development Workflow

**"Index my project for searching"**
```
Claude will scan and index your files for fast searching
```

**"Find all TODO comments"**
```
Claude will search for TODO, FIXME, HACK comments
```

**"Show me configuration files"**
```
Claude will find .json, .yaml, .config files
```

### Advanced Examples

**"Find all files in the Services directory that haven't been touched in 30 days"**

**"Search for error handling patterns in my C# code"**

**"Find all API endpoints in my project"**

**"Show me files that import React but don't use hooks"**

## 🌐 HTTP API (Auto-Started)

CodeSearch automatically starts an HTTP API on port **5020** when running in STDIO mode (default for Claude Code). This provides REST API access alongside the MCP functionality.

### Available Endpoints

#### Health & Info
- `GET /health` - Service health check
- `GET /api/health` - Detailed health status  
- `GET /api` - API information and endpoints

#### Workspace Management
- `GET /api/workspace` - List all indexed workspaces
- `GET /api/workspace/status?workspacePath={path}` - Get workspace status
- `POST /api/workspace/index?workspacePath={path}&force={bool}` - Index workspace
- `POST /api/workspace/refresh?workspacePath={path}` - Refresh workspace index
- `DELETE /api/workspace/index?workspacePath={path}` - Remove workspace index

#### Search Operations  
- `GET /api/search/symbol?name={name}&type={type}&workspace={path}&limit={n}` - Search for symbols
- `GET /api/search/text?query={text}&exact={bool}&workspace={path}&limit={n}` - Search text content
- `GET /api/check/exists?name={name}&workspace={path}` - Check if symbol exists
- `POST /api/search/batch` - Perform multiple searches in one request

#### Documentation
- `GET /swagger` - Interactive API documentation (development mode)

### Example Usage

```bash
# Check service health
curl http://localhost:5020/health

# List workspaces (returns actual workspace paths)
curl http://localhost:5020/api/workspace
# Example response:
# {
#   "workspaces": [
#     {
#       "path": "C:\\source\\COA CodeSearch MCP",
#       "isIndexed": true,
#       "fileCount": 127,
#       "lastIndexed": "2025-01-26T14:30:15Z"
#     }
#   ]
# }

# Search for a class (use actual workspace path from list above)
curl "http://localhost:5020/api/search/symbol?name=UserService&type=class&workspace=C%3A%5Csource%5CCOA%20CodeSearch%20MCP"

# Search for text (workspace path is properly URL-encoded)
curl "http://localhost:5020/api/search/text?query=async%20Task&workspace=C%3A%5Csource%5CCOA%20CodeSearch%20MCP&limit=10"

# Get workspace status (query parameter requires proper encoding)
curl "http://localhost:5020/api/workspace/status?workspacePath=C%3A%5Csource%5CCOA%20CodeSearch%20MCP"

# Index a new workspace
curl -X POST "http://localhost:5020/api/workspace/index?workspacePath=C%3A%5Csource%5CMyProject&force=false"
```

The HTTP API is automatically managed by the MCP framework's auto-service feature and will restart if it crashes.

## 🔒 Security & Thread Safety

### Path Validation
The PathResolutionService implements comprehensive path validation:
- **Directory traversal protection**: Blocks paths containing ".." sequences
- **Path length validation**: Prevents excessively long paths (240+ characters)
- **Input sanitization**: Validates and normalizes all workspace paths
- **Cross-platform compatibility**: Handles path separators and special folders

### Thread Safety
- **Concurrent metadata access**: Semaphore locks protect workspace metadata files
- **Atomic file operations**: Metadata updates use temporary files with atomic replacement
- **Lock management**: Per-file locking prevents corruption during concurrent access
- **Safe file system operations**: All I/O operations include error handling and fallbacks

### API Security
- **Path resolution**: Internal hash directories never exposed via HTTP API
- **Real path validation**: Only existing, accessible workspace paths are returned
- **URL encoding support**: Handles special characters in workspace paths
- **Fallback handling**: Gracefully handles unresolvable or corrupted metadata

## ⚙️ Configuration

Configuration via `appsettings.json`:

```json
{
  "CodeSearch": {
    "BasePath": "~/.coa/codesearch",
    "LogsPath": "~/.coa/codesearch/logs",
    "HttpPort": 5020,
    "HttpApi": {
      "Enabled": true
    },
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
- **Indexes**: `~/.coa/codesearch/indexes/[workspace-name_hash]/`
  - Uses descriptive names with hash suffixes for uniqueness
  - Metadata files enable resolution of original workspace paths
  - HTTP API returns actual workspace paths, not internal hash names
- **Logs**: `~/.coa/codesearch/logs/`
- **Configuration**: Workspace-specific settings

### Framework Integration
Built on **COA MCP Framework 1.7.19**:
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

**"Claude says it can't find files I know exist"**
```
Tell Claude: "Please index my project for searching first"
```

**"Search results seem outdated"**
```
Ask Claude: "Rebuild the search index for this project"
```

**"Claude is responding slowly to search requests"**
- Try asking for fewer results: "Find 5 recent TypeScript files"
- Ask Claude to check system memory: "Check CodeSearch memory usage"

**"Getting index lock errors"**
```
Close Claude Code completely and restart it
```

**Installation issues:**
- Make sure you have .NET 9.0 installed: `dotnet --version`
- Try reinstalling: `dotnet tool uninstall -g COA.CodeSearch && dotnet tool install -g COA.CodeSearch`

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
- **Framework**: [COA MCP Framework 1.7.19](https://www.nuget.org/packages/COA.Mcp.Framework)
- **NuGet Package**: [COA.CodeSearch](https://www.nuget.org/packages/COA.CodeSearch)

---

**Built with** [COA MCP Framework 1.7.19](https://www.nuget.org/packages/COA.Mcp.Framework) • **Powered by** [Lucene.NET](https://lucenenet.apache.org/)