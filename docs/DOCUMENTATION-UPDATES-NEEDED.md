# Documentation Updates Needed

Based on the recent improvements to the codesearch tools, the following documentation needs updating:

## Files to Update

### 1. TOOLS.md
This is the main file that needs updating. See TOOLS-UPDATE.md for the specific changes needed:

- **index_workspace**: Add documentation about the new `projectInfo` field that returns project type detection and search tips
- **fast_text_search_v2**: Update to show the enhanced zero-result insights with specific commands and project-aware suggestions
- **fast_file_search_v2**: Update the description to clarify it searches FOR file names (not INSIDE files)
- Add a note about redundant index prevention

### 2. CLAUDE.md (Minor Updates)
Consider adding a section about the recent improvements:

```markdown
## Recent Improvements (January 2025)

### Enhanced Search Feedback
- `fast_text_search_v2` now provides intelligent feedback when file pattern restrictions cause zero results
- Detects when you're missing results in other file types (e.g., searching only .cs files in a Blazor project)
- Provides copy-pasteable commands with appropriate file extensions

### Project-Aware Index
- `index_workspace` now detects project type (Blazor, ASP.NET, React, etc.)
- Returns project-specific search tips and primary file extensions
- Prevents redundant indexes when subdirectories are already covered

### Clearer Tool Descriptions
- Tools now include "When to use" guidance
- `fast_text_search_v2`: "Finding where something is used/implemented"
- `fast_file_search_v2`: "Finding specific files"
```

### 3. README.md (Optional)
Consider mentioning in the features section:
- Project-aware search suggestions
- Intelligent zero-result feedback
- Redundant index prevention

## Summary

The main work is updating TOOLS.md with the content from TOOLS-UPDATE.md. This will ensure users understand:
1. The new project detection features
2. The enhanced zero-result insights that help avoid missing search results
3. The clearer tool descriptions
4. The redundant index prevention

These improvements directly address the feedback about users missing results due to overly restrictive file patterns (like searching for "pushover" only in .cs files and missing .razor files).