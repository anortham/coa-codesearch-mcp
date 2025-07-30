# CodeSearch MCP Server

A high-performance Model Context Protocol (MCP) server for blazing-fast code search and intelligent memory management. Built with .NET 9.0, featuring Lucene-powered millisecond search and AI-optimized architecture for pattern matching and content analysis.

## 🚀 Features

- **Lightning-Fast Search**: Lucene indexing enables instant search across millions of lines
- **Smart Memory System**: Persistent architectural knowledge and decision tracking
- **🆕 Advanced Memory Intelligence**: Natural language commands, semantic search, temporal scoring
- **AI-Optimized Architecture**: Pattern matching and content analysis for AI assistants
- **Progressive Disclosure**: Intelligent summarization with drill-down capabilities
- **Real-time Updates**: File watchers automatically update indexes on changes
- **🆕 Parameter Standardization**: Consistent `query` parameter across all search tools
- **🆕 Workflow Discovery**: Proactive tool guidance and dependency mapping
- **🆕 Enhanced Error Handling**: Actionable recovery guidance instead of generic errors
- **⚡ Confidence-Based Limiting**: Dynamic result counts based on score distribution (60-85% token savings)
- **🔗 Resource URI System**: Two-tier access with minimal initial responses + full results on demand
- **📊 Standardized Responses**: Unified `resultsSummary` format across all search tools

### Performance
- Startup: < 500ms (simplified architecture)
- Text search: < 10ms indexed
- File search: < 50ms
- Memory usage: < 200MB typical

### 🎯 Token Optimization (New!)
- **60-85% token reduction** for high-confidence searches
- **Confidence-based limiting**: 2-3 results for high confidence vs 10+ default
- **Minimal result fields**: Essential data only (path, score vs 6+ fields)
- **Resource URIs**: Full results available on-demand without initial token cost
- **Smart context handling**: Fewer results when context lines are included

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
- `index_workspace` - Build search index (required for all search operations)
- `text_search` - Search text across codebase with advanced filters and context
- `file_search` - Find files by name with fuzzy matching and patterns
- `directory_search` - Find directories with pattern matching support
- `recent_files` - Find recently modified files with time filters
- `similar_files` - Find files with similar content using MLT algorithm
- `file_size_analysis` - Analyze file sizes and distributions

### Essential Memory System (10 Tools)
- `recall_context` - Load relevant context from previous sessions (**use at session start!**)
- `store_memory` - Store any type of memory with custom fields
- `search_memories` - Search stored memories with intelligent query expansion
- `unified_memory` - Natural language memory operations with intent detection
- `semantic_search` - Find memories by concepts and meaning, not just keywords
- `hybrid_search` - Combine text search with semantic understanding
- `memory_quality_assessment` - Evaluate and improve memory quality with scoring
- `load_context` - Auto-load relevant memories for current working environment
- `backup_memories` - Export memories to JSON for team sharing and version control
- `restore_memories` - Import memories from JSON backup files

### Advanced Analysis Tools
- `search_assistant` - Orchestrate multi-step search operations with AI guidance
- `pattern_detector` - Analyze code for patterns and anti-patterns with severity levels
- `memory_graph_navigator` - Explore memory relationships visually with graph insights
- `tool_usage_analytics` - View tool performance and usage patterns
- `workflow_discovery` - Discover tool dependencies and suggested workflows for AI agents

### Utilities
- `batch_operations` - Execute multiple search operations in parallel for efficiency
- `index_health_check` - Check Lucene index status and performance metrics
- `system_health_check` - Comprehensive system health monitoring
- `log_diagnostics` - Manage debug logs and cleanup old log files
- `get_version` - Get server version info and build details

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

# NEW: Discover available workflows
workflow_discovery --goal "getting started"
```

### Daily Usage
```bash
# Search for code patterns (NEW: standardized 'query' parameter)
text_search --query "authentication" --workspacePath "C:/YourProject"

# File search (unified parameter naming - backward compatible)  
file_search --query "AuthService" --workspacePath "C:/YourProject"

# Directory search (standardized parameter names)
directory_search --query "Services" --workspacePath "C:/YourProject"

# Find similar implementations
similar_files --sourcePath "AuthService.cs" --workspacePath "C:/YourProject"

# Analyze recent changes with time filters
recent_files --timeFrame "24h" --workspacePath "C:/YourProject"

# NEW: Multi-step search with AI guidance
search_assistant --goal "Find all error handling patterns" --workspacePath "C:/YourProject"

# NEW: Discover tool workflows and dependencies
workflow_discovery --goal "search code"
workflow_discovery --toolName "text_search"
```

### Memory System
```bash
# Store architectural decision
store_memory --type "ArchitecturalDecision" \
  --content "Using JWT for authentication" \
  --files ["AuthService.cs"]

# Track technical debt with custom fields
store_memory --type "TechnicalDebt" \
  --content "UserService needs refactoring" \
  --fields '{"priority": "high", "effort": "days"}'

# Search memories with AI-powered understanding
search_memories --query "authentication decisions"

# Navigate memory relationships visually
memory_graph_navigator --startPoint "authentication patterns"

# Backup for version control (team sharing enabled)
backup_memories  # Creates JSON in .codesearch/backups/
```

### 🆕 Phase 3: Advanced Memory Intelligence
```bash
# Natural language memory operations
unified_memory --command "remember that UserService has performance issues"
unified_memory --command "find all technical debt related to authentication"
unified_memory --command "create checklist for database migration project"

# Semantic search - find by concepts, not keywords
semantic_search --query "security vulnerabilities in user login systems"

