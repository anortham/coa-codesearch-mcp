---
allowed-tools: mcp__tusk__recall
description: Restore context from previous work sessions
argument-hint: [days]
---

$if($ARGUMENTS)
**Recalling Context:** Loading context from the last $ARGUMENTS days.

Using `recall(days=$ARGUMENTS)` to restore recent work context and progress.

$else
**Smart Recall:** Loading context intelligently based on your session state.

Using `recall()` with smart session-aware context detection to restore the optimal amount of recent work context.

The system automatically determines the ideal lookback period:
- Active sessions (deep in flow): 1 day
- Fresh sessions (just starting): 2-7 days to include previous context
- After breaks: Up to 14 days for full context arc

This helps maintain continuity across Claude sessions by recovering:
- **Active plan** - Your current project roadmap (always shown first)
- Recent checkpoints and progress
- Project context and decisions
- Key breakthroughs and discoveries

Use `/recall 7` for week-long context or `/recall 1` for just today if you need manual control.

**New features:**
- Active plans shown automatically at the top
- Optional standup report: add `standup: "meeting"` parameter
- Plans survive compaction and guide your work

Multi-workspace support: By default shows current workspace only. The tool supports workspace filtering for cross-project context.
$endif