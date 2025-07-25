# TypeScript Support Guide

The COA CodeSearch MCP Server provides comprehensive TypeScript and JavaScript analysis through automatic tsserver integration.

## Prerequisites

⚠️ **IMPORTANT**: TypeScript support requires npm (Node.js) to be installed.
- Without npm: TypeScript tools will fail with "installation failed" error
- Install from: https://nodejs.org/

## Automatic Setup

On first use, the server automatically:
1. Checks for existing TypeScript installation
2. Downloads TypeScript if needed (via npm)
3. Caches installation in `%LOCALAPPDATA%`
4. Starts tsserver process

## Available Tools

### Search
```bash
mcp__codesearch__search_typescript 
  --symbolName "UserInterface" 
  --workspacePath "C:/project"
  --mode "both"  # definition, references, or both
```

### Navigation
```bash
# Go to definition
mcp__codesearch__typescript_go_to_definition 
  --filePath "src/services/api.ts" 
  --line 42 
  --column 15

# Find references  
mcp__codesearch__typescript_find_references 
  --filePath "src/models/User.ts" 
  --line 10 
  --column 5
```

### Refactoring
```bash
# Preview rename
mcp__codesearch__typescript_rename_symbol 
  --filePath "src/components/Button.tsx" 
  --line 5 
  --column 10 
  --newName "PrimaryButton" 
  --preview true
```

### Type Information
```bash
# Get hover info (auto-detects TypeScript)
mcp__codesearch__get_hover_info 
  --filePath "src/utils/helpers.ts" 
  --line 20 
  --column 15
```

## Language Detection

Tools that work with both C# and TypeScript:
- `go_to_definition` - Auto-detects based on file extension
- `get_hover_info` - Auto-detects language

File extensions recognized:
- TypeScript: `.ts`, `.tsx`, `.mts`, `.cts`
- JavaScript: `.js`, `.jsx`, `.mjs`, `.cjs`

## Configuration

TypeScript settings in `appsettings.json`:

```json
{
  "TypeScript": {
    "TsServerPath": null,  // Auto-detected if null
    "MaxMemory": 4096,     // MB
    "LogFile": "%TEMP%\\tsserver.log"
  }
}
```

## Troubleshooting

### Installation Failed
```
Error: TypeScript installation failed
```
**Fix**: Install Node.js/npm from https://nodejs.org/

### TypeScript Not Found
```
Error: tsserver.js not found
```
**Fix**: 
1. Delete `%LOCALAPPDATA%\COA.CodeSearch\typescript`
2. Restart MCP server to reinstall

### Slow Performance
- Increase `TypeScript:MaxMemory` in settings
- Ensure project has `tsconfig.json`
- Exclude `node_modules` in tsconfig

### No Results
- Verify file is in a TypeScript project (has tsconfig.json)
- Check that TypeScript can compile the file
- Try running `tsc` manually to check for errors

## Cross-Language Analysis

### Important: Language Boundaries

TypeScript and C# are analyzed separately:
- C# references won't find TypeScript usages
- TypeScript references won't find C# usages
- Use `text_search` for cross-language string matching

### Full-Stack Workflow

When refactoring across C# backend and TypeScript frontend:

```bash
# 1. Find C# usages
mcp__codesearch__find_references --file "Models/User.cs"

# 2. Find TypeScript usages  
mcp__codesearch__typescript_find_references --file "models/User.ts"

# 3. Find string references (API routes, etc.)
mcp__codesearch__text_search --query "\"User\"" --searchType phrase

# 4. Rename backend
mcp__codesearch__rename_symbol --file "Models/User.cs" --newName "Customer"

# 5. Rename frontend
mcp__codesearch__typescript_rename_symbol --file "models/User.ts" --newName "Customer"
```

## Performance Tips

1. **Project Structure**: Ensure proper `tsconfig.json` setup
2. **Exclude Folders**: Add `node_modules` to exclude paths
3. **Batch Operations**: tsserver reuses process across calls
4. **Memory Usage**: Monitor and adjust MaxMemory if needed

## Known Limitations

1. **npm Required**: Direct .tgz download exists but extraction not implemented
2. **No Cross-Language Refs**: Each language analyzed independently  
3. **Project Required**: Loose `.ts` files without project work poorly
4. **Large Projects**: May need memory limit increases

## Future Enhancements

- Direct TypeScript installation without npm
- Cross-language reference tracking
- Support for other JS tools (ESLint, Prettier)
- JavaScript-specific optimizations