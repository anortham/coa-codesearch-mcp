# COA CodeSearch MCP Server - Tool Descriptions Guide

## Overview
The COA CodeSearch MCP Server provides high-performance code navigation and search capabilities that outperform Python-based alternatives. Built on .NET 9.0 with Roslyn for semantic analysis and Lucene.NET for blazing-fast text search.

## When to Use This MCP Server

Use COA CodeSearch when you need:
- **Lightning-fast code navigation** across large codebases
- **Semantic understanding** of C#, Blazor, and Razor files
- **Text search** that's orders of magnitude faster than grep/ripgrep
- **Real-time code analysis** without waiting for indexing
- **Memory-efficient** operations on massive solutions

## Tool Categories

### 🔍 Search Tools

#### `fast_text_search` ⚡ NEW!
**When to use**: For blazing-fast text search across all file types
- Searches through millions of lines in milliseconds
- Supports wildcards, fuzzy matching, and phrase search
- Returns results with context lines
- Perfect for finding TODOs, comments, string literals, or any text pattern
- **Example**: "Find all files containing 'TODO' with 2 lines of context"

#### `search_symbols`
**When to use**: For finding classes, methods, properties by name
- Semantic search that understands code structure
- Filters by symbol type (class, interface, method, etc.)
- Supports wildcards and fuzzy matching
- **Example**: "Find all classes that start with 'User'"

#### `advanced_symbol_search`
**When to use**: For complex semantic queries
- Filter by accessibility (public, private, etc.)
- Find abstract/virtual/override members
- Search within specific namespaces
- **Example**: "Find all public abstract methods in the Services namespace"

### 🧭 Navigation Tools

#### `go_to_definition`
**When to use**: Navigate to where a symbol is defined
- Jump to class, method, property definitions
- Works across projects in a solution
- Handles partial classes and inheritance
- **Example**: "Go to the definition of IUserService"

#### `find_references`
**When to use**: Find all usages of a symbol
- Locate every place a method is called
- Find all variable usages
- Track interface implementations
- **Example**: "Find all calls to the SaveAsync method"

#### `get_implementations`
**When to use**: Find concrete implementations
- Locate all classes implementing an interface
- Find overrides of virtual/abstract methods
- Navigate inheritance hierarchies
- **Example**: "Find all implementations of IRepository"

### 📊 Analysis Tools

#### `get_diagnostics`
**When to use**: Check for compilation errors
- Get all errors and warnings in a file/project
- Filter by severity level
- Identify build issues quickly
- **Example**: "Show all errors in the current project"

#### `get_call_hierarchy`
**When to use**: Understand method call chains
- See what methods call a specific method (incoming)
- See what methods are called by a specific method (outgoing)
- Trace execution flow
- **Example**: "Show all methods that call ProcessOrder"

#### `dependency_analysis`
**When to use**: Analyze code dependencies
- Understand coupling between components
- Find circular dependencies
- Track assembly references
- **Example**: "Analyze dependencies of the UserService class"

#### `project_structure_analysis`
**When to use**: Get project metrics and structure
- View solution organization
- Get code metrics (lines, classes, methods)
- List NuGet packages
- **Example**: "Show metrics for the entire solution"

### 📝 Code Information Tools

#### `get_hover_info`
**When to use**: Get detailed symbol information
- View type information
- Read XML documentation
- See method signatures
- **Example**: "Get documentation for the CalculateTotal method"

#### `get_document_symbols`
**When to use**: List all symbols in a C# file
- Get file outline/structure
- Find all classes and members
- Navigate large files
- **C# ONLY** - uses Roslyn document symbols
- **Example**: "List all methods in UserService.cs"

### 🔧 Refactoring Tools

#### `rename_symbol`
**When to use**: Safely rename code elements
- Rename across entire solution
- Updates all references
- Preserves code functionality
- **Example**: "Rename UserManager to UserService everywhere"

#### `batch_operations`
**When to use**: Perform multiple operations efficiently
- Chain multiple searches/navigations
- Reduce round-trip overhead
- Complex multi-step operations
- **Example**: "Find definition, then get all references, then analyze dependencies"
- **Supported operations**: `fast_text_search`, `search_symbols`, `find_references`, `go_to_definition`, `get_hover_info`, `get_implementations`, `get_document_symbols`, `get_diagnostics`, `get_call_hierarchy`, `analyze_dependencies`

**Important**: The `workspacePath` can be specified at the top level (recommended) or in each individual operation. If an operation doesn't specify `workspacePath`, it will use the top-level value.

