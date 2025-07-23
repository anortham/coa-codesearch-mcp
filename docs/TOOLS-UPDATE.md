# Tools Documentation Updates

The following updates should be made to TOOLS.md to reflect recent improvements:

## 1. Update index_workspace Documentation

### Current
The index_workspace tool documentation should be updated to mention the new project detection feature.

### Updated Documentation
```markdown
### index_workspace 🏗️

Build search index for a workspace. Required before using any fast_* search tools. Detects project type and provides search tips.

**Parameters:**
- `workspacePath` (string, required): The workspace path to index
- `forceRebuild` (boolean, optional): Force rebuild even if index exists (default: false)

**Example:**
```
index_workspace --workspacePath "C:/project"
```

**Returns:**
```json
{
  "success": true,
  "message": "Successfully indexed 177 files",
  "workspacePath": "C:/project",
  "filesIndexed": 177,
  "duration": "0.55 seconds",
  "action": "created",
  "fileWatching": "enabled",
  "projectInfo": {
    "type": "Blazor Server, ASP.NET Core",
    "primaryExtensions": [".cs", ".razor", ".cshtml"],
    "tips": [
      "Use filePattern: '**/*.{cs,razor}' to search both C# and Blazor component files"
    ]
  }
}
```

**New Features:**
- Detects project type (Blazor, ASP.NET Core, WPF, React, Angular, Vue, etc.)
- Provides project-specific search tips
- Lists primary file extensions for the project
- Prevents redundant indexes when subdirectories are already covered by parent index
```

## 2. Update fast_text_search_v2 Documentation

### Updated Documentation
```markdown
### fast_text_search_v2 🔍

AI-OPTIMIZED text search INSIDE files! Search for code, strings, or patterns across your entire codebase. Returns structured insights with hotspots and smart suggestions. ⚡ <50ms for millions of lines.

**When to use:** Finding where something is used/implemented (e.g., 'where is IPushoverService used?')

**Parameters:**
- `query` (string, required): Text to search for - supports wildcards (*), fuzzy (~), and phrases ("exact match")
- `workspacePath` (string, required): Path to solution (.sln), project (.csproj), or directory to search
- `filePattern` (string, optional): Filter by file pattern (e.g., '*.cs' for C# only, 'src/**/*.ts' for TypeScript in src)
- `extensions` (array, optional): Limit to specific file types (e.g., ['.cs', '.razor', '.js'])
- `contextLines` (integer, optional): Show N lines before/after each match for context (default: 0)
- `maxResults` (integer, optional): Maximum number of results (default: 50)
- `caseSensitive` (boolean, optional): Case sensitive search (default: false)
- `searchType` (string, optional): Search mode - 'standard' (default), 'wildcard' (with *), 'fuzzy' (approximate), 'phrase' (exact)
- `responseMode` (string, optional): Response mode: 'summary' (default) or 'full'. Auto-switches to summary for large results.

**Enhanced Zero-Result Insights:**
When no results are found due to file pattern restrictions, the tool now:
- Detects if removing the restriction would find results
- Provides specific, copy-pasteable commands to try
- Shows project-aware suggestions (e.g., "Blazor project detected - UI components are in .razor files!")
- Lists which file types contain matches you're missing

**Example Response with Zero Results:**
```json
{
  "insights": [
    "No matches found for 'IPathResolutionService'",
    "Found 29 matches in other file types: .cs (27), .md (2)",
    "💡 TIP: Remove filePattern/extensions to search ALL file types",
    "🔍 Try: fast_text_search --query \"IPathResolutionService\" --workspacePath \"C:\\project\"",
    "🎯 Blazor project detected - UI components are in .razor files!",
    "🔍 Try: fast_text_search --query \"IPathResolutionService\" --extensions .cs,.razor --workspacePath \"C:\\project\""
  ]
}
```
```

## 3. Update fast_file_search_v2 Documentation

### Updated Documentation
```markdown
### fast_file_search_v2 📁

AI-OPTIMIZED search FOR file names! Find files by name with typo tolerance and smart patterns. Returns directory hotspots and insights. ⚡ <10ms response.

**When to use:** Finding specific files (e.g., 'where is PushoverService.cs?')

**Parameters:**
[Keep existing parameters]

**Updated Description:**
The tool descriptions now clearly distinguish between:
- `fast_text_search_v2` - Search INSIDE files for content
- `fast_file_search_v2` - Search FOR file names
```

## 4. Add Note About Redundant Index Prevention

Add to the index_workspace section:

```markdown
**Redundant Index Prevention:**
The tool now detects when you try to index a subdirectory that's already covered by a parent directory's index. For example:
- If you've indexed `C:\project\`
- Attempting to index `C:\project\src\` will return: "This path is already indexed as part of parent workspace: C:\project"
- This prevents accumulation of redundant indexes and saves disk space
```