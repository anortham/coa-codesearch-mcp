---
allowed-tools: ["mcp__codesearch__recall_context", "mcp__codesearch__search_memories", "mcp__codesearch__load_context", "mcp__codesearch__unified_memory"]
description: "Resume work from the most recent checkpoint"
---

Load the most recent checkpoint and continue work from where we left off.

$ARGUMENTS

Steps:
1. Search for WorkSession memories containing "Session Checkpoint:" 
   - Order by created date descending
   - Limit to 5 most recent
   - Look for the structured checkpoint format

2. Find the MOST RECENT checkpoint (check timestamps in content)

3. Extract and display:
   - **Last checkpoint time**: [timestamp from memory]
   - **What was accomplished**: [from checkpoint]
   - **Current state**: [from checkpoint]
   - **Next steps**: [from checkpoint, numbered list]
   - **Files that were being worked on**: [from checkpoint]

4. If no checkpoint found, fall back to load_context for general memories

5. End with: "Ready to continue from checkpoint. What would you like to work on?"

Use search_memories with query "Session Checkpoint" to find recent checkpoints.