---
allowed-tools: ["mcp__projectknowledge__list_checkpoints", "mcp__projectknowledge__load_checkpoint", "mcp__projectknowledge__find_knowledge", "mcp__projectknowledge__get_checklist", "mcp__projectknowledge__show_activity"]
description: "Resume work from the most recent checkpoint with enhanced display"
---

Load the most recent checkpoint and continue work from where we left off.

$ARGUMENTS

## Resume Process:

### 1. Find the Latest Checkpoint
If a sessionId is provided:
- Use list_checkpoints with that sessionId
- Get the most recent by sequence number

Otherwise:
- Use find_knowledge with query "type:Checkpoint" maxResults: 1
- The chronological IDs ensure we get the most recent

### 2. Display Checkpoint Information
Format the checkpoint display as:

```
═══════════════════════════════════════════════════════════
📍 RESUMING FROM CHECKPOINT: {ID}
═══════════════════════════════════════════════════════════

Session: {sessionId} | Sequence: #{sequenceNumber}
Created: {timestamp} ({timeAgo})

[Full checkpoint content here]

Active Files:
• file1.ext
• file2.ext
═══════════════════════════════════════════════════════════
```

### 3. Show Recent Timeline
Use show_activity with:
- HoursAgo: 24
- MaxPerGroup: 5

Display as: "📊 Recent Activity (Last 24 Hours)"

### 4. Check Active Work Items
Search for active checklists:
- Use find_knowledge with query "type:Checklist" maxResults: 3
- For each checklist found, use get_checklist to show completion status
- Display as progress bars: [████████░░] 80% - Checklist Name

### 5. Load Related Knowledge
If checkpoint mentions specific topics/files:
- Search for related ProjectInsights or TechnicalDebt
- Display any critical items

### 6. Ready Message
End with:
```
═══════════════════════════════════════════════════════════
✅ Session restored successfully
📝 Next steps loaded from checkpoint
🚀 Ready to continue. What would you like to work on?
═══════════════════════════════════════════════════════════
```

### 7. Fallback (No Checkpoint)
If no checkpoint found:
- Use show_activity for last 7 days
- Display: "⚠️ No checkpoint found. Showing recent activity:"
- Show timeline and suggest creating a checkpoint