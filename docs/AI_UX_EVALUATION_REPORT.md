# AI Agent UX Evaluation: COA CodeSearch MCP Server Tools

**Date:** January 27, 2025  
**Evaluator:** AI Experience Engineer (Claude)  
**Scope:** All MCP tools in COA CodeSearch Server  
**Impact:** Critical - Affects all AI agents using these tools

## Executive Summary

After analyzing the COA CodeSearch MCP Server tool implementations, I've identified critical issues that significantly impact AI agent usability. The tools exhibit inconsistent parameter naming, ambiguous descriptions, unpredictable response formats, and poor error recovery guidance. These issues create confusion and increase failure rates for AI agents trying to use the tools effectively.

## Critical Issues (Must Fix)

### 1. Parameter Naming Inconsistencies

**Problem:** Mixed naming conventions across tools for similar concepts.

**Examples:**
- `query` parameter has different meanings:
  - In `text_search`: "Text to search for - supports wildcards (*), fuzzy (~), and phrases"
  - In `file_search`: "File name to search for"
  - In `directory_search`: "Directory name to search for"
- Path parameters vary: `workspacePath`, `filePath`, `sourceFilePath`
- Result limiting: `maxResults` vs `limit`
- Response format: `mode` vs `responseMode`

**AI Impact:** AI agents must remember different parameter names for similar concepts, increasing cognitive load and error rates.

**Recommendation:**
```csharp
// BEFORE (inconsistent):
text_search --query "TODO" --workspacePath "C:\project"
file_search --query "UserService" --workspacePath "C:\project"
similar_files --sourceFilePath "C:\project\file.cs" --workspacePath "C:\project"

// AFTER (consistent):
text_search --searchQuery "TODO" --workspacePath "C:\project"
file_search --nameQuery "UserService" --workspacePath "C:\project"
similar_files --sourcePath "C:\project\file.cs" --workspacePath "C:\project"
```

### 2. Ambiguous Tool Descriptions

**Problem:** Vague descriptions that don't help AI agents understand when to use each tool.

**Example - BEFORE:**
```
"Search for text content within files across the codebase. REQUIRES index_workspace to be run first - will fail with error if workspace not indexed."
```

**Issues:**
- Passive voice ("to be run") unclear about who runs it
- "will fail with error" doesn't specify what error
- No recovery guidance
- No clear use cases

**Example - AFTER:**
```
"Searches file contents for text patterns (literals, wildcards, regex).
Returns: File paths with line numbers and optional context.
Prerequisites: Call index_workspace first for the target directory.
Error handling: Returns INDEX_NOT_FOUND error with recovery steps if not indexed.
Use cases: Finding code patterns, error messages, TODOs, configuration values.
Not for: File name searches (use file_search), directory searches (use directory_search)."
```

### 3. Unpredictable Response Formats

**Problem:** Different tools return different response structures even for similar operations.

**Current State:**
```csharp
// Tool 1: Raw data
return CreateSuccessResult(result);

// Tool 2: Wrapped response
return new ClaudeOptimizedResponse<object> { 
    Success = true, 
    Mode = actualMode,
    Data = responseData,
    Metadata = metadata
};

// Tool 3: Anonymous object
return new { 
    success = false, 
    error = "Failed to backup memories" 
};

// Tool 4: Error response method
return CreateErrorResponse<object>("Search query cannot be empty");
```

**AI Impact:** AI agents must handle multiple response formats, making parsing unreliable.

**Recommendation - Unified Format:**
```json
{
  "success": true,
  "mode": "summary",
  "data": {
    // Tool-specific data
  },
  "metadata": {
    "totalResults": 150,
    "returnedResults": 50,
    "estimatedTokens": 2500,
    "autoModeSwitched": false,
    "detailRequestToken": "abc123"
  },
  "error": null,
  "recovery": null
}
```

## Important Issues (Should Fix)

### 4. Poor Error Recovery Guidance

**Problem:** Generic error messages provide no actionable recovery path.

**BEFORE:**
```json
{
  "success": false,
  "error": "Failed to index workspace"
}
```

**AFTER:**
```json
{
  "success": false,
  "error": {
    "code": "INDEX_NOT_FOUND",
    "message": "No search index exists for C:\\MyProject"
  },
  "recovery": {
    "steps": [
      "Run index_workspace with workspacePath='C:\\MyProject'",
      "Wait for indexing to complete (typically 10-60 seconds)",
      "Retry your search"
    ],
    "suggestedActions": [
      {
        "tool": "index_workspace",
        "params": {"workspacePath": "C:\\MyProject"},
        "description": "Create search index for this workspace"
      }
    ]
  }
}
```

### 5. Unclear Progressive Disclosure

**Problem:** The `detailRequest` parameter lacks clear documentation and examples.

**BEFORE:**
```csharp
detailRequest = new { 
    type = "object", 
    description = "Optional detail request for cached data",
    properties = new {
        detailLevel = new { type = "string" },
        detailRequestToken = new { type = "string" }
    }
}
```

