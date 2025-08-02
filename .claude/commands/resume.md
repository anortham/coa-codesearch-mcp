---
allowed-tools: ["mcp__codesearch__recall_context", "mcp__codesearch__search_memories", "mcp__codesearch__load_context"]
description: "Resume work from previous session by loading recent work context"
---

Load my recent work context and memories from the previous session.

$ARGUMENTS

Steps:
1. First, use load_context to get memories relevant to the current directory
2. Search for recent WorkSession memories to understand what was accomplished
3. Look for any pending tasks or next steps mentioned in the memories
4. Provide a summary of:
   - What was accomplished in the last session
   - Current state of the work
   - Suggested next steps based on the context

If specific context is provided in arguments, focus the search on that topic.