---
allowed-tools: mcp__tusk__plan
description: Manage long-running project plans that survive sessions
argument-hint: [action]
---

Manage your project plans - living documents that guide your work and survive context compaction.

**Plans vs Checkpoints:**
- **Plans** are long-running roadmaps (days/weeks) that appear in recall()
- **Checkpoints** are point-in-time snapshots of progress

$if($1 == "list")
List all plans using `plan(action: "list")` to see your workspace plans.
$elif($1 == "get")
Retrieve a plan using `plan(action: "get", planId: "...")` to view details.
$elif($1 == "update")
Update progress using `plan(action: "update", planId: "...", progress: "...")` to track completion.
$elif($1 == "complete")
Mark plan done using `plan(action: "complete", planId: "...")` when finished.
$elif($1 == "activate")
Activate a plan using `plan(action: "activate", planId: "...")` to make it current.
$else
**Save a plan** immediately after ExitPlanMode:
```
plan({
  action: "save",
  title: "Brief plan summary",
  content: "Your full plan details..."
})
```

**CRITICAL:** Plans must be saved within 1 exchange of creating them in plan mode, or they'll be lost during compaction!
$endif

**Key Features:**
- Only ONE active plan per workspace
- Active plan appears automatically in recall()
- Track progress over time with update action
- Plans survive crashes and compaction

**Workflow:**
1. Create plan in plan mode
2. Call ExitPlanMode
3. **Immediately save with /plan** ← Don't skip this!
4. Reference plan in recall()
5. Update progress as you work

Examples:
- `/plan` - Save a new plan (after ExitPlanMode)
- `/plan list` - See all your plans
- `/plan update` - Add progress notes
- `/plan complete` - Mark current plan done