# Hybrid search - combine text and semantic understanding
hybrid_search --query "authentication patterns"

# Memory quality assessment with scoring
memory_quality_assessment --memoryId "memory_123"

# Auto-load relevant context for current work
load_context --workingDirectory "C:/YourProject/Services"

# Advanced temporal scoring for recency-weighted results
search_memories --query "architecture decisions" --boostRecent true
```

### Advanced Workflows
```bash
# Pattern analysis with severity levels
pattern_detector --workspacePath "C:/YourProject" \
  --patternTypes ["architecture", "security", "performance"]

# Parallel operations for comprehensive analysis
batch_operations --operations '[
  {"operation": "text_search", "query": "TODO"},
  {"operation": "file_search", "query": "*.cs"},
  {"operation": "recent_files", "timeFrame": "7d"}
]' --workspacePath "C:/YourProject"

# AI-guided multi-step search with context preservation
search_assistant --goal "Understand authentication implementation" \
  --workspacePath "C:/YourProject"
```

## 🚀 AI Agent Optimizations

This MCP server is specifically optimized for AI agent workflows with **60-85% token reduction**:

### 🎯 Token Optimization Features
- **⚡ Confidence-Based Result Limiting**: Dynamic result counts based on search quality
  - High confidence (score > 0.8): Show 2-3 results 
  - Medium confidence (score > 0.5): Show 3-5 results
  - Low confidence: Show 5-8 results with refinement suggestions
- **🔗 Resource URI System**: Two-tier access pattern
  - Minimal initial response with essential results
  - Full results accessible via `resourceUri` when needed
- **📊 Standardized Response Structure**: Consistent `resultsSummary` across all tools
  - `included`: Number of results in response
  - `total`: Total results available  
  - `hasMore`: Whether more results exist
- **🗂️ Field Minimization**: Essential data only (path + score vs 6+ fields)

### 🧠 Intelligence Features  
- **Progressive Disclosure**: Automatic token-aware response summarization
- **Enhanced Error Handling**: Actionable recovery guidance instead of generic errors
- **Workflow Discovery**: Proactive tool guidance and dependency mapping
- **Parameter Standardization**: Consistent `query` parameter across search tools

### Example Optimized Response

```json
// Example response showing token optimizations
{
  "success": true,
  "operation": "text_search",
  "query": {
    "text": "authentication",
    "type": "standard",
    "workspace": "MyProject"
  },
  "results": [
    // Only 3 results (confidence-limited from 15 total)
    {
      "file": "AuthService.cs",
      "path": "src/services/AuthService.cs", 
      "score": 0.89
      // Minimal fields - no redundant filename, relativePath, etc.
    }
  ],
  "resultsSummary": {
    "included": 3,      // What's shown (confidence-limited)  
    "total": 15,        // What's available
    "hasMore": true     // More available via resourceUri
  },
  "meta": {  
    "searchTime": "4ms",
    "resourceUri": "codesearch-search://search_abc123"  // Full results
  }
}
```

**Token Comparison:**
- **Before**: ~4,500 tokens (15 results × 300 tokens each)
- **After**: ~900 tokens (3 results × 50 tokens each) 
- **Savings**: 80% reduction for high-confidence searches

## 🤖 Claude Code Usage Best Practices

### Recommended Global CLAUDE.md Settings

For optimal Claude Code experience across ALL your projects, add these patterns to your global `~/.claude/CLAUDE.md`:

```markdown
## MCP Tool Usage Best Practices

### CoA CodeSearch MCP Server - Efficient Tool Usage
- **ALWAYS use MCP tools directly** for search operations
- **DON'T use Task tool** for simple content searches or file finding
- **DO use text_search with contextLines** for code snippets and context
- **Trust summary mode insights** - hotspots and actions often contain what you need
- **Use batch_operations** for multiple related searches, not Task tool

### Common Anti-Patterns to Avoid
❌ Task tool for text search → ✅ mcp__codesearch__text_search with contextLines  
❌ Task tool for file finding → ✅ mcp__codesearch__file_search  
❌ Ignoring summary insights → ✅ Use hotspots and actionable suggestions  
❌ Multiple separate tools → ✅ mcp__codesearch__batch_operations  

### Efficient Search Patterns
1. **Index first**: `mcp__codesearch__index_workspace` (required once per session)
2. **Search content**: `text_search --query "pattern" --contextLines 3`
3. **Explore results**: Use hotspots and insights from summary mode
4. **Drill down**: Use provided actions or `responseMode: "full"` only when needed

### Memory System Workflow  
1. **Start sessions**: `recall_context --query "what I'm working on"`
2. **Store findings**: `store_memory --type "TechnicalDebt" --content "..."`  
3. **Find related**: `search_memories --query "authentication"`
```

Copy this guidance to your global configuration for consistent tool usage across all projects.

## 🐛 Troubleshooting

**Stuck indexes**
- Delete `.codesearch/index/*/write.lock` files
- Use `index_health_check` for automated diagnostics

**Debug logging**
```bash
log_diagnostics --action status
log_diagnostics --action cleanup --cleanup true
```

**Performance monitoring**
```bash
system_health_check  # Comprehensive system status
tool_usage_analytics --action summary  # Tool performance insights
```

## 📄 License

MIT License - see [LICENSE](LICENSE) file

## 🙏 Acknowledgments

Built with [Lucene.NET](https://lucenenet.apache.org/) and implements [Model Context Protocol](https://modelcontextprotocol.io/). Optimized for AI assistant workflows with pattern matching and intelligent memory management.