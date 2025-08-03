---
allowed-tools: ["mcp__codesearch__recall_context", "mcp__codesearch__search_memories", "mcp__codesearch__load_context", "mcp__codesearch__unified_memory"]
description: "Resume work from the most recent checkpoint"
---

Load the most recent checkpoint and continue work from where we left off.

$ARGUMENTS

Steps:
1. Use ONE search query to find the most recent checkpoint:
   ```
   search_memories with:
   - query: "Session Checkpoint:"
   - types: ["WorkSession"] 
   - orderBy: "created"
   - orderDescending: true
   - maxResults: 1
   - mode: "full"
   ```

2. If a checkpoint is found:
   - Extract and display the full checkpoint content
   - The checkpoint already contains all needed sections:
     - Timestamp (in title)
     - What was accomplished
     - Current state
     - Next steps
     - Files modified

3. If no checkpoint found:
   - Fall back to load_context for general project memories
   - Display: "No recent checkpoint found. Here's the current project context:"

4. End with: "Ready to continue from checkpoint. What would you like to work on?"

IMPORTANT: Use only ONE search query. The full mode will return complete content in a single call.