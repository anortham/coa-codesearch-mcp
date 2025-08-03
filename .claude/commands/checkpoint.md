---
allowed-tools: ["mcp__codesearch__store_checkpoint"]
description: "Create a checkpoint of current work session with sequential ID"
---

Create a checkpoint with the following information:

$ARGUMENTS

Include in the checkpoint:
- What was accomplished in this session
- Current state/progress  
- Next steps/todos (be specific)
- Any blockers or problems encountered
- Key files modified in this session

Format the content as:
```
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
1. Use store_checkpoint with the formatted content
2. The system will automatically assign a sequential checkpoint ID
3. Show the checkpoint ID that was created
4. Remind user: "Use /resume to continue from this checkpoint"