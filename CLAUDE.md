# COA CodeSearch MCP Server - Claude AI Assistant Guide

This document provides context and guidelines for AI assistants working on the COA CodeSearch MCP Server project.

## IMPORTANT: Environment Context

**This project is being developed in Claude Code, NOT Claude Desktop.** The MCP server integration will be different:
- Claude Code has its own MCP integration mechanism
- Do not look for Claude Desktop configuration files
- MCP servers in Claude Code are configured differently than in Claude Desktop

## Project Overview

COA CodeSearch MCP Server is a high-performance Model Context Protocol (MCP) server built in .NET 9.0 that provides Language Server Protocol (LSP)-like capabilities for navigating and searching codebases across multiple languages. It leverages Roslyn for C# code analysis and is being expanded to support Blazor/Razor files and other file types through fast text indexing. Designed to be significantly faster than Python-based alternatives.

## Key Commands

### Build and Test
```bash
# Build the project
dotnet build

# Run the server
dotnet run

# Build for release with AOT
dotnet publish -c Release -r win-x64

# Run tests
dotnet test

# Check for package updates
dotnet list package --outdated
```

### MCP Server Commands
```bash
# Run server in STDIO mode for Claude Desktop
dotnet run -- stdio

# Run server with verbose logging
dotnet run -- stdio --log-level debug

# Test with MCP inspector
npx @modelcontextprotocol/inspector dotnet run -- stdio
```

## Project Structure

```
COA.CodeSearch.McpServer/
├── Program.cs                     # Entry point, MCP server setup
├── Services/
│   ├── CodeAnalysisService.cs     # Manages code analysis and workspaces
│   ├── CodeNavigationService.cs   # Navigation operations
│   └── SymbolSearchService.cs     # Symbol search functionality
├── Tools/
│   ├── GoToDefinitionTool.cs      # Navigate to definitions
│   ├── FindReferencesTool.cs      # Find all references
│   ├── SearchSymbolsTool.cs       # Search for symbols
│   └── GetDiagnosticsTool.cs      # Get compilation errors
├── Resources/
│   ├── ProjectStructureResource.cs # Expose project structure
│   └── SymbolInfoResource.cs      # Symbol information
├── Models/
│   └── [Various DTOs]              # Data transfer objects
├── appsettings.json               # Configuration
└── COA.CodeSearch.McpServer.csproj   # Project file
```

## Architecture Decisions

### 1. **Roslyn Integration**
- Uses Microsoft.CodeAnalysis for all code analysis
- MSBuildWorkspace for loading solutions
- Incremental compilation for performance
- Cached semantic models

### 2. **MCP Implementation**
- STDIO transport for Claude Desktop integration
- Strongly-typed tools using [McpTool] attributes
- Resources for read-only data access
- Structured error handling

### 3. **Performance Optimizations**
- Native AOT compilation for faster startup
- Workspace caching with LRU eviction
- Lazy loading of projects
- Parallel symbol search
- Memory-mapped file access for large files

### 4. **Error Handling**
- Graceful degradation when projects don't compile
- Timeout protection for long operations
- Detailed error messages for debugging
- Fallback to syntax-only analysis

## Development Guidelines

### Code Style
- Use C# 12 features with nullable reference types
- Follow standard C# naming conventions
- Async all the way down
- Use ILogger for all logging
- Document public APIs with XML comments

### MCP Tool Implementation Pattern
```csharp
[McpTool("tool_name")]
[Description("Clear description of what this tool does")]
public async Task<ToolResult> ToolName(
    [Description("Parameter description")] string param1,
    [Description("Optional parameter")] string? param2 = null)
{
    try
    {
        // Implementation
        return new ToolResult { Success = true, Data = result };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in ToolName");
        return new ToolResult { Success = false, Error = ex.Message };
    }
}
```

### Adding New Tools

1. Create a new class in the Tools folder
2. Inherit from `McpToolBase` or implement `IMcpTool`
3. Add the `[McpTool]` attribute with a unique name
4. Implement the tool logic using Roslyn APIs
5. Register in DI container in Program.cs
6. Add integration tests

