---
allowed-tools: ["mcp__codesearch__get_latest_checkpoint", "mcp__codesearch__load_context"]
description: "Resume work from the most recent checkpoint"
---

Load the most recent checkpoint and continue work from where we left off.

$ARGUMENTS

Steps:
1. Use get_latest_checkpoint to retrieve the most recent checkpoint
   - This automatically returns the checkpoint with the highest sequential ID
   - No need to worry about time zones or sorting

2. If a checkpoint is found:
   - Display the checkpoint ID (e.g., "Resuming from CHECKPOINT-00003")
   - Extract and display the full checkpoint content
   - The checkpoint contains:
     - Checkpoint ID
     - Creation timestamp
     - What was accomplished
     - Current state
     - Next steps
     - Files modified

3. If no checkpoint found:
   - Fall back to load_context for general project memories
   - Display: "No checkpoint found. Loading general project context..."

4. End with: "Ready to continue. What would you like to work on?"