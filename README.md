# CodeSearch MCP Server

A lightning-fast code search and navigation tool for Claude Code that helps you find files, search code, navigate symbols, and understand your projects instantly. Just ask Claude to "find all my React components", "show me recent changes", or "find the definition of UserService" and get results in milliseconds.

Built with .NET 9.0 and COA MCP Framework 2.0.1, featuring Lucene-powered search with AI-optimized responses.

## 🚀 Features

- **⚡ Lightning-Fast Search**: Lucene indexing enables instant search across millions of lines
- **🔍 Smart Code Analysis**: Custom analyzer preserves code patterns like `: ITool`, `[Fact]`, and enhanced CamelCase tokenization with full generic type support (finds `McpToolBase<TParams, TResult>` when searching for "McpToolBase")
- **📁 File Discovery**: Pattern-based file and directory search with fuzzy matching
- **🧬 Advanced Type Extraction**: Extract types, interfaces, classes, and methods from 25+ languages including C#, TypeScript, Python, Java, Rust, Go, C++, Ruby, PHP, Swift, Scala, Vue, Razor, and more
- **🧭 Code Navigation**: Symbol search, find references, and goto definition without compilation
- **📝 Line-Level Search**: Get ALL occurrences with exact line numbers - faster than grep with structured JSON output
- **🔄 Search and Replace**: Bulk find/replace across entire codebase with preview mode for safety
- **✏️ Surgical Line Editing**: Insert, replace, or delete specific line ranges without reading entire files
- **⚡ Batch Operations**: Execute multiple searches efficiently in a single request
- **⏱️ Recent Files**: Track and find recently modified files
- **🔗 Similar Files**: Content-based similarity detection
- **🎯 Real-time Updates**: File watchers automatically update indexes on changes
- **📊 AI-Optimized**: Token-efficient responses with confidence-based result limiting
- **🏠 Hybrid Local Indexing**: Indexes stored in workspace `.coa/codesearch/indexes/` with multi-workspace support

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

### 🧬 Supported Languages for Type Extraction

The type extraction system supports **25+ programming languages** using Tree-sitter parsers:

**Systems Languages**: C, C++, Rust, Go  
**Object-Oriented**: C#, Java, Swift, Scala  
**Scripting**: Python, Ruby, PHP, JavaScript, TypeScript  
**Web**: Vue.js (SFC), Razor/Blazor, HTML, CSS  
**Functional**: Haskell, OCaml, Julia  
**Hardware**: Verilog  
**Shell**: Bash  
**Data**: JSON, TOML  

**Special Support**:
- **Vue Single File Components**: Extracts types from `<script>` blocks (TS/JS)
- **Razor/Blazor**: Extracts types from `@code` and `@functions` blocks
- **Mixed Languages**: Handles embedded code in templating systems

## 📋 Prerequisites

- .NET 9.0 SDK or later

### macOS Tree-sitter Setup

**⚠️ Important for macOS users:** This project uses Tree-sitter for advanced type extraction from 25+ languages. The NuGet package doesn't ship native Darwin libraries, so you need to install them manually:

#### Quick Setup (Recommended)
```bash
# 1. Install Tree-sitter runtime via Homebrew
brew install tree-sitter

# 2. Build grammar libraries using our automation script
sh scripts/build-grammars-macos.sh
```

This builds the most common grammars: C#, TypeScript, TSX, JavaScript, Python, Go, Rust, Java, JSON, HTML, CSS, and Bash.

#### Custom Installation Directory
If you want to install in a local directory instead of Homebrew's default location:

```bash
# Build to custom directory
DEST_DIR="$PWD/native" sh scripts/build-grammars-macos.sh

# Then set the environment variable for runtime discovery
export TREE_SITTER_NATIVE_PATHS="$PWD/native:/opt/homebrew/lib:/usr/local/lib"
```

#### Build Specific Grammars Only
```bash
# Build only C# and TypeScript grammars
GRAMMARS="c-sharp typescript" sh scripts/build-grammars-macos.sh
```

#### Manual Verification
After setup, verify the installation:
```bash
# Check installed libraries
ls /opt/homebrew/lib/libtree-sitter*.dylib

# For Intel Macs, check:
ls /usr/local/lib/libtree-sitter*.dylib
```

The build script automatically detects your Mac architecture (Apple Silicon vs Intel) and adjusts paths accordingly.

### Linux Tree-sitter Setup

**⚠️ Important for Linux users:** Similar to macOS, Linux users need to install Tree-sitter libraries manually for advanced type extraction from 25+ languages:

