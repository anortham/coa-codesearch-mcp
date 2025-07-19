# TypeScript Support in COA CodeSearch MCP Server

The COA CodeSearch MCP Server includes support for TypeScript analysis, including features like go-to-definition, find-references, and symbol search for TypeScript, JavaScript, and Vue files.

## Automatic Installation

When installed as a global dotnet tool, the TypeScript language server (tsserver) will be **automatically installed** on first use if:
1. Node.js is available on your system
2. You have internet connectivity

The server will:
1. First check if TypeScript is already installed locally
2. If not found, attempt to install TypeScript using npm
3. Store TypeScript in your local app data directory: `%LOCALAPPDATA%\COA.CodeSearch.McpServer\typescript`

## Manual Installation (Optional)

If automatic installation fails or you prefer manual setup:

### Option 1: Local Installation
If you're running from source:
```bash
cd COA.CodeSearch.McpServer
npm install
```

### Option 2: Global npm Installation
Install TypeScript globally:
```bash
npm install -g typescript
```

Then configure the path in `appsettings.json`:
```json
{
  "TypeScript": {
    "ServerPath": "C:\\Users\\[YourUser]\\AppData\\Roaming\\npm\\node_modules\\typescript\\lib\\tsserver.js"
  }
}
```

### Option 3: Project-specific Installation
For project-specific TypeScript versions, the server will use TypeScript from your project's node_modules if available.

## Requirements

- **Node.js**: Required for TypeScript Language Service features
  - Download from: https://nodejs.org/
  - The server will work without Node.js but with limited TypeScript functionality

## Features Available

### With TypeScript Language Service:
- Go to definition
- Find all references
- Hover information
- Symbol renaming
- Full semantic analysis

### Without TypeScript Language Service (text-based only):
- Fast text search
- Basic symbol search
- File indexing
- Pattern matching

## Troubleshooting

### TypeScript not installing automatically?
1. Check Node.js is installed: `node --version`
2. Check npm is available: `npm --version`
3. Check internet connectivity
4. Check logs for error messages

### Manual TypeScript path configuration
Add to your `appsettings.json`:
```json
{
  "TypeScript": {
    "ServerPath": "/path/to/typescript/lib/tsserver.js"
  }
}
```

### Permissions issues
The automatic installer stores TypeScript in:
- Windows: `%LOCALAPPDATA%\COA.CodeSearch.McpServer\typescript`
- Linux/Mac: `~/.local/share/COA.CodeSearch.McpServer/typescript`

Ensure your user has write permissions to this directory.

## Performance Notes

- TypeScript installation happens only once
- The TypeScript language server starts on-demand
- Text-based search works immediately without TypeScript installation
- Language service features require Node.js and TypeScript