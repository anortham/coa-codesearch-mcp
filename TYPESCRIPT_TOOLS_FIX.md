# TypeScript Tools Analysis and Fixes

## Issue Summary

The TypeScript language server tools (`typescript_go_to_definition` and `typescript_find_references`) were failing while the text-based search tools were working correctly.

## Root Causes Identified

### 1. TSServer Protocol Implementation Issues

**Problem**: The TypeScript language server communication was missing proper message handling:
- No event loop to handle multiple message types from tsserver
- Only reading a single line instead of handling the protocol's event/response pattern
- Missing response correlation using sequence numbers

**Fix Applied**: Enhanced `SendRequestAsync` in `TypeScriptAnalysisService.cs`:
- Added proper message loop to handle events before responses
- Implemented request/response correlation using sequence numbers
- Added timeout protection (10 seconds) to prevent infinite hangs
- Enhanced debug logging for troubleshooting

### 2. File Not Opened in TSServer

**Problem**: Before querying definitions or references, files must be explicitly opened in the TypeScript language server using the "open" command.

**Fix Applied**: Modified both `GetDefinitionAsync` and `FindReferencesAsync` to:
- Send an "open" request before querying
- Ensure the file is loaded in tsserver's context
- Handle failures gracefully with logging

### 3. Mixed Implementation Architecture

**Current State**:
- `search_typescript` tool uses `TypeScriptTextAnalysisService` (regex-based text search)
- `typescript_go_to_definition` and `typescript_find_references` use `TypeScriptAnalysisService` (actual tsserver)
- This explains why symbol search returns 0 results while text search works

## Changes Made

### TypeScriptAnalysisService.cs

1. Enhanced `SendRequestAsync` method:
   - Added message loop with timeout
   - Proper response correlation
   - Enhanced debug logging

2. Modified `GetDefinitionAsync`:
   - Opens file in tsserver before querying
   - Better error handling

3. Modified `FindReferencesAsync`:
   - Opens file in tsserver before querying
   - Better error handling

## Testing Instructions

After rebuilding and restarting the MCP server:

1. Test TypeScript go-to-definition:
```
mcp__codesearch__typescript_go_to_definition(
  filePath: "path/to/file.ts",
  line: 10,
  column: 15
)
```

2. Test TypeScript find references:
```
mcp__codesearch__typescript_find_references(
  filePath: "path/to/file.ts", 
  line: 10,
  column: 15
)
```

3. Enable debug logging to see tsserver communication:
- Set log level to "Debug" in appsettings.json
- Watch for "Sending TypeScript request" and "Received TypeScript message" entries

## Known Limitations

1. **Symbol Search**: The `search_typescript` tool uses text-based search, not semantic analysis. For accurate TypeScript symbol search, consider using `fast_text_search` with appropriate patterns.

2. **Initial Performance**: The first request to tsserver may be slow as it needs to:
   - Start the Node.js process
   - Load tsserver
   - Parse the TypeScript project

3. **Project Detection**: The tool attempts to find tsconfig.json files but may need explicit project configuration for complex setups.

## Future Improvements

1. Implement proper TypeScript symbol search using tsserver's "navto" command
2. Add project caching to avoid reopening files repeatedly
3. Implement background tsserver lifecycle management
4. Add support for TypeScript language service plugins
5. Consider implementing a proper event handler for tsserver notifications

## Troubleshooting

If TypeScript tools still fail after these fixes:

1. Check Node.js is installed and in PATH
2. Verify TypeScript is installed (automatic installation should handle this)
3. Enable debug logging to see actual tsserver communication
4. Check for tsconfig.json in the project
5. Ensure file paths are absolute, not relative