#### Quick Setup (Recommended)
```bash
# 1. Install Tree-sitter runtime via your package manager
# Ubuntu/Debian:
sudo apt install libtree-sitter-dev

# Fedora/RHEL/CentOS:
sudo dnf install tree-sitter-devel
# or: sudo yum install tree-sitter-devel

# Arch Linux:
sudo pacman -S tree-sitter

# 2. Build grammar libraries using our automation script
bash scripts/build-grammars-linux.sh
```

This builds the most common grammars: C#, TypeScript, TSX, JavaScript, Python, Go, Rust, Java, JSON, HTML, CSS, and Bash.

#### Custom Installation Directory
If you want to install in a local directory instead of system paths:

```bash
# Build to custom directory
DEST_DIR="$PWD/native" bash scripts/build-grammars-linux.sh

# Then set the environment variable for runtime discovery
export TREE_SITTER_NATIVE_PATHS="$PWD/native:/usr/local/lib:/usr/lib:/usr/lib/x86_64-linux-gnu"
```

#### Build Specific Grammars Only
```bash
# Build only C# and TypeScript grammars
GRAMMARS="c-sharp typescript" bash scripts/build-grammars-linux.sh
```

#### Manual Verification
After setup, verify the installation:
```bash
# Check installed libraries
ls /usr/local/lib/libtree-sitter*.so
# or: ls /usr/lib/libtree-sitter*.so
# or: ls /usr/lib/x86_64-linux-gnu/libtree-sitter*.so
```

The build script automatically detects common Linux library paths and supports various distributions.

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

**Optional: Add to .gitignore**
```
# CodeSearch local indexes (can be regenerated)
.coa/
```

## 🌟 What Makes This Special

Unlike basic file search, CodeSearch understands your code:

- **Smart Pattern Recognition**: Finds `async Task`, `[Fact]`, `interface IService` patterns, plus enhanced CamelCase splitting for generic types
- **Context-Aware**: Knows the difference between C# classes and JavaScript functions using Tree-sitter parsing
- **Instant Results**: Millisecond search across millions of lines of code
- **Fuzzy Matching**: Finds files even with typos in names
- **Content Similarity**: "Find files like this one" using advanced analysis
- **Recent Activity**: Tracks what you've been working on lately
- **Multi-Workspace Support**: Index and search multiple projects simultaneously with perfect isolation
- **Local Storage**: Fast access with indexes stored directly in your workspace
- **Code Navigation**: Symbol search, find references, and goto definition without compilation
- **Structured Line Search**: Better than grep - returns JSON with exact line numbers and context
- **Safe Bulk Edits**: Preview mode for search/replace prevents accidental changes
- **Type-Aware**: Extracts and indexes types from 25+ languages for accurate navigation
- **Precise Editing**: Complete line-based editing suite for surgical code modifications without full file reads

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

### Navigation Tools

| Tool | Purpose | Parameters |
|------|---------|------------|
| `symbol_search` | Find classes, interfaces, methods by name | `symbol`, `workspacePath`, `includeReferences`, `maxResults` |
| `find_references` | Find all usages of a symbol | `symbol`, `workspacePath`, `groupByFile`, `contextLines` |
| `goto_definition` | Jump to symbol definition | `symbol`, `workspacePath`, `includeFullContext`, `contextLines` |

### Advanced Search Tools

| Tool | Purpose | Parameters |
|------|---------|------------|
| `line_search` | Get ALL occurrences with line numbers | `pattern`, `workspacePath`, `contextLines`, `maxResultsPerFile` |
| `search_and_replace` | Replace patterns across files with preview | `searchPattern`, `replacePattern`, `workspacePath`, `preview` |

### Editing Tools

| Tool | Purpose | Parameters |
|------|---------|------------|
| `insert_at_line` | Insert content at specific line numbers | `filePath`, `lineNumber`, `content`, `preserveIndentation` |
| `replace_lines` | Replace line ranges with new content | `filePath`, `startLine`, `endLine`, `content`, `preserveIndentation` |
| `delete_lines` | Delete line ranges with surgical precision | `filePath`, `startLine`, `endLine`, `contextLines` |

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

### Code Navigation

**"Find the definition of UserService"**
```
Claude will jump directly to where UserService class is defined
Shows exact line and column, with optional context snippet
```

**"Show me all references to the UpdateUser method"**
```
Claude will find all places where UpdateUser is called
Groups results by file for easy scanning
```

**"Search for all classes ending with Controller"**
```
Claude will find all classes matching the pattern like UserController, OrderController
Uses Tree-sitter type extraction for accurate results
```

