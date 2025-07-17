# Using COA Roslyn MCP Server as a Dotnet Tool

## Installation

### From Internal NuGet Feed

```bash
# Add your internal NuGet source if not already added
dotnet nuget add source https://your-devops-url/YourProject/_packaging/YourFeed/nuget/v3/index.json -n YourCompanyFeed

# Install the tool globally
dotnet tool install -g COA.Roslyn.McpServer --add-source https://your-devops-url/YourProject/_packaging/YourFeed/nuget/v3/index.json

# Or install locally in a project
dotnet new tool-manifest # if not already present
dotnet tool install COA.Roslyn.McpServer --add-source https://your-devops-url/YourProject/_packaging/YourFeed/nuget/v3/index.json
```

## Usage

### As a Global Tool

Once installed globally, you can run it from anywhere:

```bash
# Run the MCP server
coa-roslyn-mcp stdio

# Get help
coa-roslyn-mcp --help
```

### With Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "coa-roslyn-mcp",
      "args": ["stdio"]
    }
  }
}
```

### With MCP Inspector

```bash
# Test with the inspector
npx @modelcontextprotocol/inspector -- coa-roslyn-mcp stdio
```

## Benefits of Dotnet Tool Distribution

1. **Easy Installation**: Single command to install/update
2. **Version Management**: Can pin specific versions
3. **No Build Required**: Pre-compiled and ready to use
4. **Global Access**: Available from any directory
5. **Auto-updates**: Can update with `dotnet tool update`

## Development Workflow

1. Make changes to the code
2. Bump version in .csproj
3. Push to main branch
4. Pipeline automatically builds and publishes
5. Users update with: `dotnet tool update -g COA.Roslyn.McpServer`

## Local Testing During Development

```bash
# Pack locally
dotnet pack

# Install from local package
dotnet tool install -g COA.Roslyn.McpServer --add-source ./COA.Roslyn.McpServer/bin/Debug --version 1.0.0

# Uninstall if needed
dotnet tool uninstall -g COA.Roslyn.McpServer
```