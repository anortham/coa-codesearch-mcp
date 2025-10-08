---
allowed-tools: mcp__tusk__recall
description: Generate standup reports from your development journal
argument-hint: [style] [days]
---

Generate a standup report from your journal showing recent work progress and accomplishments.

Available styles:
- **meeting**: Classic standup format (default)
- **written**: Narrative summary format
- **executive**: High-level impact summary
- **metrics**: Dashboard with productivity stats

$if($1 == "executive")
Generate executive standup using `recall(days=3, standup="executive")` for leadership updates.
$elif($1 == "metrics")
Generate metrics dashboard using `recall(days=7, standup="metrics")` for detailed analytics.
$elif($1 == "written")
Generate written summary using `recall(days=2, standup="written")` for narrative format.
$elif($1)
Generate standup using `recall(days=$1, standup="meeting")` for the last $1 days.
$else
Generate daily standup using `recall(days=1, standup="meeting")` for recent progress.
$endif

**Features:**
- **Active plan** shown at the top (if you have one)
- Full context restoration combined with standup report
- Multi-workspace support (current workspace by default)
- Automatic project and git context
- Progress highlights and next steps
- File activity tracking (optional)

**Note:** Standup is integrated into recall() for combined context + reporting.

Examples:
- `/standup` - Daily meeting format with active plan and context
- `/standup executive` - Executive summary with context
- `/standup metrics` - Analytics dashboard with full context
- `/standup 7` - Weekly meeting format with 7 days of context