**Example batch with text search and dependency analysis**:
```json
{
  "workspacePath": "C:\\source\\MyProject",
  "operations": [
    {
      "operation": "fast_text_search",
      "query": "UseAuthentication",
      "maxResults": 10
    },
    {
      "operation": "search_symbols",
      "searchPattern": "*Controller",
      "searchType": "wildcard"
    },
    {
      "operation": "analyze_dependencies",
      "symbol": "SERFormsController",
      "direction": "outgoing",
      "depth": 2
    }
  ]
}
```

You can also override the workspacePath for specific operations if needed:
```json
{
  "workspacePath": "C:\\source\\MyProject",
  "operations": [
    {
      "operation": "fast_text_search",
      "query": "TODO",
      "maxResults": 10
    },
    {
      "operation": "search_symbols",
      "searchPattern": "ILogger",
      "workspacePath": "C:\\source\\DifferentProject"
    }
  ]
}
```

### Batch Operations Parameter Reference

Each operation must include `"operation"` (or `"type"`) and the required parameters:

1. **fast_text_search**
   - `query`: Search query (required)
   - `workspacePath`: Path to workspace (required if not specified at top level)
   - `filePattern`: Optional file glob pattern
   - `extensions`: Optional array of file extensions
   - `contextLines`: Optional number of context lines
   - `maxResults`: Optional max results (default: 50)
   - `caseSensitive`: Optional case sensitivity
   - `searchType`: Optional search type ("standard", "wildcard", "fuzzy", "phrase")

2. **search_symbols**
   - `searchPattern`: Search pattern (required)
   - `workspacePath`: Path to workspace (required if not specified at top level)
   - `symbolTypes`: Optional array of symbol types
   - `searchType`: Optional search type ("exact", "contains", "startsWith", "wildcard", "fuzzy")
   - `maxResults`: Optional max results (default: 100)

3. **find_references**
   - `filePath`: File path (required)
   - `line`: Line number (required)
   - `column`: Column number (required)
   - `includeDeclaration`: Optional include declaration (default: true)

4. **go_to_definition**
   - `filePath`: File path (required)
   - `line`: Line number (required)
   - `column`: Column number (required)

5. **get_hover_info**
   - `filePath`: File path (required)
   - `line`: Line number (required)
   - `column`: Column number (required)

6. **get_implementations**
   - `filePath`: File path (required)
   - `line`: Line number (required)
   - `column`: Column number (required)

7. **get_document_symbols**
   - `filePath`: File path (required)
   - `includeMembers`: Optional include members (default: true)

8. **get_diagnostics**
   - `path`: Path to file/project/solution (required)
   - `severities`: Optional array of severities
   - `maxResults`: Optional max results (default: 100)
   - `summaryOnly`: Optional summary only mode

9. **get_call_hierarchy**
   - `filePath`: File path (required)
   - `line`: Line number (required)
   - `column`: Column number (required)
   - `direction`: Optional direction ("incoming", "outgoing", "both", default: "both")
   - `maxDepth`: Optional max depth (default: 2)

10. **analyze_dependencies**
    - `symbol`: Symbol name (required)
    - `workspacePath`: Path to workspace (required if not specified at top level)
    - `direction`: Optional direction ("incoming", "outgoing", "both", default: "both")
    - `depth`: Optional analysis depth (default: 3)
    - `includeTests`: Optional include test projects
    - `includeExternalDependencies`: Optional include external dependencies

## Performance Comparison

| Operation | COA CodeSearch | Python-based MCP | Improvement |
|-----------|---------------|------------------|-------------|
| Text search (1M lines) | <100ms | 2-5s | 20-50x faster |
| Symbol search | <50ms | 500ms+ | 10x faster |
| Go to definition | <30ms | 200ms+ | 7x faster |
| Find references | <200ms | 1s+ | 5x faster |

## Best Practices

1. **Start with `fast_text_search`** for general text finding
2. **Use semantic tools** (`search_symbols`, `go_to_definition`) for code understanding
3. **Combine tools** with `batch_operations` for complex workflows
4. **Index once** - the Lucene index persists across sessions
5. **Use filters** to narrow results and improve performance

## Tips for AI Assistants

When helping users navigate code:
1. Use `fast_text_search` first to quickly locate relevant files
2. Then use semantic tools for precise navigation
3. Combine `get_diagnostics` with fixes to ensure code health
4. Use `project_structure_analysis` to understand codebase organization
5. Leverage `batch_operations` for multi-step investigations