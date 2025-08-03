---
allowed-tools: ["mcp__codesearch__store_memory"]
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

Steps:
1. Use store_memory with memoryType="WorkSession" and isShared=false
2. Include the full formatted content as shown above
3. Add all relevant files from this session to the files parameter
4. After saving, provide the memory ID for easy recall
5. Show a brief summary of what was saved
6. Remind user: "Use /resume to continue from this checkpoint"