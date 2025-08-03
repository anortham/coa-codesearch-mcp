---
allowed-tools: ["mcp__codesearch__unified_memory", "mcp__codesearch__store_memory", "mcp__codesearch__recall_context"]
description: "Create a timestamped checkpoint memory of current work session"
---

Using the information below, please save a new WorkSession memory with a clear timestamp:

$ARGUMENTS

The memory MUST start with:
**Session Checkpoint: [Current Date/Time]**

Include in the memory:
- Timestamp (e.g., "2025-08-03 15:30 UTC")
- What was accomplished in this session
- Current state/progress  
- Next steps/todos (be specific)
- Any blockers or problems encountered
- Key files modified in this session

Format example:
```
**Session Checkpoint: 2025-08-03 15:30 UTC**

## Accomplished
- [Specific task 1]
- [Specific task 2]

## Current State
[Where things stand right now]

## Next Steps
1. [Concrete next action]
2. [Another specific task]

## Files Modified
- path/to/file1.cs (what changed)
- path/to/file2.md (what changed)
```

Then:
1. Provide the memory ID for easy recall
2. Show a brief summary of what was saved
3. Remind user: "Use /resume to continue from this checkpoint"

Use the unified_memory tool with "save work session:" prefix.