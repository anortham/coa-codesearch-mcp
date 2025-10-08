#!/usr/bin/env bun
/**
 * Tusk Stop Hook - Work Completion Detection
 *
 * Analyzes Claude's responses to detect when work is completed and automatically
 * saves a checkpoint to capture the final state.
 *
 * Cross-platform compatible for Windows, macOS, and Linux.
 */

import { spawnSync } from "bun";
import { logHookActivity, logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

function detectCompletionPatterns(text: string): boolean {
  const completionPatterns = [
    /\b(completed?|finished|done|resolved|fixed|implemented|deployed)\b/i,
    /\b(all set|ready to go|good to go|successfully)\b/i,
    /\b(final|complete|finish|wrap up|concluded)\b/i,
    /✅|✓|☑️/, // Check marks
    /\b(that should do it|we're done|everything is working)\b/i,
  ];

  return completionPatterns.some(pattern => pattern.test(text));
}

function extractKeyContent(text: string): string {
  // Get first meaningful line
  const lines = text.split('\n');
  const firstMeaningfulLine = lines.find(line =>
    line.trim() && !line.trim().startsWith('#')
  ) || "Work completed";

  // Truncate if too long
  if (firstMeaningfulLine.length > 100) {
    return firstMeaningfulLine.substring(0, 97) + "...";
  }
  return firstMeaningfulLine;
}

async function main() {
  let claudeCodeSessionId: string | undefined;

  try {
    // Read JSON input from stdin
    const stdinBuffer = [];
    for await (const chunk of process.stdin) {
      stdinBuffer.push(chunk);
    }
    const inputData = JSON.parse(Buffer.concat(stdinBuffer).toString());
    claudeCodeSessionId = inputData.session_id;

    // Try multiple possible paths for assistant content
    let content = '';
    if (inputData.assistant_message?.content) {
      content = inputData.assistant_message.content;
    } else if (inputData.content) {
      content = inputData.content;
    } else if (inputData.message?.content) {
      content = inputData.message.content;
    }

    // Check if this looks like completion
    if (!detectCompletionPatterns(content)) {
      logSkip("stop", "no completion patterns");
      process.exit(0);
    }

    const keyContent = extractKeyContent(content);
    const description = `Work completed: ${keyContent}`;

    // Create detailed context from the full completion content
    const details = [
      "=== Completion Summary ===",
      keyContent,
      "",
      "=== Full Response Context ===",
      content.substring(0, 1000) + (content.length > 1000 ? "..." : ""),
      "",
      "=== Session Info ===",
      `Completed at: ${new Date().toLocaleString()}`,
      `Content length: ${content.length} characters`,
      `Detection confidence: High (completion patterns matched)`
    ].join('\n');

    // Find tusk CLI using smart path resolution
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("stop", "CLI not found in any expected location");
      console.error(`⚠️ Tusk CLI not found. Set TUSK_CLI_PATH environment variable or ensure tusk is in a standard location.`);
      process.exit(0);
    }

    // Create checkpoint with rich completion context
    const tags = [
      "completion",
      "work-done",
      "session-end",
      "auto-checkpoint"
    ];

    const cliArgs = [
      "bun", cliPath, "checkpoint",
      `${description}\n\n${details}`,
      tags.join(","),
      "--entry-type=completion",
      "--confidence=0.95"
    ];

    // Add session ID if available
    if (claudeCodeSessionId) {
      cliArgs.push(`--session-id=${claudeCodeSessionId}`);
    }

    const result = spawnSync(cliArgs, {
      stdout: "pipe",
      stderr: "pipe",
    });

    if (result.success) {
      const sessionInfo = claudeCodeSessionId ? ` [${claudeCodeSessionId.slice(0, 8)}...]` : "";
      logSuccess("stop", `${keyContent}${sessionInfo}`);
      console.error(`✅ Work completion detected and saved to journal`);
      console.error(`📝 Summary: ${keyContent}`);
      console.error(`🔖 Tagged: ${tags.join(", ")}`);
      console.error(`💾 Context preserved with ${content.length} characters of completion details`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("stop", errorOutput);
      console.error(`⚠️ Completion checkpoint failed: ${errorOutput}`);
    }
  } catch (error) {
    logError("stop", String(error));
    console.error(`⚠️ Stop hook error: ${error}`);
  }

  // Always exit successfully to not interfere with Claude
  process.exit(0);
}

main();