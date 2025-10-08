#!/usr/bin/env bun
/**
 * Tusk Post Tool Use Hook - Git Commit Detection
 *
 * Automatically detects when Claude performs git commits and saves the commit
 * message as a checkpoint to preserve the detailed commit context.
 *
 * Cross-platform compatible for Windows, macOS, and Linux.
 */

import { spawnSync } from "bun";
import { logHookActivity, logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

function extractCommitMessage(command: string): string | null {
  // Match various git commit patterns
  const patterns = [
    // git commit -m "message"
    /git commit[^"]*-m\s*"([^"]+)"/,
    // git commit -m 'message'
    /git commit[^']*-m\s*'([^']+)'/,
    // git commit --message="message"
    /git commit[^"]*--message=?"([^"]+)"/,
    // git commit with heredoc
    /git commit.*-m.*\$\(cat <<['"]*EOF['"]*\s*\n(.*?)\s*EOF/s,
  ];

  for (const pattern of patterns) {
    const match = command.match(pattern);
    if (match) {
      return match[1].trim();
    }
  }

  return null;
}

function isGitCommitCommand(command: string): boolean {
  // Check for git commit patterns
  return /git\s+commit/.test(command) && /-m|--message/.test(command);
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

    // Extract tool information
    const toolName = inputData.tool_name || '';
    const toolInput = inputData.tool_input || {};
    const toolResponse = inputData.tool_response || {};

    // Only process Bash tool usage
    if (toolName !== 'Bash') {
      logSkip("post_tool_use", `not bash tool (${toolName})`);
      process.exit(0);
    }

    const command = toolInput.command || '';

    // Check if this is a git commit command
    if (!isGitCommitCommand(command)) {
      logSkip("post_tool_use", "not git commit");
      process.exit(0);
    }

    // Check if the command was successful
    const success = toolResponse.success !== false && !toolResponse.stderr?.includes('error');
    if (!success) {
      logSkip("post_tool_use", "commit failed");
      process.exit(0);
    }

    // Extract commit message
    const commitMessage = extractCommitMessage(command);
    const description = commitMessage
      ? `Git commit: ${commitMessage}`
      : "Git commit completed (could not extract message)";

    // Find tusk CLI using smart path resolution
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("post_tool_use", "CLI not found in any expected location");
      console.error(`⚠️ Tusk CLI not found. Set TUSK_CLI_PATH environment variable or ensure tusk is in a standard location.`);
      process.exit(0);
    }

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      "git-commit,completion",
      "--entry-type=completion",
      "--confidence=1.0"
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
      logSuccess("post_tool_use", `${commitMessage || 'commit completed'}${sessionInfo}`);
      console.error(`✅ Git commit checkpoint saved: ${commitMessage || 'commit completed'}`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("post_tool_use", errorOutput);
      console.error(`⚠️ Git commit checkpoint failed: ${errorOutput}`);
    }
  } catch (error) {
    logError("post_tool_use", String(error));
    console.error(`⚠️ Post tool use hook error: ${error}`);
  }

  // Always exit successfully to not interfere with Claude
  process.exit(0);
}

main();