### Testing Strategy

- Unit tests for individual services
- Integration tests for MCP tools
- Performance benchmarks for large codebases
- Test with various project types (.NET Core, Framework, etc.)
- Memory usage tests for long-running scenarios

## Common Tasks

### 1. **Adding a new navigation feature**
```bash
# Create new tool class
# Update RoslynWorkspaceService if needed
# Add tests
# Update MCP manifest
```

### 2. **Improving performance**
- Profile with dotTrace or PerfView
- Check for unnecessary allocations
- Consider caching strategies
- Use ValueTask where appropriate

### 3. **Debugging MCP communication**
- Enable debug logging in appsettings.json
- Use MCP Inspector tool
- Check STDIO input/output
- Validate JSON-RPC messages

## Configuration

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "COA.Roslyn": "Debug"
    }
  },
  "McpServer": {
    "MaxWorkspaces": 5,
    "WorkspaceTimeout": "00:30:00",
    "EnableDiagnostics": true,
    "ParallelismDegree": 4
  }
}
```

### Environment Variables
- `MCP_LOG_LEVEL`: Override log level
- `MCP_WORKSPACE_PATH`: Default workspace path
- `MCP_MAX_MEMORY_MB`: Memory limit for AOT

## Performance Targets

- Startup time: < 100ms (with AOT)
- GoToDefinition: < 50ms for cached workspace
- FindReferences: < 200ms for average project
- Symbol search: < 100ms for prefix match
- Memory usage: < 500MB for typical solution

## Integration with Claude Desktop

1. Build the server: `dotnet publish -c Release`
2. Add to Claude Desktop config:
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

## Troubleshooting

### Common Issues

1. **MSBuild not found**
   - Install Build Tools for Visual Studio
   - Run MSBuildLocator.RegisterDefaults()

2. **High memory usage**
   - Check workspace cache settings
   - Enable workspace eviction
   - Reduce ParallelismDegree

3. **Slow performance**
   - Enable AOT compilation
   - Check for compilation errors in target projects
   - Profile with performance tools

## Progressive Disclosure Pattern (Optimized for Claude)

The MCP server includes smart response handling designed specifically for Claude's workflow:

## Auto-Mode Switching
Tools automatically switch to summary mode when responses exceed 5,000 tokens:
```json
{
  "success": true,
  "mode": "summary",
  "autoModeSwitch": true,  // Indicates automatic switch
  "data": { /* summary data */ }
}
```

## Smart Summaries
Summary responses include:
- **Key Insights**: "CmsController.cs has 30% of all changes"
- **Hotspots**: Files with highest concentration of results
- **Categories**: Results grouped by file type (controllers, services, etc.)
- **Impact Analysis**: Risk factors and suggestions

## Efficient Drill-Down
Request specific details without re-executing operations:
```json
// Get details for high-impact files
{
  "detailLevel": "hotspots",
  "detailRequestToken": "..."
}

// Get category-specific details
{
  "detailLevel": "smart_batch",
  "criteria": {
    "categories": ["controllers"],
    "maxTokens": 8000
  }
}
```

## Token Awareness
All responses include token estimates:
- `estimatedTokens`: Current response size
- `estimatedFullResponseTokens`: Full data size
- Available detail levels show token cost

## Usage Example
```bash
# 1. Initial request - auto-switches to summary if large
rename_symbol --file ICmsService.cs --line 10 --newName IContentManagementService

# 2. Response includes smart analysis
# - Key insights about the changes
# - Recommended next actions with exact commands
# - Token estimates for each action

# 3. Drill down as needed using provided commands
```

# Future Enhancements

- WebSocket transport for remote deployment
- Integration with OmniSharp for additional features
- Support for F# and VB.NET
- Real-time file watching and incremental updates
- Code lens and inline hints
- Integration with .NET CLI tools
- Semantic code search with embeddings