**"Find where IRepository interface is implemented"**
```
Claude will locate all implementations of the IRepository interface
Shows inheritance relationships and usage counts
```

### Type and Code Analysis

**"Find all classes and interfaces in my project"**
```
Claude will extract types from C#, TypeScript, Python, Java, Rust, Go, C++, Ruby, PHP, Swift, Scala, and more
```

**"Show me all functions and methods in my codebase"**
```
Claude will parse multiple languages and extract function/method definitions with signatures
```

**"Find all Vue component methods"**
```
Claude will parse Vue files and extract methods from JavaScript/TypeScript script blocks
```

**"Show me all Python classes with their methods"**
```
Claude will analyze Python files and extract class definitions and methods
```

**"Find all Rust structs and impl blocks"**
```
Claude will parse Rust code and extract struct definitions and implementations
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
```
Claude will use recent_files with a time filter to find stale code
```

**"Search for error handling patterns in my C# code"**
```
Claude will use text_search to find try-catch blocks and exception handling
```

**"Find all API endpoints in my project"**
```
Claude will search for route decorators and endpoint definitions
```

**"Show me files that import React but don't use hooks"**
```
Claude will combine multiple searches to find React imports without useState/useEffect
```

### Line-Level Search Examples

**"Show me every line that contains 'Thread.Sleep'"**
```
Claude will use line_search to find ALL occurrences with exact line numbers
Returns structured JSON instead of plain text grep output
```

**"Find all console.log statements with their line numbers"**
```
Claude will return every console.log with file path and line number
Perfect for cleanup tasks before production deployment
```

### Search and Replace Examples

**"Replace all 'var' declarations with 'let' in my JavaScript files"**
```
Claude will use search_and_replace with preview mode first
Shows what will change before applying modifications
```

**"Update all copyright headers to 2025"**
```
Claude will find and replace copyright patterns across all files
Supports regex patterns for complex replacements
```


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
    "Lucene": {
      "IndexRootPath": ".coa/codesearch/indexes",
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

### Hybrid Local Indexing Storage
- **Primary Workspace Indexes**: `.coa/codesearch/indexes/[workspace-name_hash]/` 
  - Indexes stored locally within the primary workspace directory
  - Each workspace gets its own isolated index for fast, context-aware search
  - Supports multiple workspace projects from single CodeSearch session
- **Cross-Platform Lock Management**: SimpleFSLockFactory ensures compatibility across macOS, Windows, and Linux
- **Logs**: `~/.coa/codesearch/logs/` (global logging location)
- **Configuration**: Per-workspace settings with workspace-specific isolation

### Framework Integration
Built on **COA MCP Framework 2.0.1**:
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

### Template Embedding Fix (v2.1.4+)

**Background:** Prior to v2.1.4, CodeSearch would fail to connect in workspaces other than the development workspace due to attempting to load instruction templates from a `Templates/` directory that only existed in the source repository. 

**Solution:** Templates are now embedded as resources in the assembly, ensuring they're always available regardless of workspace location. The `codesearch-instructions.scriban` template is compiled into the binary as an embedded resource.

**Technical Details:**
- Template file: `Templates/codesearch-instructions.scriban`
- Embedded resource name: `COA.CodeSearch.McpServer.Templates.codesearch-instructions.scriban`
- Loading mechanism: Changed from file-based (`WithInstructionsFromTemplate`) to resource-based (`WithTemplateInstructions` with embedded content)
- Framework fix: COA.Mcp.Framework v2.1.3+ includes defensive checks for missing template directories

This change ensures CodeSearch works consistently across all workspaces without filesystem dependencies.

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

### With Other MCP Servers

CodeSearch works seamlessly with other MCP servers configured in Claude Code:

- **Goldfish MCP**: Handles session management, checkpoints, and task tracking
- **Other MCP servers**: Can be combined for comprehensive development workflows

**Example workflow:**
```bash
# 1. Search for code patterns using CodeSearch
mcp__codesearch__text_search --query "async Task"

# 2. Navigate to specific definitions
mcp__codesearch__goto_definition --symbol "UserService"

# 3. Find all references to a method
mcp__codesearch__find_references --symbol "UpdateUser"
```

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
- **Framework**: [COA MCP Framework 2.0.1](https://www.nuget.org/packages/COA.Mcp.Framework)
- **NuGet Package**: [COA.CodeSearch](https://www.nuget.org/packages/COA.CodeSearch)

---

**Built with** [COA MCP Framework 2.0.1](https://www.nuget.org/packages/COA.Mcp.Framework) • **Powered by** [Lucene.NET](https://lucenenet.apache.org/)