**AFTER:**
```csharp
detailRequest = new { 
    type = "object", 
    description = @"Request more details from a previous summary response.
    Example: After getting a summary with 150 results, use the provided 
    detailRequestToken to get full results.
    
    Usage:
    1. First call returns summary with metadata.detailRequestToken
    2. Second call with detailRequest gets additional data",
    
    properties = new {
        detailLevel = new { 
            type = "string", 
            enum = new[] { "full", "next50", "hotspots" },
            description = "Level of detail: full (all results), next50 (next batch), hotspots (high-concentration files)"
        },
        detailRequestToken = new { 
            type = "string",
            description = "Token from metadata.detailRequestToken in previous response" 
        }
    },
    
    examples = new[] {
        new {
            detailLevel = "full",
            detailRequestToken = "cache_123abc_1706332800"
        }
    }
}
```

### 6. Parameter Description Ambiguities

**Problem:** Vague parameter descriptions leave AI agents guessing about formats and values.

**Examples of improvements:**

```csharp
// BEFORE:
filePattern = new { 
    type = "string", 
    description = "Optional: Filter by file pattern" 
}

// AFTER:
filePattern = new { 
    type = "string", 
    description = @"Glob pattern to filter files. 
    Syntax:
    - '*.cs' = all C# files
    - 'src/**/*.js' = all JS files under src/
    - '*Test.cs' = files ending with Test.cs
    - '!*.min.js' = exclude minified JS files
    Uses minimatch patterns: * (any chars), ** (any dirs), ? (single char), [abc] (char set)",
    examples = new[] { "*.cs", "src/**/*.ts", "*Test.*", "!node_modules/**" }
}

// BEFORE:
searchType = new { 
    type = "string", 
    description = "Search mode: 'standard' (default)...", 
    @default = "standard" 
}

// AFTER:
searchType = new { 
    type = "string",
    enum = new[] { "standard", "fuzzy", "wildcard", "phrase", "regex" },
    description = @"Search algorithm:
    - standard: Exact substring match (case-insensitive by default)
    - fuzzy: Approximate match allowing typos (append ~ to terms)  
    - wildcard: Pattern matching with * and ?
    - phrase: Exact phrase in quotes
    - regex: Full regex support with capturing groups",
    examples = new {
        standard = "getUserName",
        fuzzy = "getUserNam~",
        wildcard = "get*Name",
        phrase = "\"get user name\"",
        regex = "get\\w+Name"
    }
}
```

## Nice-to-Have Improvements

### 7. Tool Grouping and Discovery

**Recommendation:** Add metadata to help AI agents discover related tools.

```csharp
description: @"[Category: Text Search]
[Related: file_search (find by name), similar_files (find by content similarity)]
[Prerequisites: index_workspace]
[Typical tokens: 500-2000 summary, 2000-10000 full]

Searches file contents for text patterns..."
```

### 8. Token Usage Transparency

**Recommendation:** Add token estimates to help AI agents manage context windows.

```csharp
contextLines = new { 
    type = "integer", 
    description = @"Lines of context before/after matches. 
    Token impact: ~100 tokens per result with context=3.
    Example: 50 results with context=3 ≈ 5,000 tokens" 
}
```

## Implementation Priority

### Phase 1: Critical (1 week)
1. Standardize parameter names across all tools
2. Rewrite all tool descriptions with clear structure
3. Implement consistent response format wrapper

### Phase 2: Important (1 week)
1. Add structured error recovery to all tools
2. Document progressive disclosure with examples
3. Enhance all parameter descriptions with examples

### Phase 3: Nice-to-have (1 week)
1. Add tool categorization metadata
2. Implement token usage estimates
3. Create tool discovery helper

## Validation Metrics

Success will be measured by:
- **50% reduction** in parameter confusion errors
- **75% reduction** in response parsing failures  
- **90% success rate** in error recovery without human intervention
- **100% consistency** in parameter naming across tools
- **Zero ambiguity** in tool selection decisions

## Specific Tool Fixes Needed

### text_search
- Rename `query` → `searchQuery`
- Add examples for each searchType
- Clarify filePattern vs extensions behavior

### file_search  
- Rename `query` → `nameQuery`
- Add glob pattern examples
- Clarify fuzzy search syntax

### store_memory
- Rename `type` → `memoryType` 
- Add list of valid memory types
- Provide field schema examples

### search_memories
- Clarify facets parameter format
- Document query expansion behavior
- Add context awareness examples

### batch_operations
- Provide complete operation examples
- Clarify operation parameter inheritance
- Document parallel execution behavior

## Conclusion

These changes will dramatically improve AI agent success rates when using the COA CodeSearch MCP tools. The current state creates unnecessary friction and errors that can be eliminated through consistent naming, clear documentation, and predictable response formats.

The most critical change is establishing consistency - once AI agents can rely on predictable patterns across all tools, their effectiveness will increase significantly.