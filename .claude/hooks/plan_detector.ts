#!/usr/bin/env bun
/**
 * Tusk Plan Detector Hook - ExitPlanMode Detection
 *
 * Detects when Claude exits plan mode and reminds to save the plan
 * using the mcp__tusk__plan tool to prevent loss during compaction.
 *
 * This is THE CRITICAL hook for preserving planning work.
 */

import { logHookActivity } from "./hook-logger.ts";

async function main() {
  try {
    // Read JSON input from stdin
    const stdinBuffer = [];
    for await (const chunk of process.stdin) {
      stdinBuffer.push(chunk);
    }
    const inputData = JSON.parse(Buffer.concat(stdinBuffer).toString());

    // Extract tool information
    const toolName = inputData.tool_name || '';
    const toolInput = inputData.tool_input || {};

    // Only process ExitPlanMode tool usage
    if (toolName !== 'ExitPlanMode') {
      process.exit(0);
    }

    // Extract the plan content
    const planContent = toolInput.plan || '';

    if (!planContent || planContent.length < 50) {
      // Plan too short, probably not worth saving
      process.exit(0);
    }

    // Extract a brief summary from the plan
    const firstLine = planContent.split('\n')
      .find(line => line.trim().length > 0)
      ?.trim()
      .substring(0, 80) || 'your plan';

    // Create JSON output with additionalContext to inject reminder into Claude's conversation
    // This makes the reminder visible to Claude, not just the user
    const reminderContext = `📋 **IMPORTANT: Save Your Plan Immediately!**

You just exited plan mode and created a plan. You MUST save it now using the plan tool, or this planning work will be lost during context compaction.

**If you already have an active plan:**
You'll get an error when saving. Here's how to handle it:

1. **Complete the current plan first** (if it's done):
\`\`\`typescript
plan({ action: "complete", planId: "current-plan-id" })
plan({ action: "save", title: "New plan title", content: "..." })
\`\`\`

2. **Switch to the new plan** (if both plans are ongoing):
\`\`\`typescript
plan({
  action: "switch",
  planId: "current-plan-id",
  title: "New plan title",
  content: "Full plan content..."
})
\`\`\`

3. **Save as inactive** (to work on later):
\`\`\`typescript
plan({
  action: "save",
  title: "New plan title",
  content: "...",
  activate: false
})
\`\`\`

**If no active plan exists, save normally:**
\`\`\`typescript
plan({
  action: "save",
  title: "Brief summary of your plan (one line)",
  content: "Full plan content from ExitPlanMode..."
})
\`\`\`

**Plan benefits:**
- Auto-exported to ~/.tusk/plans/{workspace}/ as markdown
- Survive context compaction and crashes
- Appear automatically in recall()
- Track progress over time

**DO NOT skip this step!** Save the plan in your very next action.`;

    // Output JSON to stdout for Claude to see
    const output = {
      hookSpecificOutput: {
        hookEventName: "PostToolUse",
        additionalContext: reminderContext
      }
    };
    console.log(JSON.stringify(output));

    logHookActivity("plan_detector", "ExitPlanMode detected", `Plan preview: ${firstLine}`);

    // Exit with code 0 when using JSON output
    // The additionalContext will be injected into Claude's conversation
    process.exit(0);
  } catch (error) {
    // Silently fail - don't block Claude's workflow
    process.exit(0);
  }
}

main();
